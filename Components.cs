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
                        Constants.BrakePipeVolume,
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

    public enum TripleValveType
    {
        Plain,
        KType,
    }

    public enum TripleValveMode
    {
        FullRelease,
        Service,
        ServiceLap,
        RetardedRelease,
        Emergency,
    }

    public static class TripleValveModeExtensions
    {
        public static char Abbrev(this TripleValveMode mode) => mode switch {
            TripleValveMode.FullRelease     => 'R',
            TripleValveMode.Service         => 'S',
            TripleValveMode.ServiceLap      => 'L',
            TripleValveMode.RetardedRelease => 'r',
            TripleValveMode.Emergency       => 'E',
            _                               => '?',
        };

        public static string FromAbbrev(char abbrev) => abbrev switch {
            'R' => "Full release",
            'S' => "Service",
            'L' => "Lap",
            'r' => "Retarded release",
            'E' => "Emergency",
            _   => "Unknown",
        };
    }

    public static class PlainTripleValve
    {
        private const float SlideThreshold = 0.10f;
        private const float GraduatingThreshold = 0.05f;

        public static void Update(ExtraBrakeState state, float dt)
        {
            var delta = state.brakePipePressureUnsmoothed - state.auxReservoirPressure;
            switch (state.tripleValveMode)
            {
                case TripleValveMode.FullRelease:
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
                        Constants.BrakePipeVolume,
                        Main.settings.chargeSpeed);

                    if (delta < -SlideThreshold)
                        state.tripleValveMode = TripleValveMode.Service;
                    return;

                case TripleValveMode.Service:
                    // APPLY
                    AirFlow.Equalize(
                        dt,
                        ref state.cylinderPressure,
                        ref state.auxReservoirPressure,
                        Constants.BrakeCylinderVolume,
                        Constants.AuxReservoirVolume,
                        Main.settings.applySpeed);

                    if (delta > GraduatingThreshold)
                        state.tripleValveMode = TripleValveMode.ServiceLap;
                    return;

                case TripleValveMode.ServiceLap:
                    if (delta < -GraduatingThreshold)
                        state.tripleValveMode = TripleValveMode.Service;
                    else if (delta > SlideThreshold)
                        state.tripleValveMode = TripleValveMode.FullRelease;
                    return;

                default:
                    state.tripleValveMode = TripleValveMode.ServiceLap;
                    return;
            }
        }
    }

    public static class KTypeTripleValve
    {
        private const float EmergencyThreshold = 1.0f;
        private const float RetardThreshold = 0.15f;
        private const float SlideThreshold = 0.10f;
        private const float GraduatingThreshold = 0.01f;
        private const float EmergencyMultiplier = 10f;

        public static void Update(ExtraBrakeState state, float dt)
        {
            var delta = state.brakePipePressureUnsmoothed - state.auxReservoirPressure;
            switch (state.tripleValveMode)
            {
                case TripleValveMode.FullRelease:
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
                        Constants.BrakePipeVolume,
                        Main.settings.chargeSpeed);

                    if (delta < -SlideThreshold)
                        state.tripleValveMode = TripleValveMode.Service;
                    else if (delta > RetardThreshold)
                        state.tripleValveMode = TripleValveMode.RetardedRelease;
                    return;

                case TripleValveMode.Service:
                    // APPLY (from brake pipe)
                    AirFlow.OneWayFlow(
                        dt,
                        ref state.cylinderPressure,
                        ref state.brakePipePressureUnsmoothed,
                        Constants.BrakeCylinderVolume,
                        Constants.BrakePipeVolume,
                        Main.settings.applySpeed * Main.settings.kTriplePipeDrainRate);

                    // APPLY (from auxiliary reservoir)
                    AirFlow.Equalize(
                        dt,
                        ref state.cylinderPressure,
                        ref state.auxReservoirPressure,
                        Constants.BrakeCylinderVolume,
                        Constants.AuxReservoirVolume,
                        Main.settings.applySpeed);

                    if (delta < -EmergencyThreshold)
                        state.tripleValveMode = TripleValveMode.Emergency;
                    else if (delta > GraduatingThreshold)
                        state.tripleValveMode = TripleValveMode.ServiceLap;
                    return;

                case TripleValveMode.ServiceLap:
                    if (delta < -GraduatingThreshold)
                        state.tripleValveMode = TripleValveMode.Service;
                    else if (delta > SlideThreshold)
                        state.tripleValveMode = TripleValveMode.FullRelease;
                    return;

                case TripleValveMode.RetardedRelease:
                    // RELEASE
                    AirFlow.Vent(
                        dt,
                        ref state.cylinderPressure,
                        Constants.BrakeCylinderVolume,
                        Main.settings.releaseSpeed * Main.settings.kTripleRetardedReleaseRate);

                    // CHARGE
                    AirFlow.OneWayFlow(
                        dt,
                        ref state.auxReservoirPressure,
                        ref state.brakePipePressureUnsmoothed,
                        Constants.AuxReservoirVolume,
                        Constants.BrakePipeVolume,
                        Main.settings.chargeSpeed * Main.settings.kTripleRetardedReleaseRate);

                    if (delta < RetardThreshold)
                        state.tripleValveMode = TripleValveMode.FullRelease;
                    return;

                case TripleValveMode.Emergency:
                    AirFlow.OneWayFlow(
                        dt,
                        ref state.cylinderPressure,
                        ref state.brakePipePressureUnsmoothed,
                        Constants.BrakeCylinderVolume,
                        Constants.BrakePipeVolume,
                        Main.settings.applySpeed * EmergencyMultiplier * EmergencyMultiplier);

                    AirFlow.Equalize(
                        dt,
                        ref state.cylinderPressure,
                        ref state.auxReservoirPressure,
                        Constants.BrakeCylinderVolume,
                        Constants.AuxReservoirVolume,
                        Main.settings.applySpeed * EmergencyMultiplier);

                    if (delta > -EmergencyThreshold)
                        state.tripleValveMode = TripleValveMode.Service;
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

            private static float Equalize(ExtraBrakeState state, float dt)
            {
                // AirBrake.DebugLog(car, $"Calling AirFlow.Vent with P={car.brakePipePressure}, minP={state.equalizingReservoirPressure}");
                return AirFlow.Vent(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    Main.settings.locoApplySpeed,
                    minPressure: state.equalizingReservoirPressure);
            }

            private static float Charge(BrakeSystem car, ExtraBrakeState state, float dt)
            {
                // AirBrake.DebugLog(car, $"BrakeValveH6.Charge");
                var massFlow = AirFlow.OneWayFlow(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    ref car.mainReservoirPressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    Constants.MainReservoirVolume,
                    Main.settings.locoRechargeSpeed,
                    Constants.MaxBrakePipePressure);
                massFlow += AirFlow.OneWayFlow(
                    dt,
                    ref state.equalizingReservoirPressure,
                    ref car.mainReservoirPressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    Constants.MainReservoirVolume,
                    Main.settings.locoRechargeSpeed,
                    Constants.MaxBrakePipePressure);
                return massFlow;
            }

            private static float Vent(ExtraBrakeState state, float dt)
            {
                // AirBrake.DebugLog(car, $"BrakeValveH6.Vent");
                return AirFlow.VentPressure(
                    dt,
                    ref state.equalizingReservoirPressure,
                    Constants.BrakePipeVolume,
                    ApplicationRate);
            }

            private static float EmergencyVent(ExtraBrakeState state, float dt)
            {
                // AirBrake.DebugLog(car, $"BrakeValveH6.EmergencyVent");
                return AirFlow.Vent(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    float.PositiveInfinity);
            }

            public static (float, float) Update(BrakeSystem car, ExtraBrakeState state, float dt)
            {
                var position = car.trainBrakePosition;
                // AirBrake.DebugLog(car, $"BrakeValveH6: position={position}");
                if (position < RunningPosition)
                {
                    return (Charge(car, state, dt), 0f);
                }
                else if (position < LapPosition)
                {
                    return (0f, Equalize(state, dt));
                }
                else if (position < ServicePosition)
                {
                    return (0f, Vent(state, dt) + Equalize(state, dt));
                }
                else
                {
                    return (0f, EmergencyVent(state, dt));
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

        public static void Update(BrakeSystem car, ExtraBrakeState state, float dt)
        {
            // AirBrake.DebugLog(car, $"BrakeValve6ET: initial BP={car.brakePipePressure}");
            var (mainChargeFlow, mainVentFlow) = BrakeValveH6.Update(car, state, dt);
            // AirBrake.DebugLog(car, $"BrakeValve6ET: after H6: BP={car.brakePipePressure}");
            // Equivalent to LA6-P independent brake valve
            var (independentChargeFlow, independentVentFlow) = BrakeValve26L.BrakeValve26SA.Update(car, state, dt);
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
            private static float Equalize(ExtraBrakeState state, float dt)
            {
                return AirFlow.Vent(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    Main.settings.locoApplySpeed,
                    minPressure: state.equalizingReservoirPressure);
            }

            private static float EmergencyVent(ExtraBrakeState state, float dt)
            {
                // AirBrake.DebugLog(car, $"BrakeValveH6.EmergencyVent");
                return AirFlow.Vent(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    float.PositiveInfinity);
            }

            private static float Charge(BrakeSystem car, ExtraBrakeState state, float dt, float targetPressure)
            {
                var massFlow = AirFlow.OneWayFlow(
                    dt,
                    ref state.brakePipePressureUnsmoothed,
                    ref car.mainReservoirPressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    Constants.MainReservoirVolume,
                    Main.settings.locoRechargeSpeed,
                    maxDestPressure: targetPressure);
                massFlow += AirFlow.OneWayFlow(
                    dt,
                    ref state.equalizingReservoirPressure,
                    ref car.mainReservoirPressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    Constants.MainReservoirVolume,
                    Main.settings.locoRechargeSpeed,
                    maxDestPressure: targetPressure);
                return massFlow;
            }

            private static float Vent(ExtraBrakeState state, float dt, float targetPressure)
            {
                return AirFlow.Vent(
                    dt,
                    ref state.equalizingReservoirPressure,
                    Constants.BrakePipeVolume,
                    minPressure: targetPressure);
            }

            private const float MinimumReduction = Constants.MaxBrakePipePressure - 0.35f;
            private static float TargetPressure(float trainBrakePosition)
            {
                if (trainBrakePosition < 0.01f)
                    return Constants.MaxBrakePipePressure;
                else
                    return Mathf.Lerp(MinimumReduction, 0f, (trainBrakePosition - 0.1f) / 0.9f);
            }

            public static (float, float) Update(BrakeSystem car, ExtraBrakeState state, float dt)
            {
                if (car.trainBrakePosition > 0.99f)
                    return (0f, EmergencyVent(state, dt) + Equalize(state, dt));
                var targetPressure = TargetPressure(car.trainBrakePosition);
                // AirBrake.DebugLog(car, $"target={targetPressure}, EQ={state.equalizingReservoirPressure}, BP={state.brakePipePressureUnsmoothed}");
                if (targetPressure > state.equalizingReservoirPressure || targetPressure > state.brakePipePressureUnsmoothed)
                    return (Charge(car, state, dt, targetPressure), Equalize(state, dt));
                if (targetPressure < state.equalizingReservoirPressure)
                    return (0f, Vent(state, dt, targetPressure) + Equalize(state, dt));
                return (0f, Equalize(state, dt));
            }
        }

        public static class BrakeValve26SA
        {
            private const float BailoffPositionLimit = 0.1f;
            private static float Charge(BrakeSystem car, ExtraBrakeState state, float dt, float targetPressure)
            {
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

            private static float Vent(ExtraBrakeState state, float dt, float targetPressure)
            {
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

            public static (float, float) Update(BrakeSystem car, ExtraBrakeState state, float dt)
            {
                var automaticTarget = Mathf.InverseLerp(Constants.MaxBrakePipePressure, Constants.FullApplicationPressure, car.brakePipePressure);
                // AirBrake.DebugLog(car, $"BP={car.brakePipePressure}, maxCyl={Constants.FullApplicationPressure}, automaticTarget={automaticTarget}");
                var independentTarget = Mathf.InverseLerp(BailoffPositionLimit * 2f, 1f, car.independentBrakePosition);
                var targetPressure = Mathf.Max(automaticTarget, independentTarget) * Constants.FullApplicationPressure;

                // AirBrake.DebugLog(car, $"BrakeValve26SA: handle={car.independentBrakePosition}, cylinder = {state.cylinderPressure}, target = {targetPressure}");
                if (car.independentBrakePosition < BailoffPositionLimit)
                    return (0f, Vent(state, dt, 0f));
                if (targetPressure < state.cylinderPressure - Constants.ApplicationThreshold)
                    return (0f, Vent(state, dt, targetPressure));
                if (targetPressure > state.cylinderPressure + Constants.ApplicationThreshold)
                    return (Charge(car, state, dt, targetPressure), 0f);
                return (0f, 0f);
            }
        }

        public static void Update(BrakeSystem car, ExtraBrakeState state, float dt)
        {
            var (mainChargeFlow, mainVentFlow) = BrakeValve26C.Update(car, state, dt);
            var (independentChargeFlow, independentVentFlow) = BrakeValve26SA.Update(car, state, dt);
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
