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
        => _cached ??= ComputeComponentTypeFqns();

    private static IReadOnlyList<string> ComputeComponentTypeFqns()
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
                if (!t.IsDefined(typeof(ComponentIdAttribute), inherit: false)) continue;
                if (t.FullName is { Length: > 0 } fqn) result.Add(fqn);
            }
        }

        return result
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }
}
