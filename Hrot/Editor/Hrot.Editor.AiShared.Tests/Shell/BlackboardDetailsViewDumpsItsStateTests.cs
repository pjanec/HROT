using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — <c>BlackboardDetailsView</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
///
/// <para>⭐ The borrowed <see cref="BlackboardAuthoringWindow"/> already self-registers under its OWN
/// id whenever <c>DrawContent()</c> runs — this rail asserts the HOSTED VIEW's own, distinct address,
/// not the borrowed window's.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class BlackboardDetailsViewDumpsItsStateTests : IDisposable
{
    private sealed class StubRefactorService : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options) =>
            new(fromKey, toKey, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview preview) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid assetId, DeleteOptions options) =>
            new(assetId, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview preview) => new(true, Array.Empty<string>(), null);
        public Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default) =>
            Task.FromResult(PreviewRename(fromKey, toKey, options));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default) =>
            Task.FromResult(ApplyRename(preview));
    }

    public BlackboardDetailsViewDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static BlackboardAuthoringWindow MakeWindow(string id)
        => new(new EditorSelectionStore(), new StubRefactorService(), idOverride: id);

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress()
    {
        var window = MakeWindow("btree_blackboard_win");
        var view   = new BlackboardDetailsView(window);
        var addr   = $"host1/{BlackboardDetailsViewDescriptor.ViewId}";
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw("host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheBorrowedWindowsId()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = MakeWindow("btree_blackboard_win");
        var view   = new BlackboardDetailsView(window);

        var addr = $"host1/{BlackboardDetailsViewDescriptor.ViewId}";
        view.SimulateDraw("host1");

        var stored = PanelSnapshot.TryGet(addr);
        Assert.NotNull(stored);
        Assert.Equal(addr, stored!.PanelId);
        Assert.Equal(BlackboardDetailsViewDescriptor.ViewId, stored.PanelKind);

        var dump = stored.Dump();
        Assert.Equal("btree_blackboard_win", dump["hostWindowId"]!.GetValue<string>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var window = MakeWindow("btree_blackboard_win");
        var view   = new BlackboardDetailsView(window);   // CaptureEnabled stays false

        var vm = view.SimulateDraw("host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains($"host1/{BlackboardDetailsViewDescriptor.ViewId}", PanelSnapshot.RegisteredPanels);
        Assert.Equal("btree_blackboard_win", vm.HostWindowId);
    }
}
