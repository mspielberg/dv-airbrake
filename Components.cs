using DV.Simulation.Brake;
using UnityEngine;

namespace DvMod.AirBrake.Components
{
    static public class AngleCocks
    {
        static private void Update(BrakeSystem car, float dt)
        {
            foreach (var cock in new HoseAndCock[] { car.Front, car.Rear })
            {
                float rate = cock.IsOpenToAtmosphere
                    ? AirSystem.Vent(
                        dt,
                        ref car.brakePipePressure,
                        BrakeSystemConsts.PIPE_VOLUME) / BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE
                    : 0f;
                cock.exhaustFlow = Mathf.SmoothDamp(cock.exhaustFlow, rate, ref cock.exhaustFlowRef, 0.1f);
            }
        }

        static public void Update(Brakeset brakeset, float dt)
        {
            Update(brakeset.firstCar, dt);
            if (brakeset.firstCar != brakeset.lastCar)
                Update(brakeset.lastCar, dt);
        }
    }

    static public class PlainTripleValve
    {
        static public void Update(BrakeSystem car, float dt)
        {
            ExtraBrakeState state = ExtraBrakeState.Instance(car);
            if (car.brakePipePressure > state.auxReservoirPressure)
            {
                // RELEASE
                AirSystem.Vent(
                    dt,
                    ref state.cylinderPressure,
                    Constants.BRAKE_CYLINDER_VOLUME,
                    Main.settings.releaseSpeed);

                // CHARGE
                AirSystem.OneWayFlow(
                    dt,
                    ref car.brakePipePressure,
                    ref state.auxReservoirPressure,
                    BrakeSystemConsts.PIPE_VOLUME,
                    Constants.AUX_RESERVOIR_VOLUME,
                    Main.settings.chargeSpeed);
            }
            else if (car.brakePipePressure < state.auxReservoirPressure - Constants.ApplicationThreshold)
            {
                // APPLY
                AirSystem.Equalize(
                    dt,
                    ref state.auxReservoirPressure,
                    ref state.cylinderPressure,
                    Constants.AUX_RESERVOIR_VOLUME,
                    Constants.BRAKE_CYLINDER_VOLUME,
                    Main.settings.applySpeed);
            }
            else
            {
                // LAP
            }
        }
    }

    static public class BrakeValve26L {
        static private class BrakeValve26C
        {
            private const float RechargeSpeed = 10f;
            private static float Charge(BrakeSystem car, float dt)
            {
                return AirSystem.OneWayFlow(
                    dt,
                    ref car.mainReservoirPressureUnsmoothed,
                    ref car.brakePipePressure,
                    BrakeSystemConsts.RESERVOIR_VOLUME,
                    BrakeSystemConsts.PIPE_VOLUME,
                    RechargeSpeed);
            }

            private static float Vent(BrakeSystem car, float dt)
            {
                return AirSystem.Vent(dt, ref car.brakePipePressure, BrakeSystemConsts.PIPE_VOLUME, Main.settings.applySpeed);
            }

            public static (float, float) Update(BrakeSystem car, float dt)
            {
                var targetPressure = BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * (1f - car.trainBrakePosition);
                if (targetPressure > car.brakePipePressure)
                    return (Charge(car, dt), 0f);
                if (targetPressure < car.brakePipePressure - Constants.ApplicationThreshold)
                    return (0f, Vent(car, dt));
                return (0f, 0f);
            }
        }

        static private class BrakeValve26SA
        {
            private static float Charge(BrakeSystem car, float dt, float targetPressure)
            {
                // AirBrake.DebugLog(car, $"26SA.Charge");
                var state = ExtraBrakeState.Instance(car);
                var pressureRequested = targetPressure - state.cylinderPressure;
                var flowLimit = Mathf.Min(1f, pressureRequested);
                return AirSystem.OneWayFlow(
                    dt,
                    ref car.mainReservoirPressureUnsmoothed,
                    ref state.cylinderPressure,
                    BrakeSystemConsts.RESERVOIR_VOLUME,
                    car.brakeset.pipeVolume,
                    Main.settings.applySpeed,
                    flowLimit);
            }

            private static float Vent(BrakeSystem car, float dt)
            {
                // AirBrake.DebugLog(car, $"26SA.Vent");
                var state = ExtraBrakeState.Instance(car);
                return AirSystem.Vent(dt, ref state.cylinderPressure, BrakeSystemConsts.PIPE_VOLUME);
            }

            public static (float, float) Update(BrakeSystem car, float dt)
            {
                var state = ExtraBrakeState.Instance(car);
                var targetPressure = Mathf.Max(car.trainBrakePosition, car.independentBrakePosition) * Constants.MAX_CYLINDER_PRESSURE;
                if (targetPressure > state.cylinderPressure + Constants.ApplicationThreshold)
                    return (Charge(car, dt, targetPressure), 0f);
                if (targetPressure < state.cylinderPressure)
                    return (0f, Vent(car, dt));
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
