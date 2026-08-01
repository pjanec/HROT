using System.Reflection;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// CA-02 (Slice 1a) — one reflected ECS component field: name, pin TypeId, and whether the
/// field's CLR type is MANAGED (a reference type, or a value type that recursively contains a
/// reference-typed field) vs. UNMANAGED (blittable). Mirrors
/// <see cref="Hrot.Blueprints.Core.Assets.ComponentFieldDecl"/> (the compiler's baked shape,
/// <c>Name</c>+<c>TypeId</c> only) plus the <see cref="IsManaged"/> flag the compiler decl does NOT
/// carry — that distinction is editor-only (drives the Details-panel persistence caveat; see
/// <see cref="ComponentNodeDrawers"/>), never persisted onto the node.
/// </summary>
internal sealed class ReflectedComponentField
{
    public string Name { get; init; } = "";
    public string TypeId { get; init; } = "";
    public bool IsManaged { get; init; }
}

/// <summary>
/// Reflects an ECS component type's public instance fields for the <c>GetComponent</c> node's
/// "Component Type" picker (<see cref="ComponentNodeDrawers"/>) and its palette discovery
/// (<see cref="ComponentPaletteEntries"/>).
/// <para>
/// Deliberately UNLIKE <see cref="SharedStructFieldReflector"/> (which the Q#14 shared-struct
/// machinery uses): component reads are typed MEMBER access
/// (<c>view.GetComponentRO&lt;T&gt;(e).FieldName</c>), not a byte-offset read into a blittable
/// blob, so there is no <c>Marshal.OffsetOf</c> call and NO field is ever rejected/bailed-out —
/// managed fields are kept (flagged <see cref="ReflectedComponentField.IsManaged"/>), matching the
/// Q#15/CA design's "reads are RO, member-access, managed fields readable" constraint.
/// </para>
/// </summary>
internal static class ComponentFieldReflector
{
    private static readonly MethodInfo IsReferenceOrContainsReferencesMethod =
        typeof(System.Runtime.CompilerServices.RuntimeHelpers)
            .GetMethod(nameof(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences))!;

    /// <summary>
    /// Reflects <paramref name="fqn"/>'s public instance fields. Returns <c>null</c> ONLY when the
    /// type itself cannot be resolved (unloaded assembly, typo, renamed/removed type) — a
    /// resolvable zero-field ("tag") component returns an EMPTY (non-null) list, so callers that
    /// need to distinguish "unresolved" from "resolved but nothing to read" (see
    /// <see cref="ResolveType"/>, used by <c>BlueprintNodeModel</c>'s stale-reference check) don't
    /// misreport a tag component as unresolved.
    /// </summary>
    internal static List<ReflectedComponentField>? TryReflect(string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return null;
        var type = ResolveType(fqn!);
        if (type is null) return null;

        var decls = new List<ReflectedComponentField>();
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            decls.Add(new ReflectedComponentField
            {
                Name      = f.Name,
                TypeId    = f.FieldType.FullName ?? f.FieldType.Name,
                IsManaged = IsManagedFieldType(f.FieldType),
            });
        }
        return decls;
    }

    /// <summary>
    /// Resolves <paramref name="fqn"/> to a loaded CLR <see cref="Type"/> across all loaded
    /// assemblies (mirrors <see cref="SharedStructFieldReflector.ResolveType"/>). Returns
    /// <c>null</c> when not found — the sole "is this component type still resolvable" check
    /// (kept separate from <see cref="TryReflect"/>'s field list so a genuine zero-field/tag
    /// component is never confused with an unresolved reference).
    /// </summary>
    internal static Type? ResolveType(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return null;
        var t = Type.GetType(fqn);
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { t = asm.GetType(fqn); } catch { continue; }
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>
    /// True when <paramref name="fieldType"/> is MANAGED — a reference type, or a value type that
    /// (recursively) contains a reference-typed field — i.e. NOT usable with
    /// <c>view.GetComponentRO&lt;T&gt;()</c>'s <c>where T : unmanaged</c> constraint (would need
    /// <c>view.GetManagedComponentRO&lt;T&gt;()</c> instead — CA-05).
    /// <para>
    /// Uses the CLR's own
    /// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences{T}"/>
    /// (invoked reflectively since the field's <see cref="Type"/> is only known at runtime here) —
    /// the exact same test the JIT applies to satisfy an <c>unmanaged</c> generic constraint, so
    /// this can never disagree with what <c>GetComponentRO&lt;T&gt;</c> actually accepts.
    /// </para>
    /// </summary>
    private static bool IsManagedFieldType(Type fieldType)
    {
        try
        {
            var method = IsReferenceOrContainsReferencesMethod.MakeGenericMethod(fieldType);
            return (bool)method.Invoke(null, null)!;
        }
        catch
        {
            // Unreflectable (e.g. a pointer, a by-ref-like/ref struct field, or an open generic
            // parameter) -- conservatively treat as managed so the drawer's persistence caveat
            // still fires rather than silently permitting something we couldn't verify as blittable.
            return true;
        }
    }

    /// <summary>
    /// CA-05 (Slice 1b) -- component-LEVEL managed check: true when <paramref name="fqn"/> resolves
    /// to a genuine reference type (<c>class</c>). Distinct from <see cref="ReflectedComponentField.IsManaged"/>
    /// (per-FIELD, uses <c>IsReferenceOrContainsReferences</c> so it also catches a managed field
    /// embedded in an otherwise-unmanaged struct component) -- THIS check answers "is the component
    /// itself a class", i.e. does it require <c>view.GetManagedComponentRO&lt;T&gt;() where T : class</c>
    /// instead of <c>view.GetComponentRO&lt;T&gt;()</c> to read at all. A component type is, by
    /// construction in this ECS, either a blittable <c>struct</c> (Tier 1, unmanaged) or a <c>class</c>
    /// (Tier 2, managed) -- never a struct that itself fails the <c>class</c> check while still
    /// containing references (that shape is covered by the per-field flag above, not this one).
    /// Unresolvable FQN (unloaded assembly, typo) -- returns <c>false</c> (safe default: the caller's
    /// stale-reference guard, not this check, is responsible for flagging an unresolved type).
    /// </summary>
    internal static bool IsManagedComponent(string? fqn)
    {
        var type = ResolveType(fqn ?? "");
        return type is { IsClass: true };
    }
}
