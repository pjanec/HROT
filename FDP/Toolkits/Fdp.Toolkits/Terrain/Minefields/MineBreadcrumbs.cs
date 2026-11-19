using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Extensions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 (DESIGN_Minefields.md MR-5; lane decision R-5049) — a mover's own recent track near a minefield: the last <see cref="Capacity"/> points, at least
    /// <see cref="MineBreadcrumbSystem.StepMetres"/> apart, kept only while the mover is within <see cref="MineBreadcrumbSystem.MarginMetres"/> of a field's bounds. A struck unit backs
    /// OUT along it and the medic comes in along it — safe by construction: what was under it has gone off or was never there. Unmanaged, recorded; written by the BRAIN node
    /// that owns the unit (its <c>BehaviorState</c>) — the node where the drill and the medic run; ⛔ not replicated (R-5049).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MineBreadcrumbs)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct MineBreadcrumbs
    {
        public const int Capacity = 32;
        /// <summary>Points in use (≤ <see cref="Capacity"/>).</summary>
        public byte Count;
        /// <summary>Where the NEXT point goes (a ring).</summary>
        public byte Head;
        public fixed float X[Capacity];
        public fixed float Y[Capacity];

        /// <summary>The newest point, or false when there is none.</summary>
        public readonly bool TryNewest(out Vector2 p)
        {
            p = default;
            if (Count == 0) return false;
            int i = (Head - 1 + Capacity) % Capacity;
            p = new Vector2(X[i], Y[i]);
            return true;
        }

        /// <summary>Point <paramref name="back"/> steps back from the newest (0 = the newest) — the back-out order.</summary>
        public readonly Vector2 Back(int back)
        {
            if ((uint)back >= Count) throw new ArgumentOutOfRangeException(nameof(back));
            int i = ((Head - 1 - back) % Capacity + Capacity) % Capacity;
            return new Vector2(X[i], Y[i]);
        }

        /// <summary>Copies the trail newest → oldest into <paramref name="into"/>; returns how many.</summary>
        public readonly int CopyBack(Span<Vector2> into)
        {
            int n = Math.Min(Count, into.Length);
            for (int k = 0; k < n; k++) into[k] = Back(k);
            return n;
        }

        public void Add(Vector2 p)
        {
            X[Head] = p.X; Y[Head] = p.Y;
            Head = (byte)((Head + 1) % Capacity);
            if (Count < Capacity) Count++;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 (MR-5, R-5049) — keeps <see cref="MineBreadcrumbs"/> for every unit this BRAIN node drives (authority over its <c>BehaviorState</c>) while it is near a
    /// minefield: a point every <see cref="StepMetres"/>; the trail is dropped once the unit has been clear of every field for <see cref="ForgetSeconds"/>. Serial; never in a
    /// replay. 0 B a steady frame (the field boxes are rebuilt only when a field changes).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class MineBreadcrumbSystem : IEcsModuleSystem
    {
        public const float StepMetres = 2f, MarginMetres = 15f, ForgetSeconds = 30f;

        private readonly bool _gateOnAuthority;
        [Fdp.Core.WorldDerived("minefield bounding boxes rebuilt from the world when count changes or after 1 s (Boxes)")]
        private readonly List<(Vector2 Min, Vector2 Max)> _boxes = new();
        [Fdp.Core.FrameScratch("outline buffer: filled by TryOutline for each field inside Boxes")]
        private readonly List<Vector2> _outline = new();
        [Fdp.Core.SimulationMemory("per-entity seconds spent out of a minefield before its breadcrumbs are forgotten (ForgetSeconds)")]
        private readonly Dictionary<Entity, float> _clearFor = new();
        private int _worldEpoch;
        [Fdp.Core.FrameScratch("the timers to drop this run: cleared before it is filled")]
        private readonly List<Entity> _forget = new();
        [Fdp.Core.WorldDerived("field count of the last box build: cache validity marker")]
        private int _fieldCount = -1;

        /// <param name="gateOnAuthority">true on a networked brain node: only the units whose <c>BehaviorState</c> this node owns.</param>
        public MineBreadcrumbSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Minefield>() || !repo.IsComponentTypeRegistered<MineBreadcrumbs>()
                || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(repo, ref _worldEpoch)) _clearFor.Clear();   // ⭐ R-4297 ② (CE-1980) — a world replacement (a reload reuses the handles, a restore) forgets each man's clear-of-the-field timer
            if (!ReferenceEquals(repo, _queryRepo))
            {
                _fields = repo.Query().With<Minefield>().Build();
                _units = repo.Query().With<BehaviorState>().With<SimTransform>().Build();
                _queryRepo = repo; _fieldCount = -1;
            }
            Boxes(repo, deltaTime);
            if (_boxes.Count == 0 && _clearFor.Count == 0) return;
            foreach (var e in _units!)
            {
                if (_gateOnAuthority && !repo.HasAuthority<BehaviorState>(e)) continue;
                var p3 = repo.GetComponentRO<SimTransform>(e).Position;
                var p = new Vector2(p3.X, p3.Y);
                bool near = false;
                for (int i = 0; i < _boxes.Count && !near; i++)
                    near = p.X >= _boxes[i].Min.X - MarginMetres && p.X <= _boxes[i].Max.X + MarginMetres && p.Y >= _boxes[i].Min.Y - MarginMetres && p.Y <= _boxes[i].Max.Y + MarginMetres;
                bool has = repo.HasComponent<MineBreadcrumbs>(e);
                if (near)
                {
                    _clearFor.Remove(e);
                    if (!has) { var b = new MineBreadcrumbs(); b.Add(p); repo.AddComponent(e, b); continue; }
                    var crumbs = repo.GetComponentRO<MineBreadcrumbs>(e);
                    if (crumbs.TryNewest(out var last) && Vector2.DistanceSquared(last, p) < StepMetres * StepMetres) continue;
                    crumbs.Add(p);
                    repo.SetComponent(e, crumbs);
                }
                else if (has)
                {
                    _clearFor.TryGetValue(e, out float t);
                    _clearFor[e] = t + deltaTime;
                }
            }
            if (_clearFor.Count == 0) return;
            _forget.Clear();
            foreach (var (e, t) in _clearFor) if (t >= ForgetSeconds || !repo.IsAlive(e)) _forget.Add(e);
            for (int i = 0; i < _forget.Count; i++)
            {
                var e = _forget[i];
                _clearFor.Remove(e);
                if (repo.IsAlive(e) && repo.HasComponent<MineBreadcrumbs>(e)) repo.RemoveComponent<MineBreadcrumbs>(e);
            }
        }

        [Fdp.Core.WorldDerived("seconds since the last box rebuild: cache age")]
        private float _sinceBoxes = float.MaxValue;
        private EntityQuery? _fields, _units;
        private EntityRepository? _queryRepo;

        // ⚠ its OWN change test: ViewTableCache<Minefield> is one per-thread stamp shared with PlaceKnowledgeTable — taking it here would make that table serve a stale answer.
        //   The boxes are rebuilt when the number of fields changes, and once a second (an outline edit is rare and a second late is harmless here).
        private void Boxes(EntityRepository repo, float dt)
        {
            int count = 0;
            foreach (var _ in _fields!) count++;
            _sinceBoxes += dt;
            if (count == _fieldCount && _sinceBoxes < 1f) return;
            _fieldCount = count; _sinceBoxes = 0f;
            _boxes.Clear();
            foreach (var f in _fields!)
            {
                if (!MinefieldKnowledgeOps.TryOutline(repo, f, _outline, out _)) continue;
                var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
                for (int i = 0; i < _outline.Count; i++) { min = Vector2.Min(min, _outline[i]); max = Vector2.Max(max, _outline[i]); }
                _boxes.Add((min, max));
            }
        }
    }
}
