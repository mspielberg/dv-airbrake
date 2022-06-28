using System;

using HarmonyLib;
using UnityModManagerNet;

namespace DvMod.AirBrake
{
    [EnableReloading]
    public static class Main
    {
        public static UnityModManager.ModEntry? mod;
        public static Settings settings = new Settings();
        public static bool enabled;

        static public bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;

            try
            {
                var loaded = Settings.Load<Settings>(modEntry);
                if (loaded.version == modEntry.Info.Version)
                    settings = loaded;
                else
                    settings = new Settings();
            }
            catch
            {
                settings = new Settings();
            }
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            Commands.Register();

            modEntry.OnGUI = OnGui;
            modEntry.OnSaveGUI = OnSaveGui;
            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;

            return true;
        }

        static private void OnGui(UnityModManager.ModEntry modEntry)
        {
            settings.Draw();
        }

        static private void OnSaveGui(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }

        static private bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            Updater.Enabled = value;
            return true;
        }

        static private bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.UnpatchAll(modEntry.Info.Id);
            return true;
        }

        public static void DebugLog(string message)
        {
            if (settings.enableLogging)
                mod?.Logger.Log(message);
        }

        public static void DebugLog(TrainCar car, Func<string> message)
        {
            if (settings.enableLogging && PlayerManager.Car == car)
                mod?.Logger.Log(message());
        }
    }
}
