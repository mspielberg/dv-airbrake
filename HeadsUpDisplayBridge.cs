using System;
using UnityModManagerNet;
using Formatter = System.Func<float, string>;
using Provider = System.Func<TrainCar, float?>;
using Pusher = System.Action<TrainCar, float>;

namespace DvMod.AirBrake
{
    internal sealed class HeadsUpDisplayBridge
    {
        public static HeadsUpDisplayBridge? instance;

        static HeadsUpDisplayBridge()
        {
            try
            {
                var hudMod = UnityModManager.FindMod("HeadsUpDisplay");
                if (hudMod?.Loaded != true)
                    return;
                instance = new HeadsUpDisplayBridge(hudMod);
            }
            catch (System.IO.FileNotFoundException)
            {
            }
        }

        private static readonly Type[] RegisterPullArgumentTypes = new Type[]
        {
            typeof(string),
            typeof(Provider),
            typeof(Formatter),
            typeof(IComparable)
        };

        private static readonly Type[] RegisterPushArgumentTypes = new Type[]
        {
            typeof(string),
            typeof(Formatter),
            typeof(IComparable)
        };

        private readonly Pusher? auxReservoirPressurePusher;
        private readonly Pusher? brakeCylinderPressurePusher;
        private readonly Pusher? equalizingReservoirPressurePusher;

        private HeadsUpDisplayBridge(UnityModManager.ModEntry hudMod)
        {
            void RegisterPull(string label, Provider provider, Formatter formatter, IComparable? order = null)
            {
                hudMod.Invoke(
                    "DvMod.HeadsUpDisplay.Registry.RegisterPull",
                    out var _,
                    new object?[] { label, provider, formatter, order },
                    RegisterPullArgumentTypes);
            }

            void RegisterPush(out Pusher pusher, string label, Formatter formatter, IComparable? order = null)
            {
                hudMod.Invoke(
                    "DvMod.HeadsUpDisplay.Registry.RegisterPush",
                    out var temp,
                    new object?[] { label, formatter, order },
                    RegisterPushArgumentTypes);
                pusher = (Pusher)temp;
            }

            RegisterPull(
                "Train brake position",
                car =>
                    !CarTypes.IsLocomotive(car.carType)
                    ? (float?)null
                    : AirBrake.IsSelfLap(car)
                        ? car.brakeSystem.trainBrakePositionSmoothed
                        : Components.BrakeValve6ET.Mode(car.brakeSystem),
                v =>
                    v <= 1 ? v.ToString("P0")
                    : v == 2 ? "Running"
                    : v == 3 ? "Lap"
                    : v == 4 ? "Service"
                    : "Emergency");

            RegisterPush(
                out auxReservoirPressurePusher,
                "Aux reservoir",
                v => $"{v:F2} bar");

            RegisterPush(
                out brakeCylinderPressurePusher,
                "Brake cylinder",
                v => $"{v:F2} bar");

            RegisterPush(
                out equalizingReservoirPressurePusher,
                "Equalizing reservoir",
                v => $"{v:F2} bar");
        }

        public void UpdateAuxReservoirPressure(TrainCar car, float pressure)
        {
            auxReservoirPressurePusher?.Invoke(car, pressure);
        }

        public void UpdateBrakeCylinderPressure(TrainCar car, float brakeCylinderPressure)
        {
            brakeCylinderPressurePusher?.Invoke(car, brakeCylinderPressure);
        }

        public void UpdateEqualizingReservoirPressure(TrainCar car, float equalizingReservoirPressure)
        {
            equalizingReservoirPressurePusher?.Invoke(car, equalizingReservoirPressure);
        }
    }
}
