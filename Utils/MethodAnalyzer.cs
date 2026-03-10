using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace STS2ShowIncomingDamage.Utils
{
    public static class MethodAnalyzer
    {
        private static readonly Dictionary<string, bool> TypeMethodOverrideCache = [];
        private static readonly Dictionary<string, bool> MethodCallsCache = [];
        private static readonly Dictionary<string, MethodInfo?> MethodCache = [];
        private static readonly Dictionary<string, Type?> AsyncStateMachineTypeCache = [];

        public static bool TypeHasMethodOverride(Type type, string methodName, Type baseType)
        {
            var cacheKey = $"{type.FullName}.{methodName}";

            if (TypeMethodOverrideCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var method = type.GetMethod(methodName);
                if (method == null)
                {
                    TypeMethodOverrideCache[cacheKey] = false;
                    return false;
                }

                var result = method.DeclaringType != baseType && method.DeclaringType != null;
                TypeMethodOverrideCache[cacheKey] = result;
                return result;
            }
            catch
            {
                TypeMethodOverrideCache[cacheKey] = false;
                return false;
            }
        }

        public static bool MethodCallsMethod(Type type, string methodName, string targetMethodName)
        {
            try
            {
                var method = GetCachedMethod(type, methodName);
                return method != null && MethodCallsMethod(method, targetMethodName);
            }
            catch
            {
                return false;
            }
        }

        public static bool MethodCallsMethod(MethodInfo method, string targetMethodName)
        {
            var cacheKey = $"{method.DeclaringType?.FullName}.{method.Name}->{targetMethodName}";

            if (MethodCallsCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var result = AnalyzeMethodCalls(method, targetMethodName);
                MethodCallsCache[cacheKey] = result;
                return result;
            }
            catch
            {
                MethodCallsCache[cacheKey] = false;
                return false;
            }
        }

        private static MethodInfo? GetCachedMethod(Type type, string methodName)
        {
            var cacheKey = $"{type.FullName}.{methodName}";

            if (MethodCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var method = type.GetMethod(methodName);
            MethodCache[cacheKey] = method;
            return method;
        }

        private static bool AnalyzeMethodCalls(MethodInfo method, string targetMethodName)
        {
            try
            {
                var stateMachineType = GetAsyncStateMachineType(method);
                if (stateMachineType != null)
                {
                    var stateMachineCacheKey = $"{stateMachineType.FullName}.MoveNext->{targetMethodName}";
                    if (MethodCallsCache.TryGetValue(stateMachineCacheKey, out var cachedResult))
                        return cachedResult;

                    var result = AnalyzeAsyncStateMachine(stateMachineType, targetMethodName);
                    MethodCallsCache[stateMachineCacheKey] = result;
                    return result;
                }

                var instructions = PatchProcessor.GetCurrentInstructions(method);

                foreach (var instruction in instructions.Where(instruction =>
                             instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt))
                    if (instruction.operand is MethodInfo calledMethod && calledMethod.Name == targetMethodName)
                        return true;

                return false;
            }
            catch (Exception ex)
            {
                Main.Logger.Info($"Exception in AnalyzeMethodCalls: {ex.Message}, trying fallback");
                return FallbackIlAnalysis(method, targetMethodName);
            }
        }

        private static Type? GetAsyncStateMachineType(MethodInfo method)
        {
            var cacheKey = $"{method.DeclaringType?.FullName}.{method.Name}";

            if (AsyncStateMachineTypeCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var asyncAttr = method.GetCustomAttribute<AsyncStateMachineAttribute>();
            var stateMachineType = asyncAttr?.StateMachineType;
            AsyncStateMachineTypeCache[cacheKey] = stateMachineType;
            return stateMachineType;
        }

        private static bool AnalyzeAsyncStateMachine(Type stateMachineType, string targetMethodName)
        {
            try
            {
                var moveNextMethod = stateMachineType.GetMethod("MoveNext",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (moveNextMethod == null) return false;

                var instructions = PatchProcessor.GetCurrentInstructions(moveNextMethod);

                foreach (var instruction in instructions.Where(instruction =>
                             instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt))
                    if (instruction.operand is MethodInfo calledMethod && calledMethod.Name == targetMethodName)
                        return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool FallbackIlAnalysis(MethodInfo method, string targetMethodName)
        {
            try
            {
                var body = method.GetMethodBody();

                var il = body?.GetILAsByteArray();
                if (il == null || il.Length == 0) return false;

                var module = method.Module;
                for (var i = 0; i < il.Length; i++)
                {
                    var opcode = il[i];

                    if (opcode is not (0x28 or 0x6F)) continue;
                    if (i + 4 >= il.Length) continue;

                    var token = BitConverter.ToInt32(il, i + 1);
                    try
                    {
                        var calledMethod = module.ResolveMethod(token);
                        if (calledMethod?.Name == targetMethodName)
                            return true;
                    }
                    catch
                    {
                        // ignored
                    }

                    i += 4;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a method calls Damage targeting enemies (via HittableEnemies or similar patterns).
        /// Returns true if the damage targets enemies, false if it targets the player/owner.
        /// </summary>
        public static bool MethodDamagesEnemies(Type type, string methodName)
        {
            try
            {
                var method = GetCachedMethod(type, methodName);
                if (method == null) return false;

                // Check if method calls get_HittableEnemies - this indicates damage to enemies
                if (MethodCallsMethod(method, "get_HittableEnemies"))
                    return true;

                // Check async state machine
                var stateMachineType = GetAsyncStateMachineType(method);
                if (stateMachineType != null)
                {
                    var moveNextMethod = stateMachineType.GetMethod("MoveNext",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (moveNextMethod != null && MethodCallsMethod(moveNextMethod, "get_HittableEnemies"))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static void ClearCache()
        {
            TypeMethodOverrideCache.Clear();
            MethodCallsCache.Clear();
            MethodCache.Clear();
            AsyncStateMachineTypeCache.Clear();
        }
    }
}
