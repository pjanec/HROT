using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Debug;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for <see cref="BlueprintDebugToNodeEditAdapter"/> — bridges
/// <see cref="IBlueprintDebugSession"/> to NodeEdit's <see cref="NodeEditor.Core.Interfaces.IDebugSession"/>.
/// </summary>
public sealed class BlueprintDebugToNodeEditAdapterTests
{
    private static readonly Guid AssetId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
    private static readonly Guid GraphId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
    private static readonly Guid OtherAssetId = Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");

    // ── helpers ────────────────────────────────────────────────────────────────

    private static (BlueprintDebugToNodeEditAdapter Adapter, CapturingDebugSession Session) Create()
    {
        var session = new CapturingDebugSession();
        var adapter = new BlueprintDebugToNodeEditAdapter(session, AssetId, GraphId);
        return (adapter, session);
    }

    // ── Test 1: ToggleBreakpoint sets when not already set ───────────────────

    [Fact]
    public void ToggleBreakpoint_Sets_WhenNotAlreadySet()
    {
        var (adapter, session) = Create();
        var nodeGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var nodeId = new NodeId(nodeGuid);

        adapter.ToggleBreakpoint(nodeId);

        var bps = session.GetBreakpoints();
        Assert.Contains(bps, bp =>
            bp.AssetId == AssetId &&
            bp.GraphId == GraphId &&
            bp.NodeId == nodeGuid.ToString("D"));
    }

    // ── Test 2: ToggleBreakpoint clears when already set ─────────────────────

    [Fact]
    public void ToggleBreakpoint_Clears_WhenAlreadySet()
    {
        var (adapter, session) = Create();
        var nodeGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var nodeId = new NodeId(nodeGuid);

        // Pre-register a breakpoint.
        session.SetBreakpoint(AssetId, GraphId, nodeGuid);

        // Toggle should clear it.
        adapter.ToggleBreakpoint(nodeId);

        Assert.DoesNotContain(session.GetBreakpoints(), bp =>
            bp.AssetId == AssetId && bp.GraphId == GraphId && bp.NodeId == nodeGuid.ToString("D"));
    }

    // ── Test 3: Breakpoints returns correct set ──────────────────────────────

    [Fact]
    public void Breakpoints_ReturnsCorrectSet()
    {
        var (adapter, session) = Create();
        var nodeGuid1 = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var nodeGuid2 = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var otherNodeGuid = Guid.Parse("55555555-5555-5555-5555-555555555555");

        // Set breakpoints: two in matching asset/graph, one in different asset.
        session.SetBreakpoint(AssetId, GraphId, nodeGuid1);
        session.SetBreakpoint(AssetId, GraphId, nodeGuid2);
        session.SetBreakpoint(OtherAssetId, GraphId, otherNodeGuid);

        var breakpoints = adapter.Breakpoints;

        Assert.Equal(2, breakpoints.Count);
        Assert.Contains(new NodeId(nodeGuid1), breakpoints);
        Assert.Contains(new NodeId(nodeGuid2), breakpoints);
    }

    // ── Test 4: IsPaused delegates to session ────────────────────────────────

    [Fact]
    public void IsPaused_DelegatesToSession()
    {
        var (adapter, session) = Create();

        Assert.False(adapter.IsPaused);

        session.IsPaused = true;
        Assert.True(adapter.IsPaused);
    }

    // ── Test 5: Continue/StepOver/StepInto/StepOut delegate ──────────────────

    [Fact]
    public void Continue_StepOver_StepInto_StepOut_Delegate()
    {
        var (adapter, session) = Create();

        adapter.Continue();
        adapter.StepOver();
        adapter.StepInto();
        adapter.StepOut();

        Assert.Equal(1, session.ContinueCallCount);
        Assert.Equal(1, session.StepOverCallCount);
        Assert.Equal(1, session.StepIntoCallCount);
        Assert.Equal(1, session.StepOutCallCount);
    }

    // ── Test 6: CurrentlyExecutingNode from history ──────────────────────────

    [Fact]
    public void CurrentlyExecutingNode_FromHistory()
    {
        var (adapter, session) = Create();
        var nodeGuid = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var nodeIdStr = nodeGuid.ToString("D");
        var self = new Fdp.Core.Entity(42);

        // Simulate node execution via the probe sink path.
        session.OnNodeEnter(self, nodeIdStr);

        var executing = adapter.CurrentlyExecutingNode;

        Assert.NotNull(executing);
        Assert.Equal(nodeGuid, executing!.Value.Value);
    }

    // ── Test 7: IsAttached returns true ──────────────────────────────────────

    [Fact]
    public void IsAttached_ReturnsTrue()
    {
        var (adapter, _) = Create();

        Assert.True(adapter.IsAttached);
    }
}
