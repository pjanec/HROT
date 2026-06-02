using System;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Fdp.Presentation.Icons;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Renderers;

/// <summary>
/// AIE-033 tests for the HSM host: runtime-overlay and breakpoint-gutter renderers
/// are injected; overlay IsActive is false when session is detached.
/// All tests are headless (no GPU / ImGui context needed).
/// </summary>
public sealed class HsmHostServicesRuntimeOverlayTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    private AiEditorAdapterBundle MakeBundle() => new(_atlas);

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder builder)
    {
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flatData);
        var meta     = HsmEmitter.BuildMachineMetadata(graph);
        return (blob, meta);
    }

    private static HsmAsset MakeSimpleAsset()
    {
        var b = new HsmBuilder("Simple");
        b.State("Idle").Initial();
        var (blob, meta) = Compile(b);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "Simple", "", false, "");
    }

    // ── AIE-033 SC1: HSM host services include expected renderers ──────────────

    [Fact]
    public void HsmHostServices_IncludeExpectedRenderers()
    {
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();

        var ctx = HsmDocumentFactory.Build(asset, bundle);

        var ids = ctx.View.Host.CustomCanvasRenderers.Select(r => r.Id).ToList();

        // Must include all required renderer ids
        ids.Should().Contain("hsm.runtime_overlay",
            "HsmRuntimeOverlayRenderer must be injected by the factory");
        ids.Should().Contain("hsm.breakpoint_gutter",
            "HsmBreakpointGutterRenderer must be injected by the factory");
        ids.Should().Contain("hsm.transition_labels");
        ids.Should().Contain("hsm.initial_state_arrows");
        ids.Should().Contain("hsm.region_conflicts");
        ids.Should().Contain("hsm.history_glyphs");
    }

    // ── AIE-033 SC2: HSM overlay IsActive==false when session is detached ──────

    [Fact]
    public void RuntimeOverlay_IsActive_FalseWhenSessionDetached()
    {
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();

        // Build without debug session (detached mode — authoring)
        var ctx = HsmDocumentFactory.Build(asset, bundle, hsmDebugSession: null);

        var overlayRenderer = ctx.View.Host.CustomCanvasRenderers
            .FirstOrDefault(r => r.Id == "hsm.runtime_overlay");

        overlayRenderer.Should().NotBeNull("runtime overlay must be in the renderer list");
        overlayRenderer!.IsActive.Should().BeFalse(
            "overlay should be inactive when no debug session is attached");
    }

    // ── AIE-033 SC3: HSM overlay IsActive==true when session is attached ───────

    [Fact]
    public void RuntimeOverlay_IsActive_TrueWhenSessionAttached()
    {
        var asset   = MakeSimpleAsset();
        var bundle  = MakeBundle();
        var session = new FakeHsmDebugSession();

        var ctx = HsmDocumentFactory.Build(asset, bundle, hsmDebugSession: session);

        var overlayRenderer = ctx.View.Host.CustomCanvasRenderers
            .FirstOrDefault(r => r.Id == "hsm.runtime_overlay");

        overlayRenderer.Should().NotBeNull();
        overlayRenderer!.IsActive.Should().BeTrue(
            "overlay should be active when a debug session is attached");
    }

    // ── AIE-033 SC4: Breakpoint toggle dispatches SetNodeProperty("isBreakpoint",true) ──

    [Fact]
    public void BreakpointToggle_OnNode_DispatchesSetNodePropertyCommand()
    {
        // Arrange — build a simple HSM asset with one state.
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();
        var ctx    = HsmDocumentFactory.Build(asset, bundle);

        var hostServices = ctx.View.Host as HsmEditorHostServices;
        hostServices.Should().NotBeNull("factory must produce HsmEditorHostServices");

        // Get the first projected state
        var firstNode = ctx.View.Model.Nodes.First();
        var nodeId    = firstNode.Id;

        // Act — toggle breakpoint ON via command sink
        hostServices!.ToggleNodeBreakpoint(nodeId, value: true);

        // Assert — the matching StateNode should have IsBreakpoint == true
        var stateNode = asset.FindStateByStableId(nodeId.Value);
        stateNode.Should().NotBeNull();
        stateNode!.IsBreakpoint.Should().BeTrue(
            "ToggleNodeBreakpoint dispatches SetNodeProperty(\"isBreakpoint\",true) " +
            "through the command sink which sets StateNode.IsBreakpoint");

        // Toggle OFF
        hostServices.ToggleNodeBreakpoint(nodeId, value: false);
        stateNode.IsBreakpoint.Should().BeFalse();
    }

    // ── AIE-033 SC5: Registration order within AfterNodes pass ────────────────

    [Fact]
    public void HsmRenderers_AfterNodesPass_RegisteredInCorrectOrder()
    {
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();
        var ctx    = HsmDocumentFactory.Build(asset, bundle);

        // Collect AfterNodes renderers in registration order
        var afterNodesIds = ctx.View.Host.CustomCanvasRenderers
            .Where(r => r.Pass == NodeEditor.Core.Canvas.CanvasRenderPass.AfterNodes)
            .Select(r => r.Id)
            .ToList();

        // Strict order per design-talk §9:
        // initial_state_arrows → region_conflicts → history_glyphs → breakpoint_gutter → runtime_overlay
        afterNodesIds.Should().ContainInOrder(
            "hsm.initial_state_arrows",
            "hsm.region_conflicts",
            "hsm.history_glyphs",
            "hsm.breakpoint_gutter",
            "hsm.runtime_overlay");
    }

    // ── Fake session used by headless tests ──────────────────────────────────────

    private sealed class FakeHsmDebugSession : IHsmDebugSession
    {
        public HsmInstanceSnapshot? GetCurrentStateSnapshot() => null;

        public System.Collections.Generic.IReadOnlyList<HsmTraceRecord>
            GetRecentTraceHistory(int max = 100) =>
            Array.Empty<HsmTraceRecord>();

        public bool HeatmapModeActive { get; set; }

        public System.Collections.Generic.IReadOnlyDictionary<Guid, int>?
            GetStateEntryCounts(Guid assetId) => null;

        public void ResetStateEntryCounts() { }

#pragma warning disable CS0067 // events never used by this fake
        public event System.Action<HsmBreakpointHit>? OnBreakpointHit;
        public event System.Action<HsmStateEntered>? OnStateEntered;
        public event System.Action<HsmStateExited>? OnStateExited;
        public event System.Action<HsmTransitionFired>? OnTransitionFired;
        public event System.Action<HsmEventQueued>? OnEventQueued;
        public event System.Action<HsmRegionConflict>? OnRegionConflict;
        public event System.Action<HsmGuardEvaluated>? OnGuardEvaluated;
        public event System.Action<HsmTimerEvent>? OnTimerEvent;

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
