using DvMod.AirBrake.Components;
using QuantitiesNet;
using static QuantitiesNet.Dimensions;
using System;
using UnityModManagerNet;
using Formatter = System.Func<float, string>;
using Provider = System.Func<TrainCar, float?>;

namespace DvMod.AirBrake
{
    internal sealed class HeadsUpDisplayBridge
    {
        public static IHeadsUpDisplayBridge? instance;

        static HeadsUpDisplayBridge()
        {
            try
            {
                var hudMod = UnityModManager.FindMod("HeadsUpDisplay");
                if (hudMod == null)
                    return;
                if (!hudMod.Active)
                    return;
                if (hudMod.Version.Major < 1)
                    return;
                instance = Activator.CreateInstance<Impl>();
            }
            catch (System.IO.FileNotFoundException)
            {
            }
        }

        public interface IHeadsUpDisplayBridge
        {
            public void UpdateAuxReservoirPressure(TrainCar car, float pressure);
            public void UpdateBrakeCylinderPressure(TrainCar car, float brakeCylinderPressure);
            public void UpdateEqualizingReservoirPressure(TrainCar car, float equalizingReservoirPressure);
        }

        private class Impl : IHeadsUpDisplayBridge
        {
            private readonly Action<TrainCar, Quantity<Pressure>> auxReservoirPressurePusher;
            private readonly Action<TrainCar, Quantity<Pressure>> brakeCylinderPressurePusher;
            private readonly Action<TrainCar, Quantity<Pressure>> equalizingReservoirPressurePusher;

            public Impl()
            {
                void RegisterFloatPull(string label, Provider provider, Formatter formatter, IComparable? order = null, bool hidden = false)
                {
                    DvMod.HeadsUpDisplay.Registry.RegisterPull(label, provider, formatter, order ?? label, hidden);
                }

                void RegisterPush<D>(out Action<TrainCar, Quantity<D>> pusher, string label, IComparable? order = null, bool hidden = false)
                    where D : IDimension, new()
                {
                    pusher = DvMod.HeadsUpDisplay.Registry.RegisterPush<D>(label, order ?? label, hidden);
                }

                RegisterFloatPull(
                    "Train brake position",
                    car =>
                    {
                        if (!CarTypes.IsLocomotive(car.carType))
                            return null;
                        return AirBrake.IsSelfLap(car.carType)
                            ? car.brakeSystem.trainBrakePosition
                            : Components.BrakeValve6ET.Mode(car.brakeSystem);
                    },
                    v =>
                    {
                        return v <= 1 ? v.ToString("P0")
                            : v == 2 ? "Running"
                            : v == 3 ? "Lap"
                            : v == 4 ? "Service"
                            : "Emergency";
                    });

                RegisterFloatPull(
                    "Triple valve mode",
                    car => CarTypes.IsAnyLocomotiveOrTender(car.carType)
                        ? null
                        : (float?)(int)ExtraBrakeState.Instance(car.brakeSystem).tripleValveMode.Abbrev(),
                    v => Components.TripleValveModeExtensions.FromAbbrev((char)v));

                RegisterPush(out auxReservoirPressurePusher, "Aux reservoir");

                RegisterPush(out brakeCylinderPressurePusher, "Brake cylinder");

                RegisterPush(out equalizingReservoirPressurePusher, "Equalizing reservoir");
            }

            public void UpdateAuxReservoirPressure(TrainCar car, float pressure)
            {
                auxReservoirPressurePusher?.Invoke(car, new Quantities.Pressure(pressure, QuantitiesNet.Units.Bar));
            }

            public void UpdateBrakeCylinderPressure(TrainCar car, float brakeCylinderPressure)
            {
                brakeCylinderPressurePusher?.Invoke(car, new Quantities.Pressure(brakeCylinderPressure, QuantitiesNet.Units.Bar));
            }

            public void UpdateEqualizingReservoirPressure(TrainCar car, float equalizingReservoirPressure)
            {
                equalizingReservoirPressurePusher?.Invoke(car, new Quantities.Pressure(equalizingReservoirPressure, QuantitiesNet.Units.Bar));
            }
        }
    }
}
