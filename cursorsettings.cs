using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using KSP.UI.Screens;
using HarmonyLib;

namespace EnhancedCursor
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class CursorSettings : MonoBehaviour
    {
        // Configuration settings
        public static bool ModEnabled = true;
        public static bool EnableInFlight = true;
        public static bool EnableInEditor = true;
        public static bool EnableInKSC = true;
        public static bool PinToCenterScreen = false;
        public static bool HideCursorInF2 = true;
        public static bool LockToWindow = false;

        public static bool EnableIdleAutoHide = true;
        public static float IdleHideTimeout = 3.0f;

        public static bool EnableCursorHalo = false;
        public static float HaloSize = 32.0f;
        public static float HaloOpacity = 0.5f;

        public static bool EnableCustomCursor = false;
        public static float CustomCursorSize = 32.0f;
        public static string SelectedCursorFileName = "";

        public static float HotspotX = 0f;
        public static float HotspotY = 0f;

        // runtime state & caching
        private static Dictionary<string, Vector2> cursorHotspots = new Dictionary<string, Vector2>();
        private static Vector2 lastAppliedHotspot = new Vector2(-1f, -1f);
        private static Texture2D lastAppliedTexture = null;

        private static ApplicationLauncherButton appButton = null;
        private bool showWindow = false;

        private Rect windowRect = new Rect(Screen.width - 350, 60, 330, 300);
        private const int WINDOW_ID = 847201;
        private const string CAM_LOCK_ID = "EnhancedCursor_UILock";
        private bool isCameraLocked = false;

        private Vector2 galleryScrollPos = Vector2.zero;
        public static bool PendingCursorApply = false;
        public static bool isCustomCursorApplied = false;

        public static bool isIdleHidden = false;

        // screenshot mode
        private bool uiHiddenF2 = false;

        private static Harmony harmonyInstance = null;
        private static Texture2D redDotTex = null;
        private static Texture2D crosshairTex = null;

        public struct CursorFileItem
        {
            public string FileName;
            public string FullPath;
            public Texture2D Texture;
        }

        public static List<CursorFileItem> LoadedCursors = new List<CursorFileItem>();
        public static CursorFileItem ActiveCursorItem;
        public static bool HasActiveCursor = false;

        public static string CursorsFolderPath
        {
            get
            {
                string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "EnhancedCursor", "Cursors");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string PluginDataPath
        {
            get
            {
                string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "EnhancedCursor", "PluginData");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        private static string HotspotsJsonPath => Path.Combine(PluginDataPath, "hotspots.json");

        private void Awake()
        {
            DontDestroyOnLoad(this);
            LoadSettings();
            InitOverlayTextures();

            try
            {
                if (harmonyInstance == null)
                {
                    harmonyInstance = new Harmony("com.enhancedcursor.patch");
                    harmonyInstance.PatchAll();
                    Debug.Log("[EnhancedCursor] Harmony patches successfully applied.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnhancedCursor] Failed to apply Harmony patches: {ex.Message}");
            }
        }

        private void InitOverlayTextures()
        {
            if (redDotTex == null)
            {
                redDotTex = new Texture2D(1, 1);
                redDotTex.SetPixel(0, 0, Color.red);
                redDotTex.Apply();
            }

            if (crosshairTex == null)
            {
                crosshairTex = new Texture2D(1, 1);
                crosshairTex.SetPixel(0, 0, new Color(0f, 1f, 0f, 0.75f));
                crosshairTex.Apply();
            }
        }

        private void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
            GameEvents.onLevelWasLoaded.Add(OnLevelWasLoaded);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnRestoreUI);

            ScanCursorsFromDisk();
        }

        private void OnDisable()
        {
            UnlockCamera();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
            GameEvents.onLevelWasLoaded.Remove(OnLevelWasLoaded);
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnRestoreUI);

            RemoveAppButton();
            UnlockCamera();
            CleanupTextures();

            if (redDotTex != null) { DestroyImmediate(redDotTex); redDotTex = null; }
            if (crosshairTex != null) { DestroyImmediate(crosshairTex); crosshairTex = null; }
        }

        private void OnLevelWasLoaded(GameScenes scene)
        {
            UnlockCamera();
            showWindow = false;
            PendingCursorApply = true;
        }

        private void OnHideUI() => uiHiddenF2 = true;
        private void OnRestoreUI() => uiHiddenF2 = false;

        private static void CleanupTextures()
        {
            foreach (var item in LoadedCursors)
            {
                if (item.Texture != null) DestroyImmediate(item.Texture);
            }
            LoadedCursors.Clear();

            HasActiveCursor = false;
            ActiveCursorItem = default(CursorFileItem);
            lastAppliedTexture = null;
            lastAppliedHotspot = new Vector2(-1f, -1f);
        }

        public static void ScanCursorsFromDisk()
        {
            CleanupTextures();
            HasActiveCursor = false;
            string folder = CursorsFolderPath;

            if (!Directory.Exists(folder)) return;

            string[] files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly);

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file).ToLower();
                string fileName = Path.GetFileName(file);

                if (ext == ".png" || ext == ".jpg")
                {
                    Texture2D tex = LoadTextureFromFile(file);
                    if (tex != null)
                    {
                        var item = new CursorFileItem
                        {
                            FileName = fileName,
                            FullPath = file,
                            Texture = tex
                        };

                        LoadedCursors.Add(item);

                        if (fileName == SelectedCursorFileName)
                        {
                            ActiveCursorItem = item;
                            HasActiveCursor = true;

                            if (cursorHotspots.TryGetValue(fileName, out Vector2 savedSpot))
                            {
                                HotspotX = savedSpot.x;
                                HotspotY = savedSpot.y;
                            }
                        }
                    }
                }
            }

            PendingCursorApply = true;
        }

		private static readonly System.Reflection.FieldInfo pointerIconField = AccessTools.Field(typeof(Mouse), "pointerIcon");

		public static void OverwriteKSPStockTextures()
		{
			if (CameraManager.Instance != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA)
			{
				if (Input.GetMouseButton(1) || Cursor.lockState == CursorLockMode.Locked)
				{
					return;
				}
			}

			if (HasActiveCursor && ActiveCursorItem.Texture != null && pointerIconField != null)
			{
				try
				{
					pointerIconField.SetValue(null, ActiveCursorItem.Texture);
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[EnhancedCursor] Could not assign Mouse.pointerIcon: {ex.Message}");
				}
			}
		}

        public static void ApplyHardwareCursor(bool forceVisible = true)
		{
			bool isCurrentlyPanning = EnhancedCursorAddon.Instance != null && EnhancedCursorAddon.Instance.IsPanning;

			if (ModEnabled && EnableCustomCursor && HasActiveCursor && IsActiveInCurrentScene())
			{
				Vector2 targetHotspot = new Vector2(Mathf.Round(HotspotX), Mathf.Round(HotspotY));

				if (lastAppliedTexture != ActiveCursorItem.Texture || lastAppliedHotspot != targetHotspot)
				{
					Cursor.SetCursor(ActiveCursorItem.Texture, targetHotspot, CursorMode.Auto);
					lastAppliedTexture = ActiveCursorItem.Texture;
					lastAppliedHotspot = targetHotspot;
				}

				OverwriteKSPStockTextures();

				if (forceVisible && !isIdleHidden && !isCurrentlyPanning)
				{
					Cursor.visible = true;
				}

				isCustomCursorApplied = true;
			}
			else if (isCustomCursorApplied)
			{
				Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

				if (forceVisible && !isCurrentlyPanning)
				{
					Cursor.visible = true;
				}

				isCustomCursorApplied = false;
				lastAppliedTexture = null;
				lastAppliedHotspot = new Vector2(-1f, -1f);
			}
		}

        private static Texture2D LoadTextureFromFile(string filePath)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (ImageConversion.LoadImage(tex, fileData))
                {
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    return tex;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnhancedCursor] Failed to load image: {filePath}\n{ex.Message}");
            }
            return null;
        }

        public static bool IsActiveInCurrentScene()
        {
            if (!ModEnabled) return false;

            switch (HighLogic.LoadedScene)
            {
                case GameScenes.FLIGHT: return EnableInFlight;
                case GameScenes.EDITOR: return EnableInEditor;
                case GameScenes.SPACECENTER:
                case GameScenes.TRACKSTATION: return EnableInKSC;
                default: return false;
            }
        }

        private void OnAppLauncherReady()
        {
            if (appButton == null && ApplicationLauncher.Instance != null)
            {
                Texture2D icon = GameDatabase.Instance.GetTexture("EnhancedCursor/Textures/icon", false);
                if (icon == null) icon = Texture2D.whiteTexture;

                appButton = ApplicationLauncher.Instance.AddModApplication(
                    OnAppTrue, OnAppFalse,
                    null, null, null, null,
                    ApplicationLauncher.AppScenes.FLIGHT |
                    ApplicationLauncher.AppScenes.VAB |
                    ApplicationLauncher.AppScenes.SPH |
                    ApplicationLauncher.AppScenes.SPACECENTER |
                    ApplicationLauncher.AppScenes.TRACKSTATION,
                    icon
                );
            }
        }

        private void OnAppLauncherDestroyed() => RemoveAppButton();

        private void RemoveAppButton()
        {
            if (appButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(appButton);
                appButton = null;
            }
        }

        private void OnAppTrue()
        {
            ScanCursorsFromDisk();
            showWindow = true;
        }

        private void OnAppFalse()
        {
            showWindow = false;
            UnlockCamera();
        }

        private void Update()
        {
            if (ModEnabled && HideCursorInF2 && uiHiddenF2)
            {
                Cursor.visible = false;
            }

            // UI camera control lock management
            if (showWindow)
            {
                Vector2 mouseGUIPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (windowRect.Contains(mouseGUIPos))
                {
                    if (!isCameraLocked)
                    {
                        InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, CAM_LOCK_ID);
                        isCameraLocked = true;
                    }
                }
                else if (isCameraLocked)
                {
                    UnlockCamera();
                }
            }
            else if (isCameraLocked)
            {
                UnlockCamera();
            }

            if (PendingCursorApply && !Input.GetMouseButton(0))
            {
                ApplyHardwareCursor(true);
                SaveSettings();
                PendingCursorApply = false;
            }
            else if (ModEnabled && EnableCustomCursor && HasActiveCursor && IsActiveInCurrentScene())
            {
                // ensure no cursor flickering occurs
                ApplyHardwareCursor(!isIdleHidden && (!HideCursorInF2 || !uiHiddenF2));
            }
        }

        private void UnlockCamera()
        {
            if (isCameraLocked)
            {
                InputLockManager.RemoveControlLock(CAM_LOCK_ID);
                isCameraLocked = false;
            }
        }

        private float CalculateDesiredWindowHeight()
        {
            if (!ModEnabled) return 90f;

            float height = 295f;

            if (EnableIdleAutoHide) height += 40f;

            if (EnableCustomCursor)
            {
                height += 30f;
                if (LoadedCursors.Count > 0)
                {
                    int columns = 7;
                    int rows = Mathf.CeilToInt((float)LoadedCursors.Count / columns);
                    float contentHeight = rows * 42f;
                    float containerHeight = Mathf.Min(contentHeight, 130f);
                    height += containerHeight + 10f;
                }
                else
                {
                    height += 25f;
                }

                if (HasActiveCursor)
                {
                    height += 120f;
                }
            }

            if (EnableCursorHalo) height += 80f;

            return height;
        }

        // GUI
        private void OnGUI()
        {
            if (showWindow)
            {
                Event e = Event.current;
                if (e != null && e.type == EventType.Layout)
                {
                    windowRect.height = CalculateDesiredWindowHeight();
                }

                windowRect = GUILayout.Window(WINDOW_ID, windowRect, DrawWindow, "Enhanced Cursor");

                windowRect.x = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
                windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);
            }
        }

        private void DrawWindow(int id)
        {
            bool prevModState = ModEnabled;
            ModEnabled = GUILayout.Toggle(ModEnabled, " <b>Enable Mod</b>");
            if (prevModState != ModEnabled)
            {
                PendingCursorApply = true;
            }

            if (ModEnabled)
            {
                GUILayout.Box("", GUILayout.Height(2));

                GUILayout.Label("<b>Active Scenes:</b>");
                bool pFlight = GUILayout.Toggle(EnableInFlight, " Flight Mode");
                if (pFlight != EnableInFlight) { EnableInFlight = pFlight; PendingCursorApply = true; }

                bool pEditor = GUILayout.Toggle(EnableInEditor, " VAB / SPH Editor");
                if (pEditor != EnableInEditor) { EnableInEditor = pEditor; PendingCursorApply = true; }

                bool pKSC = GUILayout.Toggle(EnableInKSC, " KSC & Tracking Station");
                if (pKSC != EnableInKSC) { EnableInKSC = pKSC; PendingCursorApply = true; }

                GUILayout.Space(5);
                GUILayout.Label("<b>Display Options:</b>");

                bool pCenter = GUILayout.Toggle(PinToCenterScreen, " Pin Cursor to the Center");
                if (pCenter != PinToCenterScreen) { PinToCenterScreen = pCenter; PendingCursorApply = true; }

                bool pHideF2 = GUILayout.Toggle(HideCursorInF2, " Hide Cursor while in Screenshot (F2) Mode");
                if (pHideF2 != HideCursorInF2) { HideCursorInF2 = pHideF2; PendingCursorApply = true; }

                bool pLock = GUILayout.Toggle(LockToWindow, " Lock to KSP's Window Bounds");
                if (pLock != LockToWindow) { LockToWindow = pLock; PendingCursorApply = true; }

                bool pIdleHide = GUILayout.Toggle(EnableIdleAutoHide, " Auto-hide cursor after inactivity (Flight Only)");
                if (pIdleHide != EnableIdleAutoHide)
                {
                    EnableIdleAutoHide = pIdleHide;
                    PendingCursorApply = true;
                }

                if (EnableIdleAutoHide)
                {
                    GUILayout.Label($"   Delay: <b>{IdleHideTimeout:F1}s</b>");
                    float pTimeout = IdleHideTimeout;
                    IdleHideTimeout = GUILayout.HorizontalSlider(IdleHideTimeout, 1.0f, 10.0f);
                    if (Mathf.Abs(pTimeout - IdleHideTimeout) > 0.05f) PendingCursorApply = true;
                }

                GUILayout.Space(5);
                bool pCustomCur = GUILayout.Toggle(EnableCustomCursor, " <b>Custom Cursor (.png)</b>");
                if (pCustomCur != EnableCustomCursor)
                {
                    EnableCustomCursor = pCustomCur;
                    PendingCursorApply = true;
                }

                if (EnableCustomCursor)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<b>Gallery:</b>");
                    if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                    {
                        ScanCursorsFromDisk();
                    }
                    GUILayout.EndHorizontal();

                    if (LoadedCursors.Count == 0)
                    {
                        GUILayout.Label(" <i>No .png files found in Cursors folder.</i>");
                    }
                    else
                    {
                        int columns = 7;
                        int rows = Mathf.CeilToInt((float)LoadedCursors.Count / columns);
                        float contentHeight = rows * 42f;
                        float containerHeight = Mathf.Min(contentHeight, 130f);

                        galleryScrollPos = GUILayout.BeginScrollView(galleryScrollPos, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(containerHeight));
                        GUILayout.BeginHorizontal();

                        int count = 0;
                        foreach (var item in LoadedCursors)
                        {
                            bool isSelected = (SelectedCursorFileName == item.FileName);
                            GUI.backgroundColor = isSelected ? Color.green : Color.white;

                            Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(item.Texture), GUI.skin.button, GUILayout.Width(38), GUILayout.Height(38));

                            if (GUI.Button(buttonRect, item.Texture))
                            {
                                if (HasActiveCursor && !string.IsNullOrEmpty(SelectedCursorFileName))
                                {
                                    cursorHotspots[SelectedCursorFileName] = new Vector2(HotspotX, HotspotY);
                                }

                                if (isSelected)
                                {
                                    SelectedCursorFileName = "";
                                    HasActiveCursor = false;
                                }
                                else
                                {
                                    SelectedCursorFileName = item.FileName;
                                    ActiveCursorItem = item;
                                    HasActiveCursor = true;

                                    if (cursorHotspots.TryGetValue(item.FileName, out Vector2 savedSpot))
                                    {
                                        HotspotX = savedSpot.x;
                                        HotspotY = savedSpot.y;
                                    }
                                    else
                                    {
                                        HotspotX = 0f;
                                        HotspotY = 0f;
                                        cursorHotspots[item.FileName] = Vector2.zero;
                                    }
                                }
                                SaveHotspotMemoryOnly();
                                PendingCursorApply = true;
                            }

                            GUI.backgroundColor = Color.white;
                            count++;
                            if (count % columns == 0 && count < LoadedCursors.Count)
                            {
                                GUILayout.EndHorizontal();
                                GUILayout.BeginHorizontal();
                            }
                        }

                        GUILayout.EndHorizontal();
                        GUILayout.EndScrollView();

                        if (HasActiveCursor && ActiveCursorItem.Texture != null)
                        {
                            float maxW = ActiveCursorItem.Texture.width;
                            float maxH = ActiveCursorItem.Texture.height;

                            GUILayout.Space(4);

                            // resolution and warning
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"Res: <b>{maxW}x{maxH}px</b>");
                            if (maxW > 128 || maxH > 128)
                            {
                                GUI.color = Color.yellow;
                                GUILayout.Label(" (Warning: >128px may lag OS)");
                                GUI.color = Color.white;
                            }
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            Rect previewRect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                            GUI.Box(previewRect, GUIContent.none);
                            GUI.DrawTexture(previewRect, ActiveCursorItem.Texture, ScaleMode.ScaleToFit);

                            if (crosshairTex != null)
                            {
                                float normX = Mathf.Clamp01(HotspotX / Mathf.Max(1f, maxW));
                                float normY = Mathf.Clamp01(HotspotY / Mathf.Max(1f, maxH));

                                float crossX = previewRect.x + (normX * previewRect.width);
                                float crossY = previewRect.y + (normY * previewRect.height);

                                // crosshair lines
                                GUI.DrawTexture(new Rect(previewRect.x, crossY, previewRect.width, 1), crosshairTex);
                                GUI.DrawTexture(new Rect(crossX, previewRect.y, 1, previewRect.height), crosshairTex);

                                // red hotspot dot
                                GUI.DrawTexture(new Rect(crossX - 2, crossY - 2, 5, 5), redDotTex);
                            }

                            GUILayout.BeginVertical();
                            GUILayout.Label($"  X: <b>{Mathf.RoundToInt(HotspotX)}px</b>");
                            float newHX = GUILayout.HorizontalSlider(HotspotX, 0f, maxW);
                            if (Mathf.RoundToInt(newHX) != Mathf.RoundToInt(HotspotX))
                            {
                                HotspotX = newHX;
                                UpdateActiveHotspot(HotspotX, HotspotY);
                                PendingCursorApply = true;
                            }

                            GUILayout.Label($"  Y: <b>{Mathf.RoundToInt(HotspotY)}px</b>");
                            float newHY = GUILayout.HorizontalSlider(HotspotY, 0f, maxH);
                            if (Mathf.RoundToInt(newHY) != Mathf.RoundToInt(HotspotY))
                            {
                                HotspotY = newHY;
                                UpdateActiveHotspot(HotspotX, HotspotY);
                                PendingCursorApply = true;
                            }
                            GUILayout.EndVertical();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            if (GUILayout.Button("(0,0)", GUILayout.Height(18)))
                            {
                                HotspotX = 0f; HotspotY = 0f;
                                UpdateActiveHotspot(HotspotX, HotspotY); PendingCursorApply = true;
                            }
                            if (GUILayout.Button("Center", GUILayout.Height(18)))
                            {
                                HotspotX = Mathf.Round(maxW / 2f); HotspotY = Mathf.Round(maxH / 2f);
                                UpdateActiveHotspot(HotspotX, HotspotY); PendingCursorApply = true;
                            }
                            if (GUILayout.Button("Top-Right", GUILayout.Height(18)))
                            {
                                HotspotX = maxW; HotspotY = 0f;
                                UpdateActiveHotspot(HotspotX, HotspotY); PendingCursorApply = true;
                            }
                            if (GUILayout.Button("Bot-Right", GUILayout.Height(18)))
                            {
                                HotspotX = maxW; HotspotY = maxH;
                                UpdateActiveHotspot(HotspotX, HotspotY); PendingCursorApply = true;
                            }
                            GUILayout.EndHorizontal();
                        }
                    }
                }

                GUILayout.Space(5);
                bool pHalo = GUILayout.Toggle(EnableCursorHalo, " Cursor Halo");
                if (pHalo != EnableCursorHalo)
                {
                    EnableCursorHalo = pHalo;
                    PendingCursorApply = true;
                }

                if (EnableCursorHalo)
                {
                    GUILayout.Label($"   Size: <b>{HaloSize:F0}px</b>");
                    HaloSize = GUILayout.HorizontalSlider(HaloSize, 16.0f, 64.0f);

                    GUILayout.Label($"   Opacity: <b>{Mathf.RoundToInt(HaloOpacity * 100f)}%</b>");
                    HaloOpacity = GUILayout.HorizontalSlider(HaloOpacity, 0.1f, 1.0f);
                }
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Close", GUILayout.Height(22), GUILayout.ExpandWidth(true)))
            {
                if (appButton != null) appButton.SetFalse();
                else { showWindow = false; UnlockCamera(); }
            }

            GUI.DragWindow();
        }

        private static void UpdateActiveHotspot(float x, float y)
        {
            if (HasActiveCursor && !string.IsNullOrEmpty(SelectedCursorFileName))
            {
                cursorHotspots[SelectedCursorFileName] = new Vector2(x, y);
            }
        }

        private static void SaveHotspotMemoryOnly()
        {
            if (HasActiveCursor && !string.IsNullOrEmpty(SelectedCursorFileName))
            {
                cursorHotspots[SelectedCursorFileName] = new Vector2(HotspotX, HotspotY);
            }
        }

        private static void LoadHotspotDictionary()
        {
            cursorHotspots.Clear();
            try
            {
                string path = HotspotsJsonPath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loaded = SimpleJson.FromJson(json);
                    foreach (var pair in loaded)
                    {
                        if (!string.IsNullOrEmpty(pair.Key))
                        {
                            cursorHotspots[pair.Key] = pair.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnhancedCursor] Failed to load hotspots JSON: {ex.Message}");
            }
        }

        private static void SaveHotspotDictionary()
        {
            try
            {
                SaveHotspotMemoryOnly();

                string path = HotspotsJsonPath;
                string json = SimpleJson.ToJson(cursorHotspots);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnhancedCursor] Failed to save hotspots JSON: {ex.Message}");
            }
        }

        private static void LoadSettings()
        {
            LoadHotspotDictionary();

            KSP.IO.PluginConfiguration config = KSP.IO.PluginConfiguration.CreateForType<CursorSettings>();
            config.load();
            ModEnabled = config.GetValue("ModEnabled", true);
            EnableInFlight = config.GetValue("EnableInFlight", true);
            EnableInEditor = config.GetValue("EnableInEditor", true);
            EnableInKSC = config.GetValue("EnableInKSC", true);
            PinToCenterScreen = config.GetValue("PinToCenterScreen", false);
            HideCursorInF2 = config.GetValue("HideCursorInF2", true);
            LockToWindow = config.GetValue("LockToWindow", false);
            EnableIdleAutoHide = config.GetValue("EnableIdleAutoHide", true);
            IdleHideTimeout = config.GetValue("IdleHideTimeout", 3.0f);
            EnableCursorHalo = config.GetValue("EnableCursorHalo", false);
            HaloSize = config.GetValue("HaloSize", 32.0f);
            HaloOpacity = config.GetValue("HaloOpacity", 0.5f);
            EnableCustomCursor = config.GetValue("EnableCustomCursor", false);
            SelectedCursorFileName = config.GetValue("SelectedCursorFileName", "");

            if (!string.IsNullOrEmpty(SelectedCursorFileName) && cursorHotspots.TryGetValue(SelectedCursorFileName, out Vector2 savedSpot))
            {
                HotspotX = savedSpot.x;
                HotspotY = savedSpot.y;
            }
            else
            {
                HotspotX = 0f;
                HotspotY = 0f;
            }
        }

        private static void SaveSettings()
        {
            SaveHotspotDictionary();

            KSP.IO.PluginConfiguration config = KSP.IO.PluginConfiguration.CreateForType<CursorSettings>();
            config.SetValue("ModEnabled", ModEnabled);
            config.SetValue("EnableInFlight", EnableInFlight);
            config.SetValue("EnableInEditor", EnableInEditor);
            config.SetValue("EnableInKSC", EnableInKSC);
            config.SetValue("PinToCenterScreen", PinToCenterScreen);
            config.SetValue("HideCursorInF2", HideCursorInF2);
            config.SetValue("LockToWindow", LockToWindow);
            config.SetValue("EnableIdleAutoHide", EnableIdleAutoHide);
            config.SetValue("EnableCursorHalo", EnableCursorHalo);
            config.SetValue("HaloSize", HaloSize);
            config.SetValue("HaloOpacity", HaloOpacity);
            config.SetValue("EnableCustomCursor", EnableCustomCursor);
            config.SetValue("SelectedCursorFileName", SelectedCursorFileName);
            config.save();
        }

        private static class SimpleJson
        {
            public static string ToJson(Dictionary<string, Vector2> dict)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                int index = 0;
                foreach (var pair in dict)
                {
                    string escapedKey = pair.Key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.Append($"  \"{escapedKey}\": {{ \"x\": {pair.Value.x.ToString(CultureInfo.InvariantCulture)}, \"y\": {pair.Value.y.ToString(CultureInfo.InvariantCulture)} }}");
                    if (index < dict.Count - 1) sb.Append(",");
                    sb.AppendLine();
                    index++;
                }
                sb.AppendLine("}");
                return sb.ToString();
            }

            public static Dictionary<string, Vector2> FromJson(string json)
            {
                Dictionary<string, Vector2> dict = new Dictionary<string, Vector2>();
                if (string.IsNullOrEmpty(json)) return dict;

                int index = 0;
                while (index < json.Length)
                {
                    int keyStart = json.IndexOf('"', index);
                    if (keyStart < 0) break;

                    int keyEnd = json.IndexOf('"', keyStart + 1);
                    if (keyEnd < 0) break;

                    string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");

                    int objStart = json.IndexOf('{', keyEnd);
                    if (objStart < 0) break;

                    int objEnd = json.IndexOf('}', objStart);
                    if (objEnd < 0) break;

                    string body = json.Substring(objStart, objEnd - objStart + 1);

                    float x = ExtractFloat(body, "x");
                    float y = ExtractFloat(body, "y");
                    dict[key] = new Vector2(x, y);

                    index = objEnd + 1;
                }
                return dict;
            }

            private static float ExtractFloat(string text, string key)
            {
                int keyIdx = text.IndexOf($"\"{key}\"");
                if (keyIdx < 0) return 0f;
                int colonIdx = text.IndexOf(':', keyIdx);
                if (colonIdx < 0) return 0f;
                int start = colonIdx + 1;
                while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
                int end = start;
                while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == '-')) end++;
                string numStr = text.Substring(start, end - start);
                float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float result);
                return result;
            }
        }
    }
}
