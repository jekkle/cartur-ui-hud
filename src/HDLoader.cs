using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// HD Valheim Textures loads ~24 GB of textures in ONE frame on the main menu, so the game
    /// freezes for ~38 s with nothing on screen moving. Read off HDValheimTextures 1.26.0: a postfix
    /// on ObjectDB.CopyOtherDB starts a coroutine whose third step calls TextureReplacer.LoadTextures
    /// (every texture, no yield), then ReplaceObjectDBArmorTextures and ReplaceMenuSceneTextures.
    ///
    /// This does the same load a slice per frame, starting at plugin load so it runs behind the logo
    /// screen, and holds that screen until it is done - so the logo screen's bar shows real progress
    /// and the menu opens with every texture in. Disk reads go on a worker thread; creating and
    /// uploading a texture must stay on the main thread (Unity 6000.0's Texture2D has no ThreadSafe
    /// members). When it is done, their postfix is skipped and only its two finish steps are called.
    ///
    /// Any member of theirs not found, or any failure mid-load, and this stands down: what it made
    /// is destroyed and their own load runs as before. The worst case is the old freeze.
    /// </summary>
    internal static class HDLoader
    {
        internal const string HdGuid = "Badgers.HDValheimTextures";

        // Main-thread time per frame spent creating textures. The logo screen keeps animating at
        // this; the scene load behind it gets the rest of the frame.
        private const double BudgetMs = 20.0;
        // How far the reader may run ahead of the uploads, in bytes held in memory.
        private const long ReadAhead = 1L << 30;

        internal static BepInEx.Logging.ManualLogSource Log;
        private static bool s_started, s_running, s_done;
        private static long s_totalBytes, s_doneBytes;

        // Theirs, resolved once.
        private static object s_mod, s_replacer;
        private static FieldInfo s_texturesLoaded;
        private static Dictionary<string, Texture2D> s_textures;
        private static Dictionary<string, string> s_customTextures;
        private static MethodInfo s_getCustom, s_loadCustom, s_armor, s_menu;
        private static string s_mainPath, s_overridePath;

        private static readonly FieldInfo s_indicator = AccessTools.Field(typeof(SceneLoader), "loadingIndicator");
        private static readonly FieldInfo s_fakeProgress = AccessTools.Field(typeof(SceneLoader), "_fakeProgress");

        private struct Item { public string Name, File; public int W, H; public bool Linear; public long Offset, Length; }

        private static readonly Queue<(Item item, byte[] data)> s_ready = new Queue<(Item, byte[])>();
        private static long s_queuedBytes;
        private static int s_frames, s_starved;
        private static bool s_labelled;
        private static bool s_readerDone;
        private static Exception s_readerError;
        private static readonly List<string> s_made = new List<string>();

        internal static void Init(MonoBehaviour host, Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(HdGuid))
            {
                Log.LogInfo("HD Valheim Textures not present - nothing to do");
                return;
            }
            try
            {
                if (!Resolve(harmony))
                    return;
                List<Item> items = Directory(s_overridePath, null);
                var overridden = new HashSet<string>();
                foreach (Item it in items) overridden.Add(it.Name);
                // A texture in both bundles is read twice by theirs and the override wins; skipping the
                // main copy gives the same dictionary for 0.5 GB less.
                List<Item> main = Directory(s_mainPath, overridden);
                main.AddRange(items);
                foreach (Item it in main) s_totalBytes += it.Length;

                s_started = s_running = true;
                new Thread(() => Read(main)) { IsBackground = true, Name = "CarturHDLoader" }.Start();
                host.StartCoroutine(Upload());
                Log.LogInfo($"loading {main.Count} HD textures ({s_totalBytes / 1e9:0.0} GB) behind the logo screen; HD Valheim Textures' own load skipped");
            }
            catch (Exception e)
            {
                Log.LogWarning("stood down, HD Valheim Textures loads as before: " + e.Message);
                s_running = false;
            }
        }

        /// <summary>Their members, all read off HDValheimTextures 1.26.0. Null if any is gone.</summary>
        private static bool Resolve(Harmony harmony)
        {
            Assembly hd = Chainloader.PluginInfos[HdGuid].Instance?.GetType().Assembly;
            Type patches = hd?.GetType("NS_HDValheimTextures.HarmonyPatches");
            Type copy = hd?.GetType("NS_HDValheimTextures.HarmonyPatches+ObjectDB_CopyOtherDB");
            Type modType = hd?.GetType("NS_HDValheimTextures.Mod");
            Type replacerType = hd?.GetType("NS_HDValheimTextures.TextureReplacer");
            s_mod = patches != null ? AccessTools.Field(patches, "Mod")?.GetValue(null) : null;
            s_replacer = s_mod != null ? AccessTools.Field(modType, "TextureReplacer")?.GetValue(s_mod) : null;
            s_texturesLoaded = modType != null ? AccessTools.Field(modType, "TexturesLoaded") : null;
            s_mainPath = s_mod != null ? AccessTools.Field(modType, "MainBundlePath")?.GetValue(s_mod) as string : null;
            s_overridePath = s_mod != null ? AccessTools.Field(modType, "OverrideBundlePath")?.GetValue(s_mod) as string : null;
            s_textures = s_replacer != null ? AccessTools.Field(replacerType, "m_Textures")?.GetValue(s_replacer) as Dictionary<string, Texture2D> : null;
            s_customTextures = s_replacer != null ? AccessTools.Field(replacerType, "m_CustomTextures")?.GetValue(s_replacer) as Dictionary<string, string> : null;
            s_getCustom = replacerType != null ? AccessTools.Method(replacerType, "GetCustomTextures") : null;
            s_loadCustom = replacerType != null ? AccessTools.Method(replacerType, "LoadCustomTexture") : null;
            s_armor = copy != null ? AccessTools.Method(copy, "ReplaceObjectDBArmorTextures") : null;
            s_menu = copy != null ? AccessTools.Method(copy, "ReplaceMenuSceneTextures") : null;
            MethodInfo theirPostfix = copy != null ? AccessTools.Method(copy, "Postfix") : null;
            MethodInfo isLoaded = AccessTools.PropertyGetter(
                AccessTools.TypeByName("SoftReferenceableAssets.LoadSceneAsyncOperation"), "IsLoadedButNotActivated");

            if (s_replacer == null || s_texturesLoaded == null || s_mainPath == null || s_textures == null
                || s_customTextures == null || s_getCustom == null || s_loadCustom == null || s_armor == null
                || s_menu == null || theirPostfix == null || isLoaded == null || s_indicator == null || s_fakeProgress == null)
            {
                Log.LogWarning("HD Valheim Textures has changed (a member this needs is gone) - stood down, it loads as before");
                return false;
            }

            harmony.Patch(theirPostfix, prefix: new HarmonyMethod(typeof(HDLoader), nameof(InsteadOfTheirs)));
            harmony.Patch(isLoaded, postfix: new HarmonyMethod(typeof(HDLoader), nameof(HoldLogoScreen)));
            harmony.PatchAll(typeof(HDLoader));
            return true;
        }

        /// <summary>
        /// Their bundle directory, parsed here rather than through their BundleStream so the reader
        /// thread owns its own file handle: "BVTB\0", int count at byte 5, then per texture a
        /// null-terminated name, int width, int height, byte linear, long offset, long length.
        /// A name can appear more than once - measured 2026-10-05: 249 repeats (4.0 GB) in
        /// OverrideTextures.dat, 17 (0.17 GB) in Textures.dat. Theirs loads every copy and the last one
        /// written to m_Textures wins, so only the last copy is kept here.
        /// </summary>
        private static List<Item> Directory(string path, HashSet<string> skip)
        {
            var last = new Dictionary<string, Item>();
            if (path == null || !File.Exists(path))
                return new List<Item>();
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                r.BaseStream.Position = 5;
                int n = r.ReadInt32();
                var name = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    name.Length = 0;
                    for (byte b; (b = r.ReadByte()) != 0;) name.Append((char)b);
                    var it = new Item { Name = name.ToString(), File = path, W = r.ReadInt32(), H = r.ReadInt32(),
                                        Linear = r.ReadByte() == 1, Offset = r.ReadInt64(), Length = r.ReadInt64() };
                    if (skip == null || !skip.Contains(it.Name))
                        last[it.Name] = it;
                }
            }
            return new List<Item>(last.Values);
        }

        private static void Read(List<Item> items)
        {
            try
            {
                FileStream fs = null;
                foreach (Item it in items)
                {
                    if (fs == null || fs.Name != it.File) { fs?.Dispose(); fs = File.OpenRead(it.File); }
                    var data = new byte[it.Length];
                    fs.Position = it.Offset;
                    for (int got = 0; got < data.Length;)
                    {
                        int n = fs.Read(data, got, data.Length - got);
                        if (n <= 0) throw new EndOfStreamException(it.Name);
                        got += n;
                    }
                    lock (s_ready)
                    {
                        while (s_queuedBytes > ReadAhead) Monitor.Wait(s_ready);
                        s_ready.Enqueue((it, data));
                        s_queuedBytes += data.Length;
                    }
                }
                fs?.Dispose();
            }
            catch (Exception e) { s_readerError = e; }
            lock (s_ready) s_readerDone = true;
        }

        private static IEnumerator Upload()
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                bool finished;
                s_frames++;
                try { finished = Slice(); }
                catch (Exception e) { StandDown(e); yield break; }
                if (finished) break;
                yield return null;
            }
            s_done = true;
            s_running = false;
            // Starved = frames the uploads waited on the disk; the rest were spent at the frame budget.
            Log.LogInfo($"{s_made.Count} HD textures loaded in {clock.Elapsed.TotalSeconds:0.0} s without freezing "
                + $"({s_frames} frames, {s_starved} waiting on disk)");
        }

        /// <summary>One frame's worth of uploads. True once every texture is in.</summary>
        private static bool Slice()
        {
            var frame = Stopwatch.StartNew();
            while (frame.Elapsed.TotalMilliseconds < BudgetMs)
            {
                if (s_readerError != null) throw s_readerError;
                (Item item, byte[] data) next;
                lock (s_ready)
                {
                    if (s_ready.Count == 0)
                    {
                        if (!s_readerDone) { s_starved++; return false; }
                        // Loose PNGs in CustomTextures, through their own two public calls.
                        s_getCustom.Invoke(s_replacer, null);
                        foreach (string png in s_customTextures.Values)
                            s_loadCustom.Invoke(s_replacer, new object[] { png, 0 });
                        return true;
                    }
                    next = s_ready.Dequeue();
                    s_queuedBytes -= next.data.Length;
                    Monitor.PulseAll(s_ready);
                }
                Make(next.item, next.data);
                s_doneBytes += next.item.Length;
            }
            return false;
        }

        /// <summary>
        /// Their LoadBundleTexture, line for line - including passing !Linear as the constructor's
        /// linear flag, which is what they do; changing it would change how every texture looks.
        /// </summary>
        private static void Make(Item it, byte[] data)
        {
            if (!IsPow2(it.W) || !IsPow2(it.H))
                return;   // they skip these too, with a warning of their own
            var tex = new Texture2D(it.W, it.H, TextureFormat.DXT5, true, !it.Linear);
            tex.LoadRawTextureData(data);
            tex.name = it.Name;
            tex.anisoLevel = 16;
            tex.filterMode = FilterMode.Trilinear;
            tex.Apply(false, true);
            s_textures[it.Name] = tex;
            s_made.Add(it.Name);
        }

        private static bool IsPow2(int v) => v > 0 && (v & (v - 1)) == 0;

        private static void StandDown(Exception e)
        {
            Log.LogWarning($"stood down after {s_made.Count} textures, HD Valheim Textures loads as before: {e.Message}");
            foreach (string name in s_made)
                if (s_textures.TryGetValue(name, out Texture2D t)) { UnityEngine.Object.Destroy(t); s_textures.Remove(name); }
            s_made.Clear();
            s_running = false;
        }

        /// <summary>
        /// Prefix on their ObjectDB_CopyOtherDB.Postfix - false skips it, deliberately: it would load
        /// all 24 GB again in one frame. Only its two finish steps are needed once ours is in.
        /// </summary>
        private static bool InsteadOfTheirs(ObjectDB __0)
        {
            if (!s_started || (!s_running && !s_done))
                return true;   // never started or stood down - theirs runs
            if ((bool)s_texturesLoaded.GetValue(s_mod))
                return false;
            s_texturesLoaded.SetValue(s_mod, true);
            __0.StartCoroutine(Finish());   // __0: their Postfix is static, its ObjectDB is argument 0
            return false;
        }

        private static IEnumerator Finish()
        {
            while (s_running) yield return null;   // normally already done: the logo screen waited
            if (!s_done)
            {
                Log.LogWarning("HD load did not finish - HD Valheim Textures will load on the next launch");
                yield break;
            }
            s_armor.Invoke(null, null);
            s_menu.Invoke(null, null);
        }

        /// <summary>The logo screen leaves as soon as this is true; it stays until the textures are in.</summary>
        private static void HoldLogoScreen(ref bool __result)
        {
            if (s_running)
                __result = false;
        }

        /// <summary>The logo screen's bar: a fifth scene load, four fifths textures, by bytes.</summary>
        [HarmonyPatch(typeof(SceneLoader), "Update")]
        [HarmonyPostfix]
        private static void Bar(SceneLoader __instance)
        {
            if (!s_started || s_totalBytes <= 0)
                return;
            if (!(s_indicator.GetValue(__instance) is LoadingIndicator bar) || !bar.IsVisible)
                return;
            bar.SetShowProgress(true);   // off in vanilla's prefab; Cartur's UI turns it on too
            // The indicator's own label, which LoadingScreen seats just above the bar.
            if (s_labelled != s_running)
            {
                s_labelled = s_running;
                bar.SetText(s_running ? "Loading HD textures" : "", false);
            }
            float scene = (float)s_fakeProgress.GetValue(__instance);
            float textures = s_done ? 1f : (float)((double)s_doneBytes / s_totalBytes);
            bar.SetProgress(0.2f * scene + 0.8f * textures);
        }
    }
}
