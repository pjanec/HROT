using System.Reflection;
using Fbt.Kernel;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Discovers the fully-qualified names of Category-1 shared-struct types (C# structs
/// decorated with <see cref="BlackboardDtoStructAttribute"/>) for the "Shared Type FQN"
/// picker used by <see cref="GetSharedNodeDrawer"/>/<see cref="SetSharedNodeDrawer"/>.
/// </summary>
public interface ISharedStructTypeProvider
{
    /// <summary>
    /// Returns the FQNs of all discoverable shared-struct types: sorted (ordinal) and
    /// de-duplicated.
    /// </summary>
    IReadOnlyList<string> GetSharedStructTypeFqns();
}

/// <summary>
/// Default <see cref="ISharedStructTypeProvider"/>: scans every assembly currently loaded
/// into <see cref="AppDomain.CurrentDomain"/> for value types decorated with
/// <see cref="BlackboardDtoStructAttribute"/> -- the same predicate
/// <see cref="Hrot.Editor.AiShared.Blackboard.BlackboardFieldClassifier"/> uses to recognize
/// Category-1 shared-struct fields (see <c>IsKnownType</c> there).
/// <para>
/// The scan result is cached on first call (per instance) since the set of loaded
/// assemblies rarely changes within an editor session; construct a fresh instance
/// (or add a cache-invalidation hook) if that assumption ever needs to be relaxed.
/// </para>
/// </summary>
public sealed class ReflectionSharedStructTypeProvider : ISharedStructTypeProvider
{
    private IReadOnlyList<string>? _cached;

    public IReadOnlyList<string> GetSharedStructTypeFqns()
        => _cached ??= ComputeSharedStructTypeFqns();

    private static IReadOnlyList<string> ComputeSharedStructTypeFqns()
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
                if (!t.IsValueType) continue;
                if (!t.IsDefined(typeof(BlackboardDtoStructAttribute), inherit: false)) continue;
                if (t.FullName is { Length: > 0 } fqn) result.Add(fqn);
            }
        }

        return result
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }
}
