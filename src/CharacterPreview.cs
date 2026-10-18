using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// A live copy of the local player in the equipment panel's painted figure, rendered by a
    /// private camera into a RenderTexture. Left-drag turns the copy, right-drag slides the
    /// camera, the wheel zooms; only the camera moves, the copy stays where it stands.
    ///
    /// The copy is built the way FejdStartup.SetupCharacterPreview builds the character-select
    /// one: the player prefab instantiated with ZNetView.m_forceDisableInit set (so it never
    /// becomes a networked object), its Rigidbody destroyed, its animators on Normal update.
    /// Equipment is not loaded from a profile - the real player's own Player.SetupVisEquipment is
    /// pointed at the copy's VisEquipment, which writes every slot, the model, skin and hair.
    ///
    /// Isolation is by distance, not by layer: it stands 4000 m above the world, where no
    /// vanilla camera looks. Lighting is the character-select scene's, rebuilt from its asset
    /// (bundle 10ba9da1: a white spot at the camera, the warm "Directional Light" from behind,
    /// flat blue ambient) and switched in only around the Render() call, with the world's sun,
    /// ambient and fog switched out for that one call. Custom/Player and Custom/Creature read
    /// _LightColor0 / _WorldSpaceLightPos0 / unity_SHAr / unity_Fog* (read from the shader
    /// blobs), i.e. Unity's lights and RenderSettings, not EnvMan's _SunColor globals - so this
    /// is the whole of what the figure sees.
    /// </summary>
    internal static class CharacterPreview
    {
        internal static ManualLogSource Log;

        // Set when the real player's visuals change; the copy re-syncs on its next visible frame,
        // so nothing is done while the inventory is shut.
        internal static bool Dirty = true;

        private static CharacterPreviewView s_view;
        private static bool s_failed;

        // The panel is built lazily by EquipmentPanel, after Show, so this waits for it.
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        private static void Update(InventoryGui __instance)
        {
            if (s_view != null || s_failed || !InventoryGui.IsVisible()
                || Player.m_localPlayer == null || Game.instance == null || __instance.m_player == null)
                return;
            Transform panel = __instance.m_player.Find(EquipmentPanel.PanelName);
            if (panel == null || !panel.gameObject.activeInHierarchy)
                return;
            try
            {
                s_view = CharacterPreviewView.Build(panel);
            }
            catch (Exception e)
            {
                s_failed = true;
                Log?.LogWarning("character preview not built: " + e);
            }
        }

        // The real player's visuals are rewritten here on every equip change. Ragdolls go through
        // the same method with their own VisEquipment, hence the object check.
        [HarmonyPatch(typeof(Player), "SetupVisEquipment")]
        [HarmonyPostfix]
        private static void Equipment(Player __instance, VisEquipment visEq)
        {
            if (__instance == Player.m_localPlayer && visEq != null && visEq.gameObject == __instance.gameObject)
                Dirty = true;
        }
    }

    internal sealed class CharacterPreviewView : MonoBehaviour, IDragHandler, IScrollHandler
    {
        // Zoom 1 shows the whole body, 3.5 is head and shoulders. The half width is the cap on
        // sideways slide - chosen, not measured, the body mesh is T-posed when bounds are read.
        private const float MinZoom = 1f, MaxZoom = 3.5f;
        private const float Fov = 20f, Fit = 1.1f, Margin = 0.05f, HalfWidth = 0.35f;
        private const float TurnPerPx = 0.5f;
        // Per wheel notch: 1.15 to the 4th - doubled again (Cartur, 2026-10-05; first doubled 2026-10-04).
        // Multiplicative, so "twice as fast" squares the step: whole body to head and shoulders in ~2 notches.
        private const float ZoomStep = 1.749f;
        private static readonly Vector3 Spot = new Vector3(0f, 4000f, 0f);
        private static readonly MethodInfo s_setup = AccessTools.Method(typeof(Player), "SetupVisEquipment");
        // Character-select scene RenderSettings: ambientMode Flat, m_AmbientSkyColor. Read from
        // bundle 10ba9da1, the scene FejdStartup.m_characterPreviewPoint lives in.
        private static readonly Color Ambient = new Color(0.197f, 0.376f, 0.5f);

        private RectTransform _rt;
        private Player _pv;
        private VisEquipment _vis;
        private Camera _cam;
        private GameObject _rig;
        private Light _key, _rim;
        private RenderTexture _tex, _alpha;
        private float _lo, _hi, _aspect;
        private float _zoom = MinZoom, _cx, _cy;

        internal static CharacterPreviewView Build(Transform panel)
        {
            var go = new GameObject("CarturUI_CharacterPreview",
                typeof(RectTransform), typeof(RawImage), typeof(CharacterPreviewView));
            go.transform.SetParent(panel, false);
            var view = go.GetComponent<CharacterPreviewView>();
            try
            {
                view.Init();
            }
            catch
            {
                Destroy(go);
                throw;
            }
            return view;
        }

        private void Init()
        {
            float s = EquipmentPanel.ArtScale;
            Rect f = EquipmentPanel.FigurePx;
            _rt = (RectTransform)transform;
            _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(0f, 1f);
            _rt.anchoredPosition = new Vector2(f.x * s, -f.y * s);
            _rt.sizeDelta = new Vector2(f.width * s, f.height * s);
            _aspect = f.width / f.height;

            _tex = new RenderTexture((int)f.width, (int)f.height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _alpha = new RenderTexture((int)f.width, (int)f.height, 0, RenderTextureFormat.ARGB32);
            // The grading pass writes alpha 1 everywhere (measured: every pixel came back opaque,
            // the character in a black box). So this object's own image is the pre-grading render,
            // used only as a mask, and the graded image is drawn inside it.
            GetComponent<RawImage>().texture = _alpha;
            gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var graded = new GameObject("Graded", typeof(RectTransform), typeof(RawImage));
            var gr = (RectTransform)graded.transform;
            gr.SetParent(transform, false);
            gr.anchorMin = Vector2.zero;
            gr.anchorMax = Vector2.one;
            gr.offsetMin = gr.offsetMax = Vector2.zero;
            var gi = graded.GetComponent<RawImage>();
            gi.texture = _tex;
            gi.raycastTarget = false;

            GameObject pg;
            ZNetView.m_forceDisableInit = true;
            try { pg = Instantiate(Game.instance.m_playerPrefab, Spot, Quaternion.identity); }
            finally { ZNetView.m_forceDisableInit = false; }
            Destroy(pg.GetComponent<Rigidbody>());
            // AlwaysAnimate: the camera is rendered by hand, so the renderers count as unseen to
            // the animator's culling and the pose would freeze.
            // keepAnimatorStateOnDisable: Show() deactivates the copy between inventory opens,
            // and a re-enabled Animator otherwise rebuilds its controller - parameters back to
            // the asset defaults and the state machine back at Entry. In Player_animator
            // (bundle c4210710) the "wakeup" bool defaults to true and the Base Layer's default
            // state "wakeup ?" goes wakeup==true -> "Wakeup" (the stand-up clip) -> Movement,
            // wakeup==false -> Movement. Player.SetupAwake writes wakeup=false once, in Awake,
            // because the copy has no ZDO (ZNetView.Awake destroyed itself under
            // m_forceDisableInit), so every reopen stood the copy up again.
            foreach (Animator a in pg.GetComponentsInChildren<Animator>())
            {
                a.updateMode = AnimatorUpdateMode.Normal;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.keepAnimatorStateOnDisable = true;
            }
            _pv = pg.GetComponent<Player>();
            // Player.Awake and Character.Awake list every instance; the copy is not a player in
            // the world, so AI and the player loops must not see it.
            Player.GetAllPlayers().Remove(_pv);
            Character.GetAllCharacters().Remove(_pv);

            _vis = pg.GetComponent<VisEquipment>();
            Bounds b = _vis.m_bodyModel.bounds;
            _lo = b.min.y - Spot.y;
            _hi = b.max.y - Spot.y;

            _rig = new GameObject("CarturUI_PreviewRig");
            _rig.transform.position = Spot;
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(_rig.transform, false);
            camGo.transform.rotation = Quaternion.LookRotation(Vector3.back);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _tex;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // The board's blank wood (mean RGB 12/11/7 on board_equipment.png), at alpha 0: the mask
            // keeps the edge pixels whole, and they blend into wood rather than into black.
            _cam.backgroundColor = new Color32(12, 11, 7, 0);
            _cam.fieldOfView = Fov;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 30f;
            _cam.allowHDR = false;
            // First, so it sees the render before the grading does (OnRenderImage runs in
            // component order).
            camGo.AddComponent<AlphaGrab>().Target = _alpha;
            Grade(camGo);

            // The character-select rig, in this rig's frame (camera on +z looking down -z, so
            // camera-right is -x). All numbers read from bundle 10ba9da1 relative to
            // character_position and CameraMarker_Character:
            //   Static/CharacterSpotlight_NEW: spot, white, intensity 2, range 6, angle 50, at
            //   the camera marker - 3.63 m in front of the figure, 0.57 m to camera-right,
            //   1.79 m up - aimed at the figure 9.3 deg down.
            //   Backgroundscene/Directional Light (that scene's RenderSettings.m_Sun): colour
            //   (1, 0.669, 0.368), intensity 1.2, travelling toward the camera from behind the
            //   figure's left shoulder, 11 deg down. Its soft shadows are not reproduced.
            // Both are off except inside the Render() call below. ForcePixel because the game
            // drops its own sun to vertex lighting in interiors (EnvMan.UpdateEnvironment), so
            // the pixel-light budget cannot be relied on.
            //
            // Those numbers as they were read came out flat and pale here: that key sits nearly on
            // the camera axis, and the menu's own grading is not on this camera. Six setups were
            // rendered in game and Cartur picked one (2026-10-04, "key35L_rim3"): the key swung 35
            // deg to camera-left and 1 m higher at 2.5, the warm rim at 3.
            _key = new GameObject("Key").AddComponent<Light>();
            _key.transform.SetParent(_rig.transform, false);
            _key.transform.localPosition = new Vector3(Mathf.Sin(35f * Mathf.Deg2Rad) * 3.6f, 2.79f,
                                                       Mathf.Cos(35f * Mathf.Deg2Rad) * 3.6f);
            _key.transform.LookAt(_rig.transform.position + Vector3.up * 1.1f);
            _key.type = LightType.Spot;
            _key.color = Color.white;
            _key.intensity = 2.5f;
            _key.range = 6f;
            _key.spotAngle = 50f;
            _rim = new GameObject("Rim").AddComponent<Light>();
            _rim.transform.SetParent(_rig.transform, false);
            _rim.transform.localRotation = Quaternion.LookRotation(new Vector3(-0.654f, -0.19f, 0.732f));
            _rim.type = LightType.Directional;
            _rim.color = new Color(1f, 0.669f, 0.368f);
            _rim.intensity = 3f;
            foreach (Light l in new[] { _key, _rim })
            {
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                l.enabled = false;
            }

            _cy = (_lo + _hi) * 0.5f;
            CharacterPreview.Log?.LogInfo(
                $"character preview ready: body {_hi - _lo:0.00} m ({_lo:0.00}..{_hi:0.00}), zoom {MinZoom}-{MaxZoom}");
        }

        // Lineages Races (Nexus 3395 "Custom Races", Azathoth.LinejaeserasRace 1.0.6, READ from its DLL).
        // A race is not a model index: LNE_RaceVisualController (a component on the player prefab, so the
        // copy has one) attaches the race prefab through VisEquipment.AttachArmor, rebinds its meshes and
        // hides body bones. It finds the race from the player's ZDO, its racial item or m_customData
        // "LNE_CurrentRacePrefab"/"LNE_CurrentRaceHash". The copy has no ZDO, no inventory and an empty
        // m_customData, so it found none and drew the plain body. The mod's own AzuEPI preview bridge
        // (LNE_AzuEPIPreviewBridge.ApplyRaceToAzuPreviewSafe) fixes its preview the same way: write those two
        // keys on the preview player, then Refresh() the controller. Done here by name, no-op without it.
        // Read once from the mod's own assembly: AccessTools.TypeByName logs a HarmonyX warning for a
        // missing type, and this runs on every preview refresh.
        private static Type s_raceController, s_raceItem;
        private static bool s_raceLooked;
        private static string s_lastRace = "";

        private static void CopyRace(Player real, Player copy)
        {
            if (!s_raceLooked)
            {
                s_raceLooked = true;
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("Azathoth.LinejaeserasRace", out var info) && info?.Instance != null)
                {
                    var asm = info.Instance.GetType().Assembly;
                    s_raceController = asm.GetType("LinejaeserasRace.Systems.LNE_RaceVisualController");
                    s_raceItem = asm.GetType("LinejaeserasRace.Systems.LNE_RacialEquipment");
                }
            }
            if (s_raceController == null || real == null || copy == null)
                return;
            string prefab = null;
            if (s_raceItem != null && real.GetComponent(s_raceItem) is Component eq
                && AccessTools.Property(s_raceItem, "CurrentRaceItem")?.GetValue(eq, null) is ItemDrop.ItemData item
                && item.m_dropPrefab != null)
                prefab = item.m_dropPrefab.name;
            if (string.IsNullOrEmpty(prefab) && real.m_customData != null)
                real.m_customData.TryGetValue("LNE_CurrentRacePrefab", out prefab);
            if (string.IsNullOrEmpty(prefab))
            {
                ZNetView nv = real.GetComponent<ZNetView>();
                ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
                prefab = zdo?.GetString("LNE_CurrentRacePrefab", "");
            }
            if (copy.m_customData == null)
                copy.m_customData = new System.Collections.Generic.Dictionary<string, string>();
            if ((prefab ?? "") != s_lastRace)
            {
                s_lastRace = prefab ?? "";
                CharacterPreview.Log?.LogInfo("character preview race: '" + s_lastRace + "'");
            }
            copy.m_customData["LNE_CurrentRacePrefab"] = prefab ?? "";
            copy.m_customData["LNE_CurrentRaceHash"] = string.IsNullOrEmpty(prefab) ? "0" : prefab.GetStableHashCode().ToString();
            Component ctl = copy.GetComponent(s_raceController) ?? copy.gameObject.AddComponent(s_raceController);
            AccessTools.Method(s_raceController, "Refresh")?.Invoke(ctl, null);
        }

        private float VisH => (_hi - _lo) * Fit / _zoom;

        private void LateUpdate()
        {
            bool show = InventoryGui.IsVisible();
            Show(show);
            if (!show)
                return;
            if (CharacterPreview.Dirty && Player.m_localPlayer != null)
            {
                CharacterPreview.Dirty = false;
                s_setup?.Invoke(Player.m_localPlayer, new object[] { _vis, false });
                CopyRace(Player.m_localPlayer, _pv);
            }

            // Keep the visible window inside the body: at zoom 1 there is no room to slide.
            float h = _hi - _lo, visH = VisH, visW = visH * _aspect, m = Margin * h;
            float y0 = _lo + visH * 0.5f - m, y1 = _hi - visH * 0.5f + m;
            _cy = y0 < y1 ? Mathf.Clamp(_cy, y0, y1) : (_lo + _hi) * 0.5f;
            float xr = Mathf.Max(0f, HalfWidth - visW * 0.5f);
            _cx = Mathf.Clamp(_cx, -xr, xr);

            float d = visH / (2f * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));
            _cam.transform.position = Spot + new Vector3(0f, _cy, d) + _cam.transform.right * _cx;

            // The world's sun is EnvMan.m_dirLight, a directional light, so it reaches y=4000
            // like everywhere else, and RenderSettings ambient/fog are global and follow the
            // weather and time of day (EnvMan.SetEnv). Swap all three out for this one render
            // and put them back; Camera.Render() is synchronous, so no vanilla camera sees it.
            Light sun = EnvMan.instance?.m_dirLight;
            bool sunOn = sun != null && sun.enabled;
            Color ambient = RenderSettings.ambientLight;
            bool fog = RenderSettings.fog;
            if (sunOn) sun.enabled = false;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.fog = false;
            _key.enabled = _rim.enabled = true;
            try
            {
                _cam.Render();
            }
            finally
            {
                _key.enabled = _rim.enabled = false;
                RenderSettings.fog = fog;
                RenderSettings.ambientLight = ambient;
                if (sunOn) sun.enabled = true;
            }
        }

        // The game's own post stack, PostProcessing v1 (assembly_postprocessing), on the preview
        // camera with a profile of our own holding colour grading and nothing else - the world
        // camera's profile carries bloom, eye adaptation and fog meant for the world. Saturation
        // because Cartur asked for more colour to inspect the character by (2026-10-04).
        // Picked from 1.0 / 1.2 / 1.4 / 1.6 rendered in game (2026-10-04): "a bit more".
        private const float Saturation = 1.4f;
        private UnityEngine.PostProcessing.PostProcessingBehaviour _post;
        private UnityEngine.PostProcessing.PostProcessingProfile _grade;

        private void Grade(GameObject camGo)
        {
            _grade = ScriptableObject.CreateInstance<UnityEngine.PostProcessing.PostProcessingProfile>();
            _grade.colorGrading.enabled = true;
            _post = camGo.AddComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>();
            _post.profile = _grade;
            SetSaturation(Saturation);
        }

        private void SetSaturation(float sat)
        {
            var settings = UnityEngine.PostProcessing.ColorGradingModel.Settings.defaultSettings;
            settings.tonemapping.tonemapper = UnityEngine.PostProcessing.ColorGradingModel.Tonemapper.None;
            settings.basic.saturation = sat;
            _grade.colorGrading.settings = settings;
        }

        private void OnDisable() => Show(false);

        private void Show(bool on)
        {
            if (_pv != null && _pv.gameObject.activeSelf != on)
            {
                _pv.gameObject.SetActive(on);
                if (_rig != null)
                    _rig.SetActive(on);
            }
        }

        private void OnDestroy()
        {
            if (_pv != null) Destroy(_pv.gameObject);
            if (_rig != null) Destroy(_rig);
            if (_grade != null) Destroy(_grade);
            if (_alpha != null)
            {
                _alpha.Release();
                Destroy(_alpha);
            }
            if (_tex != null)
            {
                _tex.Release();
                Destroy(_tex);
            }
        }

        /// <summary>
        /// One ZoomStep per wheel event, by direction only. The size of scrollDelta depends on which
        /// UI input module the scene runs - the Input System one reports scrollDeltaPerTick (6 by
        /// default, Unity.InputSystem.dll), the legacy one raw wheel units - so scaling by it made the
        /// speed whatever the module happened to say; "twice as fast" did not reach the screen
        /// (Cartur, 2026-10-05). The first delta is logged once so the real figure is on record.
        /// </summary>
        public void OnScroll(PointerEventData e)
        {
            if (!s_loggedWheel)
            {
                s_loggedWheel = true;
                CharacterPreview.Log?.LogInfo($"character preview: first wheel delta {e.scrollDelta.y} " +
                    $"({EventSystem.current?.currentInputModule?.GetType().Name ?? "no input module"})");
            }
            if (e.scrollDelta.y != 0f)
                _zoom = Mathf.Clamp(_zoom * (e.scrollDelta.y > 0f ? ZoomStep : 1f / ZoomStep), MinZoom, MaxZoom);
        }

        private static bool s_loggedWheel;

        public void OnDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left)
            {
                // Same sign as FejdStartup.UpdateCharacterRotation. Rotates about the copy's own
                // origin, which is its feet, so it turns on the spot.
                _pv.transform.Rotate(0f, -e.delta.x * TurnPerPx, 0f);
            }
            else if (e.button == PointerEventData.InputButton.Right)
            {
                // The picture follows the pointer: as a fraction of the rect, so any UI scale works.
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position - e.delta, e.pressEventCamera, out Vector2 a);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out Vector2 b);
                float visH = VisH;
                _cx -= (b.x - a.x) / _rt.rect.width * visH * _aspect;
                _cy -= (b.y - a.y) / _rt.rect.height * visH;
            }
        }
    }

    /// <summary>Copies the preview camera's image, alpha and all, before the grading pass.</summary>
    internal sealed class AlphaGrab : MonoBehaviour
    {
        internal RenderTexture Target;

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (Target != null)
                Graphics.Blit(src, Target);
            Graphics.Blit(src, dst);
        }
    }
}
