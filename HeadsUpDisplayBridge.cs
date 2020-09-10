using DvMod.HeadsUpDisplay;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.AirBrake
{
    class HeadsUpDisplayBridge
    {
        public static HeadsUpDisplayBridge instance;

        static HeadsUpDisplayBridge()
        {
            try
            {
                if (UnityModManager.FindMod("HeadsUpDisplay") == null)
                    return;
                instance = new HeadsUpDisplayBridge();
                instance.Register();
            }
            catch (System.IO.FileNotFoundException)
            {
            }
        }

        void Register()
        {
            PushProvider auxReservoirPressureProvider = new PushProvider(
                "Aux reservoir pressure", () => true, v => v.ToString("F2"));
            Registry.Register(RegistryKeys.AllCars, auxReservoirPressureProvider);
            PushProvider brakeCylinderPressureProvider = new PushProvider(
                "Brake cylinder pressure", () => true, v => v.ToString("F2"));
            Registry.Register(RegistryKeys.AllCars, brakeCylinderPressureProvider);
        }

        public void UpdateAuxReservoirPressure(TrainCar car, float pressure)
        {
            ((PushProvider)Registry.GetProvider(TrainCarType.NotSet, "Aux reservoir pressure")).MixSmoothedValue(car, pressure);
        }

        public void UpdateBrakeCylinderPressure(TrainCar car, float brakeCylinderPressure)
        {
            ((PushProvider)Registry.GetProvider(TrainCarType.NotSet, "Brake cylinder pressure")).MixSmoothedValue(car, brakeCylinderPressure);
        }
    }
}