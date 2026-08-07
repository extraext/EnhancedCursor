#pragma warning disable CS0649

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class CursorHotspotEntry
{
    public string cursorName;
    public float hotspotX;
    public float hotspotY;

    public CursorHotspotEntry(string cursorName, Vector2 hotspot)
    {
        this.cursorName = cursorName;
        this.hotspotX = hotspot.x;
        this.hotspotY = hotspot.y;
    }

    public Vector2 GetHotspotVector()
    {
        return new Vector2(hotspotX, hotspotY);
    }
}

[System.Serializable]
public class CursorHotspotConfigData
{
    public List<CursorHotspotEntry> entries = new List<CursorHotspotEntry>();
}

public class CursorHotspotManager : MonoBehaviour
{
    [Header("Engine Cursor Settings")]
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;
    [SerializeField] private Vector2 defaultHotspot = Vector2.zero;

    [Header("Visual Indicator (Optional)")]
    [Tooltip("Assign your existing Red Dot UI RectTransform here to automatically sync its position.")]
    [SerializeField] private RectTransform redDotTransform = null;

    private Dictionary<string, Vector2> hotspotDictionary = new Dictionary<string, Vector2>();
    private Texture2D activeCursorTexture;
    private string configFilePath;

    private void Awake()
    {
        configFilePath = Path.Combine(Application.persistentDataPath, "cursor_hotspots_config.json");
        LoadHotspotConfigurations();
    }

    private void OnApplicationQuit()
    {
        SaveHotspotConfigurations();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveHotspotConfigurations();
        }
    }

    /// <summary>
    /// Swaps the active cursor texture, retrieves its unique hotspot coordinate (Top-Left 0,0 origin),
    /// applies it to the hardware cursor, and moves the visual red dot indicator
    /// </summary>
    public void SetCursor(Texture2D newCursorTexture)
    {
        if (newCursorTexture == null)
        {
            Debug.LogWarning("[CursorHotspotManager] Target texture is null. Resetting to system default.");
            Cursor.SetCursor(null, Vector2.zero, cursorMode);
            activeCursorTexture = null;
            UpdateRedDotVisual(Vector2.zero);
            return;
        }

        activeCursorTexture = newCursorTexture;

        if (string.IsNullOrEmpty(activeCursorTexture.name))
        {
            activeCursorTexture.name = "Unnamed_Cursor_" + activeCursorTexture.GetInstanceID();
        }

        string cursorKey = activeCursorTexture.name;

        // hotspot save data  for individual .pngs
        Vector2 targetHotspot;
        if (hotspotDictionary.TryGetValue(cursorKey, out Vector2 savedHotspot))
        {
            targetHotspot = savedHotspot;
        }
        else
        {
            targetHotspot = defaultHotspot;
            hotspotDictionary[cursorKey] = targetHotspot;
            SaveHotspotConfigurations();
        }

        Cursor.SetCursor(activeCursorTexture, targetHotspot, cursorMode);

        UpdateRedDotVisual(targetHotspot);
    }

    /// <summary>
    /// Updates the stored hotspot coordinates for the current active PNG texture,
    /// applies it immediately to the active cursor, updates the red dot, and saves to disk
    /// </summary>
    public void UpdateCurrentCursorHotspot(Vector2 newHotspot)
    {
        if (activeCursorTexture == null)
        {
            Debug.LogError("[CursorHotspotManager] Cannot update hotspot: No active cursor texture set.");
            return;
        }

        string cursorKey = activeCursorTexture.name;
        hotspotDictionary[cursorKey] = newHotspot;

        Cursor.SetCursor(activeCursorTexture, newHotspot, cursorMode);

        UpdateRedDotVisual(newHotspot);

        // persist change
        SaveHotspotConfigurations();
    }

    /// <summary>
    /// Syncs the screen UI position of your existing Red Dot indicator to match the hotspot coordinates
    /// </summary>
    private void UpdateRedDotVisual(Vector2 hotspotCoordinates)
    {
        if (redDotTransform == null) return;

        redDotTransform.anchoredPosition = new Vector2(hotspotCoordinates.x, -hotspotCoordinates.y);
    }

    /// <summary>
    /// Returns the stored top-left hotspot offset for a given cursor name string
    /// </summary>
    public Vector2 GetHotspotForCursor(string cursorName)
    {
        if (hotspotDictionary.TryGetValue(cursorName, out Vector2 hotspot))
        {
            return hotspot;
        }
        return defaultHotspot;
    }

    /// <summary>
    /// Serializes current hotspot dictionary to persistent storage
    /// </summary>
    public void SaveHotspotConfigurations()
    {
        try
        {
            CursorHotspotConfigData configData = new CursorHotspotConfigData();
            foreach (KeyValuePair<string, Vector2> pair in hotspotDictionary)
            {
                configData.entries.Add(new CursorHotspotEntry(pair.Key, pair.Value));
            }

            string json = SimpleJson.ToJson(configData);
            File.WriteAllText(configFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CursorHotspotManager] Failed to save hotspot configurations: {ex.Message}");
        }
    }

    /// <summary>
    /// Deserializes JSON file from persistent storage.
    /// </summary>
    public void LoadHotspotConfigurations()
    {
        hotspotDictionary.Clear();

        if (!File.Exists(configFilePath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(configFilePath);
            CursorHotspotConfigData configData = SimpleJson.FromJson(json);

            if (configData != null && configData.entries != null)
            {
                foreach (CursorHotspotEntry entry in configData.entries)
                {
                    if (!string.IsNullOrEmpty(entry.cursorName))
                    {
                        hotspotDictionary[entry.cursorName] = entry.GetHotspotVector();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CursorHotspotManager] Failed to load hotspot configurations: {ex.Message}");
        }

        if (activeCursorTexture != null)
        {
            SetCursor(activeCursorTexture);
        }
    }

    /// <summary>
    /// Standalone JSON Helper to ensure clean builds across standard .NET Framework compilers
    /// without relying on specific Unity JSON module assembly references
    /// </summary>
    private static class SimpleJson
    {
        public static string ToJson(CursorHotspotConfigData data)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"entries\": [");
            for (int i = 0; i < data.entries.Count; i++)
            {
                CursorHotspotEntry e = data.entries[i];
                string escapedName = e.cursorName.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.Append($"    {{ \"cursorName\": \"{escapedName}\", \"hotspotX\": {e.hotspotX.ToString(CultureInfo.InvariantCulture)}, \"hotspotY\": {e.hotspotY.ToString(CultureInfo.InvariantCulture)} }}");
                if (i < data.entries.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static CursorHotspotConfigData FromJson(string json)
        {
            CursorHotspotConfigData config = new CursorHotspotConfigData();
            if (string.IsNullOrEmpty(json)) return config;

            string[] blocks = json.Split(new char[] { '{', '}' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string block in blocks)
            {
                if (block.Contains("\"cursorName\""))
                {
                    string name = ExtractStringValue(block, "cursorName");
                    float x = ExtractFloatValue(block, "hotspotX");
                    float y = ExtractFloatValue(block, "hotspotY");

                    if (!string.IsNullOrEmpty(name))
                    {
                        config.entries.Add(new CursorHotspotEntry(name, new Vector2(x, y)));
                    }
                }
            }
            return config;
        }

        private static string ExtractStringValue(string text, string key)
        {
            int keyIdx = text.IndexOf($"\"{key}\"");
            if (keyIdx < 0) return string.Empty;
            int colonIdx = text.IndexOf(':', keyIdx);
            if (colonIdx < 0) return string.Empty;
            int quoteStart = text.IndexOf('"', colonIdx);
            if (quoteStart < 0) return string.Empty;
            int quoteEnd = text.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return string.Empty;
            return text.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        private static float ExtractFloatValue(string text, string key)
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
#pragma warning restore CS0649