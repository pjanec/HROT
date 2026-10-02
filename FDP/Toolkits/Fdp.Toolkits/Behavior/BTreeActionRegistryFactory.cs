using System;
using System.Linq;
using System.Reflection;
using Fbt;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// Builds a populated <see cref="ActionRegistry{TBlackboard,TContext}"/> for the BTree
/// node logic of a single assembly by discovering its <c>[Fbt.FbtRegistrar]</c>-decorated
/// classes (the source-generated <c>FbtActionRegistrar</c>) and invoking their
/// <c>RegisterAll(ActionRegistry&lt;byte,BTreeContext&gt;)</c> method.
///
/// <para>
/// Unlike <c>Fbt.Compiler.FbtAutoDiscovery</c> (which scans <b>all</b> loaded assemblies),
/// this builds from <b>one</b> assembly only. That matters for hot-reload: scanning all
/// loaded assemblies would bind stale delegates from a superseded ALC version. The JSON
/// BTree bridges (<c>[BlueprintRegistrar]</c>) and the hand-written
/// <c>AiBehaviorFactory</c> both live in the same assembly as the registrar, so the
/// registry built here resolves every method name baked into that assembly's tree blobs.
/// </para>
/// </summary>
public static class BTreeActionRegistryFactory
{
    /// <summary>
    /// Creates a fresh registry and populates it from every <c>[FbtRegistrar]</c> type in
    /// <paramref name="assembly"/> by invoking each <c>RegisterAll(registry)</c> overload
    /// whose single parameter is an <see cref="ActionRegistry{TBlackboard,TContext}"/> of
    /// <c>byte</c>/<see cref="BTreeContext"/> — the root params slot base (<c>P4</c>-②).
    /// Never throws on a single registrar failure or a partially-loadable assembly — those are
    /// skipped so a bad registrar cannot abort the whole load.
    ///
    /// <para>
    /// ⚠ Both skips are SILENT: a <c>RegisterAll</c> whose parameter type does not match the
    /// comparison below is passed over, and a registrar whose body throws is swallowed. Either
    /// leaves keys unregistered, and <c>Interpreter.BindActions</c> then binds them to a
    /// <c>Failure</c> fallback. <c>BTreeActionRegistryFactoryTests.Scan_BindsEveryNode_ZeroFailureFallbacks</c>
    /// is the rail that turns that into a red.
    /// </para>
    /// </summary>
    public static ActionRegistry<byte, BTreeContext> BuildFromAssembly(Assembly assembly)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));

        var registry = new ActionRegistry<byte, BTreeContext>();

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Partial loads (collectible ALCs with missing deps) — use what resolved.
            types = ex.Types.Where(t => t != null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (!type.IsDefined(typeof(FbtRegistrarAttribute), false))
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "RegisterAll" || method.IsGenericMethodDefinition)
                    continue;

                var ps = method.GetParameters();
                if (ps.Length != 1 ||
                    ps[0].ParameterType != typeof(ActionRegistry<byte, BTreeContext>))
                    continue;

                try
                {
                    method.Invoke(null, new object[] { registry });
                }
                catch
                {
                    // Skip a registrar whose body throws; other registrars still apply.
                }
            }
        }

        return registry;
    }
}
