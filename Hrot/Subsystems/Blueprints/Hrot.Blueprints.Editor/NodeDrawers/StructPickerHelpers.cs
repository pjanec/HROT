using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

// ⭐ CE-440 (Q76 §12.24): the two helpers below outlived the GetShared/SetShared drawers they were written
// for, and are extracted verbatim. The type picker serves the Component and WaitForChannel drawers; the
// struct reflector feeds the Make/Break/SetMembers struct palette. ⚠ Their "Shared" names are historical —
// a rename goes through Roslyn, not a text sweep, and is left for a later slice.


/// <summary>
/// Non-ImGui filter/membership logic for the type pickers (Component type, WaitForChannel channel), kept
/// separate from <c>Draw()</c> so it is headlessly testable.
/// </summary>
internal static class SharedTypePickerLogic
{
    /// <summary>
    /// Returns the subset of <paramref name="candidates"/> whose text contains
    /// <paramref name="filterText"/> (case-insensitive, substring match). Returns all
    /// candidates unchanged when <paramref name="filterText"/> is null/empty.
    /// </summary>
    internal static IReadOnlyList<string> Filter(IReadOnlyList<string> candidates, string? filterText)
    {
        if (string.IsNullOrEmpty(filterText)) return candidates;
        return candidates
            .Where(c => c.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="value"/> is a non-empty string present (ordinal match) in
    /// <paramref name="candidates"/>.
    /// </summary>
    internal static bool Contains(IReadOnlyList<string> candidates, string? value)
        => !string.IsNullOrEmpty(value) && candidates.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// Q#14 multi-pin: reflects a shared-struct FQN into its per-field decls (Name + field-type FQN + byte
/// offset) — consumed by the Make/Break/SetMembers struct palette. Editor-side reflection (net8) —
/// the netstandard2.0 compiler never reflects; it consumes the baked <see cref="SharedFieldDecl"/> list.
/// Returns <c>null</c> when the type can't be resolved in a loaded assembly, isn't a value type, or has a
/// field whose offset can't be computed (non-blittable) — the caller then keeps the legacy whole-struct pin.
/// </summary>
internal static class SharedStructFieldReflector
{
    internal static List<SharedFieldDecl>? TryReflect(string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return null;
        var type = ResolveType(fqn!);
        if (type is null || !type.IsValueType) return null;
        // non-blittable → per-field offset write impossible; keep whole-struct (Marshal.OffsetOf used to throw here)
        if (global::Fdp.Core.TypeLayout.ContainsReferences(type)) return null;

        var decls = new List<SharedFieldDecl>();
        foreach (var f in type.GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            int offset;
            // ⭐ CE-2043 — the MANAGED offset: SetMembers writes the field at it, into managed bytes. Marshal.OffsetOf gave the
            //   INTEROP offset — for Fbt.RaycastResult.HitPoint 8 where the field sits at 4 — so the write landed in Hit/padding.
            try { offset = global::Fdp.Core.TypeLayout.OffsetOf(type, f.Name); }
            catch { return null; }
            decls.Add(new SharedFieldDecl
            {
                Name   = f.Name,
                TypeId = f.FieldType.FullName ?? f.FieldType.Name,
                Offset = offset,
            });
        }
        return decls.Count > 0 ? decls : null;
    }

    private static Type? ResolveType(string fqn)
    {
        var t = Type.GetType(fqn);
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { t = asm.GetType(fqn); } catch { continue; }
            if (t != null) return t;
        }
        return null;
    }
}
