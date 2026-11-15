using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a <see cref="BladeCut"/> cuts at its point.</summary>
    public enum BladeCutTarget : byte
    {
        None = 0,
        /// <summary>A wall panel (a free wall, a fence, a building's wall): a blade-wide, full-height Passage.</summary>
        Wall = 1,
        /// <summary>A lone tree trunk: felled.</summary>
        Tree = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4601</c> (B-4; 📄 docs/DESIGN_Vehicle_Breaching.md §7.6, R-4242, B-D11) — a bulldozer's "my blade is cutting HERE": set by its brain's <c>ClearObstacle</c>
    /// point order once the blade is at the point, cleared when the order ends. The ONE AI exception to "an AI hull never breaks anything" comes through this marker,
    /// never through <c>HullBreakGate</c>: the brain only drives and marks; the Muscle cuts (<see cref="DozerCutSystem"/>), through the one breach writer.
    /// Unmanaged, recorded; Brain group, replicated (<c>dtBladeCut</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BladeCut)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct BladeCut
    {
        /// <summary>1 while the blade cuts.</summary>
        public byte Active;
        public BladeCutTarget Target;
        /// <summary>The order's point (terrain-local).</summary>
        public Vector3 At;
        /// <summary>Seconds of blade contact the cut takes.</summary>
        public float PushSeconds;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4601</c> (B-4) — THE MUSCLE CUTS: a hull with an active <see cref="BladeCut"/> whose blade (its front, half a hull ahead) is within <see cref="ReachMetres"/>
    /// of the marked barrier for <see cref="BladeCut.PushSeconds"/> cuts it: a wall gets a blade-wide (the hull's width + a margin), full-height Passage at the point — the
    /// same <see cref="BuildingDamageEvent"/> a player's break writes; a trunk is felled (<see cref="TreeFellEvent"/>). The progress is this node's own state per hull
    /// (R-5016's precedent). ⛔ Never in a replay; only on the node that moves the hull (<see cref="WorldPosOrdinal"/>'s owner — a hull with no authority is ours).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class DozerCutSystem : IEcsModuleSystem
    {
        /// <summary>The blade's reach to the barrier (m).</summary>
        public const float ReachMetres = 2f;
        /// <summary>The cut's margin each side of the hull's width (m).</summary>
        public const float MarginMetres = 0.5f;
        /// <summary><c>EDescriptorType.dtWorldPos</c> (NED) — the descriptor whose owner moves the hull. ⚠ A rail pins the two equal.</summary>
        public const long WorldPosOrdinal = 2;

        private readonly Dictionary<int, (Vector3 At, float Seconds)> _progress = new();
        private readonly List<int> _near = new();
        private EntityQuery? _query;
        private EntityRepository? _queryRepo;

        /// <summary>Cuts made (rails, diagnostics).</summary>
        public int Cuts { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<BladeCut>()) return;
            if (!repo.HasSingletonManaged<TerrainWorld>() || repo.GetSingletonManaged<TerrainWorld>() is not { } world) return;
            if (!ReferenceEquals(repo, _queryRepo)) { _query = repo.Query().With<BladeCut>().With<SimTransform>().Build(); _queryRepo = repo; }
            long key = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(WorldPosOrdinal, 0);
            foreach (var e in _query!)
            {
                var cut = repo.GetComponentRO<BladeCut>(e);
                if (cut.Active == 0 || cut.Target == BladeCutTarget.None) { _progress.Remove(e.Index); continue; }
                if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkAuthority>() && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>(e)
                    && !Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, e, key)) continue;   // another node moves it
                var tf = repo.GetComponentRO<SimTransform>(e);
                float length = 6f, width = 3.5f;
                if (repo.IsComponentTypeRegistered<VehicleParams>() && repo.HasComponent<VehicleParams>(e))
                {
                    var hull = repo.GetComponentRO<VehicleParams>(e);
                    if (hull.Length > 0f) length = hull.Length;
                    if (hull.Width > 0f) width = hull.Width;
                }
                var fwd = Vector3.Transform(Vector3.UnitX, tf.Rotation);
                var f2 = new Vector2(fwd.X, fwd.Y);
                f2 = f2.LengthSquared() > 1e-6f ? Vector2.Normalize(f2) : Vector2.UnitX;
                var blade = new Vector2(tf.Position.X, tf.Position.Y) + f2 * (length * 0.5f);
                var at = new Vector2(cut.At.X, cut.At.Y);
                if (Vector2.Distance(blade, at) > ReachMetres + width) { _progress.Remove(e.Index); continue; }   // the blade is not at the point

                float seconds = (_progress.TryGetValue(e.Index, out var p) && p.At == cut.At ? p.Seconds : 0f) + deltaTime;
                if (seconds < MathF.Max(cut.PushSeconds, 0.1f)) { _progress[e.Index] = (cut.At, seconds); continue; }
                _progress.Remove(e.Index);
                if (cut.Target == BladeCutTarget.Wall) CutWall(repo, world, at, f2, width + 2f * MarginMetres, e);
                else FellTree(repo, world, at, e);
                Cuts++;
            }
        }

        private void CutWall(EntityRepository repo, TerrainWorld world, Vector2 at, Vector2 fwd, float width, Entity by)
        {
            if (!repo.Bus.IsRegistered<BuildingDamageEvent>()) return;
            int panel = NearestPanel(world, at, ReachMetres, _near);
            if (panel < 0) return;
            var p = world.Panels[panel];
            float len = p.Length;
            if (len <= 1e-3f) return;
            var dir = (p.B - p.A) / len;
            float along = Math.Clamp(Vector2.Dot(at - p.A, dir), 0f, len);
            float from = MathF.Max(0f, along - width * 0.5f), to = MathF.Min(len, along + width * 0.5f);
            repo.Bus.Publish(new BuildingDamageEvent
            {
                Building = WallGroups.Of(world, panel), Panel = panel, From = from, To = to, Bottom = p.BaseZ, Top = p.TopZ,
                Kind = (byte)BreachKind.Passage, Shooter = by,
            });
        }

        private void FellTree(EntityRepository repo, TerrainWorld world, Vector2 at, Entity by)
        {
            if (!repo.Bus.IsRegistered<TreeFellEvent>()) return;
            _near.Clear();
            LoneTrees.Near(world, at, ReachMetres + 1f, _near);
            int best = -1; float bestD = float.MaxValue;
            foreach (int i in _near) { var c = (world.Prisms[i].Min + world.Prisms[i].Max) * 0.5f; float d = Vector2.Distance(c, at); if (d < bestD) { bestD = d; best = i; } }
            if (best < 0) return;
            var key = TreeDamages.KeyOf(world.Prisms[best]);
            repo.Bus.Publish(new TreeFellEvent { X = key.X, Y = key.Y, By = by });
        }

        /// <summary>The wall panel nearest <paramref name="at"/> within <paramref name="radius"/> (its centre line), or −1. Shared with the brain's resolver.</summary>
        public static int NearestPanel(TerrainWorld world, Vector2 at, float radius, List<int> scratch)
        {
            scratch.Clear();
            world.BuildingPanels.Near(at, radius + 1f, scratch);
            int best = -1; float bestD = float.MaxValue;
            foreach (int i in scratch)
            {
                var p = world.Panels[i];
                var ab = p.B - p.A; float l2 = ab.LengthSquared();
                float t = l2 > 1e-9f ? Math.Clamp(Vector2.Dot(at - p.A, ab) / l2, 0f, 1f) : 0f;
                float d = Vector2.Distance(at, p.A + ab * t) - p.Thickness * 0.5f;
                if (d <= radius && d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// ⭐ The brain's success test: does a PASSAGE in <paramref name="doors"/>' view cover <paramref name="at"/> on its nearest wall (open just above the wall's base and at a
        /// man's height)? A wall gone with its building's collapse counts too.
        /// </summary>
        public static bool PassageAt(TerrainWorld world, DoorStates? doors, Vector2 at, float radius, List<int> scratch)
        {
            int panel = NearestPanel(world, at, radius, scratch);
            if (panel < 0) return false;
            var damage = doors?.Damage;
            if (damage == null) return false;
            var p = world.Panels[panel];
            if (p.Building >= 0 && damage.Collapsed(p.Building)) return true;
            if (!damage.HasBreaches(panel)) return false;
            float len = p.Length;
            if (len <= 1e-3f) return false;
            float along = Math.Clamp(Vector2.Dot(at - p.A, (p.B - p.A) / len), 0f, len);
            return damage.Opens(panel, along, p.BaseZ + 0.05f) && damage.Opens(panel, along, MathF.Min(p.TopZ - 0.05f, p.BaseZ + 1.5f));
        }
    }
}
