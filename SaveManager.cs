using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace DvMod.AirBrake
{
    public static class AirBrakeSaveManager
    {
        private const string SaveKey = "DvMod.AirBrake";
        private const string MainReservoirKey = "mainReservoirPressure";

        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.GetCarSaveData))]
        public static class GetCarSaveDataPatch
        {
            public static void Postfix(TrainCar car, JObject __result)
            {
                var state = ExtraBrakeState.Instance(car.brakeSystem);
                if (!state.Valid)
                {
                    Main.DebugLog($"Skipping corrupted ExtraBrakeState {state} for {car.ID}");
                    return;
                }
                if (state.IsDefault)
                    return;

                Main.DebugLog($"Saving state for {car.ID}: {state}");
                var obj = JObject.FromObject(state);
                if (CarTypes.IsLocomotive(car.carType))
                    obj[MainReservoirKey] = car.brakeSystem.mainReservoirPressureUnsmoothed;
                __result[SaveKey] = obj;
            }
        }

        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.InstantiateCar))]
        public static class InstantiateCarPatch
        {
            public static void Postfix(JObject carData, TrainCar __result)
            {
                if (carData != null && __result != null && carData.TryGetValue(SaveKey, out var token) && token is JObject obj)
                {
                    var serializer = new JsonSerializer();
                    var state = ExtraBrakeState.Instance(__result.brakeSystem);
                    serializer.Populate(new JTokenReader(token), state);
                    if (CarTypes.IsLocomotive(__result.carType) && obj.TryGetValue(MainReservoirKey, out var mainResPressure))
                        __result.brakeSystem.mainReservoirPressure = __result.brakeSystem.mainReservoirPressureUnsmoothed = mainResPressure.Value<float>();
                    Main.DebugLog($"Loaded state for {carData["id"]}: {state}");
                }
            }
        }
    }
}
