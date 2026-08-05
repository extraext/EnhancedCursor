using System;
using System.Reflection;
using UnityEngine;

namespace EnhancedCursor
{
    public static class ModChecker
    {
		//this .cs file is mainly for having compatibility with some mods camera related like TTEOAK so enhancedcursor does not break with them
        
        private static bool isModPresent = false;
        private static FieldInfo cachedField = null;
        private static PropertyInfo cachedProperty = null;

        /// <summary>
        /// initializes and caches the target mod's reflection references once at startup.
        /// </summary>
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
                // error detection w/ silence
                Debug.LogWarning($"[EnhancedCursor] Soft-check initialization failed for {assemblyName}: {ex.Message}");
            }
        }

        /// <summary>
        /// fast cached check to call inside Update().
        /// </summary>
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
                // error detection w/ silence
                Debug.LogWarning($"[EnhancedCursor] Soft-check failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// checks if a third-party mod's assembly is loaded in the game.
        /// </summary>
        /// <param name="assemblyName">Partial or full name of the DLL file (case-insensitive)</param>
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
