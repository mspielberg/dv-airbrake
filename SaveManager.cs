using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DvMod.AirBrake
{
    public static class AirBrakeSaveManager
    {
        private const string SaveKey = "DvMod.AirBrake";

        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.GetCarSaveData))]
        public static class GetCarSaveDataPatch
        {
            public static void Postfix(TrainCar car, JObject __result)
            {
                var state = ExtraBrakeState.Instance(car.brakeSystem);
                if (state.Valid)
                {
                    Main.DebugLog($"Saving state for {car.ID}: {state}");
                    __result[SaveKey] = JObject.FromObject(state);
                }
                else
                {
                    Main.DebugLog($"Skipping corrupted ExtraBrakeState {state} for {car.ID}");
                }
            }
        }

        [HarmonyPatch(typeof(CarsSaveManager), nameof(CarsSaveManager.InstantiateCar))]
        public static class InstantiateCarPatch
        {
            public static void Postfix(JObject carData, TrainCar __result)
            {
                if (carData.TryGetValue(SaveKey, out var token))
                {
                    var serializer = new JsonSerializer();
                    var state = ExtraBrakeState.Instance(__result.brakeSystem);
                    serializer.Populate(new JTokenReader(token), state);
                    Main.DebugLog($"Loaded state for {carData["id"]}: {state}");
                }
            }
        }
    }
}