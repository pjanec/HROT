using Fdp.Toolkit.Behavior;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐⭐ S8b-2 / <c>CE-2025</c> (<c>DESIGN_Unified_Behaviour_Run</c> "S8b design" U2) — the ONE editor answer to <i>"what does
/// child X take as its parameters?"</i>, for every picker that hosts a behaviour: the BTree subtree node, the HSM state and
/// the blueprint Behaviour Task. It asks the runtime <see cref="BehaviorRegistry.TryGetHostedInputType"/> (the child's
/// authored input, whatever its tier) and spells the type the way an asset stores a type id (a nested type's <c>+</c> as
/// <c>.</c>). ⛔ It replaces the editor catalogue's <c>IBehaviorInputsContract</c>, which answered for BTree children only.
/// </summary>
public static class ChildInputTypes
{
    /// <summary>A lookup over the host's registry, read when called (a child registered later is found).
    /// ⛔ A host that HAS a <see cref="BehaviorRegistry"/> must pass it (the silent-default rule).</summary>
    public static Func<string, string?> Lookup(Func<BehaviorRegistry?> registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        return name => !string.IsNullOrWhiteSpace(name)
                       && registry() is { } r && r.TryGetHostedInputType(name, out var type) && type.FullName is { } full
            ? full.Replace('+', '.')
            : null;
    }

    /// <summary>⭐ <c>CE-2079</c> — a behaviour's AUTHORED params type (<c>BehaviorDefinition.JsonParamsDtoType</c>): what an SOP
    /// order serialises into the assignment's JSON. ⚠ Not the hosted-input type — a curated behaviour with a source resolver
    /// hosts one type and parses another. ⛔ A class DTO cannot be a blackboard variable ⇒ <c>null</c> (authored defaults).</summary>
    public static Func<string, string?> ParamsDtoLookup(Func<BehaviorRegistry?> registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        return name => !string.IsNullOrWhiteSpace(name)
                       && registry() is { } r && r.TryGetId(name, out int id) && r.TryGetDefinition(id, out var def)
                       && def.JsonParamsDtoType is { IsValueType: true, FullName: { } full }
            ? full.Replace('+', '.')
            : null;
    }
}
