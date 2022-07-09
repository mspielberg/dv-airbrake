using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;
using UnityEngine;

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
                    if (AirBrake.IsSelfLap((TrainCarType)carType))
                        return;
                    if (lever.name == "C train_brake_lever" || lever.name == "C brake")
                    {
                        lever.useSteppedJoint = true;
                        lever.notches = 4;
                        lever.scrollWheelHoverScroll = 1;
                    }
                    else if (lever.name == "C independent_brake_lever")
                    {
                        lever.useSteppedJoint = true;
                        lever.notches = 5;
                        lever.scrollWheelHoverScroll = 1;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SteppedJoint), nameof(SteppedJoint.Update))]
        public static class SteppedJointUpdatePatch
        {
            public static void Postfix(SteppedJoint __instance)
            {
                if (!__instance.isSpringActive
                    || __instance.notches != 5
                    || KeyBindings.increaseIndependentBrakeKeys.IsPressed()
                    || KeyBindings.decreaseIndependentBrakeKeys.IsPressed())
                {
                    return;
                }

                var spring = __instance.joint.spring;
                var currentAngle = spring.targetPosition - __instance.joint.limits.min;
                var newAngle = Mathf.Clamp(
                    currentAngle,
                    1 * __instance.SingleNotchAngle, // 2nd notch
                    3 * __instance.SingleNotchAngle); // 4th notch
                if (newAngle == currentAngle)
                    return;
                spring.targetPosition = newAngle + __instance.joint.limits.min;
                __instance.joint.spring = spring;
            }
        }
    }
}
