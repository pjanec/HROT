using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <see cref="IEnumValueProvider"/> that reflects project enum types at editor time (net8.0).
/// <para>
/// Given a <see cref="TypeKey"/> whose <c>Id</c> starts with <c>"global::"</c> (the Blueprint
/// enum sentinel, per ENUM-DESIGN.md §RESOLVED), strips the prefix, resolves the CLR
/// <see cref="Type"/> by scanning loaded assemblies, and returns one <see cref="EnumValueEntry"/>
/// per enum member (Value = underlying integer cast to <c>long</c>, DisplayName = member name).
/// Returns an empty list when the TypeKey is not an enum sentinel or the type cannot be resolved.
/// </para>
/// <para>
/// Thread-safety: the provider is stateless except for a best-effort resolved-type cache that
/// is populated lazily and never mutated once set.  Reads are always safe; a benign race on the
/// first call may resolve the type twice (harmless).
/// </para>
/// </summary>
internal sealed class BlueprintEnumValueProvider : IEnumValueProvider
{
    private const string GlobalPrefix = "global::";

    // Lazy cache: enum TypeKey.Id → resolved entries (null = not an enum / unresolvable).
    private readonly Dictionary<string, IReadOnlyList<EnumValueEntry>?> _cache = new();

    // ── IEnumValueProvider ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<EnumValueEntry> GetValues(TypeKey enumType)
    {
        if (string.IsNullOrEmpty(enumType.Id)
            || !enumType.Id.StartsWith(GlobalPrefix, StringComparison.Ordinal))
            return Array.Empty<EnumValueEntry>();

        // Check cache first (avoid repeated AppDomain scans).
        if (_cache.TryGetValue(enumType.Id, out var cached))
            return cached ?? Array.Empty<EnumValueEntry>();

        var fqn     = enumType.Id[GlobalPrefix.Length..];
        var entries = BuildEntries(fqn);
        _cache[enumType.Id] = entries; // store null for failures (avoids repeated scan)
        return entries ?? Array.Empty<EnumValueEntry>();
    }

    /// <inheritdoc/>
    public int GetMaxInlineValues() => 8;

    // ── private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the enum type by FQN across loaded assemblies and builds the value list.
    /// Returns <see langword="null"/> when the type cannot be found or is not an enum.
    /// </summary>
    private static IReadOnlyList<EnumValueEntry>? BuildEntries(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return null;

        // Fast path: Type.GetType handles assembly-qualified names and some built-ins.
        var enumType = Type.GetType(fqn, throwOnError: false);

        // Slow path: scan loaded assemblies.
        if (enumType == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fqn, throwOnError: false);
                    if (t != null) { enumType = t; break; }
                }
                catch
                {
                    // ignore assemblies that fail type resolution
                }
            }
        }

        if (enumType == null || !enumType.IsEnum)
            return null;

        try
        {
            var names  = Enum.GetNames(enumType);
            var values = Enum.GetValues(enumType);

            var entries = new EnumValueEntry[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                // Convert to the underlying integer type, then widen to long.
                var rawValue = Convert.ToInt64(values.GetValue(i));
                entries[i] = new EnumValueEntry(rawValue, names[i], null, null);
            }
            return entries;
        }
        catch
        {
            return null;
        }
    }
}
