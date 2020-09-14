using DV.Simulation.Brake;
using DvMod.AirBrake.Components;
using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace DvMod.AirBrake
{
    static internal class Constants
    {
        public const float ApplicationThreshold = 0.05f;

        public const float BRAKE_CYLINDER_VOLUME = 1f;
        public const float AUX_RESERVOIR_VOLUME = 2.5f;
        public const float MAX_CYLINDER_PRESSURE =
            ((BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * AUX_RESERVOIR_VOLUME) + BRAKE_CYLINDER_VOLUME) / (AUX_RESERVOIR_VOLUME + BRAKE_CYLINDER_VOLUME);

        public const float MaxMainReservoirPressure = 8f;
        public const float CylinderThresholdPressure = 0.5f;
    }

    internal class ExtraBrakeState
    {
        public float cylinderPressure;
        public float auxReservoirPressure;

        private static readonly Cache<BrakeSystem, ExtraBrakeState> cache = new Cache<BrakeSystem, ExtraBrakeState>(_ => new ExtraBrakeState());
        public static ExtraBrakeState Instance(BrakeSystem system) => cache[system];
    }

    public static class BrakeSystemExtensions
    {
        private static readonly Cache<BrakeSystem, TrainCar> trainCars =
            new Cache<BrakeSystem, TrainCar>(bs => TrainCar.Resolve(bs.gameObject));
        public static TrainCar GetTrainCar(this BrakeSystem brakeSystem) => trainCars[brakeSystem];

        public static ref float CylinderPressure(this BrakeSystem brakeSystem) =>
            ref ExtraBrakeState.Instance(brakeSystem).cylinderPressure;
    }

    internal static class AirSystem
    {
        /// <summary>Simulates air exchange between two connected components.</summary>
        /// <param name="dt">Time period to simulate.</param>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <param name="multiplier"></param>
        /// <param name="limit"></param>
        /// <returns>Rate of change of p1 normalized to bar/s</returns>
        public static float Equalize(float dt, ref float p1, ref float p2, float v1, float v2, float multiplier = 1f, float limit = 1f)
        {
            var before = p1;
            Brakeset.EqualizePressure(ref p1, ref p2, v1, v2, multiplier * BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER, limit * BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT, dt);
            return dt == 0f ? 0f : (p1 - before) / dt;
        }

        public static float OneWayFlow(float dt, ref float fromPressure, ref float toPressure, float fromVolume, float toVolume, float speed = 1f, float limit = 1f)
        {
            if (fromPressure <= toPressure)
                return 0f;
            return Equalize(dt, ref toPressure, ref fromPressure, toVolume, fromVolume, speed, limit);
        }

        public static float Vent(float dt, ref float pressure, float volume, float speed = 1f, float limit = 1f)
        {
            float atmosphere = 0f;
            return OneWayFlow(dt, ref pressure, ref atmosphere, volume, BrakeSystemConsts.ATMOSPHERE_VOLUME, speed, limit);
        }
    }

    public static class AirBrake
    {
        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.Awake))]
        public static class TrainCarAwakePatch
        {
            public static void Postfix(TrainCar __instance)
            {
                __instance.brakeSystem.brakePipePressure = 0f;
                __instance.brakeSystem.mainReservoirPressure = 0f;
                __instance.brakeSystem.mainReservoirPressureUnsmoothed = 0f;

                __instance.InteriorPrefabLoaded += (gameObject) => {
                    var mainResIndicator = __instance.carType switch
                    {
                        TrainCarType.LocoDiesel => gameObject.GetComponent<IndicatorsDiesel>().brakeAux,
                        TrainCarType.LocoShunter => gameObject.GetComponent<IndicatorsShunter>().brakeAux,
                        TrainCarType.LocoSteamHeavy => gameObject.GetComponent<IndicatorsSteam>().brakeAux,
                        _ => default
                    };
                    if (mainResIndicator == null)
                        return;
                    mainResIndicator.maxValue = Constants.MaxMainReservoirPressure;
                };
            }
        }

        [HarmonyPatch(typeof(Brakeset), nameof(Brakeset.Update))]
        public static class BrakesetUpdatePatch
        {
            private static void RechargeMainReservoir(BrakeSystem car, float dt)
            {
                if (car.compressorRunning)
                {
                    var increase = car.compressorProductionRate * Main.settings.compressorSpeed * dt;
                    car.mainReservoirPressureUnsmoothed =
                         Mathf.Clamp(car.mainReservoirPressureUnsmoothed + increase, 0f, Constants.MaxMainReservoirPressure);
                }
                car.mainReservoirPressure = Mathf.SmoothDamp(car.mainReservoirPressure, car.mainReservoirPressureUnsmoothed, ref car.mainResPressureRef, 0.8f);
            }

            private static void EqualizeBrakePipe(Brakeset brakeset, float dt)
            {
                var cars = brakeset.cars.AsReadOnly();
                foreach (var (a, b) in cars.Zip(cars.Skip(1), (a, b) => (a, b)))
                {
                    // AirBrake.DebugLog(a, $"EqualizeBrakePipe: a={a.brakePipePressure}, b={b.brakePipePressure}");
                    AirSystem.Equalize(
                        dt,
                        ref a.brakePipePressure,
                        ref b.brakePipePressure,
                        BrakeSystemConsts.PIPE_VOLUME,
                        BrakeSystemConsts.PIPE_VOLUME,
                        Main.settings.pipeBalanceSpeed,
                        Main.settings.pipeBalanceSpeed
                    );
                }
                brakeset.firstCar.Front.UpdatePressurized();
                brakeset.lastCar.Rear.UpdatePressurized();
            }

            private static float GetMechanicalBrakeFactor(BrakeSystem car)
            {
                if (car.hasIndependentBrake && !car.hasCompressor) // Caboose only
                    return car.independentBrakePosition;
                return 0f;
            }

            private static void ApplyBrakingForce(BrakeSystem car)
            {
                var state = ExtraBrakeState.Instance(car);
                var cylinderBrakingFactor = Mathf.InverseLerp(Constants.CylinderThresholdPressure, Constants.MAX_CYLINDER_PRESSURE, state.cylinderPressure);
                var mechanicalBrakingFactor = GetMechanicalBrakeFactor(car);
                car.brakingFactor = Mathf.Max(mechanicalBrakingFactor, cylinderBrakingFactor);
            }

            private static void UpdateHUD(BrakeSystem car)
            {
                var state = ExtraBrakeState.Instance(car);
                HeadsUpDisplayBridge.instance?.UpdateAuxReservoirPressure(car.GetTrainCar(), state.auxReservoirPressure);
                HeadsUpDisplayBridge.instance?.UpdateBrakeCylinderPressure(car.GetTrainCar(), state.cylinderPressure);
            }

            private static void Update(Brakeset brakeset, float dt)
            {
                AngleCocks.Update(brakeset, dt);
                EqualizeBrakePipe(brakeset, dt);
                foreach (var car in brakeset.cars)
                {
                    if (car.hasCompressor)
                    {
                        RechargeMainReservoir(car, dt);
                        BrakeValve26L.Update(car, dt);
                    }
                    else
                    {
                        PlainTripleValve.Update(car, dt);
                    }
                    ApplyBrakingForce(car);
                    UpdateHUD(car);
                }
            }

            public static bool Prefix(float dt)
            {
                if (!Main.enabled)
                    return true;
                foreach (var brakeset in Brakeset.allSets)
                    Update(brakeset, dt);
                return false;
            }
        }

        public static void DebugLog(BrakeSystem car, string msg)
        {
            if (PlayerManager.Car?.brakeSystem == car)
                Main.DebugLog(msg);
        }
    }
}
