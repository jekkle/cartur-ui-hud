using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// The picture on the loading screen. Vanilla holds one Image for it - Hud.m_loadingImage,
    /// public - so this is a sprite swap and nothing else: no new object, no layout, and the
    /// tip, the bar and the fade are all still the game's.
    ///
    /// Random, but not the kind of random that shows the same one twice running. It is a bag,
    /// not a dice roll: every screen is drawn once before any is drawn again, and the bag's
    /// state is kept in the config so it survives a restart. When the bag empties it refills
    /// with everything except the one just shown, so the join between two cycles cannot repeat
    /// either. With nine screens that is nine loads before you see one twice.
    ///
    /// One texture is loaded per world load, not nine. A 1920x1080 RGBA texture is 8MB, and
    /// holding the set in memory for a picture that shows for ten seconds is the kind of thing
    /// that gets a mod blamed for someone's frame rate. The previous one is destroyed when the
    /// next is picked.
    /// </summary>
    internal static class LoadingArt
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string FolderName = "loading";

        private static ConfigFile s_config;
        private static ConfigEntry<bool> s_enabled;
        private static ConfigEntry<string> s_shown;

        private static Texture2D s_texture;
        private static Sprite s_sprite;

        internal static void Init(ConfigFile config)
        {
            s_config = config;
            s_enabled = config.Bind("Loading screens", "enabled", true,
                "Show the pictures in assets/loading on the loading screen. Off leaves vanilla's.");

            // The bag, kept between sessions. Written back every pick, so a crash mid-load at
            // worst repeats one screen rather than restarting the cycle.
            s_shown = config.Bind("Loading screens", "shown", "",
                "Which screens have been shown this cycle. Managed by the mod; clear it to reshuffle.");
        }

        /// <summary>
        /// Awake, not Show: the Hud is built long before the black screen fades up, so the
        /// picture is in place before anything can see it, and the swap costs nothing later.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Awake")]
        [HarmonyPostfix]
        private static void Dress(Hud __instance)
        {
            if (s_enabled == null || !s_enabled.Value || __instance.m_loadingImage == null)
                return;

            string dir = Path.Combine(AssetLoader.AssetsDir ?? "", FolderName);
            if (!Directory.Exists(dir))
            {
                Log.LogWarning("no " + dir + " - the loading screen is left vanilla");
                return;
            }

            var files = new List<string>();
            foreach (string path in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    files.Add(Path.GetFileName(path));
            }
            if (files.Count == 0)
            {
                Log.LogWarning(dir + " is empty - the loading screen is left vanilla");
                return;
            }
            files.Sort();   // so the bag means the same thing whatever order the disk answers in

            string pick = Draw(files);
            Show(__instance.m_loadingImage, Path.Combine(dir, pick), pick, files.Count);
        }

        /// <summary>
        /// Takes one out of the bag. Refills when empty, minus the one just shown, so two
        /// cycles cannot meet on the same picture.
        /// </summary>
        private static string Draw(List<string> files)
        {
            var seen = new List<string>(s_shown.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

            var left = new List<string>();
            foreach (string f in files)
                if (!seen.Contains(f))
                    left.Add(f);

            string last = seen.Count > 0 ? seen[seen.Count - 1] : null;
            if (left.Count == 0)
            {
                foreach (string f in files)
                    if (f != last || files.Count == 1)
                        left.Add(f);
                seen.Clear();
            }

            string pick = left[UnityEngine.Random.Range(0, left.Count)];
            seen.Add(pick);
            s_shown.Value = string.Join(",", seen.ToArray());
            s_config?.Save();
            return pick;
        }

        private static void Show(Image image, string path, string name, int total)
        {
            Texture2D tex = AssetLoader.LoadFile(path, Log);
            if (tex == null)
                return;

            // The one before it goes. Unity does not collect textures on its own, and a world
            // load can happen a dozen times in a session.
            if (s_sprite != null)
                UnityEngine.Object.Destroy(s_sprite);
            if (s_texture != null)
                UnityEngine.Object.Destroy(s_texture);

            s_texture = tex;
            s_sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            s_sprite.name = "cartur_loading_" + name;

            image.sprite = s_sprite;
            image.type = Image.Type.Simple;
            image.color = Color.white;

            // Filled, not fitted. These are all within two percent of the screen's own shape -
            // 1.74 to 1.81 against 1.78 - so letterboxing them to be exact would show black
            // bars to fix something nobody can see.
            image.preserveAspect = false;

            Log.LogInfo("loading screen: " + name + " (" + tex.width + "x" + tex.height
                + ", 1 of " + total + ")");
        }
    }
}
