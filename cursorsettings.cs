using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using KSP.UI.Screens;
using HarmonyLib;

namespace EnhancedCursor
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class CursorSettings : MonoBehaviour
    {
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

        // the hotspot
        public static float HotspotX = 0f;
        public static float HotspotY = 0f;

        // cache
        private static Vector2 lastAppliedHotspot = new Vector2(-1f, -1f);
        private static Texture2D lastAppliedTexture = null;

        private static ApplicationLauncherButton appButton = null;
        private bool showWindow = false;

        private Rect windowRect = new Rect(Screen.width - 340, 60, 320, 300);
        private const int WINDOW_ID = 847201;
        private const string CAM_LOCK_ID = "EnhancedCursor_UILock";
        private bool isCameraLocked = false;

        private Vector2 galleryScrollPos = Vector2.zero;

        public static bool PendingCursorApply = false;
        public static bool isCustomCursorApplied = false;

        private static Harmony harmonyInstance = null;

        private static Texture2D redDotTex = null;

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

        private void Awake()
        {
            DontDestroyOnLoad(this);
            LoadSettings();
            InitDotTexture();

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

        private void InitDotTexture()
        {
            if (redDotTex == null)
            {
                redDotTex = new Texture2D(1, 1);
                redDotTex.SetPixel(0, 0, Color.red);
                redDotTex.Apply();
            }
        }

        private void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
            GameEvents.onLevelWasLoaded.Add(OnLevelWasLoaded);
            ScanCursorsFromDisk();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
            GameEvents.onLevelWasLoaded.Remove(OnLevelWasLoaded);
            RemoveAppButton();
            UnlockCamera();
            CleanupTextures();
        }

        private void OnLevelWasLoaded(GameScenes scene)
        {
            PendingCursorApply = true;
        }

        private static void CleanupTextures()
        {
            foreach (var item in LoadedCursors)
            {
                if (item.Texture != null) Destroy(item.Texture);
            }
            LoadedCursors.Clear();
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
                        }
                    }
                }
            }

            PendingCursorApply = true;
        }

        public static void OverwriteKSPStockTextures()
        {
            if (HasActiveCursor && ActiveCursorItem.Texture != null)
            {
                try
                {
                    var pointerIconField = AccessTools.Field(typeof(Mouse), "pointerIcon");
                    if (pointerIconField != null)
                    {
                        pointerIconField.SetValue(null, ActiveCursorItem.Texture);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[EnhancedCursor] Could not assign Mouse.pointerIcon: {ex.Message}");
                }
            }
        }

        public static void ApplyHardwareCursor(bool forceVisible = true)
        {
            if (ModEnabled && EnableCustomCursor && HasActiveCursor && IsActiveInCurrentScene() && ActiveCursorItem.Texture != null)
            {
                Vector2 targetHotspot = new Vector2(Mathf.Round(HotspotX), Mathf.Round(HotspotY));

                if (lastAppliedTexture != ActiveCursorItem.Texture || lastAppliedHotspot != targetHotspot)
                {
                    OverwriteKSPStockTextures();
                    Cursor.SetCursor(ActiveCursorItem.Texture, targetHotspot, CursorMode.Auto);
                    lastAppliedTexture = ActiveCursorItem.Texture;
                    lastAppliedHotspot = targetHotspot;
                }

                if (forceVisible) Cursor.visible = true;
                isCustomCursorApplied = true;
            }
            else if (isCustomCursorApplied)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                if (forceVisible) Cursor.visible = true;
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
                    int columns = 5;
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

                bool pIdleHide = GUILayout.Toggle(EnableIdleAutoHide, " Idle Auto-Hide (Flight Only)"); // next version will have this working in all scenes
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
                        int columns = 5;
                        int rows = Mathf.CeilToInt((float)LoadedCursors.Count / columns);
                        float contentHeight = rows * 42f;
                        float containerHeight = Mathf.Min(contentHeight, 130f);

                        galleryScrollPos = GUILayout.BeginScrollView(galleryScrollPos, false, true, GUILayout.Height(containerHeight));
                        GUILayout.BeginHorizontal();

                        int count = 0;
                        foreach (var item in LoadedCursors)
                        {
                            bool isSelected = (SelectedCursorFileName == item.FileName);
                            GUI.backgroundColor = isSelected ? Color.green : Color.white;

                            Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(item.Texture), GUI.skin.button, GUILayout.Width(38), GUILayout.Height(38));

                            if (GUI.Button(buttonRect, item.Texture))
                            {
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
                                }
                                PendingCursorApply = true;
                            }

                            if (isSelected && item.Texture != null && redDotTex != null)
                            {
                                float normX = Mathf.Clamp01(HotspotX / (float)item.Texture.width);
                                float normY = Mathf.Clamp01(HotspotY / (float)item.Texture.height);

                                float dotX = buttonRect.x + (normX * (buttonRect.width - 6));
                                float dotY = buttonRect.y + (normY * (buttonRect.height - 6));

                                GUI.DrawTexture(new Rect(dotX, dotY, 5, 5), redDotTex);
                            }

                            GUI.backgroundColor = Color.white;
                            count++;
                            if (count % columns == 0)
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
                            
                            GUILayout.Label($"   Hotspot X: <b>{Mathf.RoundToInt(HotspotX)}px</b>");
                            float newHX = GUILayout.HorizontalSlider(HotspotX, 0f, maxW);
                            if (Mathf.RoundToInt(newHX) != Mathf.RoundToInt(HotspotX))
                            {
                                HotspotX = newHX;
                                PendingCursorApply = true;
                            }

                            GUILayout.Label($"   Hotspot Y: <b>{Mathf.RoundToInt(HotspotY)}px</b>");
                            float newHY = GUILayout.HorizontalSlider(HotspotY, 0f, maxH);
                            if (Mathf.RoundToInt(newHY) != Mathf.RoundToInt(HotspotY))
                            {
                                HotspotY = newHY;
                                PendingCursorApply = true;
                            }

                            GUILayout.BeginHorizontal();
                            if (GUILayout.Button("Top-Left (0,0)", GUILayout.Height(20)))
                            {
                                HotspotX = 0f;
                                HotspotY = 0f;
                                PendingCursorApply = true;
                            }
                            if (GUILayout.Button("Center Hotspot", GUILayout.Height(20)))
                            {
                                HotspotX = Mathf.Round(maxW / 2f);
                                HotspotY = Mathf.Round(maxH / 2f);
                                PendingCursorApply = true;
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

        private static void LoadSettings()
        {
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
            HotspotX = config.GetValue("HotspotX", 0f);
            HotspotY = config.GetValue("HotspotY", 0f);
        }

        private static void SaveSettings()
        {
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
            config.SetValue("HotspotX", HotspotX);
            config.SetValue("HotspotY", HotspotY);
            config.save();
        }
    }
}
