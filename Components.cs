using DV.Simulation.Brake;
using UnityEngine;

namespace DvMod.AirBrake.Components
{
    public static class AngleCocks
    {
        private const float VentRate = 10f;
        private static void Update(BrakeSystem car, float dt)
        {
            foreach (var cock in new HoseAndCock[] { car.Front, car.Rear })
            {
                float rate = cock.IsOpenToAtmosphere
                    ? AirFlow.Vent(
                        dt,
                        ref car.brakePipePressure,
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
        private const float ActivationThreshold = 0.1f;

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
                        ref car.brakePipePressure,
                        Constants.AUX_RESERVOIR_VOLUME,
                        BrakeSystemConsts.PIPE_VOLUME,
                        Main.settings.chargeSpeed);

                    if (car.brakePipePressure < state.auxReservoirPressure - ActivationThreshold)
                        state.tripleValveMode = Mode.Apply;
                    return;

                case Mode.Apply:
                    // APPLY
                    AirFlow.Equalize(
                        dt,
                        ref state.cylinderPressure,
                        ref state.auxReservoirPressure,
                        Constants.BrakeCylinderVolume,
                        Constants.AUX_RESERVOIR_VOLUME,
                        Main.settings.applySpeed);

                    if (car.brakePipePressure > state.auxReservoirPressure + ActivationThreshold)
                        state.tripleValveMode = Mode.Lap;
                    return;

                case Mode.Lap:
                    if (car.brakePipePressure < state.auxReservoirPressure - (ActivationThreshold * 2))
                        state.tripleValveMode = Mode.Apply;
                    else if (car.brakePipePressure > state.auxReservoirPressure + (ActivationThreshold * 2))
                        state.tripleValveMode = Mode.Charge;
                    return;
            }
        }
    }

    public static class BrakeValve26L
    {
        private static class BrakeValve26C
        {
            private const float RechargeSpeed = 10f;
            private static float Charge(BrakeSystem car, float dt, float targetPressure)
            {
                var massFlow = AirFlow.OneWayFlow(
                    dt,
                    ref car.brakePipePressure,
                    ref car.mainReservoirPressureUnsmoothed,
                    BrakeSystemConsts.PIPE_VOLUME,
                    BrakeSystemConsts.RESERVOIR_VOLUME,
                    RechargeSpeed,
                    targetPressure - car.brakePipePressure);
                return massFlow;
            }

            private static float Vent(BrakeSystem car, float dt, float targetPressure)
            {
                return AirFlow.Vent(
                    dt,
                    ref car.brakePipePressure,
                    BrakeSystemConsts.PIPE_VOLUME,
                    Main.settings.applySpeed,
                    car.brakePipePressure - targetPressure);
            }

            public static (float, float) Update(BrakeSystem car, float dt)
            {
                var targetPressure = BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * (1f - car.trainBrakePosition);
                if (targetPressure > car.brakePipePressure + Constants.ApplicationThreshold)
                    return (Charge(car, dt, targetPressure), 0f);
                if (targetPressure < car.brakePipePressure - Constants.ApplicationThreshold)
                    return (0f, Vent(car, dt, targetPressure));
                return (0f, 0f);
            }
        }

        static private class BrakeValve26SA
        {
            private const float BailoffPositionLimit = 0.1f;
            private static float Charge(BrakeSystem car, float dt, float targetPressure)
            {
                var state = ExtraBrakeState.Instance(car);
                AirBrake.DebugLog(car, $"26SA.Charge before: cylinder={state.cylinderPressure}");
                float massFlow = AirFlow.OneWayFlow(
                    dt,
                    ref state.cylinderPressure,
                    ref car.mainReservoirPressureUnsmoothed,
                    Constants.BrakeCylinderVolume,
                    BrakeSystemConsts.RESERVOIR_VOLUME,
                    Main.settings.applySpeed,
                    targetPressure - state.cylinderPressure);
                AirBrake.DebugLog(car, $"26SA.Charge after: cylinder={state.cylinderPressure}");
                return massFlow;
            }

            private static float Vent(BrakeSystem car, float dt, float targetPressure)
            {
                var state = ExtraBrakeState.Instance(car);
                AirBrake.DebugLog(car, $"26SA.Vent before: cylinder={state.cylinderPressure}");
                float massFlow = AirFlow.Vent(
                    dt,
                    ref state.cylinderPressure,
                    Constants.BrakeCylinderVolume,
                    pressureChangeLimit: state.cylinderPressure - targetPressure);
                AirBrake.DebugLog(car, $"26SA.Vent after: cylinder={state.cylinderPressure}");
                return massFlow;
            }

            public static (float, float) Update(BrakeSystem car, float dt)
            {
                var state = ExtraBrakeState.Instance(car);
                var automaticTarget = Mathf.InverseLerp(BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE, Constants.FullApplicationPressure, car.brakePipePressure);
                // AirBrake.DebugLog(car, $"BP={car.brakePipePressure}, maxCyl={Constants.MAX_CYLINDER_PRESSURE}, automaticTarget={automaticTarget}");
                var independentTarget = Mathf.InverseLerp(BailoffPositionLimit * 2f, 1f, car.independentBrakePosition);
                var targetPressure = Mathf.Max(automaticTarget, independentTarget) * Constants.FullApplicationPressure;

                AirBrake.DebugLog(car, $"handle={car.independentBrakePosition}, cylinder = {state.cylinderPressure}, target = {targetPressure}");
                if (car.independentBrakePosition < BailoffPositionLimit)
                    return (0f, Vent(car, dt, 0f));
                if (targetPressure < state.cylinderPressure - Constants.ApplicationThreshold)
                    return (0f, Vent(car, dt, targetPressure));
                if (targetPressure > state.cylinderPressure + Constants.ApplicationThreshold)
                    return (Charge(car, dt, targetPressure), 0f);
                return (0f, 0f);
            }
        }

        static public void Update(BrakeSystem car, float dt)
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
