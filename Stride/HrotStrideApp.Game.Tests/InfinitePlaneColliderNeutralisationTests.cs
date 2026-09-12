#nullable enable
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Physics;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// 🔴🔴 <b>CE-232 — the rail for the defect that made every physics-bodied scenario entity teleport.</b>
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> <c>MainScene</c>'s four template walls are
/// <c>StaticPlaneColliderShapeDesc</c>, and a Bullet static plane is an <b>infinite half-space</b>, not a
/// wall segment. Everything beyond ±20 m was therefore the inside of a solid, so bodies authored at
/// <c>(446, 420)</c> were expelled at up to <b>780 m/s</b> and came to rest jammed inside the template
/// arena — which also made a tank report 2.4 m/s of velocity while never translating, because the motor
/// was pushing into a wall the whole time.</para>
///
/// <para>⚠ <b>Why a new class rather than a test folded into an existing suite</b> (<c>R-142</c> ④ prefers
/// the latter): the nearest suites are <c>DynamicBodyInitialPoseTests</c>, which is about the
/// <c>IPhysicsBodyService</c> CONTRACT against a fake, and <c>StrideSceneGeometryExtractorTests</c>, which
/// is in a different assembly and about navmesh extraction. Neither owns scene sanitising at boot, and
/// there was no suite that did.</para>
///
/// <para>⭐⭐ <b>The load-bearing half is the second test.</b> Asserting that the filter removes a plane
/// mostly restates the code. Asserting it leaves a <c>BoxColliderShapeDesc</c> ALONE is what protects the
/// <c>CE-231</c> ground slab — a box collider in the same scene — from a future widening of this filter
/// that would silently delete the floor and put the falling back.</para>
/// </remarks>
public sealed class InfinitePlaneColliderNeutralisationTests
{
    private static StaticColliderComponent PlaneCollider()
    {
        var c = new StaticColliderComponent();
        c.ColliderShapes.Add(new StaticPlaneColliderShapeDesc
        {
            Normal = new Vector3(-1f, 0f, 0f),
            Offset = 0f,
        });
        return c;
    }

    private static StaticColliderComponent BoxCollider()
    {
        var c = new StaticColliderComponent();
        c.ColliderShapes.Add(new BoxColliderShapeDesc { Size = new Vector3(20000f, 1f, 20000f) });
        return c;
    }

    [Fact]
    public void AnInfinitePlaneColliderIsCollectedForRemoval()
    {
        var wall = new Entity("Wall_East") { PlaneCollider() };

        var victims = new List<(Entity Entity, StaticColliderComponent Collider)>();
        StrideHrotGame.CollectInfinitePlaneColliders(wall, victims);

        Assert.Single(victims);
        Assert.Same(wall, victims[0].Entity);
    }

    /// <summary>
    /// ⭐ The CE-231 ground slab is a <c>BoxColliderShapeDesc</c> in the same scene. If this filter ever
    /// widens to "static collider" rather than "static PLANE collider", the floor goes with the walls and
    /// every entity falls again — silently, because both symptoms look like physics being physics.
    /// </summary>
    [Fact]
    public void ABoxColliderIsLeftAlone()
    {
        var ground = new Entity("ScenarioGroundPlane") { BoxCollider() };

        var victims = new List<(Entity Entity, StaticColliderComponent Collider)>();
        StrideHrotGame.CollectInfinitePlaneColliders(ground, victims);

        Assert.Empty(victims);
    }

    /// <summary>
    /// The walls are root entities today, but the arena is a prefab hierarchy and nothing guarantees that
    /// stays true — the walk must descend.
    /// </summary>
    [Fact]
    public void APlaneColliderNestedUnderAChildIsFound()
    {
        var root  = new Entity("Arena");
        var child = new Entity("Wall_North") { PlaneCollider() };
        root.AddChild(child);

        var victims = new List<(Entity Entity, StaticColliderComponent Collider)>();
        StrideHrotGame.CollectInfinitePlaneColliders(root, victims);

        Assert.Single(victims);
        Assert.Same(child, victims[0].Entity);
    }

    /// <summary>
    /// ⛔ Anti-vacuity: an entity with no collider at all must contribute nothing, so a filter that
    /// accidentally matched everything could not pass the tests above by matching nothing here.
    /// </summary>
    [Fact]
    public void AnEntityWithNoColliderContributesNothing()
    {
        var victims = new List<(Entity Entity, StaticColliderComponent Collider)>();
        StrideHrotGame.CollectInfinitePlaneColliders(new Entity("Camera"), victims);

        Assert.Empty(victims);
    }
}
