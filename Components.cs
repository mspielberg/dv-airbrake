using DV.MultipleUnit;
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
        KnorrKE,
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
        public static char Abbrev(this TripleValveMode mode) => mode switch
        {
            TripleValveMode.FullRelease => 'R',
            TripleValveMode.Service => 'S',
            TripleValveMode.ServiceLap => 'L',
            TripleValveMode.RetardedRelease => 'r',
            TripleValveMode.Emergency => 'E',
            _ => '?',
        };

        public static string FromAbbrev(char abbrev) => abbrev switch
        {
            'R' => "Full release",
            'S' => "Service",
            'L' => "Lap",
            'r' => "Retarded release",
            'E' => "Emergency",
            _ => "Unknown",
        };
    }

    public static class PlainTripleValve
    {
        private const float SlideThreshold = 0.10f;
        private const float GraduatingThreshold = 0.05f;

        public static (float, float) Update(BrakeSystem car, ExtraBrakeState state, float dt)
        {
            var delta = state.brakePipePressureUnsmoothed - state.auxReservoirPressure;
            float exhaustFlowTarget = 0f;
            float applyFlow = 0f;
            switch (state.tripleValveMode)
            {
                case TripleValveMode.FullRelease:
                    // RELEASE
                    exhaustFlowTarget = AirFlow.Vent(
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
                    break;

                case TripleValveMode.Service:
                    // APPLY
                    applyFlow = AirFlow.Equalize(
                        dt,
                        ref state.cylinderPressure,
                        ref state.auxReservoirPressure,
                        Constants.BrakeCylinderVolume,
                        Constants.AuxReservoirVolume,
                        Main.settings.applySpeed);

                    if (delta > GraduatingThreshold)
                        state.tripleValveMode = TripleValveMode.ServiceLap;
                    break;

                case TripleValveMode.ServiceLap:
                    if (delta < -GraduatingThreshold)
                        state.tripleValveMode = TripleValveMode.Service;
                    else if (delta > SlideThreshold)
                        state.tripleValveMode = TripleValveMode.FullRelease;
                    break;

                default:
                    state.tripleValveMode = TripleValveMode.ServiceLap;
                    break;
            }
            car.pipeExhaustFlow = Mathf.SmoothDamp(
                car.pipeExhaustFlow,
                exhaustFlowTarget,
                ref car.pipeExhaustFlowRef,
                0.2f);
            return (car.pipeExhaustFlow, applyFlow);
        }
    }

    public static class KTypeTripleValve
    {
        private const float EmergencyThreshold = 1.0f;
        private const float RetardThreshold = 0.15f;
        private const float SlideThreshold = 0.10f;
        private const float GraduatingThreshold = 0.01f;
        private const float EmergencyMultiplier = 50f;

        public static void Update(BrakeSystem car, ExtraBrakeState state, float dt)
        {
            var delta = state.brakePipePressureUnsmoothed - state.auxReservoirPressure;
            float exhaustFlowTarget = 0f;
            switch (state.tripleValveMode)
            {
                case TripleValveMode.FullRelease:
                    // RELEASE
                    exhaustFlowTarget = AirFlow.Vent(
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
                    break;

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
                    break;

                case TripleValveMode.ServiceLap:
                    if (delta < -GraduatingThreshold)
                        state.tripleValveMode = TripleValveMode.Service;
                    else if (delta > SlideThreshold)
                        state.tripleValveMode = TripleValveMode.FullRelease;
                    break;

                case TripleValveMode.RetardedRelease:
                    // RELEASE
                    exhaustFlowTarget = AirFlow.Vent(
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
                    break;

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
                    break;
            }
            car.pipeExhaustFlow = Mathf.SmoothDamp(
                car.pipeExhaustFlow,
                exhaustFlowTarget,
                ref car.pipeExhaustFlowRef,
                0.2f);
        }
    }

    public static class KnorrKEControlValve
    {
        private const float ControlChamberVolume = 2.5f;
        private const float CommunicationChamberVolume = 0.5f;
        private const float SlowFillThreshold = 0.3f;
        private const float FillThreshold = 0.05f;
        private const float VentThreshold = 0.01f;

        public static void Update(BrakeSystem car, ExtraBrakeState state, float dt)
        {
            // charge aux reservoir only if control chamber is fully charged
            if (state.controlReservoirPressure + FillThreshold >= state.brakePipePressureUnsmoothed)
            {
                AirFlow.OneWayFlow(
                    dt,
                    ref state.auxReservoirPressure,
                    ref state.brakePipePressureUnsmoothed,
                    Constants.AuxReservoirVolume,
                    Constants.BrakePipeVolume,
                    state.controlReservoirPressure - state.auxReservoirPressure < SlowFillThreshold
                        ? 0.25f : 1f);
            }

            // charge control reservoir with reference (maximum brake pipe) pressure
            AirFlow.OneWayFlow(
                dt,
                ref state.controlReservoirPressure,
                ref state.brakePipePressureUnsmoothed,
                ControlChamberVolume,
                Constants.BrakePipeVolume);

            float exhaustFlowTarget = 0;
            float delta = state.controlReservoirPressure - state.brakePipePressureUnsmoothed - state.cylinderPressure / Constants.CylinderScaleFactor;
            if (delta < VentThreshold)
            {
                exhaustFlowTarget = AirFlow.Vent(
                    dt,
                    ref state.cylinderPressure,
                    Constants.BrakeCylinderVolume,
                    Main.settings.releaseSpeed);
                state.tripleValveMode = TripleValveMode.FullRelease;
            }
            else if (delta > FillThreshold)
            {
                AirFlow.OneWayFlow(
                    dt,
                    ref state.cylinderPressure,
                    ref state.auxReservoirPressure,
                    Constants.BrakeCylinderVolume,
                    Constants.AuxReservoirVolume,
                    Main.settings.applySpeed);

                // take from brake pipe to speed propagation
                AirFlow.OneWayFlow(
                    dt,
                    ref state.communicationChamberPressure,
                    ref state.brakePipePressureUnsmoothed,
                    CommunicationChamberVolume,
                    Constants.BrakePipeVolume);
                state.tripleValveMode = TripleValveMode.Service;
            }
            else
            {
                state.tripleValveMode = TripleValveMode.ServiceLap;
            }

            // prepare to take from brake pipe next application after full release (BP reaches max pressure)
            if (state.brakePipePressureUnsmoothed >= state.controlReservoirPressure - VentThreshold)
            {
                AirFlow.Vent(
                    dt,
                    ref state.communicationChamberPressure,
                    CommunicationChamberVolume);
            }

            car.pipeExhaustFlow = Mathf.SmoothDamp(
                car.pipeExhaustFlow,
                exhaustFlowTarget,
                ref car.pipeExhaustFlowRef,
                0.2f);
        }
    }

    public static class BrakeValve6ET
    {
        public static class BrakeValveH6
        {
            private const float RunningPosition = 0.1f;
            private const float LapPosition = 0.5f;
            private const float ServicePosition = 0.9f;

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
        }

        public static class BrakeValveS6
        {
            private const int NumNotches = 5;
            private const float NotchStride = 1f / (NumNotches - 1);
            private const float NotchOffset = NotchStride / 2;
            private const float ReleasePosition = NotchOffset + 0 * NotchStride;
            private const float RunningPosition = NotchOffset + 1 * NotchStride;
            private const float LapPosition = NotchOffset + 2 * NotchStride;
            private const float SlowApplyPosition = NotchOffset + 3 * NotchStride;

            private const float MaxPressure = 3.1f; // 45 psi

            private static float Apply(BrakeSystem car, ExtraBrakeState state, float rate, float dt)
            {
                float massFlow = AirFlow.OneWayFlow(
                    dt,
                    ref state.cylinderPressure,
                    ref car.mainReservoirPressureUnsmoothed,
                    Constants.BrakeCylinderVolume,
                    Constants.MainReservoirVolume,
                    rate,
                    MaxPressure);
                return massFlow;
            }

            private static float Vent(ExtraBrakeState state, float dt)
            {
                float massFlow = AirFlow.Vent(
                    dt,
                    ref state.cylinderPressure,
                    Constants.BrakeCylinderVolume,
                    Main.settings.locoReleaseSpeed);
                return massFlow;
            }

            public static int Mode(BrakeSystem car)
            {
                var position = car.independentBrakePosition;
                if (position < ReleasePosition)
                    return 2;
                if (position < RunningPosition)
                    return 3;
                else if (position < LapPosition)
                    return 4;
                else if (position < SlowApplyPosition)
                    return 5;
                else
                    return 6;
            }

            public static (float, float) Update(BrakeSystem car, ExtraBrakeState state, float dt)
            {
                if (car.independentBrakePosition < ReleasePosition)
                {
                    return (Vent(state, dt), 0f);
                }
                else if (car.independentBrakePosition < RunningPosition)
                {
                    return PlainTripleValve.Update(car, state, dt);
                }
                else if (car.independentBrakePosition < LapPosition)
                {
                    return (0f, 0f);
                }
                else if (car.independentBrakePosition < SlowApplyPosition)
                {
                    return (0f, Apply(car, state, Main.settings.locoApplySpeed / 10f, dt));
                }
                else
                {
                    return (0f, Apply(car, state, Main.settings.locoApplySpeed / 2f, dt));
                }
            }
        }

        public static void Update(BrakeSystem car, ExtraBrakeState state, float dt)
        {
            // AirBrake.DebugLog(car, $"BrakeValve6ET: initial BP={car.brakePipePressure}");
            var (mainChargeFlow, mainVentFlow) = BrakeValveH6.Update(car, state, dt);
            // AirBrake.DebugLog(car, $"BrakeValve6ET: after H6: BP={car.brakePipePressure}");
            // Equivalent to LA6-P independent brake valve
            var (independentChargeFlow, independentVentFlow) = BrakeValveS6.Update(car, state, dt);
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

            private const float MinimumReduction = Constants.MaxBrakePipePressure - 0.4f;
            private const float Curve = 2f;
            private static float TargetPressure(float trainBrakePosition)
            {
                if (trainBrakePosition < 0.01f)
                    return Constants.MaxBrakePipePressure;
                else
                    return Mathf.Lerp(MinimumReduction, 0f, Mathf.Pow(trainBrakePosition, Curve));
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

            private static float Bailoff(ExtraBrakeState state, float dt)
            {
                // AirBrake.DebugLog(car, $"26SA.Vent before: cylinder={state.cylinderPressure}");
                float massFlow = AirFlow.Vent(
                    dt,
                    ref state.cylinderPressure,
                    Constants.BrakeCylinderVolume,
                    Main.settings.locoReleaseSpeed);
                massFlow += AirFlow.Vent(
                    dt,
                    ref state.controlReservoirPressure,
                    Constants.BrakePipeVolume,
                    Main.settings.locoReleaseSpeed,
                    state.brakePipePressureUnsmoothed);
                // AirBrake.DebugLog(car, $"26SA.Vent after: cylinder={state.cylinderPressure}");
                return massFlow;
            }

            private static bool IsConnectedToPlayerLoco(BrakeSystem brakeSystem)
            {
                var car = brakeSystem.GetTrainCar();
                var playerTrain = PlayerManager.Car;
                if (playerTrain == car)
                    return true; // player in this car
                if (playerTrain != PlayerManager.LastLoco)
                    return false; // player not in a locomotive
                var playerTrainGO = playerTrain.gameObject;

                static MultipleUnitCable? Next(MultipleUnitCable cable)
                {
                    var counterpartCable = cable.connectedTo;
                    if (counterpartCable == null)
                        return default;
                    var counterpartModule = counterpartCable.muModule;
                    return counterpartCable.isFront ? counterpartModule.rearCable : counterpartModule.frontCable;
                }

                var muModule = car.GetComponent<MultipleUnitModule>();
                if (!muModule)
                    return false;
                foreach (var origin in new MultipleUnitCable[] { muModule.frontCable, muModule.rearCable })
                {
                    for (var cable = origin; cable != null; cable = Next(cable))
                    {
                        if (cable.muModule.gameObject == playerTrainGO)
                            return true;
                    }
                }
                return false;
            }

            public static (float, float) Update(BrakeSystem car, ExtraBrakeState state, float dt)
            {
                var controlChargeFlow = AirFlow.OneWayFlow(
                    dt,
                    ref state.controlReservoirPressure,
                    ref state.brakePipePressureUnsmoothed,
                    Constants.BrakePipeVolume,
                    Constants.BrakePipeVolume,
                    Main.settings.locoApplySpeed);
                var automaticTarget = Mathf.Clamp01(
                    (state.controlReservoirPressure - state.brakePipePressureUnsmoothed) /
                    (Constants.MaxBrakePipePressure - Constants.FullApplicationPressure));
                // AirBrake.DebugLog(car, $"BrakeValve26SA: control = {state.controlReservoirPressure},BP = {state.brakePipePressureUnsmoothed},automaticTarget = {automaticTarget}");
                var independentTarget = car.independentBrakePosition;
                var targetPressure = Mathf.Max(automaticTarget, independentTarget) * Constants.FullApplicationPressure;

                // AirBrake.DebugLog(car, $"BrakeValve26SA: handle = {car.independentBrakePosition}, cylinder = {state.cylinderPressure}, target = {targetPressure}");
                if (AirBrake.IsManualReleasePressed() && IsConnectedToPlayerLoco(car))
                    return (controlChargeFlow, Bailoff(state, dt));
                if (targetPressure < state.cylinderPressure - Constants.ApplicationThreshold)
                    return (controlChargeFlow, Vent(state, dt, targetPressure));
                if (targetPressure > state.cylinderPressure + Constants.ApplicationThreshold)
                    return (controlChargeFlow + Charge(car, state, dt, targetPressure), 0f);
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
