using System;
using System.Reflection;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Marks a generated class as a Utility AI registrar.
    /// Used by <see cref="UtilityAutoDiscovery"/> to find and invoke RegisterAll at startup.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class UtilityRegistrarAttribute : Attribute { }

    /// <summary>
    /// Scans all loaded assemblies for <see cref="UtilityRegistrarAttribute"/> types and
    /// calls their static void RegisterAll() method exactly once.
    /// </summary>
    public static class UtilityAutoDiscovery
    {
        private static volatile bool _initialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// One-time scan. Safe to call multiple times; only the first call does work.
        /// </summary>
        public static void ScanAndRegister()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;
                ScanInternal();
            }
        }

        private static void ScanInternal()
        {
            var attrType = typeof(UtilityRegistrarAttribute);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var type in types)
                {
                    if (type.GetCustomAttributes(attrType, false).Length == 0) continue;
                    var method = type.GetMethod(
                        "RegisterAll",
                        BindingFlags.Public | BindingFlags.Static,
                        null, Type.EmptyTypes, null);
                    method?.Invoke(null, null);
                }
            }
        }

        /// <summary>
        /// FOR TESTS ONLY. Resets the initialized flag so tests can call ScanAndRegister
        /// multiple times in the same process.
        /// </summary>
        internal static void ResetForTesting() => _initialized = false;

        // ── Decision registrar scan ────────────────────────────────────────────────

        private static volatile bool _decisionsInitialized = false;
        private static readonly object _decisionLock = new object();
        private static UtilityRegistry _cachedDecisionRegistry = null!;

        /// <summary>
        /// One-time scan for <see cref="UtilityRegistrarAttribute"/> types that expose a
        /// static <c>RegisterAll(out UtilityRegistry)</c> method and calls each exactly once,
        /// aggregating the results into a single registry. Safe to call multiple times.
        /// </summary>
        public static void ScanAndRegisterDecisions(out UtilityRegistry registry)
        {
            if (!_decisionsInitialized)
            {
                lock (_decisionLock)
                {
                    if (!_decisionsInitialized)
                    {
                        _decisionsInitialized = true;
                        ScanDecisionsInternal();
                    }
                }
            }
            registry = _cachedDecisionRegistry;
        }

        private static void ScanDecisionsInternal()
        {
            var combined     = new UtilityRegistry();
            var attrType     = typeof(UtilityRegistrarAttribute);
            var registryType = typeof(UtilityRegistry);
            var byRefType    = registryType.MakeByRefType();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type.GetCustomAttributes(attrType, false).Length == 0) continue;

                    var method = type.GetMethod(
                        "RegisterAll",
                        BindingFlags.Public | BindingFlags.Static,
                        null, new[] { byRefType }, null);
                    if (method == null) continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].IsOut) continue;

                    var args = new object?[] { null };
                    method.Invoke(null, args);
                    if (args[0] is UtilityRegistry r)
                        combined.MergeFrom(r);
                }
            }

            _cachedDecisionRegistry = combined;
        }

        /// <summary>
        /// FOR TESTS ONLY. Resets the decision-scan state so tests can call
        /// <see cref="ScanAndRegisterDecisions"/> multiple times in the same process.
        /// </summary>
        internal static void ResetDecisionsForTesting()
        {
            _decisionsInitialized   = false;
            _cachedDecisionRegistry = null!;
        }
    }
}
