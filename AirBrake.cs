using DV.Simulation.Brake;
using DvMod.AirBrake.Components;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;

namespace DvMod.AirBrake
{
    static internal class Constants
    {
        public const float AtmosphereVolume = 1e6f;
        public const float ApplicationThreshold = 0.01f;

        public const float BrakeCylinderVolume = 1f;
        public const float AUX_RESERVOIR_VOLUME = 2.5f;
        public const float FullApplicationPressure =
            BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * AUX_RESERVOIR_VOLUME / (AUX_RESERVOIR_VOLUME + BrakeCylinderVolume);

        public const float MaxMainReservoirPressure = 8f;
        public const float CylinderThresholdPressure = 0.5f;
    }

    internal class ExtraBrakeState
    {
        public float cylinderPressure;
        public float auxReservoirPressure;
        public PlainTripleValve.Mode tripleValveMode;

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

    internal static class AirFlow
    {
        private static void AdjustMass(ref float pressure, float volume, float deltaMass, float min, float max)
            => pressure = Mathf.Clamp(pressure + (deltaMass / volume), min, max);

        public static float TransferMass(ref float destPressure, ref float srcPressure, float destVolume, float srcVolume, float massTransferToLimit)
        {
            Assert.IsTrue(destPressure <= srcPressure);
            var totalMass = (destPressure * destVolume) + (srcPressure * srcVolume);
            var totalVolume = destVolume + srcVolume;
            var equilibriumPressure = totalMass / totalVolume;
            var massTransferToEquilibrium = (equilibriumPressure - destPressure) * destVolume;
            var massTransfer = Mathf.Min(massTransferToLimit, massTransferToEquilibrium);

            AdjustMass(ref srcPressure, srcVolume, -massTransfer, equilibriumPressure, srcPressure);
            AdjustMass(ref destPressure, destVolume, massTransfer, destPressure, equilibriumPressure);

            Assert.IsTrue(destPressure <= srcPressure);
            return massTransfer;
        }

        public static float TransferPressure(ref float destPressure, ref float srcPressure, float destVolume, float srcVolume, float pressureChangeLimit)
            => TransferMass(ref destPressure, ref srcPressure, destVolume, srcVolume, pressureChangeLimit * destVolume);

        public static float Equalize(float dt, ref float p1, ref float p2, float v1, float v2, float rateMultiplier, float pressureChangeLimit = float.PositiveInfinity)
        {
            if (p1 > p2)
                return Equalize(dt, ref p2, ref p1, v2, v1, rateMultiplier, pressureChangeLimit);

            var pressureDelta = p2 - p1;
            Assert.IsTrue(pressureDelta >= 0);
            var naturalMassTransfer = Mathf.Sqrt(pressureDelta) * rateMultiplier * dt;
            var massTransferToLimit = pressureChangeLimit * v1;
            var actualTransfer = TransferMass(ref p1, ref p2, v1, v2, Mathf.Min(naturalMassTransfer, massTransferToLimit));
            return actualTransfer;
        }

        public static float OneWayFlow(float dt, ref float destPressure, ref float srcPressure, float destVolume,
            float srcVolume, float multiplier = 1f, float pressureChangeLimit = float.PositiveInfinity)
        {
            return srcPressure <= destPressure ? 0f
                : Equalize(dt, ref destPressure, ref srcPressure, destVolume, srcVolume, multiplier, pressureChangeLimit);
        }

        public static float Vent(float dt, ref float pressure, float volume, float multiplier = 1f, float pressureChangeLimit = float.PositiveInfinity)
        {
            float atmosphere = 0f;
            // Main.DebugLog($"AirBrake.Vent: P={pressure}, V={volume}");
            return OneWayFlow(dt, ref atmosphere, ref pressure, Constants.AtmosphereVolume, volume, multiplier, pressureChangeLimit);
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

                __instance.InteriorPrefabLoaded += (gameObject) =>
                {
                    if (gameObject == null)
                        return;
                    var mainResIndicator = TrainCar.Resolve(gameObject).carType switch
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
                    AirFlow.Equalize(
                        dt,
                        ref a.brakePipePressure,
                        ref b.brakePipePressure,
                        BrakeSystemConsts.PIPE_VOLUME,
                        BrakeSystemConsts.PIPE_VOLUME,
                        Main.settings.pipeBalanceSpeed);
                }
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
                var cylinderBrakingFactor = Mathf.InverseLerp(Constants.CylinderThresholdPressure, Constants.FullApplicationPressure, state.cylinderPressure);
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
