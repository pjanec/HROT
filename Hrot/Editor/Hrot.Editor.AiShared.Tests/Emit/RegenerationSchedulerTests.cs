using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Emit;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Emit;

// ── Minimal stub asset (no domain knowledge needed) ───────────────────────────

file sealed class _StubAsset : IEditableAsset
{
    public Guid AssetId { get; } = Guid.NewGuid();
    public string Name { get; init; } = "Stub";
    public AssetKind Kind { get; init; } = AssetKind.BTree;
    public string SourceFilePath => string.Empty;
    public bool IsDirty { get; set; }
    public bool IsEditorOwned => true;
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AIE-026 tests for <see cref="RegenerationScheduler"/>:
///   • Debounce burst → single flush
///   • Single schedule + tick before debounce window → no flush
///   • Single schedule + tick after debounce window → 1 flush
///   • Multiple distinct assets → all flushed together
/// </summary>
public sealed class RegenerationSchedulerTests
{
    // ── Deterministic clock helper ─────────────────────────────────────────────

    private long _clock;
    private long TickNow() => _clock;
    private void Advance(long ms) => _clock += ms;

    // ── AIE-026 SC1: N rapid schedules → 1 flush, deterministic ───────────────

    [Fact]
    public void RegenerationScheduler_DebouncesBurst_IntoSingleSave()
    {
        // Arrange: 0 ms debounce so any tick after "schedule" will flush.
        var flushed = new List<IEditableAsset>();
        var scheduler = new RegenerationScheduler(
            flushAction:   a => flushed.Add(a),
            tickProvider:  TickNow,
            debounceTicks: 200);

        var asset = new _StubAsset();

        // Act: schedule 5 times rapidly (clock not advancing → same tick).
        for (int i = 0; i < 5; i++)
            scheduler.Schedule(asset);

        // Tick before debounce window — nothing flushed yet.
        Advance(100);
        int flushedBeforeWindow = scheduler.Tick();
        Assert.Equal(0, flushedBeforeWindow);
        Assert.Empty(flushed);

        // Advance past debounce window, then tick.
        Advance(200); // total 300 ms
        int flushedAfterWindow = scheduler.Tick();

        // Assert: exactly 1 flush despite 5 Schedule calls.
        Assert.Equal(1, flushedAfterWindow);
        Assert.Single(flushed);
        Assert.Same(asset, flushed[0]);
    }

    [Fact]
    public void RegenerationScheduler_NoPendingAssets_TickReturnsZero()
    {
        var scheduler = new RegenerationScheduler(
            flushAction:   _ => { },
            tickProvider:  TickNow,
            debounceTicks: 100);

        Advance(1000);
        int count = scheduler.Tick();
        Assert.Equal(0, count);
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void RegenerationScheduler_TickBeforeDebounceWindow_DoesNotFlush()
    {
        var flushed = new List<IEditableAsset>();
        var scheduler = new RegenerationScheduler(
            flushAction:   a => flushed.Add(a),
            tickProvider:  TickNow,
            debounceTicks: 500);

        var asset = new _StubAsset();
        scheduler.Schedule(asset);

        Advance(300); // < 500 ms debounce
        int count = scheduler.Tick();

        Assert.Equal(0, count);
        Assert.Empty(flushed);
        Assert.True(scheduler.HasPending);
    }

    [Fact]
    public void RegenerationScheduler_TickAfterDebounceWindow_FlushesOnce()
    {
        var flushed = new List<IEditableAsset>();
        var scheduler = new RegenerationScheduler(
            flushAction:   a => flushed.Add(a),
            tickProvider:  TickNow,
            debounceTicks: 500);

        var asset = new _StubAsset();
        scheduler.Schedule(asset);

        Advance(600); // > 500 ms debounce
        int count = scheduler.Tick();

        Assert.Equal(1, count);
        Assert.Single(flushed);

        // Second tick after flush — nothing more pending.
        Advance(1000);
        int count2 = scheduler.Tick();
        Assert.Equal(0, count2);
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void RegenerationScheduler_MultipleAssets_AllFlushedTogether()
    {
        var flushed = new List<IEditableAsset>();
        var scheduler = new RegenerationScheduler(
            flushAction:   a => flushed.Add(a),
            tickProvider:  TickNow,
            debounceTicks: 100);

        var asset1 = new _StubAsset { Name = "A" };
        var asset2 = new _StubAsset { Name = "B" };
        var asset3 = new _StubAsset { Name = "C" };

        scheduler.Schedule(asset1);
        scheduler.Schedule(asset2);
        scheduler.Schedule(asset3);

        Assert.Equal(3, scheduler.PendingCount);

        Advance(200);
        int count = scheduler.Tick();

        Assert.Equal(3, count);
        Assert.Equal(3, flushed.Count);
        Assert.Contains(asset1, flushed);
        Assert.Contains(asset2, flushed);
        Assert.Contains(asset3, flushed);
        Assert.Equal(0, scheduler.PendingCount);
    }

    [Fact]
    public void RegenerationScheduler_SameAssetScheduledTwice_OnlyFlushedOnce()
    {
        var flushed = new List<IEditableAsset>();
        var scheduler = new RegenerationScheduler(
            flushAction:   a => flushed.Add(a),
            tickProvider:  TickNow,
            debounceTicks: 100);

        var asset = new _StubAsset();
        scheduler.Schedule(asset);
        scheduler.Schedule(asset); // second call: same AssetId, should not duplicate

        Assert.Equal(1, scheduler.PendingCount);

        Advance(200);
        scheduler.Tick();

        Assert.Single(flushed);
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// PU-601 tests for <see cref="RegenerationScheduler.FlushNow"/>:
///   • FlushNow on a scheduled asset drains immediately (no debounce elapsed)
///   • FlushNow on empty queue returns 0
///   • After FlushNow the queue is empty
///   • FlushNow does not affect the debounced Tick path — re-schedule + Tick still works
/// </summary>
public sealed class FlushNowTests
{
    private long _clock;
    private long TickNow() => _clock;
    private void Advance(long ms) => _clock += ms;

    [Fact]
    public void FlushNow_WithScheduledAsset_FlushesDespiteNoClock()
    {
        // Arrange — schedule an asset but do NOT advance the clock (debounce has NOT elapsed).
        var flushed = new List<IEditableAsset>();
        var scheduler = new RegenerationScheduler(
            flushAction:   a => flushed.Add(a),
            tickProvider:  TickNow,
            debounceTicks: 500);

        var asset = new _StubAsset { Name = "X" };
        scheduler.Schedule(asset);

        // Confirm debounce guard prevents Tick from flushing.
        Advance(100); // < 500 ms
        Assert.Equal(0, scheduler.Tick());
        Assert.Empty(flushed);

        // Act — FlushNow ignores the debounce window.
        int count = scheduler.FlushNow();

        // Assert — asset flushed immediately.
        Assert.Equal(1, count);
        Assert.Single(flushed);
        Assert.Same(asset, flushed[0]);
    }

    [Fact]
    public void FlushNow_EmptyQueue_ReturnsZero()
    {
        var scheduler = new RegenerationScheduler(
            flushAction:   _ => { },
            tickProvider:  TickNow,
            debounceTicks: 500);

        Advance(1000);
        int count = scheduler.FlushNow();

        Assert.Equal(0, count);
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void FlushNow_ClearsQueue_SubsequentFlushNowReturnsZero()
    {
        var flushed = new List<IEditableAsset>();
        var scheduler = new RegenerationScheduler(
            flushAction:   a => flushed.Add(a),
            tickProvider:  TickNow,
            debounceTicks: 500);

        var asset = new _StubAsset();
        scheduler.Schedule(asset);

        int first  = scheduler.FlushNow();
        int second = scheduler.FlushNow(); // queue already empty

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(flushed); // only one flush total
        Assert.False(scheduler.HasPending);
        Assert.Equal(0, scheduler.PendingCount);
    }

    [Fact]
    public void FlushNow_ReentrancySafe_ScheduleDuringFlush_QueuesToNextTick()
    {
        // If flushAction re-schedules an asset, that re-queued asset should NOT be
        // flushed in the same FlushNow call — only after the next debounce/FlushNow.
        var flushCount = 0;
        IEditableAsset? requeued = null;
        RegenerationScheduler? schedulerRef = null;

        var asset1 = new _StubAsset { Name = "First" };
        var asset2 = new _StubAsset { Name = "Requeued" };

        schedulerRef = new RegenerationScheduler(
            flushAction: a =>
            {
                flushCount++;
                if (ReferenceEquals(a, asset1) && requeued == null)
                {
                    requeued = asset2;
                    schedulerRef!.Schedule(asset2);
                }
            },
            tickProvider:  TickNow,
            debounceTicks: 500);

        schedulerRef.Schedule(asset1);

        int count = schedulerRef.FlushNow();

        // Only asset1 should have been flushed in this call.
        Assert.Equal(1, count);
        Assert.Equal(1, flushCount);
        // asset2 was scheduled during flush — it is still pending.
        Assert.True(schedulerRef.HasPending);
        Assert.Equal(1, schedulerRef.PendingCount);

        // A subsequent FlushNow flushes asset2.
        int count2 = schedulerRef.FlushNow();
        Assert.Equal(1, count2);
        Assert.Equal(2, flushCount);
        Assert.False(schedulerRef.HasPending);
    }

    [Fact]
    public void Tick_DebounceUnaffected_AfterFlushNow_RescheduleAndTickStillWork()
    {
        // Verify FlushNow does not corrupt the debounce state.
        var flushed = new List<IEditableAsset>();
        var scheduler = new RegenerationScheduler(
            flushAction:   a => flushed.Add(a),
            tickProvider:  TickNow,
            debounceTicks: 200);

        var asset = new _StubAsset { Name = "A" };
        scheduler.Schedule(asset);

        // FlushNow drains the first schedule.
        int first = scheduler.FlushNow();
        Assert.Equal(1, first);
        Assert.False(scheduler.HasPending);

        // Re-schedule and verify Tick still respects debounce.
        scheduler.Schedule(asset);
        Advance(100);
        Assert.Equal(0, scheduler.Tick()); // debounce not elapsed

        Advance(200); // now elapsed
        int second = scheduler.Tick();
        Assert.Equal(1, second);
        Assert.Equal(2, flushed.Count);
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AIE-026 tests for <see cref="AiDocument.ReconcileAsset"/> and
/// <see cref="AiDocumentManager.ReconcileFromCatalog"/>:
///   • Reconcile updates asset reference by AssetId/StableId
///   • Reconcile with wrong AssetId is no-op
///   • Document dirty flag cleared on reconcile
///   • ActiveChanged fires when active doc is reconciled
/// </summary>
public sealed class ReloadReconciliationTests
{
    private sealed class _FakeEditableAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Asset";
        public AssetKind Kind { get; init; } = AssetKind.BTree;
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }
    }

    // ── Reload_ReconcilesModel_ByStableId ─────────────────────────────────────

    [Fact]
    public void Reload_ReconcilesModel_ByStableId()
    {
        // Arrange: original asset projected before save.
        var assetId  = Guid.NewGuid();
        var original = new _FakeEditableAsset { AssetId = assetId, Name = "Original" };

        var doc = new AiDocument(original, AssetKind.BTree);
        doc.MarkDirty();
        Assert.True(doc.IsDirty);
        Assert.Same(original, doc.Asset);

        // Simulated hot-reload produces a freshly projected asset with the SAME AssetId
        // (positions/comments are preserved by the projector reading Layout attribute).
        var reloaded = new _FakeEditableAsset { AssetId = assetId, Name = "Reloaded" };

        // Act: reconcile the document with the reloaded asset.
        doc.ReconcileAsset(reloaded);

        // Assert: document now points at the freshly projected asset.
        Assert.Same(reloaded, doc.Asset);
        Assert.Equal("Reloaded", doc.Asset.Name);
        // Dirty flag cleared because the reload supersedes the in-memory edits.
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Reload_ReconcileAsset_WrongAssetId_IsNoOp()
    {
        var original  = new _FakeEditableAsset { AssetId = Guid.NewGuid() };
        var different = new _FakeEditableAsset { AssetId = Guid.NewGuid() };

        var doc = new AiDocument(original, AssetKind.BTree);
        doc.ReconcileAsset(different);

        // Original is unchanged — wrong AssetId must not replace the asset.
        Assert.Same(original, doc.Asset);
    }

    [Fact]
    public void Reload_ReconcileFromCatalog_UpdatesMatchingOpenDoc()
    {
        // Arrange: a document manager with one open Blueprint document.
        // PU-302: editor-owned BTree/HSM now use the stitch path rather than full-replace.
        // Blueprint (and hand-authored non-IsEditorOwned assets) still use ReconcileAsset.
        var assetId  = Guid.NewGuid();
        var original = new _FakeEditableAsset { AssetId = assetId, Kind = AssetKind.Blueprint, Name = "Before" };

        int activeChangedCount = 0;
        var mgr = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
        mgr.ActiveChanged += () => activeChangedCount++;

        var doc = mgr.Open(original);
        int baseChangedCount = activeChangedCount; // from Open → Activate

        // Simulated reload produces a freshly projected version.
        var reloaded = new _FakeEditableAsset { AssetId = assetId, Kind = AssetKind.Blueprint, Name = "After" };

        // Act: reconcile from catalog.
        mgr.ReconcileFromCatalog(new[] { reloaded });

        // Assert: active Blueprint doc now has the freshly projected asset (full-replace path).
        Assert.Same(reloaded, doc.Asset);
        Assert.Equal("After", doc.Asset.Name);

        // ActiveChanged must have fired once more (because the active doc was reconciled).
        Assert.Equal(baseChangedCount + 1, activeChangedCount);
    }

    [Fact]
    public void Reload_ReconcileFromCatalog_UnknownAsset_IsNoOp()
    {
        var assetId  = Guid.NewGuid();
        var original = new _FakeEditableAsset { AssetId = assetId };

        var mgr = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
        var doc = mgr.Open(original);

        int changedCount = 0;
        mgr.ActiveChanged += () => changedCount++;

        // Catalog contains an unrelated asset — should not touch the open doc.
        var unrelated = new _FakeEditableAsset { AssetId = Guid.NewGuid() };
        mgr.ReconcileFromCatalog(new[] { unrelated });

        Assert.Same(original, doc.Asset);
        Assert.Equal(0, changedCount); // no ActiveChanged fired
    }
}
