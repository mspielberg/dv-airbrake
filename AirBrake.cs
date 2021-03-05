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

        public const float MaxMainReservoirPressure = 8f;
        public const float MaxBrakePipePressure = 5f;

        public const float BrakePipeVolume = 10f;
        public const float MainReservoirVolume = 20f * AuxReservoirVolume;
        public const float AuxReservoirVolume = 45f;
        public const float BrakeCylinderVolume = AuxReservoirVolume / 2.5f;
        public const float FullApplicationPressure =
            MaxBrakePipePressure * AuxReservoirVolume / (AuxReservoirVolume + BrakeCylinderVolume);

        public const float PressureGaugeMax = 10f;
    }

    internal class ExtraBrakeState
    {
        public float brakePipePressureUnsmoothed;
        public float cylinderPressure;

        // Part of H6 automatic brake valve
        public float equalizingReservoirPressure;

        // Type C 10" combined car equipment
        public float auxReservoirPressure;
        public PlainTripleValve.Mode tripleValveMode;

        private static readonly Cache<BrakeSystem, ExtraBrakeState> cache = new Cache<BrakeSystem, ExtraBrakeState>(_ => new ExtraBrakeState());
        public static ExtraBrakeState Instance(BrakeSystem system) => cache[system];

        public override string ToString()
        {
            return $"BP={brakePipePressureUnsmoothed},Cyl={cylinderPressure},EQ={equalizingReservoirPressure},Aux={auxReservoirPressure},tripleMode={tripleValveMode}";
        }

        public bool Valid {
            get =>
                float.IsFinite(brakePipePressureUnsmoothed)
                && float.IsFinite(cylinderPressure)
                && float.IsFinite(equalizingReservoirPressure)
                && float.IsFinite(auxReservoirPressure);
        }
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

        public static float Equalize(float dt, ref float p1, ref float p2, float v1, float v2, float rateMultiplier, float pressureChangeLimit = float.PositiveInfinity)
        {
            Assert.IsTrue(pressureChangeLimit >= 0f);
            if (p1 > p2)
                return Equalize(dt, ref p2, ref p1, v2, v1, rateMultiplier, pressureChangeLimit);

            var pressureDelta = p2 - p1;
            Assert.IsTrue(pressureDelta >= 0);
            var naturalMassTransfer = pressureDelta * rateMultiplier * dt;
            var massTransferToLimit = pressureChangeLimit * v1;
            var actualTransfer = TransferMass(ref p1, ref p2, v1, v2, Mathf.Min(naturalMassTransfer, massTransferToLimit));
            return actualTransfer;
        }

        public static float OneWayFlow(float dt, ref float destPressure, ref float srcPressure, float destVolume,
            float srcVolume, float multiplier = 1f, float maxDestPressure = float.PositiveInfinity)
        {
            return srcPressure <= destPressure ? 0f
                : Equalize(dt, ref destPressure, ref srcPressure, destVolume, srcVolume, multiplier, maxDestPressure - destPressure);
        }

        public static float Vent(float dt, ref float pressure, float volume, float multiplier = 1f, float minPressure = 0f)
        {
            float atmosphere = 0f;
            // Main.DebugLog($"AirBrake.Vent: P={pressure}, V={volume}, minP={minPressure}");
            return pressure <= minPressure ? 0f
                : Equalize(dt, ref atmosphere, ref pressure, Constants.AtmosphereVolume, volume, multiplier, pressure - minPressure);
        }

        public static float VentPressure(float dt, ref float pressure, float volume, float pressureRate)
        {
            float atmosphere = 0f;
            // var pressureBefore = pressure;
            var massTransfer = TransferMass(ref atmosphere, ref pressure, Constants.AtmosphereVolume, volume, pressureRate * volume * dt);
            // var pressureAfter = pressure;
            // Main.DebugLog($"VentPressure: requestedRate={pressureRate};deltaP={pressureAfter-pressureBefore};actualRate={(pressureAfter-pressureBefore)/dt}");
            return massTransfer;
        }
    }

    public static class AirBrake
    {
        private static void RechargeMainReservoir(BrakeSystem car, float dt)
        {
            var state = ExtraBrakeState.Instance(car);
            if (car.compressorRunning)
            {
                var increase = car.compressorProductionRate * Main.settings.compressorSpeed * dt;
                car.mainReservoirPressureUnsmoothed =
                        Mathf.Clamp(car.mainReservoirPressureUnsmoothed + increase, 0f, Constants.MaxMainReservoirPressure);
            }
            AirFlow.OneWayFlow(
                dt,
                ref car.mainReservoirPressureUnsmoothed,
                ref state.brakePipePressureUnsmoothed,
                Constants.MainReservoirVolume,
                Constants.BrakePipeVolume,
                float.PositiveInfinity);
            car.mainReservoirPressure = Mathf.SmoothDamp(car.mainReservoirPressure, car.mainReservoirPressureUnsmoothed, ref car.mainResPressureRef, 0.8f);
        }

        private static void BalanceBrakePipe(Brakeset brakeset, float dt)
        {
            var states = brakeset.cars.Select(ExtraBrakeState.Instance).ToArray();
            var fullCycles = Mathf.Floor(Main.settings.pipeBalanceSpeed);
            var partialCycle = Main.settings.pipeBalanceSpeed - fullCycles;

            for (int i = 0; i < fullCycles; i++)
            {
                for (int carIndex = 0; carIndex < states.Length - 1; carIndex++)
                {
                    var stateA = states[carIndex];
                    var pressureA = stateA.brakePipePressureUnsmoothed;
                    var stateB = states[carIndex + 1];
                    var pressureB = stateB.brakePipePressureUnsmoothed;
                    var mean = (pressureA + pressureB) / 2f;
                    stateA.brakePipePressureUnsmoothed = stateB.brakePipePressureUnsmoothed = mean;
                }
            }

            if (partialCycle >= 0.01f)
            {
                for (int carIndex = 0; carIndex < states.Length - 1; carIndex++)
                {
                    var stateA = states[carIndex];
                    var pressureA = stateA.brakePipePressureUnsmoothed;
                    var stateB = states[carIndex + 1];
                    var pressureB = stateB.brakePipePressureUnsmoothed;
                    var mean = (pressureA + pressureB) / 2f;
                    stateA.brakePipePressureUnsmoothed = Mathf.Lerp(pressureA, mean, partialCycle);
                    stateB.brakePipePressureUnsmoothed = Mathf.Lerp(pressureB, mean, partialCycle);
                }
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
            var cylinderBrakingFactor = Mathf.InverseLerp(Main.settings.returnSpringStrength, Constants.FullApplicationPressure, state.cylinderPressure);
            var mechanicalBrakingFactor = GetMechanicalBrakeFactor(car);
            car.brakingFactor = Mathf.Max(mechanicalBrakingFactor, cylinderBrakingFactor);
        }

        private static void UpdateBrakePipeGauge(BrakeSystem car)
        {
            // AirBrake.DebugLog(car, $"before: BP={ExtraBrakeState.Instance(car).brakePipePressureUnsmoothed}, gauge={car.brakePipePressure}, vel={car.brakePipePressureRef}");
            car.brakePipePressure = Mathf.SmoothDamp(
                car.brakePipePressure,
                ExtraBrakeState.Instance(car).brakePipePressureUnsmoothed,
                ref car.brakePipePressureRef, 0.2f);
            // AirBrake.DebugLog(car, $"after: BP={ExtraBrakeState.Instance(car).brakePipePressureUnsmoothed}, gauge={car.brakePipePressure}, vel={car.brakePipePressureRef}");
        }

        private static void UpdateHUD(BrakeSystem car)
        {
            var state = ExtraBrakeState.Instance(car);
            if (CarTypes.IsAnyLocomotiveOrTender(car.GetTrainCar().carType))
                HeadsUpDisplayBridge.instance?.UpdateEqualizingReservoirPressure(car.GetTrainCar(), state.equalizingReservoirPressure);
            else
                HeadsUpDisplayBridge.instance?.UpdateAuxReservoirPressure(car.GetTrainCar(), state.auxReservoirPressure);
            HeadsUpDisplayBridge.instance?.UpdateBrakeCylinderPressure(car.GetTrainCar(), state.cylinderPressure);
        }

        public static bool IsSelfLap(TrainCarType carType)
        {
            return carType switch
            {
                TrainCarType.LocoShunter => Main.settings.shunterSelfLap,
                TrainCarType.LocoSteamHeavy => Main.settings.steamHeavySelfLap,
                TrainCarType.LocoDiesel => Main.settings.dieselSelfLap,
                _ => true,
            };
        }

        private static void UpdateTender(TrainCar tender)
        {
            var possibleLoco = tender.frontCoupler.coupledTo?.train;
            if (possibleLoco != null && CarTypes.IsSteamLocomotive(possibleLoco.carType))
                tender.brakeSystem.CylinderPressure() = possibleLoco.brakeSystem.CylinderPressure();
        }

        public static void Update(Brakeset brakeset, float dt)
        {
            AngleCocks.Update(brakeset, dt);
            BalanceBrakePipe(brakeset, dt);
            foreach (var car in brakeset.cars)
            {
                var trainCar = car.GetTrainCar();
                var carType = trainCar.carType;
                if (car.hasCompressor)
                {
                    RechargeMainReservoir(car, dt);

                    if (IsSelfLap(carType))
                        BrakeValve26L.Update(car, dt);
                    else
                        BrakeValve6ET.Update(car, dt);
                }
                else if (CarTypes.IsTender(carType))
                {
                    UpdateTender(trainCar);
                }
                else
                {
                    PlainTripleValve.Update(car, dt);
                }
                ApplyBrakingForce(car);
                UpdateBrakePipeGauge(car);
                UpdateHUD(car);
            }
        }

        [HarmonyPatch(typeof(Brakeset), nameof(Brakeset.Update))]
        public static class BrakesetUpdatePatch
        {
            public static bool Prefix()
            {
                if (!Main.enabled)
                    return true;
                Updater.Enabled = true;
                return false;
            }
        }

        public static void DebugLog(BrakeSystem car, string msg)
        {
            if (PlayerManager.Car?.brakeSystem == car)
                Main.DebugLog(msg);
        }
    }

    public class Updater : MonoBehaviour
    {
        private static GameObject? rootObject;
        public static bool Enabled
        {
            get => rootObject != null;
            set => SetEnabled(value);
        }

        private static void SetEnabled(bool enable)
        {
            if (enable && rootObject == null)
            {
                Main.DebugLog("Starting Updater");
                rootObject = new GameObject();
                rootObject.AddComponent<Updater>();
            }
            else if (!enable && rootObject != null)
            {
                Main.DebugLog("Stopping Updater");
                Destroy(rootObject);
                rootObject = null;
            }
        }

        public void FixedUpdate()
        {
            Assert.IsTrue(Main.enabled);
            foreach (var brakeset in Brakeset.allSets)
                AirBrake.Update(brakeset, Time.fixedDeltaTime);
        }
    }
}
