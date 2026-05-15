using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkits.Tests.ReplayBrowser.Search;

public class PresetRoundTripTests
{
    private static IComponentEditService BuildEditService() =>
        new ComponentEditServiceBuilder().Build();

    // ── SR-T28 ────────────────────────────────────────────────────────────
    // 3-level nested compound: Serialize -> Deserialize via System.Text.Json
    // (matches how ReplaySearchPanel saves/loads presets).
    // Verifies that the polymorphic [JsonPolymorphic] attributes on
    // SearchPredicateDto correctly reconstruct concrete subtypes.
    [Fact]
    public void SR_T28_PresetRoundTrip_ThreeLevelNested_ReconstructsEquivalentDto()
    {
        // Build a 3-level compound: Compound(And) [ Compound(Or) [ Numeric, String ] ]
        var inner = new CompoundPredicateDto
        {
            Operator = LogicalOperator.Or,
            Conditions = new List<SearchPredicateDto>
            {
                new NumericPredicateDto { MinValue = 1.0, MaxValue = 99.0 },
                new StringPredicateDto  { Substring = "combat" }
            }
        };
        var root = new CompoundPredicateDto
        {
            Operator   = LogicalOperator.And,
            Conditions = new List<SearchPredicateDto> { inner }
        };

        // Round-trip using System.Text.Json, same as ReplaySearchPanel preset I/O.
        string json = JsonSerializer.Serialize<SearchPredicateDto>(root);
        var reloaded = JsonSerializer.Deserialize<SearchPredicateDto>(json) as CompoundPredicateDto;

        Assert.NotNull(reloaded);
        Assert.Equal(LogicalOperator.And, reloaded!.Operator);
        Assert.Single(reloaded.Conditions);
        var reloadedInner = Assert.IsType<CompoundPredicateDto>(reloaded.Conditions[0]);
        Assert.Equal(LogicalOperator.Or, reloadedInner.Operator);
        Assert.Equal(2, reloadedInner.Conditions.Count);
        Assert.IsType<NumericPredicateDto>(reloadedInner.Conditions[0]);
        Assert.IsType<StringPredicateDto>(reloadedInner.Conditions[1]);
        var reloadedNum = (NumericPredicateDto)reloadedInner.Conditions[0];
        Assert.Equal(1.0, reloadedNum.MinValue);
        Assert.Equal(99.0, reloadedNum.MaxValue);
        var reloadedStr = (StringPredicateDto)reloadedInner.Conditions[1];
        Assert.Equal("combat", reloadedStr.Substring);
    }

    // ── SR-T29 ────────────────────────────────────────────────────────────
    // Resizing Conditions List causes RebuildRequired; after RebuildDocument
    // the new child is present and state returns to Stable.
    [Fact]
    public void SR_T29_ResizeConditions_SetsRebuildRequired_ThenStableAfterRebuild()
    {
        var dto = new CompoundPredicateDto
        {
            Conditions = new List<SearchPredicateDto>
            {
                new NumericPredicateDto { MinValue = 0, MaxValue = 10 },
                new StringPredicateDto  { Substring = "x" }
            }
        };
        var editSvc = BuildEditService();
        using var session = editSvc.Open(dto, typeof(CompoundPredicateDto));

        Assert.Equal(EditRebuildState.Stable, session.RebuildState);

        // Find the Conditions node (DynamicArray) in the document.
        EditNode? conditionsNode = FindNode(session.Document.Root, "Conditions");
        Assert.NotNull(conditionsNode);
        var containerBinding = Assert.IsAssignableFrom<IContainerBinding>(conditionsNode!.Binding);
        Assert.Equal(2, containerBinding.Count);

        // Add one element -- caller must manually mark structural change.
        containerBinding.Resize(3);
        session.MarkStructuralChange();

        Assert.Equal(EditRebuildState.RebuildRequired, session.RebuildState);

        session.RebuildDocument();

        Assert.Equal(EditRebuildState.Stable, session.RebuildState);

        // After rebuild, the document must reflect 3 conditions.
        conditionsNode = FindNode(session.Document.Root, "Conditions");
        Assert.NotNull(conditionsNode);
        var cb2 = Assert.IsAssignableFrom<IContainerBinding>(conditionsNode!.Binding);
        Assert.Equal(3, cb2.Count);
    }

    // Depth-first search for a node by name.
    private static EditNode? FindNode(EditNode root, string name)
    {
        if (root.Name == name) return root;
        foreach (var child in root.Children)
        {
            var found = FindNode(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
