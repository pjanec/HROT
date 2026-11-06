using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Editing;

/// <summary>
/// ⭐ U-7 / CE-4288 (docs/UX/DESIGN_Condition_Editor.md §8.1–§8.2) — the struct editor readable, measured on §8.1's own condition
/// (<i>All of: … · after 60 s · …</i>): no C# names, no <c>Bind</c> rows, numbers with units, an open bound as <i>no limit</i>, add / delete
/// named, a collapsed node's summary through the ONE hook. 🔴 Red-proof (compiles): <c>LabelOf</c> returning <c>node.Name</c> ⇒ red.
/// </summary>
public sealed class StructEditorReadableTests
{
    private static EditNode Doc(object root)
    {
        var svc = new ComponentEditServiceBuilder().Build();
        var session = svc.Open(root, root.GetType());
        return session.Document.Root;
    }

    private static EditNode Child(EditNode n, string name) => n.Children.First(c => c.Name == name);

    [Fact]
    public void U7_FieldNames_AreDisplayNames_WithUnits()
    {
        var root = Doc(new ScenarioTimePredicateDto { FromSeconds = 60 });
        var from = Child(root, nameof(ScenarioTimePredicateDto.FromSeconds));
        var to = Child(root, nameof(ScenarioTimePredicateDto.ToSeconds));
        Assert.Equal("From", ComponentEditDrawer.LabelOf(from));
        Assert.Equal("s", ComponentEditDrawer.UnitOf(from));
        Assert.Equal("To", ComponentEditDrawer.LabelOf(to));
        Assert.Equal("%g s", NumberInput.FormatFor(ComponentEditDrawer.UnitOf(from)));   // 60 → "60 s", never "60.000000"
        Assert.Equal("%g %%", NumberInput.FormatFor("%"));
    }

    [Fact]
    public void U7_ABindableNodesBindMap_IsNotARow()
    {
        var dto = new ScenarioTimePredicateDto();
        var root = Doc(dto);
        var bind = root.Children.FirstOrDefault(c => c.Name == "Bind");
        Assert.NotNull(bind);                                                        // the document still carries it (the binder reads it)
        Assert.True(ComponentEditDrawer.HidesChild(dto, bind!));                     // …but it is never drawn as a field
        Assert.False(ComponentEditDrawer.HidesChild(dto, Child(root, nameof(ScenarioTimePredicateDto.FromSeconds))));
        Assert.False(ComponentEditDrawer.HidesChild(new object(), bind!));           // only a BindableNode's map
    }

    [Fact]
    public void U7_TheKinds_AreNamed_NotTheirClrNames()
    {
        Assert.Equal("Compound", StructEdit.Core.Attributes.DisplayNames.Of(typeof(CompoundPredicateDto)));
        Assert.Equal("Scenario time", StructEdit.Core.Attributes.DisplayNames.Of(typeof(ScenarioTimePredicateDto)));
        Assert.DoesNotContain("Dto", StructEdit.Core.Attributes.DisplayNames.Of(typeof(OnEntityPredicateDto)));
    }

    [Fact]
    public void U7_ACollapsedNodesSummary_ComesFromTheOneHook()
    {
        Assert.Null(EditSummaries.Of(new ScenarioTimePredicateDto()));
        EditSummaries.Register(typeof(SearchPredicateDto), v => v is ScenarioTimePredicateDto t ? $"after {t.FromSeconds} s" : null);
        try { Assert.Equal("after 60 s", EditSummaries.Of(new ScenarioTimePredicateDto { FromSeconds = 60 })); }
        finally { EditSummaries.Register(typeof(SearchPredicateDto), _ => null); }
    }

    [Fact]
    public void U7_Labels_AreCached_NoAllocationAfterTheFirstAsk()
    {
        var root = Doc(new ScenarioTimePredicateDto());
        var from = Child(root, nameof(ScenarioTimePredicateDto.FromSeconds));
        ComponentEditDrawer.LabelOf(from); ComponentEditDrawer.UnitOf(from); NumberInput.FormatFor("s");
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { ComponentEditDrawer.LabelOf(from); ComponentEditDrawer.UnitOf(from); NumberInput.FormatFor("s"); }
        Assert.Equal(before, System.GC.GetAllocatedBytesForCurrentThread());
    }
}
