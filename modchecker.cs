using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace EnhancedCursor
{
    public static class ModChecker
    {
        private static bool isModPresent = false;
        private static Func<bool> activeGetter = null;

        public static void InitializeCheck(string assemblyName, string fullClassName, string memberName)
        {
            isModPresent = false;
            activeGetter = null;

            try
            {
                if (AssemblyLoader.loadedAssemblies == null) return;

                foreach (var loadedAssembly in AssemblyLoader.loadedAssemblies)
                {
                    if (loadedAssembly.name.IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Type targetType = loadedAssembly.assembly.GetType(fullClassName);
                        if (targetType == null) continue;

                        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                        FieldInfo field = targetType.GetField(memberName, flags);
                        if (field != null && field.FieldType == typeof(bool))
                        {
                            var fieldExpr = Expression.Field(null, field);
                            activeGetter = Expression.Lambda<Func<bool>>(fieldExpr).Compile();
                            isModPresent = true;
                            break;
                        }

                        PropertyInfo prop = targetType.GetProperty(memberName, flags);
                        if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
                        {
                            MethodInfo getMethod = prop.GetGetMethod(true);
                            if (getMethod != null)
                            {
                                activeGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getMethod);
                                isModPresent = true;
                                break;
                            }
                        }
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
            if (!isModPresent || activeGetter == null) return false;

            try
            {
                return activeGetter();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnhancedCursor] Soft-check evaluation failed: {ex.Message}");
                return false;
            }
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
