using DV.Simulation.Brake;
using UnityEngine;

namespace DvMod.AirBrake.Components
{
    static public class AngleCocks
    {
        static public void Update(BrakeSystem car, float dt)
        {
            foreach (var cock in new HoseAndCock[] { car.Front, car.Rear })
            {
                float atmospheric = 0f;
                float rate = cock.IsOpenToAtmosphere
                    ? AirSystem.Equalize(
                        dt,
                        ref car.brakePipePressure,
                        ref atmospheric,
                        BrakeSystemConsts.PIPE_VOLUME,
                        BrakeSystemConsts.ATMOSPHERE_VOLUME) / BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE
                    : 0f;
                cock.exhaustFlow = Mathf.SmoothDamp(cock.exhaustFlow, rate, ref cock.exhaustFlowRef, 0.1f);
            }
        }

        static public void Update(Brakeset brakeset, float dt)
        {
            AngleCocks.Update(brakeset.firstCar, dt);
            if (brakeset.firstCar != brakeset.lastCar)
                AngleCocks.Update(brakeset.lastCar, dt);
        }
    }

    static public class PlainTripleValve
    {
        private const float ApplicationThreshold = 0.05f;

        static public void Update(BrakeSystem car, float dt)
        {
            ExtraBrakeState state = ExtraBrakeState.Instance(car);
            if (car.brakePipePressure > state.auxReservoirPressure)
            {
                // RELEASE
                float atmosphere = 1f;
                state.EqualizeWheelCylinder(
                    dt,
                    ref atmosphere,
                    BrakeSystemConsts.ATMOSPHERE_VOLUME,
                    Main.settings.releaseSpeed);

                // CHARGE
                Brakeset.EqualizePressure(
                    ref state.auxReservoirPressure,
                    ref car.brakePipePressure,
                    Constants.AUX_RESERVOIR_VOLUME,
                    BrakeSystemConsts.PIPE_VOLUME,
                    BrakeSystemConsts.EQUALIZATION_SPEED_MULTIPLIER * Main.settings.chargeSpeed,
                    BrakeSystemConsts.EQUALIZATION_SPEED_LIMIT,
                    dt);
            }
            else if (car.brakePipePressure < state.auxReservoirPressure - ApplicationThreshold)
            {
                // APPLY
                state.EqualizeWheelCylinder(
                    dt,
                    ref state.auxReservoirPressure,
                    Constants.AUX_RESERVOIR_VOLUME,
                    Main.settings.applySpeed);
            }
            else
            {
                // LAP
            }
        }
    }

    static public class BrakeValve26C
    {
        private static void Charge(BrakeSystem car, float dt)
        {
            var flow = AirSystem.OneWayFlow(dt, ref car.mainReservoirPressureUnsmoothed, ref car.brakeset.pipePressure, BrakeSystemConsts.RESERVOIR_VOLUME, car.brakeset.pipeVolume, 1f);
            car.mainResToPipeFlow = Mathf.SmoothDamp(
                car.mainResToPipeFlow,
                flow,
                ref car.mainResToPipeFlowRef,
                0.2f);
        }

        private static void Vent(BrakeSystem car, float dt)
        {
            var flow = AirSystem.Vent(dt, ref car.brakePipePressure, BrakeSystemConsts.PIPE_VOLUME);
            car.pipeExhaustFlow = Mathf.SmoothDamp(
                car.pipeExhaustFlow,
                flow,
                ref car.pipeExhaustFlowRef,
                0.2f);
        }

        public static void Update(BrakeSystem car, float dt)
        {
            var targetPressure = BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * (1f - car.trainBrakePosition);
            if (targetPressure < car.brakePipePressure)
                Vent(car, dt);
            else
                Charge(car, dt);
        }
    }

    static public class BrakeValve26SA
    {
        public static void Update(BrakeSystem car, float dt)
        {
            var targetPressure = BrakeSystemConsts.MAX_BRAKE_PIPE_PRESSURE * (1f - car.independentBrakePosition);
            if (targetPressure < car.brakePipePressure)
                Vent(car, dt);
            else
                Charge(car, dt);
        }
    }
}