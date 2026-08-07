using System;
using System.Reflection;
using UnityEngine;

namespace EnhancedCursor
{
    public static class ModChecker
    {
        private static bool isModPresent = false;
        private static FieldInfo cachedField = null;
        private static PropertyInfo cachedProperty = null;

        public static void InitializeCheck(string assemblyName, string fullClassName, string memberName)
        {
            isModPresent = false;
            cachedField = null;
            cachedProperty = null;

            try
            {
                if (AssemblyLoader.loadedAssemblies == null) return;

                foreach (var loadedAssembly in AssemblyLoader.loadedAssemblies)
                {
                    if (loadedAssembly.name.IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Type targetType = loadedAssembly.assembly.GetType(fullClassName);
                        if (targetType == null) continue;

                        cachedField = targetType.GetField(memberName, BindingFlags.Public | BindingFlags.Static);
                        cachedProperty = targetType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);

                        if (cachedField != null || cachedProperty != null)
                        {
                            isModPresent = true;
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnhancedCursor] Soft-check initialization failed for {assemblyName}: {ex.Message}");
            }
        }

        public static bool IsActiveFast()
        {
            if (!isModPresent) return false;

            try
            {
                if (cachedField != null && cachedField.FieldType == typeof(bool))
                {
                    return (bool)cachedField.GetValue(null);
                }

                if (cachedProperty != null && cachedProperty.PropertyType == typeof(bool))
                {
                    return (bool)cachedProperty.GetValue(null, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnhancedCursor] Soft-check failed: {ex.Message}");
            }

            return false;
        }

        public static bool IsLoaded(string assemblyName)
        {
            if (AssemblyLoader.loadedAssemblies == null) return false;

            foreach (var loadedAssembly in AssemblyLoader.loadedAssemblies)
            {
                if (loadedAssembly.name.IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
