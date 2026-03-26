using Bagira.Map.Common.Components;
using Bagira.Map.Definitions;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Bagira.Map.Common.Tests;

/// <summary>
/// Tests for ROUTES1-T001 (RoutePlan managed component + RouteWaypoint struct)
/// and ROUTES1-T002 (PersonalRouteRef, RouteTrajectoryCache blittable structs,
/// CmdAppendPersonalWaypoint event struct).
/// </summary>
public class RoutePlanTests
{
    // ── T001: RoutePlan defaults ──────────────────────────────────────────────

    [Fact]
    public void RoutePlan_DefaultConstruction_WaypointsNotNull()
    {
        var plan = new RoutePlan();
        Assert.NotNull(plan.Waypoints);
    }

    [Fact]
    public void RoutePlan_DefaultConstruction_IsLoopFalse()
    {
        var plan = new RoutePlan();
        Assert.False(plan.IsLoop);
    }

    [Fact]
    public void RoutePlan_DefaultConstruction_VersionZero()
    {
        var plan = new RoutePlan();
        Assert.Equal(0, plan.Version);
    }

    [Fact]
    public void RoutePlan_MutateApiAddsWaypointAndAutoIncrementsVersion()
    {
        var plan = new RoutePlan();
        plan.Mutate(wps => wps.Add(new RouteWaypoint { Position = new Vector3(1, 2, 3), TargetSpeed = 5f }));

        Assert.Single(plan.Waypoints);
        Assert.Equal(1, plan.Version);
        Assert.Equal(new Vector3(1, 2, 3), plan.Waypoints[0].Position);
        Assert.Equal(5f, plan.Waypoints[0].TargetSpeed);
    }

    [Fact]
    public void RoutePlan_EachMutateCall_IncrementsVersionByOne()
    {
        var plan = new RoutePlan();
        plan.Mutate(wps => wps.Add(new RouteWaypoint()));
        plan.Mutate(wps => wps.Add(new RouteWaypoint()));
        plan.Mutate(wps => wps.Add(new RouteWaypoint()));

        Assert.Equal(3, plan.Version);
        Assert.Equal(3, plan.Waypoints.Count);
    }

    // ── T001: RouteWaypoint is a value type ───────────────────────────────────

    [Fact]
    public void RouteWaypoint_IsValueType()
    {
        Assert.True(typeof(RouteWaypoint).IsValueType,
            "RouteWaypoint must be a struct (value type).");
    }

    // ── T001: ECS round-trip ──────────────────────────────────────────────────

    [Fact]
    public void RoutePlan_EcsRoundTrip_PreservesAllWaypointFields()
    {
        using var world = CreateMinimalWorld();
        var entity = world.CreateEntity();

        var plan = new RoutePlan { IsLoop = true };
        plan.Mutate(wps => wps.Add(new RouteWaypoint
        {
            Position      = new Vector3(10f, 20f, 30f),
            TargetSpeed   = 7.5f,
            ExtensionJson = @"{""dangerLevel"":2}",
        }));
        // Version is 1 after first Mutate call.

        world.SetManagedComponent(entity, plan);

        var retrieved = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity);

        Assert.True(retrieved.IsLoop);
        Assert.Equal(1, retrieved.Version);
        Assert.Single(retrieved.Waypoints);
        Assert.Equal(new Vector3(10f, 20f, 30f), retrieved.Waypoints[0].Position);
        Assert.Equal(7.5f, retrieved.Waypoints[0].TargetSpeed);
        Assert.Equal(@"{""dangerLevel"":2}", retrieved.Waypoints[0].ExtensionJson);
    }

    // ── T002: PersonalRouteRef blittable ─────────────────────────────────────

    [Fact]
    public void PersonalRouteRef_IsBlittable()
    {
        Assert.True(IsBlittable<PersonalRouteRef>(),
            "PersonalRouteRef must be a blittable struct.");
    }

    [Fact]
    public void PersonalRouteRef_DefaultRouteEntity_IsNull()
    {
        var prf = new PersonalRouteRef();
        Assert.True(prf.RouteEntity.IsNull,
            "PersonalRouteRef.RouteEntity must default to Entity.Null.");
    }

    // ── T002: RouteTrajectoryCache blittable ──────────────────────────────────

    [Fact]
    public void RouteTrajectoryCache_IsBlittable()
    {
        Assert.True(IsBlittable<RouteTrajectoryCache>(),
            "RouteTrajectoryCache must be a blittable struct.");
    }

    [Fact]
    public void RouteTrajectoryCache_DefaultTrajectoryId_IsZero()
    {
        var cache = new RouteTrajectoryCache();
        Assert.Equal(0, cache.TrajectoryId);
    }

    [Fact]
    public void RouteTrajectoryCache_DefaultCompiledVersion_IsZero()
    {
        var cache = new RouteTrajectoryCache();
        Assert.Equal(0, cache.CompiledVersion);
    }

    // ── T002: CmdAppendPersonalWaypoint blittable ─────────────────────────────

    [Fact]
    public void CmdAppendPersonalWaypoint_IsBlittable()
    {
        Assert.True(IsBlittable<Bagira.Map.Common.Events.CmdAppendPersonalWaypoint>(),
            "CmdAppendPersonalWaypoint must be a blittable struct.");
    }

    [Fact]
    public void CmdAppendPersonalWaypoint_FieldsRoundTrip_Intact()
    {
        using var world = CreateMinimalWorld();
        var entity = world.CreateEntity();

        var cmd = new Bagira.Map.Common.Events.CmdAppendPersonalWaypoint
        {
            VehicleEntity = entity,
            WorldPosition = new Vector3(100f, 200f, 300f),
        };

        // Verify field values survive struct copy (stack round-trip).
        var copy = cmd;
        Assert.Equal(entity, copy.VehicleEntity);
        Assert.Equal(new Vector3(100f, 200f, 300f), copy.WorldPosition);
    }

    // ── T001: ComponentId attribute set correctly ─────────────────────────────

    [Fact]
    public void RoutePlan_ComponentId_Is220()
    {
        var attr = (ComponentIdAttribute?)Attribute.GetCustomAttribute(
            typeof(RoutePlan), typeof(ComponentIdAttribute));
        Assert.NotNull(attr);
        Assert.Equal(BagiraComponentIds.RoutePlan, attr!.Id);
    }

    [Fact]
    public void PersonalRouteRef_ComponentId_Is221()
    {
        var attr = (ComponentIdAttribute?)Attribute.GetCustomAttribute(
            typeof(PersonalRouteRef), typeof(ComponentIdAttribute));
        Assert.NotNull(attr);
        Assert.Equal(BagiraComponentIds.PersonalRouteRef, attr!.Id);
    }

    [Fact]
    public void RouteTrajectoryCache_ComponentId_Is222()
    {
        var attr = (ComponentIdAttribute?)Attribute.GetCustomAttribute(
            typeof(RouteTrajectoryCache), typeof(ComponentIdAttribute));
        Assert.NotNull(attr);
        Assert.Equal(BagiraComponentIds.RouteTrajectoryCache, attr!.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EntityRepository CreateMinimalWorld()
    {
        var world = new EntityRepository();
        world.RegisterManagedComponent<RoutePlan>();
        world.RegisterComponent<PersonalRouteRef>();
        world.RegisterComponent<RouteTrajectoryCache>();
        return world;
    }

    /// <summary>
    /// Returns true if <typeparamref name="T"/> is a blittable struct.
    /// Uses <see cref="Marshal.SizeOf{T}()"/>: this throws
    /// <see cref="ArgumentException"/> for structs that contain reference-type
    /// fields (non-blittable), confirming the type is not blittable.
    /// </summary>
    private static bool IsBlittable<T>() where T : struct
    {
        try
        {
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            Marshal.FreeHGlobal(ptr);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
