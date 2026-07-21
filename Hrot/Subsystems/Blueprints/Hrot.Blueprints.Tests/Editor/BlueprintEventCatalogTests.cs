using System.Linq;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>Q#14 slice 1c/1e: editor-authored (2b) event-def JSON round-trip + projection + unified discovery.</summary>
public sealed class BlueprintEventCatalogTests
{
    private static BlueprintEventCatalog SampleCatalog() => new()
    {
        Events =
        {
            new BlueprintEventDef
            {
                Name = "SquadRegroup",
                Category = "Squad",
                Fields =
                {
                    new BlueprintEventFieldDef { Name = "Leader",    TypeId = "Fdp.Core.Entity", IsTarget = true },
                    new BlueprintEventFieldDef { Name = "RallyPoint", TypeId = "System.Numerics.Vector3" },
                    new BlueprintEventFieldDef { Name = "Urgency",    TypeId = "System.Int32" },
                },
            },
        },
    };

    [Fact]
    public void Catalog_RoundTripsThroughJson()
    {
        var original = SampleCatalog();
        var restored = BlueprintEventCatalog.FromJson(original.ToJson());

        var ev = Assert.Single(restored.Events);
        Assert.Equal("SquadRegroup", ev.Name);
        Assert.Equal("Squad", ev.Category);
        Assert.Equal(3, ev.Fields.Count);
        Assert.Contains(ev.Fields, f => f.Name == "Leader" && f.IsTarget);
        Assert.Contains(ev.Fields, f => f.Name == "Urgency" && f.TypeId == "System.Int32");
    }

    [Fact]
    public void Def_ProjectsToDiscoveredShape_WithTarget()
    {
        var d = SampleCatalog().Events[0].ToDiscovered();

        Assert.Equal("SquadRegroup", d.EventTypeFqn);
        Assert.Equal("Squad", d.Category);
        Assert.Equal("Leader", d.TargetFieldName);
        Assert.Equal(3, d.Fields.Count);
        Assert.Contains(d.Fields, f => f.Name == "RallyPoint" && f.TypeId == "System.Numerics.Vector3");
    }

    [Fact]
    public void UnifiedDiscovery_IncludesEditorAuthoredDefs()
    {
        var all = UnifiedEventDiscovery.All(SampleCatalog()).ToList();
        Assert.Contains(all, e => e.EventTypeFqn == "SquadRegroup");
        // Also still surfaces the C# [BlueprintEvent] test struct from 1b.
        Assert.Contains(all, e => e.EventTypeFqn.EndsWith("TestPingEvent", System.StringComparison.Ordinal));
    }
}
