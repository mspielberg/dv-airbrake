using DV.Simulation.Brake;
using HarmonyLib;
using UnityEngine;

namespace DvMod.AirBrake
{
    public static class CarBrakeAudio
    {
        private const string GameObjectName = "DvMod.AirBrake.CylinderExhaust";
        private const float FlowSoundMultipler = 10;

        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.Awake))]
        public static class TrainCarAwakePatch
        {
            public static void Postfix(TrainCar __instance)
            {
                if (CarTypes.IsAnyLocomotiveOrTender(__instance.carType))
                    return;
                var prefab = __instance.transform.GetComponentInChildren<VisualCouplerInit>()
                    .hoseAdapter.hoseAudio.airflowPrefab;
                var exhaustAudio = Component.Instantiate(prefab, __instance.transform);
                var go = exhaustAudio.gameObject;
                go.name = GameObjectName;
                var brakeCylinderExhaust = go.AddComponent<BrakeCylinderExhaust>();
                brakeCylinderExhaust.exhaustAudio = exhaustAudio;
                brakeCylinderExhaust.brakeSystem = __instance.brakeSystem;
            }
        }

        [HarmonyPatch(typeof(TrainPhysicsLod), nameof(TrainPhysicsLod.SetLod))]
        public static class TrainPhysicsLodSetLodPatch
        {
            public static void Prefix(TrainPhysicsLod __instance, int lod)
            {
                if (__instance.currentLod != lod && !CarTypes.IsAnyLocomotiveOrTender(__instance.car.carType))
                    __instance.car.transform.Find(GameObjectName).gameObject.SetActive(lod <= 2);
            }
        }

        public class BrakeCylinderExhaust : MonoBehaviour
        {
            public LayeredAudio exhaustAudio;
            public BrakeSystem brakeSystem;

            public void Update()
            {
                if (exhaustAudio && brakeSystem)
                    exhaustAudio.Set(brakeSystem.pipeExhaustFlow * FlowSoundMultipler);
            }
        }
    }
}