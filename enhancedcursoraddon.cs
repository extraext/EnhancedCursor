using UnityEngine;
using System;
using System.Runtime.InteropServices;
using KSP.UI.Screens;

namespace EnhancedCursor
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class EnhancedCursorAddon : MonoBehaviour
    {
        // win32 api
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        private bool isPanning = false;
        private bool isUIHidden = false;
        private bool wasUIHidden = false;
        private bool isInFacility = false;
        private POINT savedClickPos;
        private const string LOCK_ID = "EnhancedCursor_PanLock";

        // idle auto-hide feature
        private Vector3 lastMousePos;
        private float idleTimer = 0f;
        private bool isIdleHidden = false;

        private bool isWindowClipped = false;

        private Texture2D haloTexture = null;

        private static bool IsWindows => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        private void Start()
        {
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);

            GameEvents.onGUIAstronautComplexSpawn.Add(OnFacilityOpen);
            GameEvents.onGUIAstronautComplexDespawn.Add(OnFacilityClose);

            GameEvents.onGUIMissionControlSpawn.Add(OnFacilityOpen);
            GameEvents.onGUIMissionControlDespawn.Add(OnFacilityClose);

            GameEvents.onGUIAdministrationFacilitySpawn.Add(OnFacilityOpen);
            GameEvents.onGUIAdministrationFacilityDespawn.Add(OnFacilityClose);

            GameEvents.onGUIRnDComplexSpawn.Add(OnFacilityOpen);
            GameEvents.onGUIRnDComplexDespawn.Add(OnFacilityClose);

            lastMousePos = Input.mousePosition;

            GenerateHaloTexture();

            // Initialize cached mod compatibility check
            ModChecker.InitializeCheck("ThroughTheEyes", "ThroughTheEyes.ThroughTheEyes", "active");
        }

        private void OnHideUI() => isUIHidden = true;
        private void OnShowUI() => isUIHidden = false;

        private void OnFacilityOpen() => isInFacility = true;
        private void OnFacilityClose() => isInFacility = false;

        private void GenerateHaloTexture()
        {
            int texSize = 64;
            haloTexture = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            Color transparent = new Color(0, 0, 0, 0);
            float center = texSize / 2f;
            float outerRadius = texSize / 2f - 2f;
            float innerRadius = outerRadius - 6f; 

            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    if (dist >= innerRadius && dist <= outerRadius)
                    {
                        float alpha = 1f;
                        if (dist < innerRadius + 1f) alpha = dist - innerRadius;
                        else if (dist > outerRadius - 1f) alpha = outerRadius - dist;
                        haloTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                    }
                    else
                    {
                        haloTexture.SetPixel(x, y, transparent);
                    }
                }
            }
            haloTexture.Apply();
        }

        private void Update()
        {
            UpdateWindowClamp();

            if (!CursorSettings.ModEnabled || 
                HighLogic.LoadedScene == GameScenes.MAINMENU || 
                HighLogic.LoadedScene == GameScenes.SETTINGS || 
                HighLogic.LoadedScene == GameScenes.CREDITS || 
                HighLogic.LoadedScene == GameScenes.LOADING || 
                HighLogic.LoadedScene == GameScenes.LOADINGBUFFER ||
                (HighLogic.LoadedScene == GameScenes.SPACECENTER && isInFacility))
            {
                if (isPanning) StopPanning();
                ResetIdleState();
                return;
            }

            TrackIdleActivity();

            if (HighLogic.LoadedScene == GameScenes.FLIGHT && !CursorSettings.EnableInFlight) return;
            if (HighLogic.LoadedScene == GameScenes.EDITOR && !CursorSettings.EnableInEditor) return;
            if ((HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.TRACKSTATION) && !CursorSettings.EnableInKSC) return;

            if (HighLogic.LoadedScene == GameScenes.FLIGHT && CameraManager.Instance != null)
            {
                CameraManager.CameraMode mode = CameraManager.Instance.currentCameraMode;
                if (mode == CameraManager.CameraMode.IVA || mode == CameraManager.CameraMode.Internal)
                {
                    if (isPanning) StopPanning();
                    return;
                }
            }

            if (ModChecker.IsActiveFast())
            {
                if (isPanning) StopPanning();
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                StartPanning();
            }
            else if (Input.GetMouseButtonUp(1) && isPanning)
            {
                StopPanning();
            }
        }

        private void OnGUI()
        {
            if (CursorSettings.IsActiveInCurrentScene() && 
                CursorSettings.EnableCursorHalo && 
                !isPanning && 
                !isIdleHidden && 
                Cursor.visible && 
                haloTexture != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                float size = CursorSettings.HaloSize;
                Rect haloRect = new Rect(mousePos.x - size / 2f, mousePos.y - size / 2f, size, size);

                Color prevColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, CursorSettings.HaloOpacity);
                GUI.DrawTexture(haloRect, haloTexture);
                GUI.color = prevColor;
            }
        }

        private void UpdateWindowClamp()
        {
            if (!IsWindows) return;

            bool shouldClamp = CursorSettings.IsActiveInCurrentScene() && CursorSettings.LockToWindow && Application.isFocused && !isPanning;

            if (shouldClamp)
            {
                IntPtr hWnd = GetActiveWindow();
                if (hWnd != IntPtr.Zero)
                {
                    RECT rect;
                    if (GetWindowRect(hWnd, out rect))
                    {
                        ClipCursor(ref rect);
                        isWindowClipped = true;
                        return;
                    }
                }
            }

            if (isWindowClipped)
            {
                ClipCursor(IntPtr.Zero);
                isWindowClipped = false;
            }
        }

        private void TrackIdleActivity()
        {
            Vector3 currentMousePos = Input.mousePosition;
            bool mouseMoved = Vector3.Distance(currentMousePos, lastMousePos) > 0.5f;
            bool inputPressed = Input.anyKey || Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            lastMousePos = currentMousePos;

            if (mouseMoved || inputPressed || isPanning)
            {
                idleTimer = 0f;
                if (isIdleHidden)
                {
                    isIdleHidden = false;
                    Cursor.visible = true;
                }
            }
            else if (CursorSettings.EnableIdleAutoHide && HighLogic.LoadedScene == GameScenes.FLIGHT && CursorSettings.EnableInFlight && !isUIHidden)
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= CursorSettings.IdleHideTimeout)
                {
                    isIdleHidden = true;
                }
            }
        }

        private void ResetIdleState()
        {
            idleTimer = 0f;
            if (isIdleHidden)
            {
                isIdleHidden = false;
                Cursor.visible = true;
            }
        }

        private void LateUpdate()
        {
            if (!CursorSettings.ModEnabled || 
                HighLogic.LoadedScene == GameScenes.MAINMENU || 
                HighLogic.LoadedScene == GameScenes.SETTINGS || 
                HighLogic.LoadedScene == GameScenes.CREDITS || 
                HighLogic.LoadedScene == GameScenes.LOADING || 
                HighLogic.LoadedScene == GameScenes.LOADINGBUFFER ||
                (HighLogic.LoadedScene == GameScenes.SPACECENTER && isInFacility))
            {
                if (wasUIHidden)
                {
                    Cursor.visible = true;
                    wasUIHidden = false;
                }
                return;
            }

            // the main enhancement (panning)
            if (isPanning)
            {
                if (IsWindows) SetCursorPos(savedClickPos.X, savedClickPos.Y);
                Cursor.visible = false;
                return;
            }

            // hiding the cursor while in screenshot mode
            if (CursorSettings.HideCursorInF2 && isUIHidden)
            {
                Cursor.visible = false;
                wasUIHidden = true;
                return;
            }
            else if (wasUIHidden)
            {
                Cursor.visible = true;
                wasUIHidden = false;
            }

            // idle auto-hide
            if (isIdleHidden)
            {
                Cursor.visible = false;
            }
        }

        private void StartPanning()
        {
            isPanning = true;

            if (CursorSettings.PinToCenterScreen)
            {
                savedClickPos.X = Screen.currentResolution.width / 2;
                savedClickPos.Y = Screen.currentResolution.height / 2;
            }
            else if (IsWindows)
            {
                GetCursorPos(out savedClickPos);
            }

            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                InputLockManager.SetControlLock(ControlTypes.KSC_FACILITIES, LOCK_ID);
            }

            Cursor.visible = false;
        }

        private void StopPanning()
        {
            if (!isPanning) return;

            isPanning = false;

            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                InputLockManager.RemoveControlLock(LOCK_ID);
            }

            if (IsWindows) SetCursorPos(savedClickPos.X, savedClickPos.Y);
            Cursor.visible = true;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                if (isPanning) StopPanning();
                if (isWindowClipped && IsWindows)
                {
                    ClipCursor(IntPtr.Zero);
                    isWindowClipped = false;
                }
            }
        }

        private void OnDisable()
        {
            StopPanning();
            ResetIdleState();
            isInFacility = false;
            if (isWindowClipped && IsWindows)
            {
                ClipCursor(IntPtr.Zero);
                isWindowClipped = false;
            }
        }

        private void OnDestroy()
        {
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);

            GameEvents.onGUIAstronautComplexSpawn.Remove(OnFacilityOpen);
            GameEvents.onGUIAstronautComplexDespawn.Remove(OnFacilityClose);

            GameEvents.onGUIMissionControlSpawn.Remove(OnFacilityOpen);
            GameEvents.onGUIMissionControlDespawn.Remove(OnFacilityClose);

            GameEvents.onGUIAdministrationFacilitySpawn.Remove(OnFacilityOpen);
            GameEvents.onGUIAdministrationFacilityDespawn.Remove(OnFacilityClose);

            GameEvents.onGUIRnDComplexSpawn.Remove(OnFacilityOpen);
            GameEvents.onGUIRnDComplexDespawn.Remove(OnFacilityClose);

            StopPanning();
            ResetIdleState();
            isInFacility = false;

            if (isWindowClipped && IsWindows)
            {
                ClipCursor(IntPtr.Zero);
                isWindowClipped = false;
            }

            if (haloTexture != null) Destroy(haloTexture);
        }
    }
}