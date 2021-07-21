using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;

namespace DvMod.AirBrake
{
    public static class CabControls
    {
        [HarmonyPatch(typeof(ControlsInstantiator), nameof(ControlsInstantiator.Spawn))]
        public static class SpawnPatch
        {
            public static void Prefix(ControlSpec spec)
            {
                var carType = TrainCar.Resolve(spec.gameObject)?.carType;
                if (carType != null && spec is Lever lever)
                {
                    if (!AirBrake.IsSelfLap((TrainCarType)carType) && lever.name == "C train_brake_lever")
                    {
                        lever.useSteppedJoint = true;
                        lever.notches = 4;
                        lever.scrollWheelHoverScroll = 1;
                    }
                }
            }
        }
    }
}