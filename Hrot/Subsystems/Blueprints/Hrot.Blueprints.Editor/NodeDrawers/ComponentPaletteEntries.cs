using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// CA-02 (Slice 1a) — Add-Node palette entries for the <c>GetComponent</c> node, one entry per
/// discovered ECS component type (via <see cref="IComponentTypeProvider"/>) that has at least one
/// reflectable field. Each descriptor drops a <see cref="GetComponentNode"/> pre-baked with the
/// component's FQN and its FULLY reflected field set (<see cref="ComponentFieldReflector"/>), so
/// the node has its per-field pins immediately — there is no "whole component" pin value (a
/// component isn't a single pin value), so the node created here is ALWAYS multi-pin; there is no
/// collapsed/legacy authoring path in the editor (the legacy single-"Value" shape only exists for
/// pre-CA-01 assets already on disk). Mirrors <see cref="MakeBreakStructPaletteEntries"/>.
/// </summary>
public static class ComponentPaletteEntries
{
    public static IEnumerable<NodeKindDescriptor> GetComponentEntries(IComponentTypeProvider provider)
    {
        if (provider is null) yield break;
        foreach (var fqn in provider.GetComponentTypeFqns())
        {
            var reflected = ComponentFieldReflector.TryReflect(fqn);
            // No fields to read -> no useful GetComponent node (mirrors MakeBreakStructPaletteEntries's
            // skip-if-empty for a shared struct with no reflectable fields).
            if (reflected is null || reflected.Count == 0) continue;

            var shortName = ShortName(fqn);
            var bakedFqn  = fqn; // capture for the closure

            yield return new NodeKindDescriptor
            {
                Kind        = $"Component.Get.{fqn}",
                DisplayName = $"Get Component: {shortName}",
                Category    = BlueprintNodePaletteEntries.Categories.Component,
                Tooltip     = $"Read fields off the {shortName} ECS component (self, or an optional Target entity).",
                Icon        = "bp/variable_get",
                CreateInstance = () => new GetComponentNode
                {
                    Id               = Guid.NewGuid(),
                    ComponentTypeFqn = bakedFqn,
                    // Re-reflect per CreateInstance call so each placed node gets its OWN Fields
                    // list instance (never a list shared/mutated across multiple placed nodes).
                    Fields = ToComponentFields(ComponentFieldReflector.TryReflect(bakedFqn)!),
                    // CA-05 (Slice 1b): bake whether the component TYPE itself is managed (a class).
                    IsManaged = ComponentFieldReflector.IsManagedComponent(bakedFqn),
                },
            };
        }
    }

    /// <summary>
    /// CA-04/CA-06 — Add-Node palette entries for the <c>SetComponent</c> node, one entry per
    /// discovered <c>[BlueprintWritable]</c> ECS component type (via the caller-supplied
    /// <paramref name="provider"/> -- callers pass a writable-only provider, e.g.
    /// <see cref="ReflectionWritableComponentTypeProvider"/>; the Get palette above uses the
    /// all-components provider instead). Bakes ONE of two mutually-exclusive shapes, keyed by
    /// <see cref="ComponentFieldReflector.IsManagedComponent"/> (mirrors
    /// <see cref="GetComponentEntries"/>'s CA-05 <c>IsManaged</c> bake):
    /// <list type="bullet">
    ///   <item>UNMANAGED: skipped if it has no reflectable fields (nothing to write); otherwise
    ///   <see cref="SetComponentNode.IsManaged"/> = false, <see cref="SetComponentNode.Fields"/> =
    ///   the FULL reflected field set (CA-03/CA-04, unchanged).</item>
    ///   <item>MANAGED (CA-06, Slice W2, Q#16-C): NEVER skipped for having zero fields -- a
    ///   whole-replace write doesn't depend on field count, so even a managed tag-like component is
    ///   a legitimate target. <see cref="SetComponentNode.IsManaged"/> = true,
    ///   <see cref="SetComponentNode.Fields"/> = <c>null</c> (never baked -- Stage2's BP2064 would
    ///   reject a managed node carrying per-field Fields).</item>
    /// </list>
    /// </summary>
    public static IEnumerable<NodeKindDescriptor> SetComponentEntries(IComponentTypeProvider provider)
    {
        if (provider is null) yield break;
        foreach (var fqn in provider.GetComponentTypeFqns())
        {
            bool isManaged = ComponentFieldReflector.IsManagedComponent(fqn);
            var reflected = ComponentFieldReflector.TryReflect(fqn);
            // Unmanaged, no fields to write -> no useful SetComponent node (mirrors
            // GetComponentEntries's skip-if-empty). Managed is NEVER skipped here -- see summary.
            if (!isManaged && (reflected is null || reflected.Count == 0)) continue;

            var shortName = ShortName(fqn);
            var bakedFqn  = fqn; // capture for the closure

            yield return new NodeKindDescriptor
            {
                Kind        = $"Component.Set.{fqn}",
                DisplayName = $"Set Component: {shortName}",
                Category    = BlueprintNodePaletteEntries.Categories.Component,
                Tooltip     = isManaged
                    ? $"Replace the {shortName} managed ECS component (self only, write-if-present, whole-value via ECB)."
                    : $"Write fields into the {shortName} ECS component (self only, write-if-present).",
                Icon        = "bp/variable_get",
                CreateInstance = () => new SetComponentNode
                {
                    Id               = Guid.NewGuid(),
                    ComponentTypeFqn = bakedFqn,
                    IsManaged        = isManaged,
                    // Managed: whole-replace only -- never bake per-field Fields (see summary).
                    // Unmanaged: re-reflect per CreateInstance call so each placed node gets its OWN
                    // Fields list instance (never a list shared/mutated across multiple placed nodes).
                    Fields = isManaged ? null : ToComponentFields(ComponentFieldReflector.TryReflect(bakedFqn)!),
                },
            };
        }
    }

    /// <summary>
    /// CA-07c -- Add-Node palette entries for the three component-collection CONSUMER nodes
    /// (<see cref="ComponentForEachNode"/>/<see cref="ComponentItemGetNode"/>/
    /// <see cref="ComponentItemCountNode"/>). Unlike <see cref="GetComponentEntries"/>/
    /// <see cref="SetComponentEntries"/> these have NO type picker and no per-component fan-out --
    /// there is exactly ONE static entry per kind, dropping a default-constructed node with empty
    /// baked props (<c>ComponentTypeFqn</c>/accessor FQNs all <c>""</c>). The props get baked later,
    /// on wire, by <see cref="Hrot.Blueprints.Editor.Host.BlueprintCommandSink"/>'s
    /// <c>TryBakeCollectionConsumer</c> hook when the designer connects a
    /// <see cref="GetComponentNode"/> collection out-pin into the placed node's "Collection" pin
    /// (mirrors <see cref="BlueprintNodePaletteEntries.Make{TNode}"/>'s bare default-construct
    /// pattern -- pins are projected by <c>NodePinSchema</c> at render time, nothing hand-authored
    /// here).
    /// </summary>
    public static IEnumerable<NodeKindDescriptor> ConsumerEntries()
    {
        yield return new NodeKindDescriptor
        {
            Kind           = "Component.ForEach",
            DisplayName    = "For Each Component Item",
            Category       = BlueprintNodePaletteEntries.Categories.Component,
            Tooltip        = "Iterate a wired component collection element-by-element (wire a GetComponent collection out-pin into \"Collection\").",
            Icon           = "bp/macro",
            CreateInstance = () => new ComponentForEachNode { Id = Guid.NewGuid() },
        };
        yield return new NodeKindDescriptor
        {
            Kind           = "Component.ItemGet",
            DisplayName    = "Get Component Item",
            Category       = BlueprintNodePaletteEntries.Categories.Component,
            Tooltip        = "Read a single element off a wired component collection by index (wire a GetComponent collection out-pin into \"Collection\").",
            Icon           = "bp/variable_get",
            CreateInstance = () => new ComponentItemGetNode { Id = Guid.NewGuid() },
        };
        yield return new NodeKindDescriptor
        {
            Kind           = "Component.ItemCount",
            DisplayName    = "Component Item Count",
            Category       = BlueprintNodePaletteEntries.Categories.Component,
            Tooltip        = "Read a wired component collection's element count (wire a GetComponent collection out-pin into \"Collection\").",
            Icon           = "bp/variable_get",
            CreateInstance = () => new ComponentItemCountNode { Id = Guid.NewGuid() },
        };
    }

    private static List<ComponentFieldDecl> ToComponentFields(IReadOnlyList<ReflectedComponentField> reflected)
        => reflected.Select(f => new ComponentFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList();

    private static string ShortName(string fqn)
    {
        int cut = fqn.LastIndexOfAny(new[] { '.', '+' });
        return cut >= 0 && cut < fqn.Length - 1 ? fqn[(cut + 1)..] : fqn;
    }
}
