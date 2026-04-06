using FDP.Toolkit.DER;
using Hrot.ExCon.Adapters;
using Hrot.ExCon.Logic;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Moq;
using Newtonsoft.Json.Linq;

namespace Hrot.ExCon.Tests;

public class JsonContextMenuBuilderTests
{
    // ── Test 1: Build returns correct item count; separator is included ────────

    /// <summary>
    /// After one <see cref="JsonContextMenuBuilder.AddItem"/> and one
    /// <see cref="JsonContextMenuBuilder.AddSeparator"/> call,
    /// <see cref="JsonContextMenuBuilder.Build"/> must return exactly 2 items.
    /// </summary>
    [Fact]
    public void Build_AfterAddItemAndSeparator_ReturnsTwoItems()
    {
        var builder = new JsonContextMenuBuilder();
        builder.AddItem("Delete", () => { });
        builder.AddSeparator();

        var items = builder.Build();

        Assert.Equal(2, items.Count);
    }

    // ── Test 2: GetCallbackRegistry contains the registered callback ──────────

    /// <summary>
    /// After a single <see cref="JsonContextMenuBuilder.AddItem"/> call the
    /// registry returned by <see cref="JsonContextMenuBuilder.GetCallbackRegistry"/>
    /// must contain exactly one entry, and invoking that entry must execute the
    /// original callback.
    /// </summary>
    [Fact]
    public void GetCallbackRegistry_AfterAddItem_ContainsOneInvokableCallback()
    {
        var builder = new JsonContextMenuBuilder();
        var invoked = false;
        builder.AddItem("Delete", () => { invoked = true; });

        var registry = builder.GetCallbackRegistry();

        Assert.Single(registry);

        // Invoke the stored callback and verify it executes the original lambda.
        registry[0]();
        Assert.True(invoked);
    }

    // ── Test 3: ContextMenuLogic + entity with MapVisualOverlay → "Edit Shape" ─

    /// <summary>
    /// When <see cref="ContextMenuLogic"/> is constructed with a non-null
    /// <see cref="IExConLogic"/> and the selected entity carries a
    /// <see cref="MapVisualOverlay"/> descriptor, the serialised JSON sent to the
    /// writer must contain an item labelled "Edit Shape".
    /// </summary>
    [Fact]
    public void ContextMenuLogic_EntityWithMapVisualOverlay_JsonContainsEditShape()
    {
        // Arrange
        var repo = new DerRepo();
        var entity = repo.CreateEntity(entityId: 42, tkbType: 1000L);
        entity.SetDescriptor(new MapVisualOverlay { EntityId = 42 });

        var writer = new CapturingMenuWriter();
        var mockLogic = new Mock<IExConLogic>().Object;
        var logic = new ContextMenuLogic(repo, writer, mockLogic);

        // Act
        logic.OnSelectionChanged(new SelectionChangedEvent
        {
            MapId             = 1,
            SelectedEntityIds = new List<int> { 42 }
        });

        // Assert
        Assert.Single(writer.Written);
        var json   = writer.Written[0].MenuDefinitionJson;
        var labels = JArray.Parse(json)
            .Select(t => (string?)t["label"])
            .Where(l => l != null)
            .ToList();

        Assert.Contains("Edit Shape", labels);
    }
}
