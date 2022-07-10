using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace DvMod.AirBrake
{
    public static class CabControls
    {
        [HarmonyPatch(typeof(ControlsInstantiator), nameof(ControlsInstantiator.Spawn))]
        public static class SpawnPatch
        {
            private static readonly Lazy<Type> _CopiedCarInputType =
                new Lazy<Type>(
                    () => AccessTools.TypeByName("CCL_GameScripts.CabControls.CopiedCabInput"),
                    isThreadSafe: false);
            private static Type CopiedCarInputType => _CopiedCarInputType.Value;

            private static readonly Lazy<FieldInfo> _CopiedBindingField =
                new Lazy<FieldInfo>(
                    () => AccessTools.Field(CopiedCarInputType, "InputBinding"),
                    isThreadSafe: false);
            private static FieldInfo CopiedBindingField => _CopiedBindingField.Value;

            private static readonly Lazy<Type> _ControlSetupBaseType =
                new Lazy<Type>(
                    () => AccessTools.TypeByName("CCL_GameScripts.CabControls.ControlSetupBase"),
                    isThreadSafe: false);
            private static Type ControlSetupBaseType => _ControlSetupBaseType.Value;

            private static readonly Lazy<FieldInfo> _ControlSetupBindingField =
                new Lazy<FieldInfo>(
                    () => AccessTools.Field(ControlSetupBaseType, "InputBinding"),
                    isThreadSafe: false);
            private static FieldInfo ControlSetupBindingField => _ControlSetupBindingField.Value;

            private static bool HasCCLInputBinding(ControlSpec spec, string targetBinding)
            {
                if (!(UnityModManager.FindMod("DVCustomCarLoader")?.Active ?? false))
                    return false;

                if (spec.GetComponent(CopiedCarInputType) is Component copiedCarInput)
                    return CopiedBindingField.GetValue(copiedCarInput).ToString() == targetBinding;

                if (spec.GetComponent(ControlSetupBaseType) is Component controlSetupBase)
                    return ControlSetupBindingField.GetValue(controlSetupBase).ToString() == targetBinding;

                return false;
            }

            private static bool IsTrainBrakeControl(Lever spec)
            {
                if (spec.name == "C train_brake_lever" || spec.name == "C brake")
                    return true;
                if (HasCCLInputBinding(spec, "TrainBrake"))
                    return true;
                return false;
            }

            private static bool IsIndependentBrakeControl(Lever spec)
            {
                if (spec.name == "C independent_brake_lever")
                    return true;
                if (HasCCLInputBinding(spec, "IndependentBrake"))
                    return true;
                return false;
            }

            public static void Prefix(ControlSpec spec)
            {
                // Main.DebugLog($"Spawning {spec.GetType().Name} {spec.gameObject.GetPath()}");
                var carType = TrainCar.Resolve(spec.gameObject)?.carType;
                if (carType != null && spec is Lever lever)
                {
                    // Main.DebugLog("is Lever");
                    if (AirBrake.IsSelfLap((TrainCarType)carType))
                        return;
                    // Main.DebugLog("is manual lap");
                    if (IsTrainBrakeControl(lever))
                    {
                        lever.useSteppedJoint = true;
                        lever.notches = 4;
                        lever.scrollWheelHoverScroll = 1;
                    }
                    else if (IsIndependentBrakeControl(lever))
                    {
                        // Main.DebugLog($"Setting {lever.gameObject.GetPath()} as manual-lap");
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
