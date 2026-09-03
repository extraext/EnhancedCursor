using UnityEngine;
using System;
using System.Runtime.InteropServices;

namespace EnhancedCursor
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class EnhancedCursorAddon : MonoBehaviour
    {
        public static EnhancedCursorAddon Instance { get; private set; }
		
		public bool IsPanning => isPanning;
        public bool IsPanHiding => isPanning && (IsInIVA || !CursorSettings.HideCursorOnlyOnPanMove || isPanMoving);
		private bool IsInIVA => HighLogic.LoadedScene == GameScenes.FLIGHT && 
                        CameraManager.Instance != null && 
                        (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA || 
                         CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Internal);

        [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr lpRect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
		[DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
		private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
		private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
		private bool skipNextRightClick = false;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        private bool isPanning = false;
        private bool isPanMoving = false;
        private Vector3 panStartMousePos;
        
        private bool isUIHidden = false;
        private bool isInFacility = false;
        private POINT savedClickPos;
        private const string LOCK_ID = "EnhancedCursor_PanLock";

        private Vector3 lastMousePos;
        private float idleTimer = 0f;
        private bool isIdleHidden = false;
        private bool isWindowClipped = false;
        private bool wasHiddenByUs = false; 
        private bool wasIVA = false;

        private Texture2D haloTexture = null;

        private void Awake() => Instance = this;

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
            ModChecker.InitializeCheck("ThroughTheEyes", "ThroughTheEyes.ThroughTheEyes", "active");

            CursorSettings.ApplyHardwareCursor(true);
        }

		public bool ShouldSuppressHide()
		{
			if (Cursor.lockState == CursorLockMode.Locked)
			{
				return false;
			}

			if (CameraManager.Instance != null && CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA)
			{
				return false;
			}

			return CursorSettings.ModEnabled && CursorSettings.EnableCustomCursor;
		}

        private void OnHideUI() => isUIHidden = true;
        private void OnShowUI()
        {
            isUIHidden = false;
            CursorSettings.ApplyHardwareCursor(true);
        }

        private void OnFacilityOpen() => isInFacility = true;
        private void OnFacilityClose()
        {
            isInFacility = false;
            CursorSettings.ApplyHardwareCursor(true);
        }

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

			bool isInvalidScene = !CursorSettings.IsActiveInCurrentScene() || 
			HighLogic.LoadedScene == GameScenes.LOADING || 
			HighLogic.LoadedScene == GameScenes.LOADINGBUFFER ||
			(HighLogic.LoadedScene == GameScenes.SPACECENTER && isInFacility);

			if (isInvalidScene)
			{
				if (isPanning) StopPanning();
				ResetIdleState();
				return;
			}

			TrackIdleActivity();

			if (ModChecker.IsActiveFast())
			{
				if (isPanning) StopPanning();
				return;
			}
			
			if (HighLogic.LoadedScene == GameScenes.MAINMENU || 
				HighLogic.LoadedScene == GameScenes.SETTINGS || 
				HighLogic.LoadedScene == GameScenes.CREDITS)
			{
				if (isPanning) StopPanning();
				return;
			}

			bool isIVA = IsInIVA;

            if (isIVA != wasIVA)
		{
			if (wasIVA && !isIVA) 
			{
				if (isPanning)
				{
					StopPanning();
					skipNextRightClick = true;
					mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
					mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
				}
			}
			else if (!wasIVA && isIVA) 
			{
				if (isPanning) StopPanning();
				mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
			}

			Cursor.lockState = CursorLockMode.None;
			wasIVA = isIVA;
		}

			if (isIVA)
		{
			if (Input.GetMouseButtonDown(1))
			{
				if (!isPanning) StartPanning();
				else StopPanning();
			}
		}
		else
		{
			if (skipNextRightClick)
			{
				if (Input.GetMouseButtonUp(1)) skipNextRightClick = false;
			}
			else
			{
				if (Input.GetMouseButtonDown(1)) 
				{
					StartPanning();
				}
				else if (Input.GetMouseButtonUp(1) && isPanning) 
				{
					StopPanning();
				}
			}
		}

            if (isPanning && !isPanMoving)
			{
				if (Vector3.Distance(Input.mousePosition, panStartMousePos) >= 1.5f) 
				{
					isPanMoving = true;

					if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
					{
						InputLockManager.SetControlLock(ControlTypes.KSC_FACILITIES, LOCK_ID);
					}
				}
			}
		}

        private void LateUpdate()
        {
            EnforceCursorState();
        }

        private void OnGUI()
        {
            EnforceCursorState();

            if (Event.current.type != EventType.Repaint) return;

            if (CursorSettings.IsActiveInCurrentScene() && !IsPanHiding && !isIdleHidden && !(CursorSettings.HideCursorInF2 && isUIHidden))
            {
                if (CursorSettings.EnableCursorHalo && Cursor.visible && haloTexture != null)
                {
                    GUI.depth = -10000;
                    Vector2 mousePos = Event.current.mousePosition;
                    float size = CursorSettings.HaloSize;
                    Rect haloRect = new Rect(mousePos.x - size / 2f, mousePos.y - size / 2f, size, size);
                    Color prevColor = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, CursorSettings.HaloOpacity);
                    GUI.DrawTexture(haloRect, haloTexture);
                    GUI.color = prevColor;
                }
            }
        }

        private void EnforceCursorState()
        {
            bool isInvalidScene = !CursorSettings.IsActiveInCurrentScene() || 
			HighLogic.LoadedScene == GameScenes.LOADING || 
			HighLogic.LoadedScene == GameScenes.LOADINGBUFFER ||
			(HighLogic.LoadedScene == GameScenes.SPACECENTER && isInFacility);

            if (isInvalidScene)
            {
                if (isPanning) StopPanning();
                return;
            }

            bool shouldHideCursor = IsPanHiding || (CursorSettings.HideCursorInF2 && isUIHidden) || isIdleHidden;

            if (shouldHideCursor)
            {
                if (IsPanHiding) SetCursorPos(savedClickPos.X, savedClickPos.Y);
                Cursor.visible = false;
                wasHiddenByUs = true;
            }
            else
            {
                if (CursorSettings.IsActiveInCurrentScene())
                {
                    CursorSettings.ApplyHardwareCursor(true);
                    
                    if (wasHiddenByUs)
                    {
                        Cursor.visible = true;
                        wasHiddenByUs = false;
                    }
                }
            }
        }

        private void UpdateWindowClamp()
        {
            bool shouldClamp = CursorSettings.IsActiveInCurrentScene() && CursorSettings.LockToWindow && Application.isFocused && !isPanning;

            if (shouldClamp)
            {
                IntPtr hWnd = GetActiveWindow();
                if (hWnd != IntPtr.Zero)
                {
                    RECT clientRect;
                    if (GetClientRect(hWnd, out clientRect))
                    {
                        POINT upperLeft = new POINT { X = clientRect.Left, Y = clientRect.Top };
                        POINT lowerRight = new POINT { X = clientRect.Right, Y = clientRect.Bottom };

                        if (ClientToScreen(hWnd, ref upperLeft) && ClientToScreen(hWnd, ref lowerRight))
                        {
                            RECT screenRect = new RECT
                            {
                                Left = upperLeft.X,
                                Top = upperLeft.Y,
                                Right = lowerRight.X,
                                Bottom = lowerRight.Y
                            };

                            ClipCursor(ref screenRect);
                            isWindowClipped = true;
                            return;
                        }
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
                    CursorSettings.isIdleHidden = false;
                    CursorSettings.ApplyHardwareCursor(true);
                }
            }
            else if (CursorSettings.EnableIdleAutoHide && CursorSettings.IsActiveInCurrentScene() && !isUIHidden)
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= CursorSettings.IdleHideTimeout)
                {
                    isIdleHidden = true;
                    CursorSettings.isIdleHidden = true;
                    Cursor.visible = false;
                }
            }
        }

        private void ResetIdleState()
        {
            idleTimer = 0f;
            if (isIdleHidden)
            {
                isIdleHidden = false;
                CursorSettings.isIdleHidden = false;
                CursorSettings.ApplyHardwareCursor(true);
            }
        }

        private bool GetWindowCenterPoint(out POINT centerPt)
        {
            centerPt = new POINT { X = 0, Y = 0 };
            IntPtr hWnd = GetActiveWindow();
            if (hWnd == IntPtr.Zero) return false;

            RECT clientRect;
            if (GetClientRect(hWnd, out clientRect))
            {
                POINT centerClient = new POINT
                {
                    X = (clientRect.Left + clientRect.Right) / 2,
                    Y = (clientRect.Top + clientRect.Bottom) / 2
                };

                if (ClientToScreen(hWnd, ref centerClient))
                {
                    centerPt = centerClient;
                    return true;
                }
            }
            return false;
        }

        private void StartPanning()
		{
			isPanning = true;
			isPanMoving = false;
			panStartMousePos = Input.mousePosition;

			bool shouldPin = CursorSettings.PinToCenterScreen;
			if (shouldPin && CursorSettings.PinOnlyInIVA)
			{
				bool isIVA = (HighLogic.LoadedScene == GameScenes.FLIGHT && 
						CameraManager.Instance != null && 
					   (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA || 
					  CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Internal));
                     
				if (!isIVA) shouldPin = false; 
			}

			if (shouldPin)
			{
				if (!GetWindowCenterPoint(out savedClickPos))
				{
					GetCursorPos(out savedClickPos);
				}
			}
			else
			{
				GetCursorPos(out savedClickPos);
			}
		}

        private void StopPanning()
        {
			if (!isPanning) return;

			isPanning = false;

			InputLockManager.RemoveControlLock(LOCK_ID);

			Cursor.lockState = CursorLockMode.None;
			mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);

			if (isPanMoving || !CursorSettings.HideCursorOnlyOnPanMove)
			{
				SetCursorPos(savedClickPos.X, savedClickPos.Y);
			}

            isPanMoving = false;

            Cursor.visible = true;
            wasHiddenByUs = false; 
            CursorSettings.ApplyHardwareCursor(true);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                if (isPanning) StopPanning();
                if (isWindowClipped)
                {
                    ClipCursor(IntPtr.Zero);
                    isWindowClipped = false;
                }
            }
            else
            {
                CursorSettings.ApplyHardwareCursor(true);
            }
        }

        private void OnDisable()
        {
            StopPanning();
            InputLockManager.RemoveControlLock(LOCK_ID);
            ResetIdleState();
            isInFacility = false;

            if (isWindowClipped)
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
            InputLockManager.RemoveControlLock(LOCK_ID);
            ResetIdleState();
            isInFacility = false;

            if (isWindowClipped)
            {
                ClipCursor(IntPtr.Zero);
                isWindowClipped = false;
            }

            if (haloTexture != null) Destroy(haloTexture);
        }
    }
}
