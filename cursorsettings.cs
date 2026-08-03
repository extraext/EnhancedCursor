using UnityEngine;
using KSP.UI.Screens;

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
        public static float HaloSize = 32.0f;     // 16px to 64px
        public static float HaloOpacity = 0.5f;   // 10% to 100%

        private static ApplicationLauncherButton appButton = null;
        private bool showWindow = false;
        
        private Rect windowRect = new Rect(Screen.width - 280, 60, 260, 1);
        private const int WINDOW_ID = 847201;

        private void Awake()
        {
            DontDestroyOnLoad(this);
            LoadSettings();
        }

        private void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
            RemoveAppButton();
        }

        public static bool IsActiveInCurrentScene()
        {
            if (!ModEnabled) return false;

            switch (HighLogic.LoadedScene)
            {
                case GameScenes.FLIGHT:
                    return EnableInFlight;
                case GameScenes.EDITOR: // VAB / SPH
                    return EnableInEditor;
                case GameScenes.SPACECENTER:
                case GameScenes.TRACKSTATION:
                    return EnableInKSC;
                default:
                    return false;
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

        private void OnAppLauncherDestroyed()
        {
            RemoveAppButton();
        }

        private void RemoveAppButton()
        {
            if (appButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(appButton);
                appButton = null;
            }
        }

        private void OnAppTrue() => showWindow = true;
        private void OnAppFalse() => showWindow = false;

        private void OnGUI()
        {
            if (showWindow)
            {
                windowRect = GUILayout.Window(WINDOW_ID, windowRect, DrawWindow, "Enhanced Cursor v1.3.0");
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(5);
			
			// the main switch below don't change

            bool prevModState = ModEnabled;
            ModEnabled = GUILayout.Toggle(ModEnabled, " <b>Enable Enhanced Cursor</b>");
            if (prevModState != ModEnabled)
            {
                SaveSettings();
                windowRect.height = 1; 
            }

            GUILayout.Box("", GUILayout.Height(2)); 

            GUI.enabled = ModEnabled;

            GUILayout.Label("<b>Active Scenes:</b>");

            bool pFlight = EnableInFlight;
            EnableInFlight = GUILayout.Toggle(EnableInFlight, " Flight Mode");
            if (pFlight != EnableInFlight) SaveSettings();

            bool pEditor = EnableInEditor;
            EnableInEditor = GUILayout.Toggle(EnableInEditor, " VAB / SPH Editor");
            if (pEditor != EnableInEditor) SaveSettings();

            bool pKSC = EnableInKSC;
            EnableInKSC = GUILayout.Toggle(EnableInKSC, " KSC & Tracking Station");
            if (pKSC != EnableInKSC) SaveSettings();

            GUILayout.Space(5);
            GUILayout.Label("<b>Display & Window:</b>");

            bool pCenter = PinToCenterScreen;
            PinToCenterScreen = GUILayout.Toggle(PinToCenterScreen, " Pin Cursor to Center Screen");
            if (pCenter != PinToCenterScreen) SaveSettings();

            bool pHideF2 = HideCursorInF2;
            HideCursorInF2 = GUILayout.Toggle(HideCursorInF2, " Hide Cursor in F2 Mode");
            if (pHideF2 != HideCursorInF2) SaveSettings();

            bool pLock = LockToWindow;
            LockToWindow = GUILayout.Toggle(LockToWindow, " Lock Cursor inside KSP Window");
            if (pLock != LockToWindow) SaveSettings();

            bool pIdleHide = EnableIdleAutoHide;
            EnableIdleAutoHide = GUILayout.Toggle(EnableIdleAutoHide, " Idle Cursor Auto-Hide (Flight)");
            if (pIdleHide != EnableIdleAutoHide)
            {
                SaveSettings();
                windowRect.height = 1; 
            }

            if (EnableIdleAutoHide)
            {
                GUILayout.Space(2);
                GUILayout.Label($"   Delay: <b>{IdleHideTimeout:F1}s</b>");
                float pTimeout = IdleHideTimeout;
                IdleHideTimeout = GUILayout.HorizontalSlider(IdleHideTimeout, 1.0f, 10.0f);
                if (Mathf.Abs(pTimeout - IdleHideTimeout) > 0.05f) SaveSettings();
            }

            GUILayout.Space(5);
            bool pHalo = EnableCursorHalo;
            EnableCursorHalo = GUILayout.Toggle(EnableCursorHalo, " Cursor Highlight Halo");
            if (pHalo != EnableCursorHalo)
            {
                SaveSettings();
                windowRect.height = 1;
            }

            if (EnableCursorHalo)
            {
                GUILayout.Space(2);
                GUILayout.Label($"   Halo Size: <b>{HaloSize:F0}px</b>");
                float pSize = HaloSize;
                HaloSize = GUILayout.HorizontalSlider(HaloSize, 16.0f, 64.0f);
                if (Mathf.Abs(pSize - HaloSize) > 0.5f) SaveSettings();

                GUILayout.Label($"   Halo Opacity: <b>{Mathf.RoundToInt(HaloOpacity * 100f)}%</b>");
                float pOpacity = HaloOpacity;
                HaloOpacity = GUILayout.HorizontalSlider(HaloOpacity, 0.1f, 1.0f);
                if (Mathf.Abs(pOpacity - HaloOpacity) > 0.02f) SaveSettings();
            }

            GUI.enabled = true;

            GUILayout.Space(10);
            if (GUILayout.Button("Close"))
            {
                if (appButton != null)
                    appButton.SetFalse();
                else
                    showWindow = false;
            }

            GUI.DragWindow();
        }

        private void LoadSettings()
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
        }

        private void SaveSettings()
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
            config.SetValue("IdleHideTimeout", IdleHideTimeout);
            config.SetValue("EnableCursorHalo", EnableCursorHalo);
            config.SetValue("HaloSize", HaloSize);
            config.SetValue("HaloOpacity", HaloOpacity);
            config.save();
        }
    }
}