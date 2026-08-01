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
                },
            };
        }
    }

    private static List<ComponentFieldDecl> ToComponentFields(IReadOnlyList<ReflectedComponentField> reflected)
        => reflected.Select(f => new ComponentFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList();

    private static string ShortName(string fqn)
    {
        int cut = fqn.LastIndexOfAny(new[] { '.', '+' });
        return cut >= 0 && cut < fqn.Length - 1 ? fqn[(cut + 1)..] : fqn;
    }
}
