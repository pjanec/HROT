using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-2236</c> (CV-7, R-3013; docs/DESIGN_Civilians_And_Crowd_Control.md §5e) — one candidate per EXTERIOR ground-storey door of
    /// a non-solid building within the search radius that is not Locked: a point <see cref="InsetMetres"/> past the door, inside the
    /// building's footprint. A locked door is no way in (R-4035: "if the doors can be opened"); a closed one is, the mover opens it
    /// (<c>DoorPassageSystem</c>). A world with no terrain or no buildings answers nothing.
    /// </summary>
    public sealed class EnterableDoorGenerator : PointPatternGenerator
    {
        /// <summary>How far past the door, into the building, the candidate stands (m). Default 2.</summary>
        public float InsetMetres { get; set; } = 2f;

        protected override void Layout(ISimulationView view, Entity observer, ref EqsSensor sensor, Vector2 anchor, ref Emitter emit)
        {
            if (view is not EntityRepository repo || !repo.HasSingletonManaged<TerrainWorld>()) return;
            var world = repo.GetSingletonManaged<TerrainWorld>()!;
            if (world.Doors.Count == 0 || world.Buildings.Count == 0) return;
            var doors = DoorStates.Of(view, world);
            float r2 = sensor.SearchRadius * sensor.SearchRadius;
            for (int i = 0; i < world.Doors.Count && !emit.Full; i++)
            {
                var d = world.Doors[i];
                if (doors[i] == TerrainDoorState.Locked) continue;
                if (Vector2.DistanceSquared(d.Center, anchor) > r2) continue;
                if (TryInside(world, d, InsetMetres, out var inside)) emit.Add(inside);
            }
        }

        /// <summary>The point <paramref name="inset"/> metres past <paramref name="door"/> inside its building — false for a door of a free
        /// wall, a solid building, an upper storey, or an inner door (both sides inside).</summary>
        public static bool TryInside(TerrainWorld world, TerrainDoorDef door, float inset, out Vector2 inside)
        {
            inside = default;
            if (door.Panel < 0 || door.Panel >= world.Panels.Count) return false;
            var panel = world.Panels[door.Panel];
            if (panel.Building < 0 || panel.Building >= world.Buildings.Count || panel.Storey != 0) return false;
            var building = world.Buildings[panel.Building];
            if (building.Solid || building.Footprint.Length < 3) return false;
            var along = panel.B - panel.A;
            if (along.LengthSquared() < 1e-6f) return false;
            var normal = Vector2.Normalize(new Vector2(-along.Y, along.X));
            var a = door.Center + normal * inset;
            var b = door.Center - normal * inset;
            bool inA = PolygonMath.Contains(building.Footprint, a), inB = PolygonMath.Contains(building.Footprint, b);
            if (inA == inB) return false;   // an inner door, or a door off its own footprint
            inside = inA ? a : b;
            return true;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2236</c> (CV-7) — keeps a candidate only when it stands inside the footprint of a non-solid building (any storey's floor plan
    /// is the footprint). The reusable "is this point indoors?" filter; <see cref="IsInside"/> is the same question for a caller.
    /// </summary>
    public sealed class InsideTest : IEqsTest
    {
        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var world = view is EntityRepository repo && repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId == -1L) continue;
                if (world == null || IsInside(world, new Vector2(c.PositionX, c.PositionY), out _)) continue;
                c.EntityId = -1L;
            }
        }

        /// <summary>True when <paramref name="p"/> is inside a non-solid building's footprint; <paramref name="building"/> is its index.</summary>
        public static bool IsInside(TerrainWorld world, Vector2 p, out int building)
        {
            IReadOnlyList<TerrainBuilding> list = world.Buildings;
            for (int b = 0; b < list.Count; b++)
                if (!list[b].Solid && list[b].Footprint.Length >= 3 && PolygonMath.Contains(list[b].Footprint, p)) { building = b; return true; }
            building = -1;
            return false;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2236</c> (CV-7, §5e) — where a panicking civilian hides: just inside the nearest reachable building through a door that is
    /// not locked. Door candidates (<see cref="EnterableDoorGenerator"/>), indoors (<see cref="InsideTest"/>), reachable through the doors
    /// as they are now (<see cref="NavmeshReachableTest"/> — a locked door is a wall), nearest and cheapest to walk first. No enterable
    /// building in reach answers nothing; the caller cowers instead.
    /// </summary>
    [EqsTemplate(AssetId)]
    public static class FindEnterableShelter
    {
        public const string AssetId = "fa67c557-6b8b-4d67-a0b6-11a378a0e348";
        public const uint BlueprintId = 0xC540D2FBu;   // EqsTemplateRegistry.BlueprintIdOf(AssetId) — pinned by EnterableShelterTests

        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId     = BlueprintId,
            Generator       = new EnterableDoorGenerator { AnchorSlot = 0 },
            FilterCheap     = new IEqsTest[] { new InsideTest() },
            FilterExpensive = new IEqsTest[] { new NavmeshReachableTest() },
            ScoreCheap      = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive  = new IEqsTest[] { new PathCostScoreTest() },
            MaxCandidates   = 32,
        };
    }
}
