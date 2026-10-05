using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Hugin lands once, on the first spawn after the mod is installed, and welcomes the player.
    ///
    /// Spawned the way the game's own Tutorial.SpawnRaven does it (read off assembly_valheim):
    /// instantiate Tutorial.m_ravenPrefab if no raven is up, then Raven.AddTempText. Talking to him
    /// calls Player.SetSeenTutorial(key), which is saved per character - so on its own every new
    /// character would get him again. The flag file makes it once per install instead.
    ///
    /// He respects the game's "tutorials off" setting: Raven.Spawn skips any tutorial text while
    /// Raven.m_tutorialsEnabled is false, and that is left alone.
    /// </summary>
    internal static class Welcome
    {
        private const string Key = "cartur_welcome";
        private static readonly string Flag = Path.Combine(BepInEx.Paths.ConfigPath, "cartur.welcome.seen");

        private const string Topic = "Cartur's Mods";
        private const string Text =
            "Welcome to Cartur's Mods! Thank you for downloading.\n\n" +
            "If you would like to support Cartur's Mods, endorse them on Nexus " +
            "or come say hello on the Discord: discord.gg/nd5RqpwNkz";

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        [HarmonyPostfix]
        private static void Land(Player __instance)
        {
            if (__instance != Player.m_localPlayer || File.Exists(Flag) || Tutorial.instance == null)
                return;
            if (!Raven.IsInstantiated())
                Object.Instantiate(Tutorial.instance.m_ravenPrefab, Vector3.zero, Quaternion.identity);
            Raven.AddTempText(Key, Topic, Text, Topic, false);

            // First in line. Raven.GetTempText returns the first queued text, and other mods queue
            // theirs on spawn too - the pilot's first run found Hugin carrying Better Archery's
            // message ("hugin=betterarchery"), with ours waiting behind it.
            if (s_tempTexts?.GetValue(null) is List<Raven.RavenText> queue)
            {
                int i = queue.FindIndex(t => t.m_key == Key);
                if (i > 0)
                {
                    Raven.RavenText ours = queue[i];
                    queue.RemoveAt(i);
                    queue.Insert(0, ours);
                }
            }
        }

        private static readonly FieldInfo s_tempTexts = AccessTools.Field(typeof(Raven), "m_tempTexts");

        [HarmonyPatch(typeof(Player), nameof(Player.SetSeenTutorial))]
        [HarmonyPostfix]
        private static void Seen(string name)
        {
            if (name == Key && !File.Exists(Flag))
                File.WriteAllText(Flag, "");
        }
    }
}
