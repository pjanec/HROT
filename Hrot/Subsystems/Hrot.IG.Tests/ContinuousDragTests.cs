using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Core.Network;
using Hrot.IG;
using Hrot.Map.Common.Commands;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for BUG1-I001: continuous-drag throttle feature.
///
/// Verifies that:
/// <list type="bullet">
///   <item>When <see cref="MapUserConfig.ContinuousDragUpdates"/> is <c>false</c>,
///   entity-moved events do NOT call the gateway.</item>
///   <item>When enabled, gateway calls are throttled to ~10 Hz
///   (<c>ContinuousDragIntervalSec = 0.1f</c>).</item>
///   <item>Drag-end always sends exactly one update, regardless of the flag.</item>
///   <item>Drag-end resets the throttle timer so subsequent moves start fresh.</item>
/// </list>
///
/// No DDS round-trip required — the stub <see cref="TallyGateway"/> captures calls
/// in memory; injected via <c>TestHook_SetCommandGateway</c>.
/// </summary>
public class ContinuousDragTests
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const long NetworkId = 42L;

    // 30 ms frame time: 3 × 0.033f = 0.099f < 0.1f (no trigger);
    // 4 × 0.033f = 0.132f > 0.1f → one call fires and timer resets.
    private const float Dt = 0.033f;

    // ── Stub ──────────────────────────────────────────────────────────────────

    /// <summary>Records every <see cref="ICommandGateway.SendUpdateDescriptorAsync"/> call.</summary>
    private sealed class TallyGateway : ICommandGateway
    {
        public int Calls { get; private set; }
        public void Dispose() { }
        public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default) => Task.FromResult(0);
        public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default) { Calls++; return Task.CompletedTask; }
        public Task<MissionCommitResult> SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
            => Task.FromResult(new MissionCommitResult { Success = true });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IgApplication CreateHeadlessIg(int domainId)
    {
        var app = new IgApplication();
        app.InitializeEmbedded(headless: true, domainIdOverride: domainId);
        return app;
    }

    /// <summary>
    /// Creates an entity in the IgApplication world with a NetworkIdentity,
    /// registers it in the entity map, and injects the stub command gateway.
    /// </summary>
    private static void SetupEntity(IgApplication app, long networkId, TallyGateway stub)
    {
        var entity = app.World.CreateEntity();
        app.World.AddComponent(entity, new NetworkIdentity(networkId));
        app.TestHook_EntityMap.Register(networkId, entity);
        app.TestHook_SetCommandGateway(stub);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When <see cref="MapUserConfig.ContinuousDragUpdates"/> is <c>false</c>,
    /// entity-moved callbacks must never call the gateway.
    /// </summary>
    [Fact]
    public void ContinuousDragOff_RepeatMoves_NoGatewayCalls()
    {
        var igApp = CreateHeadlessIg(220);
        try
        {
            var stub = new TallyGateway();
            SetupEntity(igApp, NetworkId, stub);
            igApp.TestHook_UserConfig.ContinuousDragUpdates = false;

            for (int i = 0; i < 20; i++)
                igApp.TestHook_SimulateEntityMoved(NetworkId, new Vector2(i, i), Dt);

            Assert.Equal(0, stub.Calls);
        }
        finally { igApp.Shutdown(ownsWindow: false); }
    }

    /// <summary>
    /// When <see cref="MapUserConfig.ContinuousDragUpdates"/> is <c>true</c>,
    /// the first gateway call fires only once the 0.1 s threshold is crossed.
    /// With Dt = 1/30 s, three moves accumulate 0.0999 s (below threshold);
    /// the fourth move brings the total to 0.1332 s (above threshold) → one call.
    /// </summary>
    [Fact]
    public void ContinuousDragOn_CallsFiredAtThreshold()
    {
        var igApp = CreateHeadlessIg(221);
        try
        {
            var stub = new TallyGateway();
            SetupEntity(igApp, NetworkId, stub);
            igApp.TestHook_UserConfig.ContinuousDragUpdates = true;

            // 3 moves: cumulative timer ≈ 0.0999 s — below threshold, no call yet
            for (int i = 0; i < 3; i++)
                igApp.TestHook_SimulateEntityMoved(NetworkId, new Vector2(i * 5f, 0f), Dt);
            Assert.Equal(0, stub.Calls);

            // 4th move: timer crosses 0.1 s → exactly one call, timer resets
            igApp.TestHook_SimulateEntityMoved(NetworkId, new Vector2(20f, 0f), Dt);
            Assert.Equal(1, stub.Calls);
        }
        finally { igApp.Shutdown(ownsWindow: false); }
    }

    /// <summary>
    /// Drag-end (drop) must send exactly one gateway call regardless of the
    /// <see cref="MapUserConfig.ContinuousDragUpdates"/> flag.
    /// </summary>
    [Fact]
    public void DragEnd_AlwaysSendsExactlyOneUpdate()
    {
        var igApp = CreateHeadlessIg(222);
        try
        {
            var stub = new TallyGateway();
            SetupEntity(igApp, NetworkId, stub);
            igApp.TestHook_UserConfig.ContinuousDragUpdates = false;

            igApp.TestHook_SimulateDragDrop(NetworkId, new Vector2(100f, 200f));

            Assert.Equal(1, stub.Calls);
        }
        finally { igApp.Shutdown(ownsWindow: false); }
    }

    /// <summary>
    /// Drag-end must reset the continuous-drag throttle timer to zero so the
    /// next drag sequence starts with a fresh accumulation.
    /// </summary>
    [Fact]
    public void DragEnd_ResetsContinuousDragTimer()
    {
        var igApp = CreateHeadlessIg(223);
        try
        {
            var stub = new TallyGateway();
            SetupEntity(igApp, NetworkId, stub);
            igApp.TestHook_UserConfig.ContinuousDragUpdates = true;

            // Pre-seed the timer close to the threshold
            igApp.TestHook_ContinuousDragTimer = 0.09f;

            igApp.TestHook_SimulateDragDrop(NetworkId, new Vector2(50f, 50f));

            Assert.Equal(0f, igApp.TestHook_ContinuousDragTimer);
            Assert.Equal(1, stub.Calls); // drop itself triggered one update
        }
        finally { igApp.Shutdown(ownsWindow: false); }
    }

    // ── BUG2-I001: Shift-key immediate drag path ──────────────────────────────

    /// <summary>
    /// When Shift is held and the entity moves to a different world position,
    /// exactly one gateway call must fire — no throttle delay.
    /// </summary>
    [Fact]
    public void OnEntityMoved_ShiftHeld_PositionChanged_SendsUpdate()
    {
        var igApp = CreateHeadlessIg(224);
        try
        {
            var stub = new TallyGateway();
            SetupEntity(igApp, NetworkId, stub);
            igApp.TestHook_UserConfig.ContinuousDragUpdates = false;

            // First call seeds _lastDragWorldPos to (0,0).
            igApp.TestHook_SimulateEntityMoved(NetworkId, new Vector2(0f, 0f), Dt, isShiftHeld: false);
            Assert.Equal(0, stub.Calls);

            // Second call: position changed + shift held → one immediate send.
            igApp.TestHook_SimulateEntityMoved(NetworkId, new Vector2(10f, 10f), Dt, isShiftHeld: true);
            Assert.Equal(1, stub.Calls);
        }
        finally { igApp.Shutdown(ownsWindow: false); }
    }

    /// <summary>
    /// When Shift is held but the world position has not changed, the gateway
    /// must NOT be called (avoids sending redundant updates mid-drag).
    /// </summary>
    [Fact]
    public void OnEntityMoved_ShiftHeld_SamePosition_DoesNotSend()
    {
        var igApp = CreateHeadlessIg(225);
        try
        {
            var stub = new TallyGateway();
            SetupEntity(igApp, NetworkId, stub);
            igApp.TestHook_UserConfig.ContinuousDragUpdates = false;

            var samePos = new Vector2(5f, 5f);
            // Seed last pos.
            igApp.TestHook_SimulateEntityMoved(NetworkId, samePos, Dt, isShiftHeld: false);

            // Shift held but same position — no send.
            igApp.TestHook_SimulateEntityMoved(NetworkId, samePos, Dt, isShiftHeld: true);
            Assert.Equal(0, stub.Calls);
        }
        finally { igApp.Shutdown(ownsWindow: false); }
    }

    /// <summary>
    /// When Shift is NOT held and continuous-drag-updates is disabled,
    /// entity-moved events must not call the gateway at all.
    /// Ensures the shift-path does not accidentally trigger without Shift.
    /// </summary>
    [Fact]
    public void OnEntityMoved_ShiftNotHeld_ContinuousDragDisabled_DoesNotSend()
    {
        var igApp = CreateHeadlessIg(226);
        try
        {
            var stub = new TallyGateway();
            SetupEntity(igApp, NetworkId, stub);
            igApp.TestHook_UserConfig.ContinuousDragUpdates = false;

            for (int i = 0; i < 10; i++)
                igApp.TestHook_SimulateEntityMoved(NetworkId, new Vector2(i * 3f, 0f), Dt, isShiftHeld: false);

            Assert.Equal(0, stub.Calls);
        }
        finally { igApp.Shutdown(ownsWindow: false); }
    }
}
