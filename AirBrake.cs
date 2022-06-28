using DV.Simulation.Brake;
using DvMod.AirBrake.Components;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;

namespace DvMod.AirBrake
{
    public static class Constants
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

        public const float BleedValveRate = 50f;
    }

    public class ExtraBrakeState
    {
        public float brakePipePressureUnsmoothed;
        public float cylinderPressure;

        public float equalizingReservoirPressure;
        // Part of bailoff control for 26F control valve.
        // Also used as the Application Chamber pressure in No. 6 distributing valve.
        public float controlReservoirPressure;

        // Type C 10" combined car equipment
        // Also used as the Pressure Chamber pressure in No. 6 distributing valve.
        public float auxReservoirPressure;
        public TripleValveMode tripleValveMode;

        private static readonly Cache<BrakeSystem, ExtraBrakeState> cache = new Cache<BrakeSystem, ExtraBrakeState>(_ => new ExtraBrakeState());
        public static ExtraBrakeState Instance(BrakeSystem system) => cache[system];

        public override string ToString()
        {
            return $"BP={brakePipePressureUnsmoothed},Cyl={cylinderPressure},EQ={equalizingReservoirPressure},Aux={auxReservoirPressure},tripleMode={tripleValveMode}";
        }

        private float[] FloatFields =>
            new float[] {
                brakePipePressureUnsmoothed,
                cylinderPressure,
                equalizingReservoirPressure,
                auxReservoirPressure,
            };

        public bool Valid
        {
            get => !FloatFields.Any(f => float.IsInfinity(f) || float.IsNaN(f));
        }

        public bool IsDefault
        {
            get => FloatFields.All(f => f < 1e-6f);
        }
    }

    public static class BrakeSystemExtensions
    {
        public static TrainCar GetTrainCar(this BrakeSystem brakeSystem) =>
            TrainCar.Resolve(brakeSystem.gameObject);

        public static ref float CylinderPressure(this BrakeSystem brakeSystem) =>
            ref ExtraBrakeState.Instance(brakeSystem).cylinderPressure;
    }

    public static class AirFlow
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
        private static void RechargeMainReservoir(BrakeSystem car, ExtraBrakeState state, float dt)
        {
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
            var range = states[0].brakePipePressureUnsmoothed > states.Last().brakePipePressureUnsmoothed
                ? Enumerable.Range(0, states.Length - 1).Reverse()
                : Enumerable.Range(0, states.Length - 1);

            for (int i = 0; i < fullCycles; i++)
            {
                foreach (int carIndex in range)
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
                foreach (int carIndex in range)
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

        private static void ApplyBrakingForce(BrakeSystem car, ExtraBrakeState state)
        {
            var cylinderBrakingFactor = Mathf.InverseLerp(Main.settings.returnSpringStrength, Constants.FullApplicationPressure, state.cylinderPressure);
            var mechanicalBrakingFactor = GetMechanicalBrakeFactor(car);
            car.brakingFactor = Mathf.Max(mechanicalBrakingFactor, cylinderBrakingFactor);
        }

        private static void AnimateBrakePads(BrakeSystem car, ExtraBrakeState state)
        {
            var train = car.GetTrainCar();
            if (train.GetComponent<TrainPhysicsLod>()?.currentLod > 1)
                return;

            const float StaticOffset = -0.005f;
            const float MaxDynamicOffset = 0.02f;
            var dynamicOffset = car.brakingFactor > 0 ? 0f
                : Mathf.Lerp(MaxDynamicOffset, 0f, Mathf.InverseLerp(0f, Main.settings.returnSpringStrength, state.cylinderPressure));
            foreach (var bogie in train.Bogies)
            {
                var brakeRoot = bogie.transform.Find("bogie_car/bogie2brakes");
                if (brakeRoot == null)
                    return;

                var frontPad = brakeRoot.Find("Bogie2Brakes01");
                if (frontPad == null)
                    return;
                var position = frontPad.localPosition;
                position.z = StaticOffset + dynamicOffset;
                frontPad.localPosition = position;
                // Main.DebugLog($"[{train.ID}] cyl={state.cylinderPressure}, dynamicOffset={dynamicOffset} set bogie2Brakes01 to {position.z}");

                var rearPad = brakeRoot.Find("Bogie2Brakes02");
                if (rearPad == null)
                    return;
                position = rearPad.localPosition;
                position.z = StaticOffset - dynamicOffset;
                rearPad.localPosition = position;
            }
        }

        private static void UpdateBrakePipeGauge(BrakeSystem car, ExtraBrakeState state)
        {
            // AirBrake.DebugLog(car, $"before: BP={ExtraBrakeState.Instance(car).brakePipePressureUnsmoothed}, gauge={car.brakePipePressure}, vel={car.brakePipePressureRef}");
            car.brakePipePressure = Mathf.SmoothDamp(
                car.brakePipePressure,
                state.brakePipePressureUnsmoothed,
                ref car.brakePipePressureRef, 0.2f);
            // AirBrake.DebugLog(car, $"after: BP={ExtraBrakeState.Instance(car).brakePipePressureUnsmoothed}, gauge={car.brakePipePressure}, vel={car.brakePipePressureRef}");
        }

        private static void UpdateHUD(TrainCar trainCar, ExtraBrakeState state)
        {
            if (CarTypes.IsAnyLocomotiveOrTender(trainCar.carType))
                HeadsUpDisplayBridge.instance?.UpdateEqualizingReservoirPressure(trainCar, state.equalizingReservoirPressure);
            else
                HeadsUpDisplayBridge.instance?.UpdateAuxReservoirPressure(trainCar, state.auxReservoirPressure);
            HeadsUpDisplayBridge.instance?.UpdateBrakeCylinderPressure(trainCar, state.cylinderPressure);
        }

        public static bool IsSelfLap(TrainCarType carType)
        {
            return Main.settings.selfLapCarIdentifiers.Contains(carType.DisplayName());
        }

        private static void UpdateTender(TrainCar tender)
        {
            var possibleLoco = tender.frontCoupler.coupledTo?.train;
            if (possibleLoco != null && CarTypes.IsSteamLocomotive(possibleLoco.carType))
                tender.brakeSystem.CylinderPressure() = possibleLoco.brakeSystem.CylinderPressure();
        }

        public static void Update(Brakeset brakeset, float dt)
        {
            if (brakeset.cars.Any(car => car.GetTrainCar() == null))
                return;
            static string GetCarID(BrakeSystem brake)
            {
                if (brake == null)
                    return "(BrakeSystem is null)";
                else if (!brake.GetTrainCar())
                    return "(TrainCar is null)";
                else
                    return brake.GetTrainCar().ID;
            }
            AngleCocks.Update(brakeset, dt);
            BalanceBrakePipe(brakeset, dt);
            foreach (var car in brakeset.cars)
            {
                if (car == null)
                {
                    Main.DebugLog("null BrakeSystem in brakeset");
                    Main.DebugLog($"brakeset: {string.Join(",", brakeset.cars.Select(GetCarID))}");
                    continue;
                }
                var trainCar = car.GetTrainCar();
                if (trainCar == null)
                {
                    Main.DebugLog("BrakeSystem without TrainCar in brakeset");
                    Main.DebugLog($"brakeset: {string.Join(",", brakeset.cars.Select(GetCarID))}");
                    continue;
                }
                var state = ExtraBrakeState.Instance(car);
                var carType = trainCar.carType;
                if (car.hasCompressor)
                {
                    RechargeMainReservoir(car, state, dt);

                    if (IsSelfLap(carType))
                        BrakeValve26L.Update(car, state, dt);
                    else
                        BrakeValve6ET.Update(car, state, dt);
                }
                else if (CarTypes.IsTender(carType))
                {
                    UpdateTender(trainCar);
                }
                else
                {
                    switch (Main.settings.tripleValveType)
                    {
                        case TripleValveType.KType: KTypeTripleValve.Update(car, state, dt); break;
                        case TripleValveType.Plain: PlainTripleValve.Update(car, state, dt); break;
                    }
                }
                ApplyBrakingForce(car, state);
                AnimateBrakePads(car, state);
                UpdateBrakePipeGauge(car, state);
                UpdateHUD(trainCar, state);
            }
        }

        public static bool IsManualReleasePressed()
        {
            return KeyCode.B.IsPressed() && (KeyCode.LeftShift.IsPressed() || KeyCode.RightShift.IsPressed());
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

        private static void CheckKeyInput()
        {
            var car = PlayerManager.Car;
            if (car == null)
                return;
            if (!AirBrake.IsManualReleasePressed())
                return;
            var brakeSystem = car.brakeSystem;
            var state = ExtraBrakeState.Instance(brakeSystem);
            var exhaustFlowTarget = 0f;
            exhaustFlowTarget += AirFlow.Vent(Time.fixedDeltaTime, ref state.cylinderPressure, Constants.BrakeCylinderVolume, Constants.BleedValveRate);
            brakeSystem.pipeExhaustFlow = Mathf.SmoothDamp(
                brakeSystem.pipeExhaustFlow,
                exhaustFlowTarget,
                ref brakeSystem.pipeExhaustFlowRef,
                0.2f);
        }

        public void FixedUpdate()
        {
            Assert.IsTrue(Main.enabled);
            foreach (var brakeset in Brakeset.allSets)
                AirBrake.Update(brakeset, Time.fixedDeltaTime);
            CheckKeyInput();
        }
    }
}
