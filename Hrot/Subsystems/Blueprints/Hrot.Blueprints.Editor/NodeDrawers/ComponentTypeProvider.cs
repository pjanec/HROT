using System.Reflection;
using Fdp.Core;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Discovers the fully-qualified names of ECS component types for the "Component Type" picker
/// used by <see cref="ComponentNodeDrawers"/> / <see cref="ComponentPaletteEntries"/>.
/// Mirrors <see cref="ISharedStructTypeProvider"/>.
/// </summary>
public interface IComponentTypeProvider
{
    /// <summary>
    /// Returns the FQNs of all discoverable ECS component types: sorted (ordinal) and
    /// de-duplicated.
    /// </summary>
    IReadOnlyList<string> GetComponentTypeFqns();
}

/// <summary>
/// Default <see cref="IComponentTypeProvider"/>: scans every assembly currently loaded into
/// <see cref="AppDomain.CurrentDomain"/> for concrete (non-interface, non-abstract) struct/class
/// types decorated with <see cref="ComponentIdAttribute"/> — the SAME marker
/// <see cref="Fdp.Core.ComponentTypeRegistry"/> requires on every ECS component type (unmanaged
/// struct or managed class alike; see <c>ComponentTypeRegistry.GetOrRegisterManaged</c>'s
/// "MUST have a [ComponentId] attribute" enforcement). There is no separate marker
/// interface/base class for "is this a component" — <c>[ComponentId]</c> IS the predicate.
/// <para>
/// The scan result is cached on first call (per instance), mirroring
/// <see cref="ReflectionSharedStructTypeProvider"/>.
/// </para>
/// </summary>
public sealed class ReflectionComponentTypeProvider : IComponentTypeProvider
{
    private IReadOnlyList<string>? _cached;

    public IReadOnlyList<string> GetComponentTypeFqns()
        => _cached ??= ComponentTypeScan.Compute(
            static t => t.IsDefined(typeof(ComponentIdAttribute), inherit: false));
}

/// <summary>
/// CA-04 (Slice W1) — writable-only view over discovered ECS component types for the
/// <c>SetComponent</c> write picker/palette (<see cref="ComponentNodeDrawers.SetComponentNodeDrawer"/>
/// / <see cref="ComponentPaletteEntries.SetComponentEntries"/>). Same <c>[ComponentId]</c> scan as
/// <see cref="ReflectionComponentTypeProvider"/> (the read side, which stays all-components), ADDITIONALLY
/// filtered to types that ALSO carry <see cref="Fdp.Core.BlueprintWritableAttribute"/> -- the write gate
/// (Q#16): system-output components (e.g. <c>SimTransform</c>) never carry that attribute and are
/// therefore never offered here. One reflection scan shared with the read provider via
/// <see cref="ComponentTypeScan"/> (DRY), just a different predicate.
/// </summary>
public sealed class ReflectionWritableComponentTypeProvider : IComponentTypeProvider
{
    private IReadOnlyList<string>? _cached;

    public IReadOnlyList<string> GetComponentTypeFqns()
        => _cached ??= ComponentTypeScan.Compute(static t =>
            t.IsDefined(typeof(ComponentIdAttribute), inherit: false)
            && t.IsDefined(typeof(BlueprintWritableAttribute), inherit: false));
}

/// <summary>
/// Shared assembly scan used by both <see cref="ReflectionComponentTypeProvider"/> and
/// <see cref="ReflectionWritableComponentTypeProvider"/> -- one walk over
/// <see cref="AppDomain.CurrentDomain"/>'s loaded assemblies, filtered by the caller's predicate, so
/// the read/write discovery paths never duplicate the (identical) enumeration/exception-handling
/// logic and can never drift out of sync on how types are found/excluded.
/// </summary>
internal static class ComponentTypeScan
{
    internal static IReadOnlyList<string> Compute(Func<Type, bool> predicate)
    {
        var result = new List<string>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Some types in this assembly failed to load (missing dependency, etc.) --
                // keep whichever ones did load rather than skipping the whole assembly.
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                // Dynamic or otherwise unintrospectable assembly -- skip it.
                continue;
            }

            foreach (var t in types)
            {
                if (t.IsInterface) continue;
                if (t.IsAbstract && t.IsClass) continue; // static classes / abstract bases aren't instantiable components
                if (!predicate(t)) continue;
                if (t.FullName is { Length: > 0 } fqn) result.Add(fqn);
            }
        }

        return result
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }
}
