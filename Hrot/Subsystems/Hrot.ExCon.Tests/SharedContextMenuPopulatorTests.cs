using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Abstractions;
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
        /// <summary>
        /// ⭐⭐ Records the ENABLED flag too, added 2026-09-18 for <c>E2</c>. ⛔ This double used to accept
        /// <c>enabled</c> and drop it, so no rail here could see whether an item was greyed out — and
        /// "the Load zone item is ALWAYS ENABLED, never gated on local freshness" is a 🔒 user ruling
        /// (design §9.6, §9.7 ③b), i.e. exactly the property the double was blind to. ⚠ Fixed in place
        /// rather than by adding a second, sighted double (<c>R-142</c> ③).
        /// </summary>
        public readonly List<(string Label, bool Enabled)> Items = new();

        /// <summary>
        /// Labels only — what the pre-existing rails assert on. ⚠ A materialised <c>List</c>, not a lazy
        /// projection: existing rails use <c>IndexOf</c> and indexing to assert item ORDER.
        /// </summary>
        public List<string> Labels => Items.Select(i => i.Label).ToList();

        public void AddItem(string label, Action callback, bool enabled = true)
        {
            Items.Add((label, enabled));
        }

        public IContextMenuBuilder BeginSubmenu(string label)
        {
            Items.Add(($"[submenu:{label}]", true));
            return this;
        }

        public void EndSubmenu() { }

        public void AddSeparator() => Items.Add(("[separator]", true));
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

        Assert.Contains("Edit Shape", builder.Labels);
    }

    [Fact]
    public void PopulateEntityMenu_HasEditablePolyline_DoesNotAddEditRoute()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 1, tkbType: 100,
            hasEditablePolyline: true, hasRoutePlan: false,
            builder, actions.Object);

        Assert.DoesNotContain("Edit Route", builder.Labels);
    }

    [Fact]
    public void PopulateEntityMenu_HasRoutePlan_AddsEditRouteItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 1, tkbType: 100,
            hasEditablePolyline: false, hasRoutePlan: true,
            builder, actions.Object);

        Assert.Contains("Edit Route", builder.Labels);
    }

    [Fact]
    public void PopulateEntityMenu_NoPolylineNoRoute_BothEditItemsAbsent()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 1, tkbType: 100,
            hasEditablePolyline: false, hasRoutePlan: false,
            builder, actions.Object);

        Assert.DoesNotContain("Edit Shape", builder.Labels);
        Assert.DoesNotContain("Edit Route", builder.Labels);
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

        Assert.DoesNotContain("Rename...", builder.Labels);
    }

    [Fact]
    public void PopulateEntityMenu_EntityIdNonZero_AddsRenameItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 42, tkbType: 100,
            hasEditablePolyline: false, hasRoutePlan: false,
            builder, actions.Object);

        Assert.Contains("Rename...", builder.Labels);
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

        Assert.Contains("Center on Entity", builder.Labels);
        Assert.Contains("Delete", builder.Labels);
    }

    [Fact]
    public void PopulateEntityMenu_Always_AddsSeparatorBeforeDelete()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 5, tkbType: 0,
            hasEditablePolyline: false, hasRoutePlan: false,
            builder, actions.Object);

        int separatorIdx = builder.Labels.IndexOf("[separator]");
        int deleteIdx    = builder.Labels.IndexOf("Delete");

        Assert.True(separatorIdx >= 0, "Expected a separator");
        Assert.True(separatorIdx < deleteIdx, "Separator must appear before Delete");
    }

    // ── PopulateEmptyMapMenu ──────────────────────────────────────────────────

    [Fact]
    public void PopulateEmptyMapMenu_AddsOnlyMeasurementToolItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEmptyMapMenu(builder, actions.Object);

        Assert.Single(builder.Labels);
        Assert.Equal("Measurement Tool", builder.Labels[0]);
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

    // ══════════════════════════════════════════════════════════════════════════
    // E2 — "Load zone", on a zone only, and ALWAYS ENABLED
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ The item appears for the zone <c>TkbType</c>. ⚠ <c>tkbType</c> was documented as "reserved for
    /// future sub-menu filtering" — this is the first thing to use it, so the rail also pins that the
    /// parameter is now load-bearing rather than decorative.
    /// </summary>
    [Fact]
    public void E2_PopulateEntityMenu_OnATerrainZone_AddsLoadZoneItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 42, tkbType: Hrot.Map.Common.TkbEntityTypes.TerrainZone,
            hasEditablePolyline: true, hasRoutePlan: false,
            builder, actions.Object);

        Assert.Contains("Load zone", builder.Labels);
    }

    /// <summary>⛔ And NOT on anything else — a tactical area has no terrain to load.</summary>
    [Fact]
    public void E2_PopulateEntityMenu_OnANonZone_DoesNotAddLoadZoneItem()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 42, tkbType: Hrot.Map.Common.TkbEntityTypes.TacGraphic_Area,
            hasEditablePolyline: true, hasRoutePlan: false,
            builder, actions.Object);

        Assert.DoesNotContain("Load zone", builder.Labels);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THAT MATTERS.</b> 🔒 User ruling (design §9.6, §9.7 ③b): the item is <b>always
    /// enabled</b> and is never gated on whether THIS node's copy looks fresh.
    ///
    /// <para>⛔ The reason is not convenience: the load marker is deliberately NEVER replicated (§9.1),
    /// so a host whose own marker reads "loaded" cannot speak for the other nodes — it may be the one
    /// node that is fine while another is stale. ⇒ greying the item out would hide the only action that
    /// fixes the cluster. ⚠ This is asserted on the ENABLED FLAG, which the double could not see before
    /// this batch.</para>
    /// </summary>
    [Fact]
    public void E2_TheLoadZoneItem_IsAlwaysEnabled_NeverGatedOnLocalFreshness()
    {
        var (builder, actions) = CreateSut();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 42, tkbType: Hrot.Map.Common.TkbEntityTypes.TerrainZone,
            hasEditablePolyline: true, hasRoutePlan: false,
            builder, actions.Object);

        var item = builder.Items.Single(i => i.Label == "Load zone");
        Assert.True(item.Enabled,
            "the zone-load item must never be disabled: the local marker cannot see the other nodes, so "
          + "a locally-fresh host may be the only one that is fine (design §9.1, §9.7 ③b)");
    }

    /// <summary>⭐ And activating it routes to the CLUSTER-wide action, with the zone's id.</summary>
    [Fact]
    public void E2_ActivatingLoadZone_InvokesTheClusterWideAction()
    {
        var capturingBuilder = new CallbackCapturingBuilder();
        var actions = new Mock<IEntityActionController>();

        SharedContextMenuPopulator.PopulateEntityMenu(
            entityId: 4242, tkbType: Hrot.Map.Common.TkbEntityTypes.TerrainZone,
            hasEditablePolyline: true, hasRoutePlan: false,
            capturingBuilder, actions.Object);

        capturingBuilder.CapturedItems.Single(i => i.Label == "Load zone").Callback();

        actions.Verify(a => a.LoadZone(4242), Times.Once);
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
