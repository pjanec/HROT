using System;
using System.Collections.Generic;
using Fdp.Core;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Renderers;
using Hrot.Editor.AiShared.Debug;
using NodeEditor.Core.Canvas;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

// Minimal fake session for SubtreeBoundaryRenderer tests.
// Only IsAttached and GetCurrentStateSnapshot() are used by the renderer.
file sealed class FakeBTreeSession : IBTreeDebugSession
{
    private readonly bool _isAttached;
    private readonly BehaviorTreeStateSnapshot? _snapshot;

    public FakeBTreeSession(bool isAttached, BehaviorTreeStateSnapshot? snapshot = null)
    {
        _isAttached = isAttached;
        _snapshot   = snapshot;
    }

    public bool IsAttached => _isAttached;
    public bool IsPaused   => false;
    public bool IsAnyBreakpointActive => false;
    public Breakpoint? PausedAt      => null;
    public Entity?     PausedOnEntity => null;

    public BehaviorTreeStateSnapshot? GetCurrentStateSnapshot() => _snapshot;

    public IReadOnlyList<BTreeNodeExecuted>    GetRecentNodeHistory(int max = 100) => Array.Empty<BTreeNodeExecuted>();
    public IReadOnlyList<BTreeAsyncEvent>      GetRecentAsyncHistory(int max = 100) => Array.Empty<BTreeAsyncEvent>();
    public IReadOnlyDictionary<Guid, int>?     GetAggregateCounters(Guid assetId) => null;
    public IReadOnlyList<Breakpoint>           GetBreakpoints() => Array.Empty<Breakpoint>();
    public IReadOnlyList<Entity>               GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

    public bool HeatmapModeActive { get; set; }

    public void Detach()                   { }
    public void ResetAggregateCounters()   { }
    public void Continue()                 { }
    public void StepOver()                 { }
    public void StepInto()                 { }
    public void StepOut()                  { }
    public void Pause()                    { }
    public void BeginObservingAsset(Guid assetId, TraceLevel level) { }
    public void EndObservingAsset(Guid assetId) { }

    public BreakpointId SetBreakpoint(Guid assetId, Guid elementId) => default;
    public void ClearBreakpoint(BreakpointId id) { }
    public void ClearAllBreakpoints() { }

    public event Action<BTreeBreakpointHit>? OnBreakpointHit  { add { } remove { } }
    public event Action<BTreeNodeExecuted>?  OnNodeExecuted    { add { } remove { } }
    public event Action<BTreeAsyncEvent>?    OnAsyncIssued     { add { } remove { } }
    public event Action<BTreeAsyncEvent>?    OnAsyncResolved   { add { } remove { } }
    public event Action<BTreeAsyncEvent>?    OnAsyncAborted    { add { } remove { } }
    public event Action?                     OnSessionStateChanged { add { } remove { } }
}

public sealed class SubtreeBoundaryRendererTests
{
    private static BehaviorTreeAsset MakeEmptyAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), "TestTree", "", false, "", "",
            new BehaviorTreeBlob
            {
                TreeName         = "T",
                Nodes            = Array.Empty<NodeDefinition>(),
                MethodNames      = Array.Empty<string>(),
                FloatParams      = Array.Empty<float>(),
                IntParams        = Array.Empty<int>(),
                SubtreeAssetIds  = Array.Empty<string>(),
            });

    [Fact]
    public void Id_is_btree_subtree_boundaries()
    {
        var renderer = new SubtreeBoundaryRenderer(MakeEmptyAsset());
        renderer.Id.Should().Be("btree.subtree_boundaries");
    }

    [Fact]
    public void Pass_is_BeforeContent()
    {
        var renderer = new SubtreeBoundaryRenderer(MakeEmptyAsset());
        renderer.Pass.Should().Be(CanvasRenderPass.BeforeContent);
    }

    [Fact]
    public void IsActive_false_when_no_session_set()
    {
        var renderer = new SubtreeBoundaryRenderer(MakeEmptyAsset());
        renderer.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_true_when_attached_session_set()
    {
        var renderer = new SubtreeBoundaryRenderer(MakeEmptyAsset());
        renderer.SetSession(new FakeBTreeSession(isAttached: true));
        renderer.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_false_when_detached_session_set()
    {
        var renderer = new SubtreeBoundaryRenderer(MakeEmptyAsset());
        renderer.SetSession(new FakeBTreeSession(isAttached: false));
        renderer.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_false_after_session_cleared()
    {
        var renderer = new SubtreeBoundaryRenderer(MakeEmptyAsset());
        renderer.SetSession(new FakeBTreeSession(isAttached: true));
        renderer.SetSession(null);
        renderer.IsActive.Should().BeFalse();
    }
}
