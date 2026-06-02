using System;
using System.Linq;
using Fbt;
using FluentAssertions;
using Fdp.Presentation.Icons;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Renderers;
using Hrot.Editor.AiShared.Adapters;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Renderers;

/// <summary>
/// AIE-033 tests: BTree host-services include runtime-overlay + breakpoint-gutter renderers;
/// breakpoint toggle dispatches SetNodeProperty through the command sink;
/// overlay IsActive is false when session is detached.
/// All tests are headless (no GPU / ImGui context needed).
/// </summary>
public sealed class BTreeHostServicesRuntimeOverlayTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    private AiEditorAdapterBundle MakeBundle() => new(_atlas);

    private static BehaviorTreeAsset MakeEmptyAsset() =>
        BehaviorTreeAssetProjector.Project(
            new BehaviorTreeBlob
            {
                TreeName        = "T",
                Nodes           = Array.Empty<NodeDefinition>(),
                MethodNames     = Array.Empty<string>(),
                FloatParams     = Array.Empty<float>(),
                IntParams       = Array.Empty<int>(),
                SubtreeAssetIds = Array.Empty<string>(),
            },
            null, null, Guid.NewGuid(), "T", "/T.cs", false, "", "");

    // ── AIE-033 SC1: BTree host services include runtime-overlay and breakpoint-gutter renderers ──

    [Fact]
    public void BTreeHostServices_IncludeRuntimeOverlayAndBreakpointRenderers()
    {
        // Arrange
        var asset  = MakeEmptyAsset();
        var bundle = MakeBundle();

        // Act — no debug session → overlay and gutter are still present in the list
        var ctx = BTreeDocumentFactory.Build(asset, bundle);

        // Assert — renderers by id
        var ids = ctx.View.Host.CustomCanvasRenderers.Select(r => r.Id).ToList();

        ids.Should().Contain("btree.runtime_overlay",
            "BTreeRuntimeOverlayRenderer must be injected by the factory");
        ids.Should().Contain("btree.breakpoint_gutter",
            "BTreeBreakpointGutterRenderer must be injected by the factory");

        // Also verify other required renderer ids are present
        ids.Should().Contain("btree.heatmap_overlay");
        ids.Should().Contain("btree.subtree_boundaries");
        ids.Should().Contain("btree.observer_guard_badges");
    }

    // ── AIE-033 SC2: RuntimeOverlay IsActive==false when session is detached ──

    [Fact]
    public void RuntimeOverlay_IsActive_FalseWhenSessionDetached()
    {
        // Arrange — build without a debug session (null = detached)
        var asset  = MakeEmptyAsset();
        var bundle = MakeBundle();

        var ctx = BTreeDocumentFactory.Build(asset, bundle, debugSession: null);

        // Find the runtime overlay renderer
        var overlayRenderer = ctx.View.Host.CustomCanvasRenderers
            .FirstOrDefault(r => r.Id == "btree.runtime_overlay");

        overlayRenderer.Should().NotBeNull("runtime overlay must be in the renderer list");

        // IsActive must be false when no session is attached — no per-frame cost
        overlayRenderer!.IsActive.Should().BeFalse(
            "overlay should be inactive (IsActive==false) when no debug session is attached");
    }

    // ── AIE-033 SC3: RuntimeOverlay IsActive==true when session is attached ──

    [Fact]
    public void RuntimeOverlay_IsActive_TrueWhenSessionAttached()
    {
        // Arrange — use a fake session
        var asset   = MakeEmptyAsset();
        var bundle  = MakeBundle();
        var session = new FakeBTreeDebugSession();

        var ctx = BTreeDocumentFactory.Build(asset, bundle, btreeDebugSession: session);

        var overlayRenderer = ctx.View.Host.CustomCanvasRenderers
            .FirstOrDefault(r => r.Id == "btree.runtime_overlay");

        overlayRenderer.Should().NotBeNull();
        overlayRenderer!.IsActive.Should().BeTrue(
            "overlay should be active when a debug session is attached");
    }

    // ── AIE-033 SC4: Breakpoint toggle dispatches SetNodeProperty("isBreakpoint",true) ──

    [Fact]
    public void BreakpointToggle_OnNode_DispatchesSetNodePropertyCommand()
    {
        // Arrange — build a BTree document context with a real one-node asset
        var blob = new BehaviorTreeBlob
        {
            TreeName    = "Toggle",
            Nodes       = new[]
            {
                new NodeDefinition
                {
                    Type            = NodeType.Action,
                    ChildCount      = 0,
                    SubtreeOffset   = 1,
                    RawPayloadIndex = 0,
                }
            },
            MethodNames     = new[] { "Ns.C.Action" },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        var asset  = BehaviorTreeAssetProjector.Project(blob, null, null, Guid.NewGuid(),
            "Toggle", "/Toggle.cs", false, "", "");
        var bundle = MakeBundle();

        var ctx = BTreeDocumentFactory.Build(asset, bundle);

        // Get the host services (typed) so we can call ToggleNodeBreakpoint
        var hostServices = ctx.View.Host as BTreeEditorHostServices;
        hostServices.Should().NotBeNull("factory must produce BTreeEditorHostServices");

        // Get the projected node
        var projectedNode = ctx.View.Model.Nodes.First();
        var nodeId = projectedNode.Id;

        // Act — toggle breakpoint ON via command sink
        hostServices!.ToggleNodeBreakpoint(nodeId, value: true);

        // Assert — the asset node should have IsBreakpoint==true
        var assetNode = asset.FindNode(nodeId.Value);
        assetNode.Should().NotBeNull();
        assetNode!.IsBreakpoint.Should().BeTrue(
            "ToggleNodeBreakpoint(true) must dispatch SetNodeProperty(\"isBreakpoint\",true) " +
            "to the command sink, which sets the node's IsBreakpoint flag");

        // Toggle OFF
        hostServices.ToggleNodeBreakpoint(nodeId, value: false);
        assetNode.IsBreakpoint.Should().BeFalse(
            "ToggleNodeBreakpoint(false) must clear IsBreakpoint");
    }

    // ── Fake session used by headless tests ──────────────────────────────────────

    private sealed class FakeBTreeDebugSession : IBTreeDebugSession
    {
        // IBTreeDebugSession
        public BehaviorTreeStateSnapshot? GetCurrentStateSnapshot() => null;

        public System.Collections.Generic.IReadOnlyList<BTreeNodeExecuted>
            GetRecentNodeHistory(int max = 100) =>
            Array.Empty<BTreeNodeExecuted>();

        public System.Collections.Generic.IReadOnlyList<BTreeAsyncEvent>
            GetRecentAsyncHistory(int max = 100) =>
            Array.Empty<BTreeAsyncEvent>();

        public bool HeatmapModeActive { get; set; }

        public System.Collections.Generic.IReadOnlyDictionary<Guid, int>?
            GetAggregateCounters(Guid assetId) => null;

        public void ResetAggregateCounters() { }

#pragma warning disable CS0067 // events never used by this fake
        public event System.Action<BTreeBreakpointHit>? OnBreakpointHit;
        public event System.Action<BTreeNodeExecuted>? OnNodeExecuted;
        public event System.Action<BTreeAsyncEvent>? OnAsyncIssued;
        public event System.Action<BTreeAsyncEvent>? OnAsyncResolved;
        public event System.Action<BTreeAsyncEvent>? OnAsyncAborted;

        // IAiDebugSession
        public event System.Action? OnSessionStateChanged;
#pragma warning restore CS0067

        public bool IsAttached => true;
        public void Detach() { }
        public Hrot.Editor.AiShared.Debug.BreakpointId SetBreakpoint(Guid assetId, Guid elementId)
            => new Hrot.Editor.AiShared.Debug.BreakpointId(0);
        public void ClearBreakpoint(Hrot.Editor.AiShared.Debug.BreakpointId id) { }
        public void ClearAllBreakpoints() { }
        public System.Collections.Generic.IReadOnlyList<Hrot.Editor.AiShared.Debug.Breakpoint>
            GetBreakpoints() => Array.Empty<Hrot.Editor.AiShared.Debug.Breakpoint>();
        public bool IsAnyBreakpointActive => false;
        public bool IsPaused => false;
        public Hrot.Editor.AiShared.Debug.Breakpoint? PausedAt => null;
        public Fdp.Core.Entity? PausedOnEntity => null;
        public void Continue() { }
        public void StepOver() { }
        public void StepInto() { }
        public void StepOut() { }
        public void Pause() { }

        // IAiTraceObserver
        public void BeginObservingAsset(Guid assetId, Hrot.Editor.AiShared.Debug.TraceLevel level) { }
        public void EndObservingAsset(Guid assetId) { }
        public System.Collections.Generic.IReadOnlyList<Fdp.Core.Entity>
            GetActiveEntities(Guid assetId) => Array.Empty<Fdp.Core.Entity>();
    }
}
