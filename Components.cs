using DV.Simulation.Brake;
using UnityEngine;

namespace DvMod.AirBrake.Components
{
    public static class AngleCocks
    {
        private const float VentRate = 100f;
        private static void Update(BrakeSystem car, float dt)
        {
            var state = ExtraBrakeState.Instance(car);
            foreach (var cock in new HoseAndCock[] { car.Front, car.Rear })
            {
                float rate = cock.IsOpenToAtmosphere
                    ? AirFlow.Vent(
                        dt,
                        ref state.brakePipePressureUnsmoothed,
                        BrakeSystemConsts.PIPE_VOLUME,
                        VentRate)
                    : 0f;
                // AirBrake.DebugLog(car, $"cockOpen={cock.IsOpenToAtmosphere}, ventRate={rate}");
                cock.exhaustFlow = Mathf.SmoothDamp(cock.exhaustFlow, rate, ref cock.exhaustFlowRef, 0.1f);
            }
        }

        public static void Update(Brakeset brakeset, float dt)
        {
            Update(brakeset.firstCar, dt);
            if (brakeset.firstCar != brakeset.lastCar)
                Update(brakeset.lastCar, dt);
            foreach (BrakeSystem car in brakeset.cars)
            {
                car.Front.UpdatePressurized();
                car.Rear.UpdatePressurized();
            }
        }
    }

    public static class PlainTripleValve
    {
        private const float ActivationThreshold = 0.05f;

        public enum Mode
        {
            Charge,
            Apply,
            Lap,
        }

        public static void Update(BrakeSystem car, float dt)
        {
            ExtraBrakeState state = ExtraBrakeState.Instance(car);
            switch (state.tripleValveMode)
            {
                case Mode.Charge:
                    // RELEASE
                    AirFlow.Vent(
                        dt,
                        ref state.cylinderPressure,
                        Constants.BrakeCylinderVolume,
                        Main.settings.releaseSpeed);

                    // CHARGE
                    AirFlow.OneWayFlow(
                        dt,
                        ref state.auxReservoirPressure,
                        ref state.brakePipePressureUnsmoothed,
                        Constants.AuxReservoirVolume,
                        BrakeSystemConsts.PIPE_VOLUME,
                        Main.settings.chargeSpeed);

                    if (state.brakePipePressureUnsmoothed < state.auxReservoirPressure - ActivationThreshold)
                        state.tripleValveMode = Mode.Apply;
                    return;

                case Mode.Apply:
                    // APPLY
                    AirFlow.Equalize(
                        dt,
                        ref state.cylinderPressure,
                        ref state.auxReservoirPressure,
                        Constants.BrakeCylinderVolume,
                        Constants.AuxReservoirVolume,
                        Main.settings.applySpeed);

                    if (state.brakePipePressureUnsmoothed > state.auxReservoirPressure + ActivationThreshold)
                        state.tripleValveMode = Mode.Lap;
                    return;

                case Mode.Lap:
                    if (state.brakePipePressureUnsmoothed < state.auxReservoirPressure - (ActivationThreshold * 2))
                        state.tripleValveMode = Mode.Apply;
                    else if (state.brakePipePressureUnsmoothed > state.auxReservoirPressure + (ActivationThreshold * 2))
                        state.tripleValveMode = Mode.Charge;
                    return;
            }
        }
    }

    public static class BrakeValve6ET
    {
        private const float RunningPosition = 0.1f;
        private const float LapPosition = 0.5f;
        private const float ServicePosition = 0.9f;

        private static class BrakeValveH6
        {
            private const float ApplicationRate = 0.175f; // ~= 2.5 psi/s

            private static float Equalize(BrakeSystem car, float dt)
            {
                var state = ExtraBrakeState.Instance(car);
                // AirBrake.DebugLog(car, $"Calling AirFlow.Vent with P={car.brakePipePressure}, minP={state.equalizingReservoirPressure}");
                return AirFlow.Vent(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    BrakeSystemConsts.PIPE_VOLUME,
                    Main.settings.locoApplySpeed,
                    minPressure: state.equalizingReservoirPressure);
            }

            private static float Charge(BrakeSystem car, float dt)
            {
                // AirBrake.DebugLog(car, $"BrakeValveH6.Charge");
                var state = ExtraBrakeState.Instance(car);
                var massFlow = AirFlow.OneWayFlow(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    ref car.mainReservoirPressureUnsmoothed,
                    BrakeSystemConsts.PIPE_VOLUME,
                    Constants.MainReservoirVolume,
                    Main.settings.locoRechargeSpeed,
                    Constants.MaxBrakePipePressure);
                massFlow += AirFlow.OneWayFlow(
                    dt,
                    ref state.equalizingReservoirPressure,
                    ref car.mainReservoirPressureUnsmoothed,
                    BrakeSystemConsts.PIPE_VOLUME,
                    Constants.MainReservoirVolume,
                    Main.settings.locoRechargeSpeed,
                    Constants.MaxBrakePipePressure);
                return massFlow;
            }

            private static float Vent(BrakeSystem car, float dt)
            {
                // AirBrake.DebugLog(car, $"BrakeValveH6.Vent");
                var state = ExtraBrakeState.Instance(car);
                return AirFlow.VentPressure(
                    dt,
                    ref state.equalizingReservoirPressure,
                    BrakeSystemConsts.PIPE_VOLUME,
                    ApplicationRate);
            }

            private static float EmergencyVent(BrakeSystem car, float dt)
            {
                var state = ExtraBrakeState.Instance(car);
                // AirBrake.DebugLog(car, $"BrakeValveH6.EmergencyVent");
                return AirFlow.Vent(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    BrakeSystemConsts.PIPE_VOLUME);
            }

            public static (float, float) Update(BrakeSystem car, float dt)
            {
                var position = car.trainBrakePosition;
                // AirBrake.DebugLog(car, $"BrakeValveH6: position={position}");
                if (position < RunningPosition)
                {
                    return (Charge(car, dt), 0f);
                }
                else if (position < LapPosition)
                {
                    return (0f, Equalize(car, dt));
                }
                else if (position < ServicePosition)
                {
                    return (0f, Vent(car, dt) + Equalize(car, dt));
                }
                else
                {
                    return (0f, EmergencyVent(car, dt));
                }
            }
        }

        public static int Mode(BrakeSystem car)
        {
            var position = car.trainBrakePosition;
            if (position < RunningPosition)
                return 2;
            else if (position < LapPosition)
                return 3;
            else if (position < ServicePosition)
                return 4;
            else
                return 5;
        }

        public static void Update(BrakeSystem car, float dt)
        {
            // AirBrake.DebugLog(car, $"BrakeValve6ET: initial BP={car.brakePipePressure}");
            var (mainChargeFlow, mainVentFlow) = BrakeValveH6.Update(car, dt);
            // AirBrake.DebugLog(car, $"BrakeValve6ET: after H6: BP={car.brakePipePressure}");
            // Equivalent to LA6-P independent brake valve
            var (independentChargeFlow, independentVentFlow) = BrakeValve26L.BrakeValve26SA.Update(car, dt);
            // AirBrake.DebugLog(car, $"BrakeValve6ET: after 26SA: BP={car.brakePipePressure}");
            car.mainResToPipeFlow = Mathf.SmoothDamp(
                car.mainResToPipeFlow,
                mainChargeFlow + independentChargeFlow,
                ref car.mainResToPipeFlowRef,
                0.2f);
            car.pipeExhaustFlow = Mathf.SmoothDamp(
                car.pipeExhaustFlow,
                mainVentFlow + independentVentFlow,
                ref car.pipeExhaustFlowRef,
                0.2f);
        }
    }

    public static class BrakeValve26L
    {
        private static class BrakeValve26C
        {
            private static float Equalize(BrakeSystem car, float dt)
            {
                var state = ExtraBrakeState.Instance(car);
                return AirFlow.Vent(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    BrakeSystemConsts.PIPE_VOLUME,
                    Main.settings.locoApplySpeed,
                    minPressure: state.equalizingReservoirPressure);
            }

            private static float Charge(BrakeSystem car, float dt, float targetPressure)
            {
                var state = ExtraBrakeState.Instance(car);
                var massFlow = AirFlow.OneWayFlow(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    ref car.mainReservoirPressureUnsmoothed,
                    BrakeSystemConsts.PIPE_VOLUME,
                    Constants.MainReservoirVolume,
                    Main.settings.locoRechargeSpeed,
                    maxDestPressure: targetPressure);
                massFlow += AirFlow.OneWayFlow(
                    dt,
                    ref state.equalizingReservoirPressure,
                    ref car.mainReservoirPressureUnsmoothed,
                    BrakeSystemConsts.PIPE_VOLUME,
                    Constants.MainReservoirVolume,
                    Main.settings.locoRechargeSpeed,
                    maxDestPressure: targetPressure);
                return massFlow;
            }

            private static float Vent(BrakeSystem car, float dt, float targetPressure)
            {
                var state = ExtraBrakeState.Instance(car);
                return AirFlow.Vent(
                    dt,
                    ref state.equalizingReservoirPressure,
                    BrakeSystemConsts.PIPE_VOLUME,
                    minPressure: targetPressure);
            }

            public static (float, float) Update(BrakeSystem car, float dt)
            {
                var state = ExtraBrakeState.Instance(car);
                var targetPressure = Constants.MaxBrakePipePressure * (1f - car.trainBrakePosition);
                // AirBrake.DebugLog(car, $"target={targetPressure}, EQ={state.equalizingReservoirPressure}, BP={state.brakePipePressureUnsmoothed}");
                if (targetPressure > state.equalizingReservoirPressure || targetPressure > state.brakePipePressureUnsmoothed)
                    return (Charge(car, dt, targetPressure), Equalize(car, dt));
                if (targetPressure < state.equalizingReservoirPressure)
                    return (0f, Vent(car, dt, targetPressure) + Equalize(car, dt));
                return (0f, Equalize(car, dt));
            }
        }

        public static class BrakeValve26SA
        {
            private const float BailoffPositionLimit = 0.1f;
            private static float Charge(BrakeSystem car, float dt, float targetPressure)
            {
                var state = ExtraBrakeState.Instance(car);
                // AirBrake.DebugLog(car, $"26SA.Charge before: cylinder={state.cylinderPressure}");
                float massFlow = AirFlow.OneWayFlow(
                    dt,
                    ref state.cylinderPressure,
                    ref car.mainReservoirPressureUnsmoothed,
                    Constants.BrakeCylinderVolume,
                    Constants.MainReservoirVolume,
                    Main.settings.locoApplySpeed,
                    targetPressure);
                // AirBrake.DebugLog(car, $"26SA.Charge after: cylinder={state.cylinderPressure}");
                return massFlow;
            }

            private static float Vent(BrakeSystem car, float dt, float targetPressure)
            {
                var state = ExtraBrakeState.Instance(car);
                // AirBrake.DebugLog(car, $"26SA.Vent before: cylinder={state.cylinderPressure}");
                float massFlow = AirFlow.Vent(
                    dt,
                    ref state.cylinderPressure,
                    Constants.BrakeCylinderVolume,
                    Main.settings.locoReleaseSpeed,
                    minPressure: targetPressure);
                // AirBrake.DebugLog(car, $"26SA.Vent after: cylinder={state.cylinderPressure}");
                return massFlow;
            }

            public static (float, float) Update(BrakeSystem car, float dt)
            {
                var state = ExtraBrakeState.Instance(car);
                var automaticTarget = Mathf.InverseLerp(Constants.MaxBrakePipePressure, Constants.FullApplicationPressure, car.brakePipePressure);
                // AirBrake.DebugLog(car, $"BP={car.brakePipePressure}, maxCyl={Constants.FullApplicationPressure}, automaticTarget={automaticTarget}");
                var independentTarget = Mathf.InverseLerp(BailoffPositionLimit * 2f, 1f, car.independentBrakePosition);
                var targetPressure = Mathf.Max(automaticTarget, independentTarget) * Constants.FullApplicationPressure;

                // AirBrake.DebugLog(car, $"BrakeValve26SA: handle={car.independentBrakePosition}, cylinder = {state.cylinderPressure}, target = {targetPressure}");
                if (car.independentBrakePosition < BailoffPositionLimit)
                    return (0f, Vent(car, dt, 0f));
                if (targetPressure < state.cylinderPressure - Constants.ApplicationThreshold)
                    return (0f, Vent(car, dt, targetPressure));
                if (targetPressure > state.cylinderPressure + Constants.ApplicationThreshold)
                    return (Charge(car, dt, targetPressure), 0f);
                return (0f, 0f);
            }
        }

        public static void Update(BrakeSystem car, float dt)
        {
            var (mainChargeFlow, mainVentFlow) = BrakeValve26C.Update(car, dt);
            var (independentChargeFlow, independentVentFlow) = BrakeValve26SA.Update(car, dt);
            car.mainResToPipeFlow = Mathf.SmoothDamp(
                car.mainResToPipeFlow,
                mainChargeFlow + independentChargeFlow,
                ref car.mainResToPipeFlowRef,
                0.2f);
            car.pipeExhaustFlow = Mathf.SmoothDamp(
                car.pipeExhaustFlow,
                mainVentFlow + independentVentFlow,
                ref car.pipeExhaustFlowRef,
                0.2f);
        }
    }
}
