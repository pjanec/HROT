using FDP.Toolkit.ImGui.Abstractions;
using Hrot.UI.Common.Menus;
using Hrot.UI.Common.Facades;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="SharedContextMenuPopulator"/>.
/// A <see cref="RecordingContextMenuBuilder"/> stub captures every
/// <c>AddItem</c> / <c>AddSeparator</c> call without requiring an active
/// ImGui render frame.
/// </summary>
public class SharedContextMenuPopulatorTests
{
    // ── Test double ───────────────────────────────────────────────────────────

    /// <summary>
    /// Simple recording stub for <see cref="IContextMenuBuilder"/> that
    /// accumulates added item labels and separator markers in a flat list.
    /// </summary>
    private sealed class RecordingContextMenuBuilder : IContextMenuBuilder
    {
        public readonly List<string> Items = new();

        public void AddItem(string label, Action callback, bool enabled = true)
        {
            Items.Add(label);
        }

        public IContextMenuBuilder BeginSubmenu(string label)
        {
            Items.Add($"[submenu:{label}]");
            return this;
        }

        public void EndSubmenu() { }

        public void AddSeparator() => Items.Add("[separator]");
    }

    // ── Helper factory ────────────────────────────────────────────────────────

    private static (RecordingContextMenuBuilder builder, Mock<IEntityActionController> actions)
        CreateSut() => (new RecordingContextMenuBuilder(), new Mock<IEntityActionController>());

    // ── PopulateEntityMenu — conditional items ────────────────────────────────

    [Fact]
    public void PopulateEntityMenu_HasEditablePolyline_AddsEditShapeItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 1, tkbType: 100,
            hasEditablePolyline: true, hasRoutePlan: false,
            builder, actions.Object);

        Assert.Contains("Edit Shape", builder.Items);
    }

    [Fact]
    public void PopulateEntityMenu_HasEditablePolyline_DoesNotAddEditRoute()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 1, tkbType: 100,
            hasEditablePolyline: true, hasRoutePlan: false,
            builder, actions.Object);

        Assert.DoesNotContain("Edit Route", builder.Items);
    }

    [Fact]
    public void PopulateEntityMenu_HasRoutePlan_AddsEditRouteItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 1, tkbType: 100,
            hasEditablePolyline: false, hasRoutePlan: true,
            builder, actions.Object);

        Assert.Contains("Edit Route", builder.Items);
    }

    [Fact]
    public void PopulateEntityMenu_NoPolylineNoRoute_BothEditItemsAbsent()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 1, tkbType: 100,
            hasEditablePolyline: false, hasRoutePlan: false,
            builder, actions.Object);

        Assert.DoesNotContain("Edit Shape", builder.Items);
        Assert.DoesNotContain("Edit Route", builder.Items);
    }

    // ── PopulateEntityMenu — entityId == 0 suppresses Rename ─────────────────

    [Fact]
    public void PopulateEntityMenu_EntityIdZero_DoesNotAddRenameItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 0, tkbType: 100,
            hasEditablePolyline: false, hasRoutePlan: false,
            builder, actions.Object);

        Assert.DoesNotContain("Rename...", builder.Items);
    }

    [Fact]
    public void PopulateEntityMenu_EntityIdNonZero_AddsRenameItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 42, tkbType: 100,
            hasEditablePolyline: false, hasRoutePlan: false,
            builder, actions.Object);

        Assert.Contains("Rename...", builder.Items);
    }

    // ── PopulateEntityMenu — always-present items ─────────────────────────────

    [Fact]
    public void PopulateEntityMenu_Always_AddsCenterOnEntityAndDeleteItems()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 5, tkbType: 0,
            hasEditablePolyline: false, hasRoutePlan: false,
            builder, actions.Object);

        Assert.Contains("Center on Entity", builder.Items);
        Assert.Contains("Delete", builder.Items);
    }

    [Fact]
    public void PopulateEntityMenu_Always_AddsSeparatorBeforeDelete()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 5, tkbType: 0,
            hasEditablePolyline: false, hasRoutePlan: false,
            builder, actions.Object);

        int separatorIdx = builder.Items.IndexOf("[separator]");
        int deleteIdx    = builder.Items.IndexOf("Delete");

        Assert.True(separatorIdx >= 0, "Expected a separator");
        Assert.True(separatorIdx < deleteIdx, "Separator must appear before Delete");
    }

    // ── PopulateEmptyMapMenu ──────────────────────────────────────────────────

    [Fact]
    public void PopulateEmptyMapMenu_AddsOnlyMeasurementToolItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEmptyMapMenu(builder, actions.Object);

        Assert.Single(builder.Items);
        Assert.Equal("Measurement Tool", builder.Items[0]);
    }

    // ── Callback wiring ───────────────────────────────────────────────────────

    [Fact]
    public void PopulateEntityMenu_CenterOnEntityCallback_InvokesCenterOnEntity()
    {
        var (builder, actions) = CreateSut();

        // Capture the callback so we can invoke it
        actions.Setup(a => a.CenterOnEntity(It.IsAny<long>()));
        var capturingBuilder = new CallbackCapturingBuilder();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 7, tkbType: 0,
            hasEditablePolyline: false, hasRoutePlan: false,
            capturingBuilder, actions.Object);

        // Find and invoke the "Center on Entity" callback
        var item = capturingBuilder.CapturedItems.First(i => i.Label == "Center on Entity");
        item.Callback();

        actions.Verify(a => a.CenterOnEntity(7), Times.Once);
    }

    [Fact]
    public void PopulateEmptyMapMenu_MeasurementToolCallback_InvokesActivateMeasureTool()
    {
        var capturingBuilder = new CallbackCapturingBuilder();
        var actions = new Mock<IEntityActionController>();

        SharedContextMenuPopulator.PopulateEmptyMapMenu(capturingBuilder, actions.Object);

        capturingBuilder.CapturedItems[0].Callback();

        actions.Verify(a => a.ActivateMeasureTool(), Times.Once);
    }

    // ── Callback-capturing test double ────────────────────────────────────────

    private sealed class CallbackCapturingBuilder : IContextMenuBuilder
    {
        public List<(string Label, Action Callback)> CapturedItems = new();

        public void AddItem(string label, Action callback, bool enabled = true)
            => CapturedItems.Add((label, callback));

        public IContextMenuBuilder BeginSubmenu(string label) => this;
        public void EndSubmenu() { }
        public void AddSeparator() { }
    }
}
