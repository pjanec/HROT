using System.Reflection;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// The set of assemblies the editor searches when resolving a component/struct type by FQN, and when
/// enumerating component types for pickers.
///
/// <para><b>Why this exists (BP-62).</b>
/// Both resolution paths used to walk <see cref="AppDomain.CurrentDomain"/>'s
/// <see cref="AppDomain.GetAssemblies"/> directly. That returns only assemblies that are
/// <b>already loaded</b>, and the CLR loads them <b>lazily on first use</b> — a
/// <c>ProjectReference</c> does not force a load. So a component whose assembly nothing had touched
/// yet simply did not resolve, and callers read that <c>null</c> as "not a component" rather than
/// "don't know yet". Concretely: <c>BlueprintCommandSink.TryBakeCollectionConsumer</c> gated on
/// <c>IsWritableComponent</c>, got <c>false</c> for an unloaded component, and left the node
/// silently unbaked — surfacing much later as a canvas bake-incomplete error / Stage2 BP2067, far
/// from the wire that caused it.
/// </para>
///
/// <para><b>What this does.</b> Once per process, walks the reference graph of everything currently
/// loaded and force-loads each referenced assembly, so lazily-loaded game assemblies are present
/// before any scan. The editor cannot reference game assemblies directly (layering — e.g.
/// <c>Hrot.Blueprints.Editor</c> must not depend on <c>Hrot.AI.Behaviors</c>), so discovering them
/// through the host process's reference graph is the only option that does not invert that
/// dependency.
/// </para>
///
/// <para><b>Why the assembly list itself is not cached.</b> <see cref="Assemblies"/> always returns
/// the live <c>AppDomain</c> list. Blueprint hot reload loads generated code into new
/// <c>AssemblyLoadContext</c>s at runtime (<c>QuickReloadService</c> / <c>FullRebuildService</c>);
/// caching the array would hide those. Only the one-time force-load pass is memoised.
/// </para>
/// </summary>
internal static class EditorTypeResolutionScope
{
    private static readonly object s_gate = new();
    private static bool s_referencedLoaded;

    /// <summary>
    /// All assemblies to search, with referenced-but-not-yet-loaded ones force-loaded first.
    /// Always reflects the live <c>AppDomain</c> so hot-reloaded assemblies are included.
    /// </summary>
    internal static IReadOnlyList<Assembly> Assemblies()
    {
        EnsureReferencedAssembliesLoaded();
        return AppDomain.CurrentDomain.GetAssemblies();
    }

    /// <summary>
    /// Transitively force-loads every assembly referenced by anything already loaded. Idempotent and
    /// thread-safe; the walk runs at most once per process. Individual failures are swallowed — a
    /// reference that cannot be resolved (missing on disk, native, unresolvable ALC) simply stays
    /// absent, exactly as before this existed.
    /// </summary>
    internal static void EnsureReferencedAssembliesLoaded()
    {
        if (Volatile.Read(ref s_referencedLoaded)) return;

        lock (s_gate)
        {
            if (s_referencedLoaded) return;

            var seen  = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<Assembly>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.FullName is { Length: > 0 } name && seen.Add(name))
                    queue.Enqueue(asm);
            }

            while (queue.Count > 0)
            {
                AssemblyName[] refs;
                try { refs = queue.Dequeue().GetReferencedAssemblies(); }
                catch { continue; }   // dynamic / unintrospectable

                foreach (var reference in refs)
                {
                    if (reference.FullName is not { Length: > 0 } refName) continue;
                    if (!seen.Add(refName)) continue;

                    try { queue.Enqueue(Assembly.Load(reference)); }
                    catch { /* unresolvable reference -- skip, same as pre-BP-62 behaviour */ }
                }
            }

            Volatile.Write(ref s_referencedLoaded, true);
        }
    }
}
