using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using CarKinem.Core;
using CarKinem.Spatial;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;

using Hrot.NED.Common;
using Hrot.Map.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <b>DIAGNOSTIC PROBE — why does a vehicle with a movement intent not move?</b>
///
/// <para>📌 <c>SpawnMovingVehicleIntegrationTests</c> reports <c>SimHost moved=0.0000m</c>: the entity
/// spawns, replicates, promotes and reaches <c>Active</c>, and then never moves in SimHost's OWN
/// simulation. ⛔ Replication is downstream and innocent.</para>
///
/// <para>⭐⭐ <c>CarKinematicsSystem</c> has FOUR independent ways to silently skip an entity, and the
/// failing assertion cannot distinguish them:
/// <list type="number">
///   <item>⛔ <c>if (!repo.HasSingleton&lt;SpatialGridData&gt;()) return;</c> — a GLOBAL early return: no
///     grid, nothing moves anywhere, no log line;</item>
///   <item>its query needs <c>VehicleState</c> + <c>SimTransform</c> + <c>SimVelocity</c> +
///     <c>VehicleParams</c> + <c>NavState</c> — any one missing and the entity is skipped;</item>
///   <item><c>.WithOwned&lt;SimTransform&gt;()</c> — the AUTHORITY bit, separate from presence;</item>
///   <item>the intent chain itself: <c>TestHook_SetMovementIntent</c> writes <c>NavigationIntent</c>,
///     and something must turn that into the <c>NavState</c> the physics reads.</item>
/// </list></para>
///
/// <para>⚠ This probe asserts NOTHING about movement. It DUMPS each gate so the next session reads a
/// cause instead of re-deriving one. ⭐ It is deliberately written as a rail rather than a scratch
/// script so the measurement is repeatable.</para>
/// </summary>
public class WhyDoesTheVehicleNotMoveProbe
{
    private readonly ITestOutputHelper _out;
    public WhyDoesTheVehicleNotMoveProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    [Trait("Category", "Diagnostic")]
    public void DumpEveryGateThatCanSilenceTheKinematics()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 });

        harness.PumpFrames(60);

        // ⭐⭐⭐ CE-103 — capture the world version IMMEDIATELY BEFORE the intent write, so the probe can
        //   ask the exact question NavigationIntentBridgeSystem asks: does its CHANGE-DETECTION query
        //   (QueryDelta, not the plain query) actually see this write?
        uint versionBeforeIntent = harness.SimHost.World!.GlobalVersion;
        harness.SimHost.TestHook_SetMovementIntent(networkId, new Vector2(500f, 500f));
        uint versionAfterIntent  = harness.SimHost.World!.GlobalVersion;

        harness.PumpFrames(60);

        var world = harness.SimHost.World!;
        Assert.True(harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var e),
            "probe precondition: the entity must exist on SimHost");

        _out.WriteLine("── GATE 1: the GLOBAL early return ─────────────────────────");
        bool hasGrid = world.HasSingleton<SpatialGridData>();
        _out.WriteLine($"  HasSingleton<SpatialGridData> = {hasGrid}"
                     + (hasGrid ? "" : "   ⛔ CarKinematicsSystem returns immediately — NOTHING moves"));

        _out.WriteLine("── GATE 2: the query's component requirements ──────────────");
        _out.WriteLine($"  VehicleState   = {world.HasComponent<VehicleState>(e)}");
        _out.WriteLine($"  SimTransform   = {world.HasComponent<SimTransform>(e)}");
        _out.WriteLine($"  SimVelocity    = {world.HasComponent<SimVelocity>(e)}");
        _out.WriteLine($"  VehicleParams  = {world.HasComponent<VehicleParams>(e)}");
        _out.WriteLine($"  NavState       = {world.HasComponent<NavState>(e)}");

        _out.WriteLine("── GATE 3: authority (separate from presence) ──────────────");
        _out.WriteLine($"  HasAuthority<SimTransform> = {world.HasAuthority<SimTransform>(e)}");

        _out.WriteLine("── GATE 4: the intent chain ────────────────────────────────");
        bool hasIntent = world.HasComponent<NavigationIntent>(e);
        _out.WriteLine($"  NavigationIntent present = {hasIntent}");
        if (hasIntent)
        {
            var ni = world.GetComponent<NavigationIntent>(e);
            _out.WriteLine($"    Mode={ni.Mode} Dest=({ni.FinalDestination.X:F1},{ni.FinalDestination.Y:F1}) "
                         + $"TargetSpeed={ni.TargetSpeed} IntentId={ni.IntentId}");
        }
        if (world.HasComponent<NavState>(e))
        {
            var ns = world.GetComponent<NavState>(e);
            _out.WriteLine($"    NavState.Mode={ns.Mode} TargetSpeed={ns.TargetSpeed} "
                         + $"Dest=({ns.FinalDestination.X:F1},{ns.FinalDestination.Y:F1}) "
                         + $"ArrivalRadius={ns.ArrivalRadius} ProgressS={ns.ProgressS} "
                         + $"HasArrived={ns.HasArrived} IsBlocked={ns.IsBlocked} TrajectoryId={ns.TrajectoryId}");
        }

        _out.WriteLine("── GATE 5: is the SIMULATION ACTUALLY ADVANCING? ───────────");
        var v = world.GetComponent<SimVelocity>(e);
        _out.WriteLine($"  SimVelocity.Linear = ({v.Linear.X:F4}, {v.Linear.Y:F4}, {v.Linear.Z:F4})");
        _out.WriteLine($"  view.Tick = {((ISimulationView)world).Tick}");
        if (world.HasSingleton<GlobalTime>())
        {
            var gt = world.GetSingleton<GlobalTime>();
            _out.WriteLine($"  GlobalTime: DeltaTime={gt.DeltaTime} TimeScale={gt.TimeScale} "
                         + $"TotalTime={gt.TotalTime:F3} Frame={gt.FrameNumber} IsAdvancing={gt.IsAdvancing}");
            if (gt.DeltaTime == 0f)
                _out.WriteLine("  \u26d4 DeltaTime is ZERO — UpdateVehicle(dt=0) integrates nothing, "
                             + "so a matched entity still cannot move.");
        }
        else _out.WriteLine("  \u26d4 no GlobalTime singleton");

        _out.WriteLine($"  SimHost TimeControllerMode = {harness.SimHost.TestHook_TimeControllerMode}"
                     + "   (Deterministic/Stepping == the cluster booted PAUSED, CE-101)");

        var vs = world.GetComponent<VehicleState>(e);
        _out.WriteLine($"  VehicleState -> {vs}");

        _out.WriteLine("── THE VERDICT: does the physics query match this entity? ──");
        int matches = 0;
        foreach (var _ in world.Query()
                     .With<VehicleState>()
                     .With<SimTransform>()
                     .WithOwned<SimTransform>()
                     .With<SimVelocity>()
                     .With<VehicleParams>()
                     .With<NavState>()
                     .Build())
            matches++;
        _out.WriteLine($"  CarKinematicsSystem's own query matches {matches} entities");

        _out.WriteLine("── GATE 6: CE-103 — does the BRIDGE's delta query see the write? ──");
        _out.WriteLine($"  GlobalVersion before intent = {versionBeforeIntent}, after = {versionAfterIntent}"
                     + (versionAfterIntent == versionBeforeIntent
                        ? "   \u26d4 UNCHANGED — the write bumped no version, so a delta query cannot see it"
                        : ""));
        var bridgeQuery = world.Query().With<NavigationIntent>().With<NavState>().Build();
        int plain = 0;
        foreach (var _ in bridgeQuery) plain++;
        int delta = 0;
        foreach (var _ in world.QueryDelta(bridgeQuery, versionBeforeIntent)) delta++;
        int deltaFromZero = 0;
        foreach (var _ in world.QueryDelta(bridgeQuery, 0)) deltaFromZero++;
        _out.WriteLine($"  plain .With<NavigationIntent>().With<NavState>() matches = {plain}");
        _out.WriteLine($"  QueryDelta(since versionBeforeIntent)              = {delta}");
        _out.WriteLine($"  QueryDelta(since 0)                                = {deltaFromZero}");
        if (plain > 0 && delta == 0)
            _out.WriteLine("  \u26d4\u26d4 THE BRIDGE IS BLIND: the entity matches the query but NOT the delta "
                         + "\u21d2 NavigationIntentBridgeSystem skips it and NavState stays default.");

        _out.WriteLine("── GATE 7: is the BRIDGE ever EXECUTED? (drive it by hand) ──");
        _out.WriteLine("  The query matches, the delta matches, and the body ends in repo.SetComponent(entity, nav).");
        _out.WriteLine("  So if driving it BY HAND fixes NavState, the logic is fine and the system is simply");
        _out.WriteLine("  never ticked by the harness's SimHost kernel.");
        new Fdp.Toolkit.Navigation.Systems.NavigationIntentBridgeSystem().Execute(world, 0.016f);
        var navAfter = world.GetComponent<NavState>(e);
        _out.WriteLine($"  NavState AFTER one hand-driven Execute: Mode={navAfter.Mode} "
                     + $"TargetSpeed={navAfter.TargetSpeed} Dest=({navAfter.FinalDestination.X:F1},{navAfter.FinalDestination.Y:F1})");
        if (navAfter.Mode != KinematicsMode.None)
            _out.WriteLine("  \u26d4\u26d4 CONFIRMED: the bridge WORKS. It is NOT being executed in the harness.");
        else
            _out.WriteLine("  \u2b50 the bridge ran and still wrote nothing \u21d2 the defect is INSIDE its logic, not scheduling.");

        var tf = world.GetComponent<SimTransform>(e);
        _out.WriteLine($"  SimTransform.Position = ({tf.Position.X:F3}, {tf.Position.Y:F3}, {tf.Position.Z:F3})");
    }
}
