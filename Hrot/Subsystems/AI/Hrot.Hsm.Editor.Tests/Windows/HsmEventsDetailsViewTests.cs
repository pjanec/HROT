using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Diagnostics.Contracts.Panels;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Windows;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 follow-up — <c>HsmEventsWindow</c> converted to a Details panel VIEW.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>Hrot.Editor.AiShared/Shell/VariablesDetailsView.cs</c> (the mirrored ASSET-scoped precedent).
///
/// <para>⛔⛔ <b>Registration into the live HSM perspective is NOT wired</b> — the only composition root
/// that could add <c>HsmEventsDetailsViewDescriptor</c> to <c>_hsmRegistrar.DetailsViews</c> is
/// <c>EditorSubsystem.cs</c>, explicitly on this batch's STOP-AND-REPORT list. These rails cover the
/// VIEW conversion itself: identity, the dump, capture-off, and the asset-scoped predicate.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class HsmEventsDetailsViewTests : IDisposable
{
    public HsmEventsDetailsViewTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class StubRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
            new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public Task<RefactorPreview> PreviewRenameAsync(string f, string t, RefactorOptions o, CancellationToken ct = default) =>
            Task.FromResult(PreviewRename(f, t, o));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview p, CancellationToken ct = default) =>
            Task.FromResult(ApplyRename(p));
    }

    private static HsmAsset MakeAsset(List<EventDefinition> events)
    {
        var root = new StateNode("__root__");
        return new HsmAsset(
            Guid.NewGuid(), "TestAsset", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            root,
            new List<StateNode> { root },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            events);
    }

    private static DetailsContext ContextFor(HsmAsset? asset) => new(
        SelectionOrigin.Unknown,
        Array.Empty<IAssetSubSelection>(),
        Array.Empty<Fdp.Core.Entity>(),
        asset,
        "HSM",
        VariableRunState.Planning);

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheView_DoesNotDeclareUntilTheFirstDraw()
    {
        // ⚠ Unlike a ManagedWindow (address known at construction), a hosted view only learns its
        // idScope from the caller's Draw — mirrors VariablesDetailsView's own deviation note.
        var view = new HsmEventsDetailsView(new StubRefactor(), new FindResultsWindow());
        Assert.NotNull(view);
    }

    [Fact]
    public void SimulateDraw_DeclaresItInstrumented_UnderTheComposedAddress()
    {
        Assert.DoesNotContain("scope1/details.hsmevents", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var events = new List<EventDefinition> { new("OnDamage", 1) { PayloadSize = 8 } };
        var asset  = MakeAsset(events);
        var view   = new HsmEventsDetailsView(new StubRefactor(), new FindResultsWindow());

        view.SimulateDraw(asset, "scope1");

        Assert.Contains("scope1/details.hsmevents", PanelSnapshot.RegisteredPanels);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterADraw_TheDumpCarriesEachEventsPayloadShape()
    {
        PanelSnapshot.CaptureEnabled = true;
        var events = new List<EventDefinition>
        {
            new("OnDamage", 1) { PayloadSize = 8, IsIndirect = true, HasGlobalTransition = false },
        };
        var asset = MakeAsset(events);
        var view  = new HsmEventsDetailsView(new StubRefactor(), new FindResultsWindow());

        view.SimulateDraw(asset, "scope1");

        var vm = PanelSnapshot.TryGet("scope1/details.hsmevents");
        Assert.NotNull(vm);
        Assert.Equal(HsmEventsDetailsViewDescriptor.ViewId, vm!.PanelKind);
        var dump = vm.Dump();
        var row  = Assert.Single(dump["events"]!.AsArray());
        Assert.Equal("OnDamage", row!["name"]!.GetValue<string>());
        Assert.Equal(8, row["payloadSize"]!.GetValue<int>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var asset = MakeAsset(new List<EventDefinition> { new("OnDamage", 1) });
        var view  = new HsmEventsDetailsView(new StubRefactor(), new FindResultsWindow());

        var vm = view.SimulateDraw(asset, "scope1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("scope1/details.hsmevents", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
    }

    // ── Rail 4 — a second window's instance gets its OWN address (U1d) ──────────────────────────

    [Fact]
    public void TwoHostsOfTheSameView_StayIndividuallyAddressable()
    {
        PanelSnapshot.CaptureEnabled = true;
        var asset = MakeAsset(new List<EventDefinition> { new("OnDamage", 1) });
        var docked = new HsmEventsDetailsView(new StubRefactor(), new FindResultsWindow());
        var floated = new HsmEventsDetailsView(new StubRefactor(), new FindResultsWindow());

        docked.SimulateDraw(asset, "ai_details_hsm");
        floated.SimulateDraw(asset, "ai_details_hsm_float_1");

        Assert.NotNull(PanelSnapshot.TryGet("ai_details_hsm/details.hsmevents"));
        Assert.NotNull(PanelSnapshot.TryGet("ai_details_hsm_float_1/details.hsmevents"));
    }

    // ── Rail 5 — the asset-scoped predicate (R-117: never claim an empty panel) ─────────────────

    [Fact]
    public void Applies_WhenAssetIsHsmWithEvents_ReturnsTrue()
    {
        var asset = MakeAsset(new List<EventDefinition> { new("OnDamage", 1) });
        Assert.True(HsmEventsDetailsViewDescriptor.Applies(ContextFor(asset)));
    }

    [Fact]
    public void Applies_WhenAssetHasNoEvents_ReturnsFalse()
    {
        var asset = MakeAsset(new List<EventDefinition>());
        Assert.False(HsmEventsDetailsViewDescriptor.Applies(ContextFor(asset)));
    }

    [Fact]
    public void Applies_WhenNoAssetIsOpen_ReturnsFalse()
    {
        Assert.False(HsmEventsDetailsViewDescriptor.Applies(ContextFor(null)));
    }

    // ── Rail 6 — rebuilds the wrapped window when the asset changes ─────────────────────────────

    [Fact]
    public void SimulateDraw_OnANewAsset_ReflectsTheNewAssetsEvents()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new HsmEventsDetailsView(new StubRefactor(), new FindResultsWindow());

        var asset1 = MakeAsset(new List<EventDefinition> { new("OnDamage", 1) });
        view.SimulateDraw(asset1, "scope1");
        Assert.Single(PanelSnapshot.TryGet("scope1/details.hsmevents")!.Dump()["events"]!.AsArray());

        var asset2 = MakeAsset(new List<EventDefinition>
        {
            new("OnHeal", 1), new("OnDeath", 2),
        });
        view.SimulateDraw(asset2, "scope1");

        var dump = PanelSnapshot.TryGet("scope1/details.hsmevents")!.Dump();
        Assert.Equal(2, dump["events"]!.AsArray().Count);
    }
}
