using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>Q#14 slice 2c: "Publish: {Event}" palette entries per discovered event, baking the event shape.</summary>
public sealed class BlueprintEventPaletteEntriesTests
{
    [Fact]
    public void PublishEntries_IncludeDiscoveredEvent_AndBakeShapeOnCreate()
    {
        var entry = BlueprintEventPaletteEntries.PublishEntries()
            .FirstOrDefault(d => d.DisplayName == "Publish: Test Ping");   // the 1b TestPingEvent

        Assert.NotNull(entry);
        Assert.StartsWith("Events", entry!.Category);

        var node = Assert.IsType<PublishEventNode>(entry.CreateInstance());
        Assert.EndsWith("TestPingEvent", node.EventTypeFqn!);
        Assert.Equal("Target", node.TargetFieldName);
        Assert.NotNull(node.PayloadFields);
        Assert.Contains(node.PayloadFields!, f => f.Name == "Count"    && f.TypeId == "System.Int32");
        Assert.Contains(node.PayloadFields!, f => f.Name == "Strength" && f.TypeId == "System.Single");
    }

    [Fact]
    public void PublishEntries_IncludeEditorAuthoredDefs()
    {
        var catalog = new BlueprintEventCatalog
        {
            Events = { new BlueprintEventDef { Name = "MyEditorEvent", Category = "Custom" } },
        };
        var names = BlueprintEventPaletteEntries.PublishEntries(catalog).Select(d => d.DisplayName).ToList();
        Assert.Contains("Publish: MyEditorEvent", names);
    }
}
