using HarmonyLib;
using UnityEngine;

namespace EnhancedCursor
{
    [HarmonyPatch(typeof(Cursor))]
    public static class UnityCursorPatches
    {
        [HarmonyPatch("SetCursor", typeof(Texture2D), typeof(Vector2), typeof(CursorMode))]
        [HarmonyPrefix]
        public static void SetCursor_Prefix(ref Texture2D texture, ref Vector2 hotspot, ref CursorMode cursorMode)
        {
            if (CursorSettings.ModEnabled && CursorSettings.EnableCustomCursor && CursorSettings.HasActiveCursor && CursorSettings.IsActiveInCurrentScene())
            {
                if (CursorSettings.ActiveCursorItem.Texture != null)
                {
                    texture = CursorSettings.ActiveCursorItem.Texture;
                    hotspot = new Vector2(CursorSettings.HotspotX, CursorSettings.HotspotY);
                    cursorMode = CursorMode.Auto;
                }
            }
        }

        [HarmonyPatch("visible", MethodType.Setter)]
        [HarmonyPrefix]
        public static bool set_visible_Prefix(ref bool value)
        {
            if (CursorSettings.ModEnabled && CursorSettings.EnableCustomCursor && CursorSettings.HasActiveCursor && CursorSettings.IsActiveInCurrentScene())
            {
                if (EnhancedCursorAddon.Instance != null && EnhancedCursorAddon.Instance.ShouldSuppressHide())
                {
                    if (!value) return false;
                }
            }
            return true;
        }
    }
}