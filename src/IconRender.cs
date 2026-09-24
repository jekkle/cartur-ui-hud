using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Renders item icons from the item prefabs themselves, at whatever size is asked for.
    ///
    /// Why this exists: the icons the game ships are 64x64, baked in the editor. They are the
    /// only copy - the whole set lives as 64x64 rects inside one 4096 atlas, read from
    /// StreamingAssets/SoftRef/Bundles/6a33a62. Upscaling them reconstructs detail that was
    /// never there; the meshes and their materials, on the other hand, still ship at full
    /// quality. So the way to get a sharp icon is to photograph the model again.
    ///
    /// Dev tool, off unless the config says otherwise, and one-shot: it renders, writes PNGs
    /// beside the config, and unticks itself. Nothing here runs in a normal session.
    ///
    /// The rig, in order:
    ///   - a parent GameObject that is INACTIVE before anything is instantiated into it. A
    ///     clone of an item prefab under an inactive parent never runs Awake, which matters
    ///     because ZNetView.Awake registers with ZNet and ItemDrop.Awake expects a spawned
    ///     world object. Strip those components while it is still inactive, then switch on.
    ///   - layer 30, which is the empty one. Read from the game's TagManager rather than
    ///     picked: 3, 6, 7 and 30 are unnamed, everything else is the game's (12 is "item",
    ///     22 "weapon", 31 "smoke").
    ///   - an orthographic camera culling everything but that layer, clearing to transparent
    ///     black, into a RenderTexture.
    ///   - its own directional light, also culled to that layer, so scene time of day cannot
    ///     change what an icon looks like.
    /// </summary>
    internal static class IconRender
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Size;
        internal static ConfigEntry<string> Items;
        internal static BepInEx.Logging.ManualLogSource Log;

        private const int RenderLayer = 30;

        // Twenty weapons and twenty pieces of armour, spread across every shape the set has.
        // Weapons are the hardest case for the 64px originals - long thin objects lose their
        // edge first - and armour is the other end: broad, busy surfaces where the detail is
        // in the material rather than the outline.
        private const string DefaultItems =
            "SwordIron,SwordBlackmetal,SwordSilver,SwordMistwalker,SwordKrom,Sword2HGold,"
            + "AxeBerzerker,AxeEarly,BattleaxeGold,"
            + "MaceIron,MaceNeedle,MaceSilver,"
            + "AtgeirIron,AtgeirBlackmetal,AtgeirHimminAfl,"
            + "SpearBronze,SpearCarapace,SpearWolffang,"
            + "SledgeIron,SledgeStagbreaker,"
            + "ArmorIronChest,ArmorIronLegs,ArmorBronzeChest,ArmorLeatherChest,"
            + "ArmorTrollLeatherChest,ArmorWolfChest,ArmorPaddedCuirass,ArmorPaddedGreaves,"
            + "ArmorCarapaceChest,ArmorRootChest,ArmorFenringChest,ArmorMageChest,"
            + "HelmetBronze,HelmetBronzeHorned,HelmetIron,HelmetCarapace,HelmetDrake,"
            + "HelmetPadded,CapeWolf,CapeLox,"
            + "bow_draugrfang";

        private static bool s_done;
        private static bool s_wasEnabled;

        public static void Init(ConfigFile config)
        {
            Enabled = config.Bind("IconRender", "Enabled", false,
                "Dev tool. Renders the items listed below from their prefabs and writes them as PNGs "
                + "next to this config file, then unticks itself.");
            Size = config.Bind("IconRender", "Size", 512, "Pixels, square.");
            Items = config.Bind("IconRender", "Items", DefaultItems,
                "Item prefab names, comma separated. Put * on its own to render every item in "
                + "ObjectDB, which is around 1500 and includes anything other mods have added.");
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        [HarmonyPostfix]
        private static void Tick()
        {
            if (Enabled == null)
                return;

            // Re-armed by ticking the config back on: HudLayout's watcher reloads the file, so
            // the value flips under us and the latch clears. One render per rising edge, and no
            // relaunch to shoot the set again after moving a pose.
            if (Enabled.Value && !s_wasEnabled)
                s_done = false;
            s_wasEnabled = Enabled.Value;

            if (s_done || !Enabled.Value)
                return;
            if (ObjectDB.instance == null || Player.m_localPlayer == null)
                return;

            s_done = true;
            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Log.LogWarning("[icons] failed: " + e);
            }
            finally
            {
                Enabled.Value = false;
            }
        }

        private static void Run()
        {
            int size = Mathf.Clamp(Size.Value, 64, 2048);
            string dir = Path.Combine(BepInEx.Paths.ConfigPath, "carturicons");
            Directory.CreateDirectory(dir);

            var rig = new GameObject("CarturIconRig");
            rig.transform.position = new Vector3(0f, -5000f, 0f);

            var camGo = new GameObject("CarturIconCam");
            camGo.transform.SetParent(rig.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;                       // rendered by hand, never per frame
            cam.orthographic = true;
            cam.cullingMask = 1 << RenderLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;

            // Three lights, not one. A single key gave flat, bleached metal next to vanilla's
            // warm contrast: the fill opens the shadow side so a blade is not half black, and
            // the rim behind picks the silhouette off the transparent background, which is what
            // makes an icon read at small sizes.
            Key(rig.transform, "Key", new Vector3(35f, 205f, 0f), 1.30f, new Color(1f, 0.97f, 0.92f));
            Key(rig.transform, "Fill", new Vector3(12f, 25f, 0f), 0.55f, new Color(0.78f, 0.84f, 1f));
            Key(rig.transform, "Rim", new Vector3(-18f, 95f, 0f), 0.85f, new Color(1f, 0.93f, 0.80f));

            var rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            int written = 0, missing = 0;
            foreach (KeyValuePair<string, GameObject> entry in Targets())
            {
                string name = entry.Key;
                GameObject prefab = entry.Value;
                if (prefab == null)
                {
                    Log.LogWarning("[icons] no prefab called " + name);
                    missing++;
                    continue;
                }

                // The pose has to come off the prefab before Spawn strips its scripts.
                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                ItemDrop.ItemData.ItemType type = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                    ? drop.m_itemData.m_shared.m_itemType
                    : ItemDrop.ItemData.ItemType.Misc;

                GameObject model = Spawn(prefab, rig.transform);
                if (model == null)
                {
                    missing++;
                    continue;
                }

                if (Frame(cam, model, Pose(type)))
                {
                    byte[] png = Shoot(cam, rt, size, out float coverage);
                    File.WriteAllBytes(Path.Combine(dir, name + ".png"), png);
                    written++;

                    // An empty shot is the thing worth measuring, not guessing at. Everything
                    // the frame decision was made from goes to the log for those, so the reason
                    // is read off one run instead of argued over across three.
                    // An empty shot gets probed rather than pondered. Two re-shoots, each
                    // changing exactly one thing: the culling mask opened to every layer, and
                    // the camera brought in to 2 units. Whichever one fills the frame names the
                    // cause - layer, clipping, or neither, which leaves the material.
                    if (coverage < 0.002f)
                    {
                        int mask = cam.cullingMask;
                        cam.cullingMask = -1;
                        Shoot(cam, rt, size, out float anyLayer);
                        cam.cullingMask = mask;

                        Vector3 far = cam.transform.position;
                        cam.transform.position = bounds(model) + (far - bounds(model)).normalized * 2f;
                        Shoot(cam, rt, size, out float close);
                        cam.transform.position = far;

                        Log.LogInfo("[icons] probe " + name
                            + " asShot=" + (coverage * 100f).ToString("F2") + "%"
                            + " allLayers=" + (anyLayer * 100f).ToString("F2") + "%"
                            + " at2units=" + (close * 100f).ToString("F2") + "%");
                    }

                    Explain(name, model, cam, coverage);
                }
                else
                {
                    Log.LogWarning("[icons] nothing renderable on " + name);
                    missing++;
                }

                Object.DestroyImmediate(model);
            }

            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rig);
            Log.LogInfo("[icons] " + written + " written to " + dir + (missing > 0 ? ", " + missing + " skipped" : ""));
        }

        /// <summary>
        /// What to render: the named list, or every item ObjectDB holds when the setting is *.
        /// Taken from m_items rather than from a name list of our own, so modded items come
        /// along and nothing goes stale when the game adds one.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, GameObject>> Targets()
        {
            if (Items.Value.Trim() == "*")
            {
                foreach (GameObject prefab in ObjectDB.instance.m_items)
                {
                    if (prefab != null)
                        yield return new KeyValuePair<string, GameObject>(prefab.name, prefab);
                }
                yield break;
            }

            foreach (string raw in Items.Value.Split(','))
            {
                string name = raw.Trim();
                if (name.Length > 0)
                    yield return new KeyValuePair<string, GameObject>(name, Find(name));
            }
        }

        /// <summary>
        /// The prefab for a name, falling back to a loose match over ObjectDB.
        ///
        /// The icon sprite and the prefab are not always named the same - the Draugr Fang's
        /// sprite is "bow_draugrfang" while its prefab is not - so a name copied off the icon
        /// atlas would otherwise miss. Rather than guess at the prefab spelling, compare with
        /// case and underscores thrown away and let the game's own list answer.
        /// </summary>
        private static GameObject Find(string name)
        {
            GameObject exact = ObjectDB.instance.GetItemPrefab(name);
            if (exact != null)
                return exact;

            string want = Flatten(name);
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab != null && Flatten(prefab.name) == want)
                {
                    Log.LogInfo("[icons] " + name + " -> prefab " + prefab.name);
                    return prefab;
                }
            }
            return null;
        }

        private static string Flatten(string s) => s.Replace("_", "").Replace(" ", "").ToLowerInvariant();

        /// <summary>
        /// A clone of the prefab with every script, collider and body taken off it, on the
        /// render layer. Built under an inactive parent so none of them ever wake: ZNetView
        /// would register a network object for an item nobody spawned.
        /// </summary>
        private static GameObject Spawn(GameObject prefab, Transform parent)
        {
            var holder = new GameObject("hold");
            holder.transform.SetParent(parent, false);
            holder.SetActive(false);

            GameObject clone = Object.Instantiate(prefab, holder.transform);
            clone.transform.localPosition = Vector3.zero;

            // Components only, never their GameObjects: destroying a particle system's object
            // takes its children with it, and the rest of the array is then dead components
            // that throw the moment anything asks them for their GameObject. Unity's fake-null
            // is why each one is checked rather than trusted.
            foreach (MonoBehaviour mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null)
                    Object.DestroyImmediate(mb);
            foreach (Collider col in clone.GetComponentsInChildren<Collider>(true))
                if (col != null)
                    Object.DestroyImmediate(col);
            foreach (Rigidbody rb in clone.GetComponentsInChildren<Rigidbody>(true))
                if (rb != null)
                    Object.DestroyImmediate(rb);
            foreach (ParticleSystem ps in clone.GetComponentsInChildren<ParticleSystem>(true))
                if (ps != null)
                    Object.DestroyImmediate(ps);
            foreach (ParticleSystemRenderer psr in clone.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (psr != null)
                    Object.DestroyImmediate(psr);

            // Measured, not guessed: every item that rendered used the Standard shader and
            // every empty one used Custom/Creature, with the renderer reporting visible and the
            // probes ruling out layer, clipping and framing. Custom/Creature draws nothing for
            // a camera outside the game's own rendering, so the clone gets its own Standard
            // material carrying the same maps across.
            //
            // New Material objects every time: the prefab's materials are shared assets and
            // mutating one would change how that item looks in the world.
            foreach (Renderer r in clone.GetComponentsInChildren<Renderer>(true))
            {
                Material[] source = r.sharedMaterials;
                var swapped = new Material[source.Length];
                bool any = false;
                for (int i = 0; i < source.Length; i++)
                {
                    Material m = source[i];
                    if (m == null || m.shader == null || !m.shader.name.StartsWith("Custom/"))
                    {
                        swapped[i] = m;
                        continue;
                    }

                    Shader target = Standard();
                    if (target == null)
                    {
                        swapped[i] = m;
                        continue;
                    }

                    var std = new Material(target);
                    if (m.HasProperty("_MainTex")) std.SetTexture("_MainTex", m.GetTexture("_MainTex"));
                    if (m.HasProperty("_BumpMap")) std.SetTexture("_BumpMap", m.GetTexture("_BumpMap"));
                    if (m.HasProperty("_MetallicGlossMap")) std.SetTexture("_MetallicGlossMap", m.GetTexture("_MetallicGlossMap"));
                    if (m.HasProperty("_EmissionMap")) std.SetTexture("_EmissionMap", m.GetTexture("_EmissionMap"));
                    if (m.HasProperty("_Color")) std.SetColor("_Color", m.GetColor("_Color"));
                    if (m.HasProperty("_EmissionColor"))
                    {
                        std.SetColor("_EmissionColor", m.GetColor("_EmissionColor"));
                        std.EnableKeyword("_EMISSION");
                    }
                    swapped[i] = std;
                    any = true;
                }

                if (any)
                    r.sharedMaterials = swapped;
            }

            foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = RenderLayer;

            holder.SetActive(true);
            return holder;
        }

        /// <summary>
        /// Points the camera at the model from a three-quarter view and zooms until the whole
        /// thing fits, with a small margin. Vanilla framed each icon by hand in the editor, so
        /// this will not land on the same angle; it is at least the same angle for every item.
        /// </summary>
        private static bool Frame(Camera cam, GameObject model, Quaternion look)
        {
            var renderers = new List<Renderer>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                    continue;
                if (r.enabled && r.gameObject.activeInHierarchy)
                    renderers.Add(r);
            }

            // Most item prefabs keep their mesh on a child that ships disabled - the first run
            // wrote 30 empty PNGs because of it. If nothing is on, switch the lot on and look
            // again rather than shooting an empty frame.
            if (renderers.Count == 0)
            {
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer)
                        continue;
                    r.gameObject.SetActive(true);
                    r.enabled = true;
                    renderers.Add(r);
                }
            }

            if (renderers.Count == 0)
                return false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++)
                bounds.Encapsulate(renderers[i].bounds);

            if (bounds.extents == Vector3.zero)
                return false;

            // Where to look from is read off the item, not chosen. Item prefabs are authored
            // along their own axis - a sword is 1.30 long in Z and 0.05 thin in Y - so a fixed
            // world-space angle pointed the camera straight down the blade and framed its
            // cross-section. Measured on one run: every empty shot had its renderer enabled and
            // sensible bounds, and an orthoSize that matched the thin axis.
            //
            // So: look along the SHORTEST extent, which is the flat face of anything that has
            // one, and put the LONGEST up the frame. The per-type pose then only tilts and
            // rolls that view, which is the part that is taste rather than geometry.
            Vector3 size = bounds.size;
            Vector3 shortAxis, longAxis;
            if (size.x <= size.y && size.x <= size.z)
            {
                shortAxis = Vector3.right;
                longAxis = size.y >= size.z ? Vector3.up : Vector3.forward;
            }
            else if (size.y <= size.x && size.y <= size.z)
            {
                shortAxis = Vector3.up;
                longAxis = size.x >= size.z ? Vector3.right : Vector3.forward;
            }
            else
            {
                shortAxis = Vector3.forward;
                longAxis = size.x >= size.y ? Vector3.right : Vector3.up;
            }

            Quaternion basis = Quaternion.LookRotation(-shortAxis, longAxis);
            cam.transform.rotation = basis * look;
            cam.transform.position = bounds.center + cam.transform.rotation * Vector3.back * 20f;

            // Fit on what the camera actually sees, not on the bounding sphere. The sphere is
            // the diagonal, so a long thin weapon ended up floating in a frame sized for a
            // length it only has corner to corner - which is why the first pass came out small.
            float halfW = 0f, halfH = 0f;
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 local = cam.transform.InverseTransformPoint(corner);
                halfW = Mathf.Max(halfW, Mathf.Abs(local.x));
                halfH = Mathf.Max(halfH, Mathf.Abs(local.y));
            }

            cam.orthographicSize = Mathf.Max(halfW, halfH) * 1.06f;
            return true;
        }

        /// <summary>
        /// Which way to point the camera, by what the item is.
        ///
        /// Vanilla framed every icon by hand, and the first pass here used one angle for
        /// everything: it foreshortened the long weapons into sticks. These are read off the
        /// item's own ItemType so a sword is shot broadside where its blade reads, and a helmet
        /// is shot from the front three-quarter where its face does.
        /// </summary>
        private static Quaternion Pose(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    // Rolled off vertical so a long weapon runs corner to corner and fills a
                    // square frame, which is how vanilla lays them out.
                    return Quaternion.Euler(0f, 0f, -32f);

                case ItemDrop.ItemData.ItemType.Bow:
                    return Quaternion.Euler(0f, 0f, -18f);

                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Helmet:
                    // A little off square: enough to read as a solid, not enough to hide the face.
                    return Quaternion.Euler(6f, 16f, 0f);

                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Hands:
                    return Quaternion.identity;

                default:
                    // Things with volume - food, ore, trophies - keep a three-quarter turn.
                    return Quaternion.Euler(14f, 26f, 0f);
            }
        }

        private static void Key(Transform parent, string name, Vector3 euler, float intensity, Color colour)
        {
            var go = new GameObject("CarturIcon" + name);
            go.transform.SetParent(parent, false);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << RenderLayer;
            light.intensity = intensity;
            light.color = colour;
            go.transform.rotation = Quaternion.Euler(euler);
        }

        /// <summary>
        /// The Standard shader, taken off a material that already uses it.
        ///
        /// Shader.Find("Standard") returns null here - it is not registered for runtime lookup
        /// in this build, which threw the first time this swap ran. Items that render fine
        /// (BattleaxeGold, MaceSilver, SledgeIron) are on Standard already, so the shader is
        /// loaded; it just has to be reached through one of them.
        /// </summary>
        private static Shader Standard()
        {
            if (s_standard != null)
                return s_standard;

            s_standard = Shader.Find("Standard");
            if (s_standard != null)
                return s_standard;

            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null)
                    continue;
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null || m.shader.name != "Standard")
                            continue;
                        s_standard = m.shader;
                        Log.LogInfo("[icons] Standard shader taken from " + prefab.name);
                        return s_standard;
                    }
                }
            }

            Log.LogWarning("[icons] no Standard shader anywhere in ObjectDB - materials left alone");
            return null;
        }

        private static Shader s_standard;

        /// <summary>Centre of everything the model draws, for moving the camera about it.</summary>
        private static Vector3 bounds(GameObject model)
        {
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                if (!any) { b = r.bounds; any = true; } else { b.Encapsulate(r.bounds); }
            }
            return any ? b.center : model.transform.position;
        }

        /// <summary>Everything the camera was given, and what came back.</summary>
        private static void Explain(string name, GameObject model, Camera cam, float coverage)
        {
            int total = 0, active = 0, skinned = 0, lods = 0;
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                total++;
                if (r.enabled && r.gameObject.activeInHierarchy)
                {
                    active++;
                    if (!any) { b = r.bounds; any = true; } else { b.Encapsulate(r.bounds); }
                }
                if (r is SkinnedMeshRenderer)
                    skinned++;
            }
            foreach (LODGroup g in model.GetComponentsInChildren<LODGroup>(true))
                lods++;

            var shaders = new List<string>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                Material mat = r.sharedMaterial;
                shaders.Add(r.GetType().Name
                    + "/layer" + r.gameObject.layer
                    + "/vis" + (r.isVisible ? "1" : "0")
                    + "/mats" + r.sharedMaterials.Length
                    + "/" + (mat == null ? "NO MATERIAL" : (mat.shader == null ? "NO SHADER" : mat.shader.name)));
            }

            Log.LogInfo("[icons] " + (coverage < 0.002f ? "EMPTY " : "ok(" + (coverage * 100f).ToString("F1") + "%) ") + name
                + " renderers=" + active + "/" + total
                + " skinned=" + skinned
                + " lodgroups=" + lods
                + " bounds=" + (any ? b.center.ToString("F2") + " size" + b.size.ToString("F2") : "none")
                + " orthoSize=" + cam.orthographicSize.ToString("F2")
                + " camPos=" + cam.transform.position.ToString("F1")
                + " | " + string.Join(" ; ", shaders.ToArray()));
        }

        private static byte[] Shoot(Camera cam, RenderTexture rt, int size, out float coverage)
        {
            RenderTexture previous = RenderTexture.active;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            Color32[] pixels = tex.GetPixels32();
            int lit = 0;
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a > 8)
                    lit++;
            coverage = (float)lit / pixels.Length;

            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }
    }
}
