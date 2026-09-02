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
        // configuration settings
        public static bool ModEnabled = true;
		public static bool EnableInMainMenu = true;
        public static bool EnableInFlight = true;
        public static bool EnableInEditor = true;
        public static bool EnableInKSC = true;
        public static bool PinToCenterScreen = false;
		public static bool PinOnlyInIVA = false;
        public static bool HideCursorInF2 = true;
        public static bool LockToWindow = false;
        public static bool HideCursorOnlyOnPanMove = true; 

        public static bool EnableIdleAutoHide = true;
        public static float IdleHideTimeout = 3.0f;

        public static bool EnableCursorHalo = false;
        public static float HaloSize = 32.0f;
        public static float HaloOpacity = 0.5f;

        public static bool EnableCustomCursor = false;
        public static float CustomCursorSize = 32.0f;
        public static string SelectedCursorFileName = "";
		public static bool EnableClickFeedback = false;

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

        private float cachedIdleTimeout = -1f;
        private string idleTimeoutStr = "";
        
        private int cachedResW = -1;
        private int cachedResH = -1;
        private string resStr = "";
        
        private float cachedHotspotX = -1f;
        private string hotspotXStr = "";
        
        private float cachedHotspotY = -1f;
        private string hotspotYStr = "";
        
        private float cachedHaloSize = -1f;
        private string haloSizeStr = "";
        
        private float cachedHaloOpacity = -1f;
        private string haloOpacityStr = "";

        public struct CursorFileItem
        {
            public string FileName;
            public string FullPath;
            public Texture2D Texture;
			public Texture2D BrightTexture; 
			public Texture2D DarkTexture; 
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

        private static string HotspotsConfigPath => Path.Combine(PluginDataPath, "hotspots.cfg");
		private static string SettingsConfigPath => Path.Combine(PluginDataPath, "settings.cfg");

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
				if (item.BrightTexture != null) DestroyImmediate(item.BrightTexture); 
				if (item.DarkTexture != null) DestroyImmediate(item.DarkTexture); 
            }
            LoadedCursors.Clear();

            HasActiveCursor = false;
            ActiveCursorItem = default(CursorFileItem);
            lastAppliedTexture = null;
            lastAppliedHotspot = new Vector2(-1f, -1f);
        }
		
		private static Texture2D CreateTintedTexture(Texture2D original, Color targetColor, float amount)
		{
			if (original == null) return null;
			Texture2D tinted = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
			Color[] pixels = original.GetPixels();
			for (int i = 0; i < pixels.Length; i++)
			{
				if (pixels[i].a > 0f)
				{
					float alpha = pixels[i].a;
					pixels[i] = Color.Lerp(pixels[i], targetColor, amount);
					pixels[i].a = alpha; 
				}
			}
			tinted.SetPixels(pixels);
			tinted.filterMode = FilterMode.Point;
			tinted.wrapMode = TextureWrapMode.Clamp;
			tinted.Apply();
			return tinted;
		}

		public static Texture2D GetActiveCursorTexture()
		{
			if (!HasActiveCursor || ActiveCursorItem.Texture == null) return null;

			if (EnableClickFeedback)
			{
				if (Input.GetMouseButton(0) && ActiveCursorItem.BrightTexture != null)
					return ActiveCursorItem.BrightTexture;
				if (Input.GetMouseButton(1) && ActiveCursorItem.DarkTexture != null)
					return ActiveCursorItem.DarkTexture;
			}

			return ActiveCursorItem.Texture;
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
                            Texture = tex,
							BrightTexture = CreateTintedTexture(tex, Color.white, 0.28f), 
							DarkTexture = CreateTintedTexture(tex, Color.black, 0.35f) 
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

			Texture2D currentTex = GetActiveCursorTexture();
			if (currentTex != null && pointerIconField != null)
			{
				try
				{
					pointerIconField.SetValue(null, currentTex);
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[EnhancedCursor] Could not assign Mouse.pointerIcon: {ex.Message}");
				}
			}
		}

        public static void ApplyHardwareCursor(bool forceVisible = true)
        {
            bool isPanHiding = EnhancedCursorAddon.Instance != null && EnhancedCursorAddon.Instance.IsPanHiding;

            if (ModEnabled && EnableCustomCursor && HasActiveCursor && IsActiveInCurrentScene())
            {
                Vector2 targetHotspot = new Vector2(Mathf.Round(HotspotX), Mathf.Round(HotspotY));

                Texture2D currentTex = GetActiveCursorTexture();

				if (lastAppliedTexture != currentTex || lastAppliedHotspot != targetHotspot)
				{
					Cursor.SetCursor(currentTex, targetHotspot, CursorMode.Auto);
					lastAppliedTexture = currentTex;
					lastAppliedHotspot = targetHotspot;
				}

                OverwriteKSPStockTextures();

                if (forceVisible && !isIdleHidden && !isPanHiding)
                {
                    Cursor.visible = true;
                }

                isCustomCursorApplied = true;
            }
            else if (isCustomCursorApplied)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

                if (forceVisible && !isPanHiding)
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
				case GameScenes.MAINMENU:
				case GameScenes.SETTINGS:
				case GameScenes.CREDITS: return EnableInMainMenu; 
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
					ApplicationLauncher.AppScenes.MAINMENU | 
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
            if (!ModEnabled) return 70f;

            float height = 315f;
			
			if (PinToCenterScreen) height += 20f;

            if (EnableIdleAutoHide) height += 40f;

            if (EnableCustomCursor)
            {
                height += 50f;
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

                windowRect = GUILayout.Window(WINDOW_ID, windowRect, DrawWindow, "EnhancedCursor");

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
				bool pMenu = GUILayout.Toggle(EnableInMainMenu, " Main Menu");
				if (pMenu != EnableInMainMenu) { EnableInMainMenu = pMenu; PendingCursorApply = true; }

                bool pFlight = GUILayout.Toggle(EnableInFlight, " Flight Mode");
                if (pFlight != EnableInFlight) { EnableInFlight = pFlight; PendingCursorApply = true; }

                bool pEditor = GUILayout.Toggle(EnableInEditor, " VAB / SPH Editor");
                if (pEditor != EnableInEditor) { EnableInEditor = pEditor; PendingCursorApply = true; }

                bool pKSC = GUILayout.Toggle(EnableInKSC, " KSC & Tracking Station");
                if (pKSC != EnableInKSC) { EnableInKSC = pKSC; PendingCursorApply = true; }

                GUILayout.Space(5);
                GUILayout.Label("<b>Display Options:</b>");

				bool pCenter = GUILayout.Toggle(PinToCenterScreen, " Pin the cursor to the center");
				if (pCenter != PinToCenterScreen) { PinToCenterScreen = pCenter; PendingCursorApply = true; }

				if (PinToCenterScreen)
				{
					Rect parentRect = GUILayoutUtility.GetLastRect();

					GUILayout.BeginHorizontal();
					GUILayout.Space(14); 
					bool pIVA = GUILayout.Toggle(PinOnlyInIVA, " Only in IVA");
					if (pIVA != PinOnlyInIVA) { PinOnlyInIVA = pIVA; PendingCursorApply = true; }
    
					Rect childRect = GUILayoutUtility.GetLastRect(); 
					GUILayout.EndHorizontal();

					if (Event.current.type == EventType.Repaint)
					{
						Color prevColor = GUI.color;
						GUI.color = new Color(0.9f, 0.9f, 0.9f, 1f); 

						float lineX = parentRect.x + 7.5f; 
						float startY = parentRect.y + 17f; 
						float midY = childRect.y + (childRect.height / 2f) + 2f; 
        
						float endX = childRect.x - 0.1f; 
						float lineWidth = Mathf.Max(1f, endX - lineX); 

						GUI.DrawTexture(new Rect(lineX, startY, 1f, midY - startY), Texture2D.whiteTexture);
        
						GUI.DrawTexture(new Rect(lineX, midY, lineWidth, 1f), Texture2D.whiteTexture);

						GUI.color = prevColor;
					}
				}

                bool pLock = GUILayout.Toggle(LockToWindow, " Lock to KSP's window bounds");
                if (pLock != LockToWindow) { LockToWindow = pLock; PendingCursorApply = true; }
                
                bool pHidePan = GUILayout.Toggle(HideCursorOnlyOnPanMove, " Don't hide the cursor on right click when steady");
                if (pHidePan != HideCursorOnlyOnPanMove) { HideCursorOnlyOnPanMove = pHidePan; PendingCursorApply = true; }

                bool pHideF2 = GUILayout.Toggle(HideCursorInF2, " Hide the cursor while in screenshot mode");
                if (pHideF2 != HideCursorInF2) { HideCursorInF2 = pHideF2; PendingCursorApply = true; }

                bool pIdleHide = GUILayout.Toggle(EnableIdleAutoHide, " Hide the cursor after inactivity");
                if (pIdleHide != EnableIdleAutoHide)
                {
                    EnableIdleAutoHide = pIdleHide;
                    PendingCursorApply = true;
                }

                if (EnableIdleAutoHide)
                {
                    if (Mathf.Abs(cachedIdleTimeout - IdleHideTimeout) > 0.05f)
                    {
                        cachedIdleTimeout = IdleHideTimeout;
                        idleTimeoutStr = $"   Delay: <b>{IdleHideTimeout:F1}s</b>";
                    }
                    GUILayout.Label(idleTimeoutStr);
                    
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
					Rect parentRect = GUILayoutUtility.GetLastRect();

					GUILayout.BeginHorizontal();
					GUILayout.Space(14); 
					bool pFeedback = GUILayout.Toggle(EnableClickFeedback, " RMB and LMB clicks feedback");
					if (pFeedback != EnableClickFeedback)
					{
						EnableClickFeedback = pFeedback;
						PendingCursorApply = true;
					}
					Rect childRect = GUILayoutUtility.GetLastRect(); 
					GUILayout.EndHorizontal();

					if (Event.current.type == EventType.Repaint)
					{
						Color prevColor = GUI.color;
						GUI.color = new Color(0.9f, 0.9f, 0.9f, 1f); 

						float lineX = parentRect.x + 7.5f; 
						float startY = parentRect.y + 17f; 
						float midY = childRect.y + (childRect.height / 2f) + 2f; 

						float endX = childRect.x - 0.1f; 
						float lineWidth = Mathf.Max(1f, endX - lineX); 

						GUI.DrawTexture(new Rect(lineX, startY, 1f, midY - startY), Texture2D.whiteTexture);
						GUI.DrawTexture(new Rect(lineX, midY, lineWidth, 1f), Texture2D.whiteTexture);

						GUI.color = prevColor;
					}

					GUILayout.Space(4);
					
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
                            int curW = ActiveCursorItem.Texture.width;
                            int curH = ActiveCursorItem.Texture.height;

                            GUILayout.Space(4);

                            // resolution and warning
                            GUILayout.BeginHorizontal();
                            if (cachedResW != curW || cachedResH != curH)
                            {
                                cachedResW = curW;
                                cachedResH = curH;
                                resStr = $"Res: <b>{curW}x{curH}px</b>";
                            }
                            GUILayout.Label(resStr);
                            
                            if (curW > 128 || curH > 128)
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
                                float normX = Mathf.Clamp01(HotspotX / Mathf.Max(1f, curW));
                                float normY = Mathf.Clamp01(HotspotY / Mathf.Max(1f, curH));

                                float crossX = previewRect.x + (normX * previewRect.width);
                                float crossY = previewRect.y + (normY * previewRect.height);

                                // crosshair lines
                                GUI.DrawTexture(new Rect(previewRect.x, crossY, previewRect.width, 1), crosshairTex);
                                GUI.DrawTexture(new Rect(crossX, previewRect.y, 1, previewRect.height), crosshairTex);

                                // red hotspot dot
                                GUI.DrawTexture(new Rect(crossX - 2, crossY - 2, 5, 5), redDotTex);
                            }

                            GUILayout.BeginVertical();
                            if (Mathf.RoundToInt(cachedHotspotX) != Mathf.RoundToInt(HotspotX))
                            {
                                cachedHotspotX = HotspotX;
                                hotspotXStr = $"  X: <b>{Mathf.RoundToInt(HotspotX)}px</b>";
                            }
                            GUILayout.Label(hotspotXStr);
                            
                            float newHX = GUILayout.HorizontalSlider(HotspotX, 0f, curW);
                            if (Mathf.RoundToInt(newHX) != Mathf.RoundToInt(HotspotX))
                            {
                                HotspotX = newHX;
                                UpdateActiveHotspot(HotspotX, HotspotY);
                                PendingCursorApply = true;
                            }

                            if (Mathf.RoundToInt(cachedHotspotY) != Mathf.RoundToInt(HotspotY))
                            {
                                cachedHotspotY = HotspotY;
                                hotspotYStr = $"  Y: <b>{Mathf.RoundToInt(HotspotY)}px</b>";
                            }
                            GUILayout.Label(hotspotYStr);
                            
                            float newHY = GUILayout.HorizontalSlider(HotspotY, 0f, curH);
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
                                HotspotX = Mathf.Round(curW / 2f); HotspotY = Mathf.Round(curH / 2f);
                                UpdateActiveHotspot(HotspotX, HotspotY); PendingCursorApply = true;
                            }
                            if (GUILayout.Button("Top-Right", GUILayout.Height(18)))
                            {
                                HotspotX = curW; HotspotY = 0f;
                                UpdateActiveHotspot(HotspotX, HotspotY); PendingCursorApply = true;
                            }
                            if (GUILayout.Button("Bot-Right", GUILayout.Height(18)))
                            {
                                HotspotX = curW; HotspotY = curH;
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
                    if (Mathf.Abs(cachedHaloSize - HaloSize) > 0.1f)
                    {
                        cachedHaloSize = HaloSize;
                        haloSizeStr = $"   Size: <b>{HaloSize:F0}px</b>";
						PendingCursorApply = true;
                    }
                    GUILayout.Label(haloSizeStr);
                    HaloSize = GUILayout.HorizontalSlider(HaloSize, 16.0f, 64.0f);

                    if (Mathf.Abs(cachedHaloOpacity - HaloOpacity) > 0.01f)
                    {
                        cachedHaloOpacity = HaloOpacity;
                        haloOpacityStr = $"   Opacity: <b>{Mathf.RoundToInt(HaloOpacity * 100f)}%</b>";
						PendingCursorApply = true;
                    }
                    GUILayout.Label(haloOpacityStr);
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
                string path = HotspotsConfigPath;
                if (File.Exists(path))
                {
                    ConfigNode node = ConfigNode.Load(path);
                    if (node != null)
                    {
                        foreach (ConfigNode.Value val in node.values)
                        {
                            string key = val.name.Replace("___", " ");
                            Vector2 hotspot = KSPUtil.ParseVector2(val.value);
                            cursorHotspots[key] = hotspot;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnhancedCursor] Failed to load hotspots config: {ex.Message}");
            }
        }

        private static void SaveHotspotDictionary()
        {
            try
            {
                SaveHotspotMemoryOnly();

                string path = HotspotsConfigPath;
                ConfigNode node = new ConfigNode("HOTSPOTS");
                foreach (var pair in cursorHotspots)
                {
                    node.AddValue(pair.Key.Replace(" ", "___"), KSPUtil.WriteVector(pair.Value));
                }
                node.Save(path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnhancedCursor] Failed to save hotspots config: {ex.Message}");
            }
        }

        private static void LoadSettings()
		{
			LoadHotspotDictionary();

			try
			{
				if (File.Exists(SettingsConfigPath))
				{
					ConfigNode node = ConfigNode.Load(SettingsConfigPath);
					if (node != null)
					{
						node.TryGetValue("ModEnabled", ref ModEnabled);
						node.TryGetValue("EnableInMainMenu", ref EnableInMainMenu);
						node.TryGetValue("EnableInFlight", ref EnableInFlight);
						node.TryGetValue("EnableInEditor", ref EnableInEditor);
						node.TryGetValue("EnableInKSC", ref EnableInKSC);
						node.TryGetValue("PinToCenterScreen", ref PinToCenterScreen);
						node.TryGetValue("PinOnlyInIVA", ref PinOnlyInIVA);
						node.TryGetValue("HideCursorInF2", ref HideCursorInF2);
						node.TryGetValue("LockToWindow", ref LockToWindow);
						node.TryGetValue("HideCursorOnlyOnPanMove", ref HideCursorOnlyOnPanMove);
						node.TryGetValue("EnableIdleAutoHide", ref EnableIdleAutoHide);
						node.TryGetValue("IdleHideTimeout", ref IdleHideTimeout);
						node.TryGetValue("EnableCursorHalo", ref EnableCursorHalo);
						node.TryGetValue("HaloSize", ref HaloSize);
						node.TryGetValue("HaloOpacity", ref HaloOpacity);
						node.TryGetValue("EnableCustomCursor", ref EnableCustomCursor);
						node.TryGetValue("EnableClickFeedback", ref EnableClickFeedback);
                
						if (node.HasValue("SelectedCursorFileName"))
							SelectedCursorFileName = node.GetValue("SelectedCursorFileName");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[EnhancedCursor] Failed to load settings config: {ex.Message}");
			}

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

			try
			{
				ConfigNode node = new ConfigNode("CURSOR_SETTINGS");
        
				node.AddValue("ModEnabled", ModEnabled);
				node.AddValue("EnableInMainMenu", EnableInMainMenu);
				node.AddValue("EnableInFlight", EnableInFlight);
				node.AddValue("EnableInEditor", EnableInEditor);
				node.AddValue("EnableInKSC", EnableInKSC);
				node.AddValue("PinToCenterScreen", PinToCenterScreen);
				node.AddValue("PinOnlyInIVA", PinOnlyInIVA);
				node.AddValue("HideCursorInF2", HideCursorInF2);
				node.AddValue("LockToWindow", LockToWindow);
				node.AddValue("HideCursorOnlyOnPanMove", HideCursorOnlyOnPanMove);
				node.AddValue("EnableIdleAutoHide", EnableIdleAutoHide);
				node.AddValue("IdleHideTimeout", IdleHideTimeout); 
				node.AddValue("EnableCursorHalo", EnableCursorHalo);
				node.AddValue("HaloSize", HaloSize);
				node.AddValue("HaloOpacity", HaloOpacity);
				node.AddValue("EnableCustomCursor", EnableCustomCursor);
				node.AddValue("EnableClickFeedback", EnableClickFeedback);
				node.AddValue("SelectedCursorFileName", SelectedCursorFileName);

				node.Save(SettingsConfigPath);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[EnhancedCursor] Failed to save settings config: {ex.Message}");
			}
		}
    }
}
