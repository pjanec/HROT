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
        // Arrange: a document manager with one open document.
        var assetId  = Guid.NewGuid();
        var original = new _FakeEditableAsset { AssetId = assetId, Name = "Before" };

        int activeChangedCount = 0;
        var mgr = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
        mgr.ActiveChanged += () => activeChangedCount++;

        var doc = mgr.Open(original);
        int baseChangedCount = activeChangedCount; // from Open → Activate

        // Simulated reload produces a freshly projected version.
        var reloaded = new _FakeEditableAsset { AssetId = assetId, Name = "After" };

        // Act: reconcile from catalog.
        mgr.ReconcileFromCatalog(new[] { reloaded });

        // Assert: active doc now has the freshly projected asset.
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
