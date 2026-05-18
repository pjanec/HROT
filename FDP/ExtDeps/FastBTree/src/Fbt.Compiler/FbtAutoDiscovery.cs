using System;
using System.Reflection;
using Fbt.Runtime;

namespace Fbt.Compiler
{
    /// <summary>
    /// Scans all loaded assemblies for types annotated with <see cref="FbtRegistrarAttribute"/>
    /// and invokes their <c>RegisterAll</c> method.
    /// </summary>
    public static class FbtAutoDiscovery
    {
        /// <summary>
        /// Scans <see cref="AppDomain.CurrentDomain"/> assemblies for <c>[FbtRegistrar]</c>-annotated
        /// types and invokes <c>RegisterAll(registry)</c> via reflection on each.
        /// Non-reflectable assemblies are silently skipped.
        /// Registrar invocation failures (e.g. TBlackboard/TContext type mismatch) are silently skipped.
        /// </summary>
        public static void ScanAndRegister<TBlackboard, TContext>(
            ActionRegistry<TBlackboard, TContext> registry)
            where TBlackboard : unmanaged
            where TContext : struct, IAIContext
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!type.IsDefined(typeof(FbtRegistrarAttribute), false))
                            continue;

                        // Iterate all RegisterAll overloads; try each and silently skip mismatches
                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (method.Name != "RegisterAll" || method.IsGenericMethodDefinition)
                                continue;

                            try
                            {
                                method.Invoke(null, new object[] { registry });
                            }
                            catch
                            {
                                // Skip registrars that fail (type mismatch for TBlackboard/TContext)
                            }
                        }
                    }
                }
                catch
                {
                    // Skip non-reflectable assemblies (COM, dynamic, etc.)
                }
            }
        }
    }
}
