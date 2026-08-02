using System.Linq;
using System.Reflection;
using Fdp.Core;
using Hrot.Blueprints.Core.Assets;

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
/// CA-07a (R1 curated-accessor) — one discovered virtual collection: a component-level
/// <c>[BlueprintCollection]</c>/<c>[BlueprintCollectionItem]</c> accessor PAIR (same
/// <c>ComponentType</c>+<c>Name</c>), reflected off the loaded static-helper classes (e.g.
/// <c>UnitRosterOps</c>). Mirrors <see cref="Hrot.Blueprints.Core.Assets.ComponentFieldDecl"/>'s
/// collection fields (<c>ElementTypeId</c>/<c>CountAccessorFqn</c>/<c>ItemAccessorFqn</c>) — this
/// is the editor-only reflected shape baked verbatim into that decl by
/// <see cref="ComponentNodeDrawers.GetComponentNodeSession.ApplyComponentTypeFqn"/>.
/// </summary>
internal sealed class ReflectedComponentCollection
{
    public string Name { get; init; } = "";
    public string ElementTypeId { get; init; } = "";
    public string CountAccessorFqn { get; init; } = "";
    public string ItemAccessorFqn { get; init; } = "";

    /// <summary>CA-07d-2: <c>CuratedStatic</c> (default) for an accessor-pair discovery; <c>ManagedMember</c> for a native managed-member discovery (see <see cref="CollectionFieldName"/>).</summary>
    public CollectionKind CollectionKind { get; init; }

    /// <summary>CA-07d-2: for <see cref="Hrot.Blueprints.Core.Assets.CollectionKind.ManagedMember"/>, the name of the managed <c>List&lt;T&gt;</c>/<c>IReadOnlyList&lt;T&gt;</c>/<c>T[]</c> member. Empty for curated entries.</summary>
    public string CollectionFieldName { get; init; } = "";
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

    // ── CA-07a: virtual-collection discovery (R1 curated-accessor) ─────────────

    /// <summary>
    /// Discovers virtual collections exposed by the component type <paramref name="componentFqn"/>
    /// via <c>[BlueprintCollection]</c>/<c>[BlueprintCollectionItem]</c>-attributed static methods
    /// found anywhere across <see cref="AppDomain.CurrentDomain"/>'s loaded assemblies (mirrors
    /// <see cref="ComponentTypeScan"/>'s enumeration/exception-handling shape, but scans METHODS
    /// rather than component TYPES). Candidates are grouped by their attribute's <c>Name</c>; a
    /// group is only emitted when BOTH a valid Count accessor (see
    /// <see cref="IsValidCountAccessor"/>) AND a valid Item accessor (see
    /// <see cref="IsValidItemAccessor"/>) are present for that name — a lone/malformed accessor is
    /// silently skipped (declares no collection). Never returns <c>null</c> — an unresolvable or
    /// collection-less component simply yields an empty list. Result is sorted by <c>Name</c>
    /// (ordinal) for deterministic bake order, independent of assembly load order.
    /// </summary>
    internal static List<ReflectedComponentCollection> TryReflectCollections(string? componentFqn)
    {
        var result = new List<ReflectedComponentCollection>();
        if (string.IsNullOrEmpty(componentFqn)) return result;

        var counts = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        var items  = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue; // dynamic or otherwise unintrospectable assembly
            }

            foreach (var t in types)
            {
                MethodInfo[] methods;
                try
                {
                    methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var m in methods)
                {
                    var countAttr = m.GetCustomAttribute<BlueprintCollectionAttribute>();
                    if (countAttr != null
                        && countAttr.ComponentType.FullName == componentFqn
                        && IsValidCountAccessor(m, countAttr.ComponentType))
                    {
                        counts[countAttr.Name] = m;
                    }

                    var itemAttr = m.GetCustomAttribute<BlueprintCollectionItemAttribute>();
                    if (itemAttr != null
                        && itemAttr.ComponentType.FullName == componentFqn
                        && IsValidItemAccessor(m, itemAttr.ComponentType))
                    {
                        items[itemAttr.Name] = m;
                    }
                }
            }
        }

        foreach (var (name, countMethod) in counts)
        {
            if (!items.TryGetValue(name, out var itemMethod)) continue; // lone Count -- no collection
            result.Add(new ReflectedComponentCollection
            {
                Name             = name,
                ElementTypeId    = itemMethod.ReturnType.FullName ?? itemMethod.ReturnType.Name,
                CountAccessorFqn = AccessorFqn(countMethod),
                ItemAccessorFqn  = AccessorFqn(itemMethod),
            });
        }

        // CA-07d-2 (Q#18-C/D): managed-member discovery -- mirrors the curated accessor-pair pass
        // above, but for a MANAGED component's own native collection MEMBERS (no
        // [BlueprintCollection]/[BlueprintCollectionItem] accessors involved). Scope is
        // deliberately narrow: T[] / List<T> / IReadOnlyList<T> only -- no IEnumerable<T>, no
        // Dictionary/HashSet (Q#18-D). Only runs for managed (class) components; a curated entry
        // for the same name always wins (never happens in practice -- a managed class doesn't also
        // carry curated accessors -- but the dedup keeps this pass strictly additive).
        if (IsManagedComponent(componentFqn))
        {
            var componentType = ResolveType(componentFqn!);
            if (componentType != null)
            {
                var existingNames = new HashSet<string>(result.Select(c => c.Name), StringComparer.Ordinal);

                foreach (var f in componentType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (existingNames.Contains(f.Name)) continue;
                    if (!TryGetManagedCollectionElement(f.FieldType, out var elementType)) continue;
                    result.Add(new ReflectedComponentCollection
                    {
                        Name                = f.Name,
                        ElementTypeId       = elementType.FullName ?? elementType.Name,
                        CountAccessorFqn    = "",
                        ItemAccessorFqn     = "",
                        CollectionKind      = CollectionKind.ManagedMember,
                        CollectionFieldName = f.Name,
                    });
                    existingNames.Add(f.Name);
                }

                foreach (var p in componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetGetMethod() is null) continue; // no public getter -- unreadable
                    if (p.GetIndexParameters().Length > 0) continue; // indexer, not a member collection
                    if (existingNames.Contains(p.Name)) continue;
                    if (!TryGetManagedCollectionElement(p.PropertyType, out var elementType)) continue;
                    result.Add(new ReflectedComponentCollection
                    {
                        Name                = p.Name,
                        ElementTypeId       = elementType.FullName ?? elementType.Name,
                        CountAccessorFqn    = "",
                        ItemAccessorFqn     = "",
                        CollectionKind      = CollectionKind.ManagedMember,
                        CollectionFieldName = p.Name,
                    });
                    existingNames.Add(p.Name);
                }
            }
        }

        return result.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// CA-07d-2: true when <paramref name="memberType"/> is one of the three managed-collection
    /// shapes in scope (Q#18-D) -- a single-dimension array, <c>List&lt;T&gt;</c>, or
    /// <c>IReadOnlyList&lt;T&gt;</c> -- with <paramref name="elementType"/> set to <c>T</c>.
    /// Anything else (including <c>IEnumerable&lt;T&gt;</c>, <c>Dictionary</c>, <c>HashSet</c>)
    /// returns <c>false</c> -- deliberately out of scope, not a managed collection member.
    /// </summary>
    private static bool TryGetManagedCollectionElement(Type memberType, out Type elementType)
    {
        if (memberType.IsArray && memberType.GetArrayRank() == 1)
        {
            elementType = memberType.GetElementType()!;
            return true;
        }
        if (memberType.IsGenericType)
        {
            var genericDef = memberType.GetGenericTypeDefinition();
            if (genericDef == typeof(List<>) || genericDef == typeof(IReadOnlyList<>))
            {
                elementType = memberType.GetGenericArguments()[0];
                return true;
            }
        }
        elementType = typeof(void);
        return false;
    }

    /// <summary>
    /// Validates a candidate <c>[BlueprintCollection]</c> method: public static (already guaranteed
    /// by the <see cref="BindingFlags"/> scan), returns <c>int</c>, and takes exactly one byref
    /// parameter (<c>in</c>/<c>ref</c> — both compile to a byref parameter, and the compiler's
    /// callers only ever emit <c>in</c>) whose referenced type is <paramref name="componentType"/>.
    /// </summary>
    private static bool IsValidCountAccessor(MethodInfo m, Type componentType)
    {
        if (m.ReturnType != typeof(int)) return false;
        var ps = m.GetParameters();
        if (ps.Length != 1) return false;
        var pt = ps[0].ParameterType;
        return pt.IsByRef && pt.GetElementType() == componentType;
    }

    /// <summary>
    /// Validates a candidate <c>[BlueprintCollectionItem]</c> method: public static (already
    /// guaranteed by the <see cref="BindingFlags"/> scan), returns a NON-<c>void</c> type (the
    /// collection's element type), and takes exactly two parameters -- a byref parameter (<c>in</c>/
    /// <c>ref</c>) whose referenced type is <paramref name="componentType"/>, followed by an
    /// <c>int</c> index.
    /// </summary>
    private static bool IsValidItemAccessor(MethodInfo m, Type componentType)
    {
        if (m.ReturnType == typeof(void)) return false;
        var ps = m.GetParameters();
        if (ps.Length != 2) return false;
        var pt = ps[0].ParameterType;
        if (!pt.IsByRef || pt.GetElementType() != componentType) return false;
        return ps[1].ParameterType == typeof(int);
    }

    /// <summary>
    /// FQN of a static accessor method as baked onto the node: <c>DeclaringType.FullName + "." +
    /// Name</c> -- no argument list, no <c>global::</c> prefix (mirrors how <c>FlowForEachNode</c>'s
    /// <c>CountAccessorFqn</c>/<c>ItemAccessorFqn</c> are authored, e.g.
    /// "Hrot.AI.Behaviors.Brains.UnitRosterOps.Count").
    /// </summary>
    private static string AccessorFqn(MethodInfo m) => $"{m.DeclaringType!.FullName}.{m.Name}";
}
