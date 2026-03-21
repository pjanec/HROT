using System.Numerics;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG;
using Bagira.Map.Common.Commands;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;

namespace Bagira.IG.Tests;

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

    /// <summary>Records every <see cref="IBdcCommandGateway.SendUpdateDescriptor"/> call.</summary>
    private sealed class TallyGateway : IBdcCommandGateway
    {
        public int Calls { get; private set; }
        public void SendUpdateDescriptor(UpdateEntityDescriptorRequest request) => Calls++;
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
}
