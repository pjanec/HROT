using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Windows;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐ <b>U-obs-5 — <c>HsmEventsWindow.BuildViewModel</c>, the BUILD half only.</b>
/// 📄 <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 6 — the queue flags this
/// class explicitly ("neither Id nor Title, only an unused WindowId const").
///
/// <para>⚠ <b>No <c>PanelSnapshot</c> rails here on purpose.</b> Measured: <see cref="HsmEventsWindow"/>
/// has ZERO callers anywhere in the repository — it is not a <c>ManagedWindow</c> subclass, never
/// constructed, <c>Render()</c> never invoked, and <c>WindowId</c> never referenced outside its own
/// declaration. There is no host to declare/register from. Mirrors
/// <c>HsmSelectionBridgeHelperTests.StubRefactor</c>'s stub shape.</para>
/// </summary>
public sealed class HsmEventsWindowBuildsItsModelTests
{
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

    [Fact]
    public void TheDump_CarriesEachEventsPayloadShape()
    {
        var events = new List<EventDefinition>
        {
            new("OnDamage", 1) { PayloadSize = 8, IsIndirect = true, HasGlobalTransition = false },
        };
        var window = new HsmEventsWindow(MakeAsset(events), new StubRefactor(), new FindResultsWindow("HSM"));

        var vm = window.BuildViewModel();

        Assert.Equal(HsmEventsWindow.WindowId, vm.PanelId);
        Assert.Equal(HsmEventsWindow.WindowId, vm.PanelKind);
        var row = Assert.Single(vm.Events);
        Assert.Equal("OnDamage", row.Name);
        Assert.Equal(8, row.PayloadSize);
        Assert.True(row.IsIndirect);
    }
}
