using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Emit;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Emit;

/// <summary>
/// AIE-026 (Blueprint): verifies that dirty Blueprint assets are routed through the
/// Blueprint trigger (not the BTree/HSM emit path) when the scheduler flushes.
///
/// The light wiring in Phase 2 uses a <c>AssetKind.Blueprint</c> check in the flush
/// action; Phase 4 will replace the null trigger with a real QuickReloadService.TriggerAsync.
/// </summary>
public sealed class BlueprintSchedulerRoutingTests
{
    private long _clock;
    private long TickNow() => _clock;
    private void Advance(long ms) => _clock += ms;

    private sealed class _BpAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; } = "MyBlueprint";
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath => "/bp/MyBlueprint.bp.json";
        public bool IsDirty { get; set; } = true;
        public bool IsEditorOwned => false;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class _BTreeAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; } = "MyTree";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "/trees/MyTree.cs";
        public bool IsDirty { get; set; } = true;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }
    }

    // ── Blueprint dirty → routed to blueprint trigger, NOT emit path ──────────

    [Fact]
    public void Blueprint_Dirty_RoutedToBlueprintTrigger_NotEmitService()
    {
        var emitCalled    = new List<IEditableAsset>();
        var bpTriggered   = new List<IEditableAsset>();

        // Simulate the EditorSubsystem flush action: Blueprint → trigger; else emit.
        var scheduler = new RegenerationScheduler(
            flushAction: asset =>
            {
                if (asset.Kind == AssetKind.Blueprint)
                {
                    bpTriggered.Add(asset); // simulates _blueprintQuickReloadTrigger?.Invoke(asset)
                    return;
                }
                emitCalled.Add(asset); // simulates emitService.Emit(asset)
            },
            tickProvider:  TickNow,
            debounceTicks: 100);

        var bpAsset   = new _BpAsset();
        var bpAsset2  = new _BpAsset();
        var btreeAsset = new _BTreeAsset();

        scheduler.Schedule(bpAsset);
        scheduler.Schedule(bpAsset2);
        scheduler.Schedule(btreeAsset);

        Advance(200); // past debounce
        scheduler.Tick();

        // Blueprint assets went to blueprint trigger.
        Assert.Equal(2, bpTriggered.Count);
        Assert.Contains(bpAsset,  bpTriggered);
        Assert.Contains(bpAsset2, bpTriggered);

        // BTree asset went to emit path.
        Assert.Single(emitCalled);
        Assert.Same(btreeAsset, emitCalled[0]);
    }

    [Fact]
    public void Blueprint_NullTrigger_DoesNotThrow()
    {
        // When _blueprintQuickReloadTrigger is null, flush is a no-op for Blueprints.
        Action<IEditableAsset>? bpTrigger = null;

        var scheduler = new RegenerationScheduler(
            flushAction: asset =>
            {
                if (asset.Kind == AssetKind.Blueprint)
                {
                    bpTrigger?.Invoke(asset); // null → no-op
                    return;
                }
            },
            tickProvider:  TickNow,
            debounceTicks: 100);

        var bpAsset = new _BpAsset();
        scheduler.Schedule(bpAsset);

        Advance(200);
        var ex = Record.Exception(() => scheduler.Tick());

        Assert.Null(ex); // must not throw when trigger is null
    }
}
