using System.Linq;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using EDescriptorType = Hrot.NED.Descriptors.EDescriptorType;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <b>DIAGNOSTIC PROBE — CE-500: who owns a SimHost-created tank's brain, on each node?</b>
/// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.2 (the sequence this dumps, step by step).
/// <para>⚠ Asserts NOTHING. It dumps, per node, the record (<c>NetworkAuthority</c>, <c>DescriptorOwnership</c>) and the
/// claim (per-component authority) for the brain anchor and the kinematics anchor, and whether the brain grant
/// is still pending — so a red <c>SimHost_MoveToLocationMission_EntityMovesWithoutGhostTick</c> names its link.</para>
/// </summary>
public class WhoOwnsTheBrainProbe
{
    private static int _domainCounter = 140;   // 141+: free in this assembly (CycloneDDS domain ids stop at 232)
    private readonly ITestOutputHelper _out;
    public WhoOwnsTheBrainProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    [Trait("Category", "Diagnostic")]
    public void DumpTheBrainGrantChainOnBothNodes()
    {
        using var harness = new HrotRunnerHarness("simhost,cgf", Interlocked.Increment(ref _domainCounter));

        long id = harness.SimHost.TestHook_SpawnEntity(TkbEntityTypes.Tank_M1Abrams,
            new CoreGeoPoint { Latitude = 52.524, Longitude = 13.415, Altitude = 0 });

        bool both = harness.PumpUntil(() =>
            harness.Cgf!.GhostEntityMap is { } m && m.TryGetEntity(id, out _)
            && harness.SimHost.TestHook_EntityMap.TryGetEntity(id, out _), 300);
        _out.WriteLine($"entity {id} on both nodes: {both}");
        harness.PumpFrames(180);

        Dump("SimHost", harness.SimHost.App.WorldOrNull!, harness.SimHost.TestHook_EntityMap, id);
        Dump("CGF",     harness.Cgf!.World!,              harness.Cgf!.GhostEntityMap!,     id);

        // ── Transport: write the intent on CGF (as its brain would) and see whether SimHost gets it and moves ──
        var simW = harness.SimHost.App.WorldOrNull!;
        harness.SimHost.TestHook_EntityMap.TryGetEntity(id, out var se);
        var start = simW.GetComponent<SimTransform>(se).Position;
        var cgfW = harness.Cgf!.World!;
        harness.Cgf!.GhostEntityMap!.TryGetEntity(id, out var ce);
        var intent = cgfW.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(ce)
            ? cgfW.GetComponent<Fdp.Toolkit.Navigation.NavigationIntent>(ce) : default;
        intent.Mode             = Fdp.Toolkit.Navigation.NavigationMode.DirectPoint;
        intent.FinalDestination = start + new System.Numerics.Vector3(500f, 500f, 0f);
        intent.TargetSpeed      = 15f;
        intent.ArrivalRadius    = 20f;
        unchecked { intent.IntentId++; }
        cgfW.BumpMemoryVersion();
        cgfW.SetComponent(ce, intent);
        _out.WriteLine($"[CGF] wrote NavigationIntent id={intent.IntentId} dest={intent.FinalDestination}");

        harness.PumpFrames(240);

        var simIntent = simW.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(se)
            ? simW.GetComponent<Fdp.Toolkit.Navigation.NavigationIntent>(se) : default;
        var end = simW.GetComponent<SimTransform>(se).Position;
        _out.WriteLine($"[SimHost] NavigationIntent mode={simIntent.Mode} id={simIntent.IntentId} dest={simIntent.FinalDestination}");
        _out.WriteLine($"[SimHost] moved {System.Numerics.Vector3.Distance(start, end):F2} m");
    }

    private void Dump(string node, EntityRepository w, NetworkEntityMap map, long id)
    {
        if (!map.TryGetEntity(id, out var e) || !w.IsAlive(e)) { _out.WriteLine($"[{node}] entity {id} ABSENT"); return; }

        string auth = w.HasComponent<NetworkAuthority>(e)
            ? $"primary={w.GetComponentRO<NetworkAuthority>(e).PrimaryOwnerId} local={w.GetComponentRO<NetworkAuthority>(e).LocalNodeId}"
            : "no NetworkAuthority";
        _out.WriteLine($"[{node}] {auth} lifecycle={w.GetLifecycleState(e)} pendingGrants={w.HasManagedComponent<PendingAuthorityGrants>(e)}");

        var owner = w.HasManagedComponent<DescriptorOwnership>(e) ? w.GetComponent<DescriptorOwnership>(e) : null;
        foreach (var (name, d, cid) in new[]
                 {
                     ("dtNavigationIntent", (long)EDescriptorType.dtNavigationIntent, Fdp.Toolkit.Navigation.NavigationContractsComponentIds.NavigationIntent),
                     ("dtEntityMission",    (long)EDescriptorType.dtEntityMission,    GlobalComponentIds.MissionPlanQueue),
                     ("dtWorldPos",         (long)EDescriptorType.dtWorldPos,         GlobalComponentIds.SimTransform),
                 })
        {
            long key = OwnershipExtensions.PackKey(d, 0);
            string rec = owner != null && owner.TryGetOwner(key, out int o) ? o.ToString() : "-";
            bool gate  = ((Fdp.ModuleHost.Abstractions.ISimulationView)w).HasAuthority(e, key);
            bool has   = w.HasComponentByTypeId(e, cid);
            bool claim = has && w.HasAuthority(e, cid);
            _out.WriteLine($"[{node}]   {name}: record={rec} gate={gate} component={has} claim={claim}");
        }
    }
}
