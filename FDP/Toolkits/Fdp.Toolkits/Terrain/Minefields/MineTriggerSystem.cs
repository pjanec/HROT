using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-T1 (docs/designs/minefields/DESIGN_Minefields.md §3b "As built — MS-T1 trigger"; MF-4, MR-14, MR-16, MR-17) — the mine TRIGGER: one serial system that,
    /// for every body this node owns the position of, tests the SEGMENT from where it was last frame to where it is now against the mines of every field, and for each mine
    /// it crosses publishes <see cref="MineStruck"/> (the record writers' event) and the burst — a <see cref="DetonationNotification"/> at the mine with the munition 6051 / 6052
    /// (the one path effects, sound, hearing, craters and damage already follow, MR-17).
    /// <list type="bullet">
    /// <item><b>Who:</b> every body with a position collider and a GROUND locomotion — a man, a wheeled / tracked / amphibious vehicle, a quadruped (livestock). ⛔ never a bird or a flock,
    /// never an embarked passenger (its vehicle is the body), never a body another node owns (<see cref="Fdp.Toolkit.Behavior.Embarkation.OwnsPosition"/>), never in a replay.</item>
    /// <item><b>Anti-personnel:</b> a foot (<see cref="FootRadius"/>) within reach of the mine — a person's own; a vehicle's footprint covering it (MF-4: any person or vehicle).
    /// <b>Anti-tank:</b> a VEHICLE whose footprint (<c>EntityFootprint.Dimensions</c>, turned by its heading) covers it AND whose mass ≥ <see cref="AntiTankMinMassKg"/>; a man walks over it.
    /// <b>Livestock</b> sets off an AP mine, an AT mine only rarely (<see cref="AnimalAntiTankChance"/>, a hash of the mine and the animal — the same every run, MR-16).</item>
    /// <item><b>The segment (MR-14):</b> candidates by LATTICE ARITHMETIC in the field frame — per row the segment crosses, the column range — never a scan of the mines; a fast
    /// mover cannot step over a mine at any time scale.</item>
    /// <item><b>Once per mine:</b> a mine in this frame's fired set, in the pending set (struck, its record not yet landed) or in any strikes record (<see cref="MinefieldStrikesSystem.IsGone"/>,
    /// memoised) never fires again; two bodies on one mine fire it once.</item>
    /// <item><b>0 B a steady frame (R-2084):</b> reused buffers, cached queries, no LINQ; a field's outline is read into a reused list and compared with its cache.</item>
    /// <item><b>⭐ The muscle (MS-T4, MR-11, MR-15, MR-17, MF-9) — after the strike pass:</b> a body carrying an ACTIVE <see cref="MineClearing"/> clears the mines of the field it is inside — a plough / flail every live mine within
    /// <c>WidthM / 2</c> of its path (and <see cref="ClearAheadMetres"/> beyond its hull's front), each surviving with a seeded chance (<see cref="PloughSurvival"/>, <see cref="FlailSurvival"/> — hashed per field, row and column: the
    /// same every run), the FLAIL bursting what it clears (a <see cref="DetonationNotification"/> at the mine with its own munition, never the flail as a target), the plough turning them aside in silence; a LINE CHARGE, once
    /// on its activation edge, bursts the line every <see cref="LineBurstSpacingMetres"/> m (munition 6053) and clears everything within <c>WidthM / 2</c> of it, no roll. A cleared mine is a <see cref="MinesCleared"/>
    /// event (recorded as <see cref="MineGone.Cleared"/>: no knowledge, no burst of its own); when a plough / flail leaves the field — or its tool goes idle — ONE <see cref="LaneCleared"/> (entry → exit, its width, its side).</item>
    /// </list>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class MineTriggerSystem : IEcsModuleSystem
    {
        /// <summary>How close a foot must come to an anti-personnel mine to set it off (m).</summary>
        public const float FootRadius = 0.4f;
        /// <summary>The lightest vehicle an anti-tank mine takes (kg).</summary>
        public const float AntiTankMinMassKg = 1500f;
        /// <summary>The chance a given animal sets off a given anti-tank mine it walks over (MR-16, a user's call — the lean).</summary>
        public const float AnimalAntiTankChance = 0.05f;
        /// <summary>A step longer than this is a teleport / a spawn, not a walk: tested as a point (m).</summary>
        public const float MaxStepMetres = 60f;
        /// <summary>How long a struck mine stays in the pending set waiting for its record (sim s).</summary>
        public const float PendingSeconds = 20f;
        /// <summary>⭐ MS-T4 (MR-15) — the chance a mine in the plough's / the flail's swath SURVIVES it (a starter value; hashed per mine, so a survivor survives every pass and every run).</summary>
        public const float PloughSurvival = 0.05f, FlailSurvival = 0.02f;
        /// <summary>⭐ MS-T4 — how far beyond its hull's FRONT a plough / flail clears (its chain / share reaches out; also what keeps a flail's own bursts off its hull).</summary>
        public const float ClearAheadMetres = 3f;
        /// <summary>⭐ MS-T4 (MF-9) — a line charge bursts every this many metres along its line, and reaches at most <see cref="LineMaxMetres"/>.</summary>
        public const float LineBurstSpacingMetres = 8f, LineMaxMetres = 100f;
        /// <summary>A cleared run shorter than this is no lane (a tool switched on and off in place).</summary>
        public const float MinLaneMetres = 2f;
        /// <summary>The swath width when the marker states none (m).</summary>
        public const float DefaultWidthMetres = 4.5f, DefaultLineWidthMetres = 8f;

        private sealed class ClearState
        {
            public bool WasActive;
            public bool HasPrev;
            public Vector2 Prev;
            public bool InField;
            public Entity Field;
            public Vector2 Entry, Exit;
            public float Width;
            public byte SidePlus1;
        }
        private readonly Dictionary<Entity, ClearState> _clearers = new();
        private EntityQuery? _clearQuery;
        private readonly short[] _batchRow = new short[MinesCleared.MaxMines];
        private readonly short[] _batchCol = new short[MinesCleared.MaxMines];
        private int _batchCount;
        private float _lcPen, _lcDmg;

        private sealed class FieldCache
        {
            public Minefield Field;
            public Vector2 Anchor;
            public readonly List<Vector2> Outline = new();
            public float MinX, MinY, MaxX, MaxY;
            public Guid Guid;
            public bool HasGuid;
            public int Stamp;
        }

        private readonly Dictionary<Entity, FieldCache> _fields = new();
        private readonly Dictionary<Entity, Vector2> _last = new();
        private readonly Dictionary<Entity, float> _mass = new();
        private readonly HashSet<long> _firedThisFrame = new();
        private readonly Dictionary<long, float> _pending = new();
        private readonly HashSet<long> _gone = new();
        private readonly List<Vector2> _scratch = new();
        private readonly List<Entity> _stale = new();
        private readonly List<long> _expired = new();
        private EntityQuery? _fieldQuery, _bodyQuery;
        private EntityRepository? _queryRepo;
        private int _stamp, _frame;
        private bool _ammoRead;
        private float _apPen, _apDmg, _atPen, _atDmg;

        /// <summary>Mines set off since the system was built (rails, diagnostics).</summary>
        public long Struck { get; private set; }

        /// <summary>⭐ MS-T4 — mines cleared by a plough, a flail or a line charge since the system was built (rails, diagnostics).</summary>
        public long Cleared { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<Minefield>() || !repo.IsComponentTypeRegistered<SimTransform>() || !repo.IsComponentTypeRegistered<PhysicsCollider>()) return;
            if (!repo.Bus.IsRegistered<MineStruck>() || !repo.Bus.IsRegistered<DetonationNotification>()) return;
            if (!ReferenceEquals(repo, _queryRepo))
            {
                _fieldQuery = repo.Query().With<Minefield>().With<SimTransform>().Build();
                _bodyQuery = repo.Query().With<SimTransform>().With<PhysicsCollider>().Build();
                _clearQuery = repo.IsComponentTypeRegistered<MineClearing>() ? repo.Query().With<MineClearing>().With<SimTransform>().Build() : null;
                _queryRepo = repo; _fields.Clear(); _last.Clear(); _mass.Clear(); _pending.Clear(); _gone.Clear(); _clearers.Clear(); _ammoRead = false;
            }
            _frame++;
            _stamp++;
            bool any = false;
            foreach (var f in _fieldQuery!)
            {
                if (Refresh(repo, f)) any = true;
            }
            if (!any) { _last.Clear(); return; }   // no field: nothing to remember (a body seen later starts from its first sight)
            _firedThisFrame.Clear();
            if (_pending.Count > 0) Expire(repo.SimulationTime);

            foreach (var e in _bodyQuery!)
            {
                if (!Eligible(repo, e, out bool vehicle, out bool animal)) continue;
                var t = repo.GetComponentRO<SimTransform>(e);
                var to = new Vector2(t.Position.X, t.Position.Y);
                Vector2 from = to;
                if (_last.TryGetValue(e, out var prev) && Vector2.DistanceSquared(prev, to) <= MaxStepMetres * MaxStepMetres) from = prev;
                _last[e] = to;
                if (!Fdp.Toolkit.Behavior.Embarkation.OwnsPosition(repo, e)) continue;
                foreach (var kv in _fields)
                {
                    var c = kv.Value;
                    if (c.Stamp != _stamp) continue;   // a field that no longer exists
                    TestBody(repo, e, vehicle, animal, t, from, to, kv.Key, c);
                }
            }
            if (_clearQuery != null) ClearingPass(repo);   // ⭐ MS-T4 — after the strike pass: the movers moved, the strikes are decided
            if ((_frame & 127) == 0) Sweep(repo);
        }

        // ── fields ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Refreshes <paramref name="field"/>'s cache (rebuilt only when its parameters, outline or position changed); false when it has no usable outline.</summary>
        private bool Refresh(EntityRepository repo, Entity field)
        {
            if (!MinefieldKnowledgeOps.TryOutline(repo, field, _scratch, out var anchor)) return false;
            ref readonly var f = ref repo.GetComponentRO<Minefield>(field);
            if (!(f.SpacingM > 0.1f)) return false;
            if (!_fields.TryGetValue(field, out var c)) { c = new FieldCache(); _fields[field] = c; c.Stamp = -1; }
            if (c.Stamp < 0 || !Same(c, in f, anchor, _scratch))
            {
                c.Field = f; c.Anchor = anchor; c.Outline.Clear(); c.Outline.AddRange(_scratch);
                c.MinX = c.MinY = float.MaxValue; c.MaxX = c.MaxY = float.MinValue;
                for (int i = 0; i < _scratch.Count; i++)
                {
                    var p = _scratch[i];
                    c.MinX = MathF.Min(c.MinX, p.X); c.MaxX = MathF.Max(c.MaxX, p.X); c.MinY = MathF.Min(c.MinY, p.Y); c.MaxY = MathF.Max(c.MaxY, p.Y);
                }
                c.HasGuid = MinefieldIds.TryGuid(repo, field, out c.Guid);
            }
            c.Stamp = _stamp;
            return true;
        }

        private static unsafe bool Same(FieldCache c, in Minefield f, Vector2 anchor, List<Vector2> outline)
        {
            if (c.Anchor != anchor || c.Outline.Count != outline.Count) return false;
            if (c.Field.Type != f.Type || c.Field.SpacingM != f.SpacingM || c.Field.OrientationDeg != f.OrientationDeg || c.Field.Seed != f.Seed || c.Field.LaneCount != f.LaneCount) return false;
            for (int i = 0; i < f.LaneCount && i < Minefield.MaxLanes; i++)
                if (c.Field.LaneAx[i] != f.LaneAx[i] || c.Field.LaneAy[i] != f.LaneAy[i] || c.Field.LaneBx[i] != f.LaneBx[i] || c.Field.LaneBy[i] != f.LaneBy[i] || c.Field.LaneWidth[i] != f.LaneWidth[i]) return false;
            for (int i = 0; i < outline.Count; i++) if (c.Outline[i] != outline[i]) return false;
            return true;
        }

        // ── bodies ───────────────────────────────────────────────────────────────────────────────────────────────

        private static bool Eligible(EntityRepository repo, Entity e, out bool vehicle, out bool animal)
        {
            vehicle = animal = false;
            var cls = Locomotions.Of(repo, e);
            switch (cls)
            {
                case LocomotionClass.Pedestrian: break;
                case LocomotionClass.Wheeled: case LocomotionClass.Tracked: case LocomotionClass.Amphibious: vehicle = true; break;
                case LocomotionClass.Quadruped: animal = true; break;
                default: return false;   // a bird, an aircraft, a rope, a vessel, an unknown: never
            }
            if (repo.IsComponentTypeRegistered<AnimalState>() && repo.HasComponent<AnimalState>(e))
            {
                var species = repo.GetComponentRO<AnimalState>(e).Species;
                if (species is AnimalSpecies.BirdFlock or AnimalSpecies.Bird or AnimalSpecies.Duck or AnimalSpecies.DuckFlock) return false;   // ⛔ birds never (MR-16), whatever their locomotion says
                animal = true; vehicle = false;
            }
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>() && repo.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) return false;   // its vehicle is the body
            if (repo.IsComponentTypeRegistered<StaticObstacle>() && repo.HasComponent<StaticObstacle>(e)) return false;
            return true;
        }

        private void TestBody(EntityRepository repo, Entity body, bool vehicle, bool animal, in SimTransform t, Vector2 from, Vector2 to, Entity field, FieldCache c)
        {
            // field-frame bounds first: a body nowhere near this field costs four compares
            float reach = vehicle ? 12f : 2f;
            if (MathF.Max(from.X, to.X) + reach < c.MinX || MathF.Min(from.X, to.X) - reach > c.MaxX
                || MathF.Max(from.Y, to.Y) + reach < c.MinY || MathF.Min(from.Y, to.Y) - reach > c.MaxY) return;

            float halfL = 0f, halfW = 0f;
            Vector2 fwd = Vector2.UnitX;
            if (vehicle)
            {
                Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.Dimensions(repo, body, out float len, out float wid);
                halfL = len * 0.5f; halfW = wid * 0.5f;
                var f3 = Vector3.Transform(Vector3.UnitX, t.Rotation);
                fwd = new Vector2(f3.X, f3.Y);
                if (fwd.LengthSquared() < 1e-6f) fwd = Vector2.UnitX; else fwd = Vector2.Normalize(fwd);
            }
            float rad = vehicle ? MathF.Sqrt(halfL * halfL + halfW * halfW) : FootRadius;

            float s = c.Field.SpacingM, a = c.Field.OrientationDeg;
            var uv0 = MinePattern.ToField(from, c.Anchor, a);
            var uv1 = MinePattern.ToField(to, c.Anchor, a);
            float jitter = c.Field.Seed != 0 ? 0.15f * s * 1.5f : 0f;
            float band = rad + jitter + 0.01f;
            float vMin = MathF.Min(uv0.Y, uv1.Y) - band, vMax = MathF.Max(uv0.Y, uv1.Y) + band;
            int r0 = (int)MathF.Floor(vMin / s), r1 = (int)MathF.Ceiling(vMax / s);
            float du = uv1.X - uv0.X, dv = uv1.Y - uv0.Y;

            for (int r = r0; r <= r1; r++)
            {
                if (r < short.MinValue || r > short.MaxValue) continue;
                float rv = r * s;
                // the part of the segment within the row's band, then its u extent
                float tA = 0f, tB = 1f;
                if (MathF.Abs(dv) < 1e-6f) { if (MathF.Abs(uv0.Y - rv) > band) continue; }
                else
                {
                    float t0 = (rv - band - uv0.Y) / dv, t1 = (rv + band - uv0.Y) / dv;
                    if (t0 > t1) (t0, t1) = (t1, t0);
                    tA = MathF.Max(0f, t0); tB = MathF.Min(1f, t1);
                    if (tA > tB) continue;
                }
                float uA = uv0.X + du * tA, uB = uv0.X + du * tB;
                float uLo = MathF.Min(uA, uB) - band, uHi = MathF.Max(uA, uB) + band;
                float off = (r & 1) != 0 ? 0.5f : 0f;
                int cLo = (int)MathF.Ceiling(uLo / s - off), cHi = (int)MathF.Floor(uHi / s - off);
                for (int col = cLo; col <= cHi; col++)
                {
                    if (col < short.MinValue || col > short.MaxValue) continue;
                    var mp = MinePattern.At(c.Field, c.Anchor, r, col);
                    var type = MinePattern.TypeAt(c.Field.Type, r);
                    if (type == MineType.None) continue;
                    if (!Reaches(vehicle, from, to, mp, halfL, halfW, fwd)) continue;
                    if (!MinePattern.Laid(c.Field, c.Outline, mp)) continue;   // ⭐ CE-4655 — the one test (edge inset)
                    if (type == MineType.AntiTank)
                    {
                        if (animal) { if (!AnimalSetsOffAntiTank(c, r, col, body)) continue; }
                        else if (!vehicle || MassOf(repo, body) < AntiTankMinMassKg) continue;   // a man walks over it
                    }
                    Fire(repo, body, field, c, (short)r, (short)col, mp, type, t.Position.Z);
                }
            }
        }

        /// <summary>True when mine point <paramref name="m"/> lies under the body at some point of its step.</summary>
        private static bool Reaches(bool vehicle, Vector2 from, Vector2 to, Vector2 m, float halfL, float halfW, Vector2 fwd)
        {
            if (!vehicle) return DistanceSquaredToSegment(m, from, to) <= FootRadius * FootRadius;
            // the footprint at the end pose swept back along the step: in the body frame, mine − body(s) = mb + s·d for s in [0,1] (s = how far BACK along the step)
            var lat = new Vector2(-fwd.Y, fwd.X);
            var rel = m - to; var d = to - from;
            float mx = Vector2.Dot(rel, fwd), my = Vector2.Dot(rel, lat), dx = Vector2.Dot(d, fwd), dy = Vector2.Dot(d, lat);
            float lo = 0f, hi = 1f;
            return Clip(mx, dx, halfL, ref lo, ref hi) && Clip(my, dy, halfW, ref lo, ref hi);
        }

        /// <summary>Intersects [lo, hi] with the s where |p + s·d| ≤ half.</summary>
        private static bool Clip(float p, float d, float half, ref float lo, ref float hi)
        {
            if (MathF.Abs(d) < 1e-6f) return MathF.Abs(p) <= half;
            float s0 = (-half - p) / d, s1 = (half - p) / d;
            if (s0 > s1) (s0, s1) = (s1, s0);
            lo = MathF.Max(lo, s0); hi = MathF.Min(hi, s1);
            return lo <= hi;
        }

        private static float DistanceSquaredToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.LengthSquared();
            float t = l2 < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
            return Vector2.DistanceSquared(p, a + ab * t);
        }

        private static bool AnimalSetsOffAntiTank(FieldCache c, int row, int col, Entity body)
        {
            uint h = (uint)row * 0x85EBCA77u ^ (uint)col * 0xC2B2AE3Du ^ (uint)body.Index * 0x9E3779B1u ^ (c.Field.Seed + 0x27D4EB2Fu);
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFu) / 65535f < AnimalAntiTankChance;
        }

        /// <summary>The vehicle's mass (kg), read once the way <c>ContactBreach</c> reads it (its TKB template's <c>VehicleParametersDto</c>).</summary>
        private float MassOf(EntityRepository repo, Entity body)
        {
            if (_mass.TryGetValue(body, out float m)) return m;
            m = 0f;
            if (Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(repo, body, out var template) && template.GetDescriptor<VehicleParametersDto>() is { } dto) m = dto.Mass;
            _mass[body] = m;
            return m;
        }

        // ── the burst ────────────────────────────────────────────────────────────────────────────────────────────

        private static long KeyOf(Entity field, int row, int col) => ((long)field.Index << 32) | ((long)(ushort)row << 16) | (ushort)col;

        private void Fire(EntityRepository repo, Entity body, Entity field, FieldCache c, short row, short col, Vector2 at, MineType type, float z)
        {
            long key = KeyOf(field, row, col);
            if (_firedThisFrame.Contains(key) || _pending.ContainsKey(key) || _gone.Contains(key)) return;
            if (c.HasGuid && MinefieldStrikesSystem.IsGone(repo, c.Guid, row, col)) { _gone.Add(key); return; }
            _firedThisFrame.Add(key);
            _pending[key] = repo.SimulationTime + PendingSeconds;
            Struck++;
            ReadAmmo(repo);

            repo.Bus.Publish(new MineStruck { Field = field, Row = row, Col = col, By = body, X = at.X, Y = at.Y, At = (SimInstant)(double)repo.SimulationTime });
            bool anti = type == MineType.AntiTank;
            repo.Bus.Publish(new DetonationNotification
            {
                Shooter = field,
                // ⭐ MR-10 — an anti-tank mine bursts UNDER the hull: the vehicle is the target and the round travels UP, so the armour model reads its BOTTOM
                Target = anti ? body : Entity.Null,
                HitX = at.X, HitY = at.Y, HitZ = z,
                Penetration = anti ? _atPen : _apPen, Damage = anti ? _atDmg : _apDmg,
                Ammo = anti ? Minefields.MunitionAntiTank : Minefields.MunitionAntiPersonnel,
                TravelX = 0f, TravelY = 0f, TravelZ = anti ? 1f : 0f,
                TriggeredBy = anti ? Entity.Null : body,   // ⭐ CE-4654 D-3 — the man who stepped on it: the area effect maims exactly him
            });
        }

        private void ReadAmmo(EntityRepository repo)
        {
            if (_ammoRead || !repo.HasSingletonManaged<ITkbDatabase>()) return;
            _ammoRead = true;
            var db = repo.GetSingletonManaged<ITkbDatabase>();
            if (db == null) return;
            (_apPen, _apDmg) = BallisticsOf(db, Minefields.MunitionAntiPersonnel);
            (_atPen, _atDmg) = BallisticsOf(db, Minefields.MunitionAntiTank);
            (_lcPen, _lcDmg) = BallisticsOf(db, Minefields.MunitionLineCharge);
        }

        private static (float Pen, float Dmg) BallisticsOf(ITkbDatabase db, long ammo)
        {
            if (!db.TryGetByType(ammo, out var t) || t == null) return (0f, 0f);
            foreach (var (type, _, data) in t.GetAllDescriptors())
                if (type == typeof(AmmoWeaponBallisticsDto) && data is AmmoWeaponBallisticsDto b && b.WeaponGuid == 0) return (b.PenetrationMm, b.Damage);
            return (0f, 0f);
        }

        // ── ⭐ the muscle (MS-T4) ────────────────────────────────────────────────────────────────────────────────────

        private void ClearingPass(EntityRepository repo)
        {
            if (!repo.Bus.IsRegistered<MinesCleared>() || !repo.Bus.IsRegistered<LaneCleared>()) return;
            foreach (var e in _clearQuery!)
            {
                if (!Fdp.Toolkit.Behavior.Embarkation.OwnsPosition(repo, e)) continue;
                ref readonly var c = ref repo.GetComponentRO<MineClearing>(e);
                var t = repo.GetComponentRO<SimTransform>(e);
                var to = new Vector2(t.Position.X, t.Position.Y);
                if (!_clearers.TryGetValue(e, out var st)) { st = new ClearState(); _clearers[e] = st; }
                bool active = c.Active != 0 && c.Tool != MineClearingTool.NoTool;
                if (c.Tool == MineClearingTool.LineCharge)
                {
                    if (active && !st.WasActive) FireLine(repo, e, in c, in t, to);
                }
                else Swath(repo, e, in c, st, active, t.Position.Z, to);
                st.WasActive = active;
                st.Prev = to; st.HasPrev = true;
            }
        }

        /// <summary>A plough / flail: while active it clears the swath of the field it is working in — the one the tool already REACHES (<see cref="ClearAheadMetres"/> ahead of the hull) or the hull is inside, so the
        /// mines under the hull were cleared before it arrived; the lane is the run the HULL is inside the field: entering starts it, leaving (or going idle) ends it with ONE <see cref="LaneCleared"/>.</summary>
        private void Swath(EntityRepository repo, Entity clearer, in MineClearing mc, ClearState st, bool active, float z, Vector2 to)
        {
            if (!active) { EndLane(repo, st); return; }
            var from = st.HasPrev && Vector2.DistanceSquared(st.Prev, to) <= MaxStepMetres * MaxStepMetres ? st.Prev : to;
            var dir = to - from;
            // ⭐ the tool reaches ClearAheadMetres beyond the hull's FRONT (not its centre: a 7 m hull covers mines 3.5 m ahead of its centre, and the strike pass above has already run this frame)
            Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.Dimensions(repo, clearer, out float hullLength, out _);
            var ahead = dir.LengthSquared() > 1e-8f ? to + Vector2.Normalize(dir) * (hullLength * 0.5f + ClearAheadMetres) : to;
            Entity hullIn = Entity.Null, work = Entity.Null; FieldCache? hullCache = null, workCache = null;
            foreach (var kv in _fields)
            {
                var f = kv.Value;
                if (f.Stamp != _stamp) continue;
                if (hullCache == null && Holds(f, to)) { hullIn = kv.Key; hullCache = f; }
                if (workCache == null && (Holds(f, to) || Holds(f, ahead))) { work = kv.Key; workCache = f; }
            }
            float width = mc.WidthM > 0.1f ? mc.WidthM : DefaultWidthMetres;
            if (hullCache == null) EndLane(repo, st);
            else
            {
                if (!st.InField || st.Field != hullIn)
                {
                    EndLane(repo, st);
                    st.InField = true; st.Field = hullIn; st.Entry = to; st.Width = width;
                    st.SidePlus1 = repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(clearer) ? (byte)((int)repo.GetComponentRO<EntityInfo>(clearer).ForceId + 1) : (byte)0;
                }
                st.Exit = to;
            }
            if (workCache == null) return;
            bool flail = mc.Tool == MineClearingTool.Flail;
            ClearMines(repo, clearer, work, workCache, from, ahead, width * 0.5f, flail ? FlailSurvival : PloughSurvival, flail, z);
        }

        private static bool Holds(FieldCache f, Vector2 p)
            => p.X >= f.MinX && p.X <= f.MaxX && p.Y >= f.MinY && p.Y <= f.MaxY && MinePattern.Inside(f.Outline, p);

        private void EndLane(EntityRepository repo, ClearState st)
        {
            if (!st.InField) return;
            st.InField = false;
            if (Vector2.DistanceSquared(st.Entry, st.Exit) < MinLaneMetres * MinLaneMetres || !repo.IsAlive(st.Field)) return;
            repo.Bus.Publish(new LaneCleared { Field = st.Field, Lane = 255, SidePlus1 = st.SidePlus1, Ax = st.Entry.X, Ay = st.Entry.Y, Bx = st.Exit.X, By = st.Exit.Y, Width = st.Width });
        }

        /// <summary>A line charge on its activation edge: a burst every <see cref="LineBurstSpacingMetres"/> m from the vehicle toward <see cref="MineClearing.LineTo"/> (≤ <see cref="LineMaxMetres"/>), every mine within
        /// <c>WidthM / 2</c> of the line cleared (no survival roll), and one <see cref="LaneCleared"/> per field the line touches.</summary>
        private void FireLine(EntityRepository repo, Entity launcher, in MineClearing mc, in SimTransform t, Vector2 from)
        {
            var d = new Vector2(mc.LineTo.X, mc.LineTo.Y) - from;
            float len = MathF.Min(d.Length(), LineMaxMetres);
            if (len < 1f) return;
            var dir = d / d.Length();
            var end = from + dir * len;
            float width = mc.WidthM > 0.1f ? mc.WidthM : DefaultLineWidthMetres;
            ReadAmmo(repo);
            int bursts = Math.Max(1, (int)MathF.Floor(len / LineBurstSpacingMetres));
            for (int k = 1; k <= bursts; k++)
            {
                var p = from + dir * MathF.Min(k * LineBurstSpacingMetres, len);
                repo.Bus.Publish(new DetonationNotification
                {
                    Shooter = launcher, Target = Entity.Null, HitX = p.X, HitY = p.Y, HitZ = t.Position.Z,
                    Penetration = _lcPen, Damage = _lcDmg, Ammo = Minefields.MunitionLineCharge,
                });
            }
            byte side = repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(launcher) ? (byte)((int)repo.GetComponentRO<EntityInfo>(launcher).ForceId + 1) : (byte)0;
            foreach (var kv in _fields)
            {
                var f = kv.Value;
                if (f.Stamp != _stamp || MathF.Max(from.X, end.X) + width < f.MinX || MathF.Min(from.X, end.X) - width > f.MaxX
                    || MathF.Max(from.Y, end.Y) + width < f.MinY || MathF.Min(from.Y, end.Y) - width > f.MaxY) continue;
                long clearedBefore = Cleared;
                ClearMines(repo, launcher, kv.Key, f, from, end, width * 0.5f, 0f, false, t.Position.Z);
                if (Cleared > clearedBefore || SegmentTouches(f, from, end))
                    repo.Bus.Publish(new LaneCleared { Field = kv.Key, Lane = 255, SidePlus1 = side, Ax = from.X, Ay = from.Y, Bx = end.X, By = end.Y, Width = width });
            }
        }

        private static bool SegmentTouches(FieldCache f, Vector2 a, Vector2 b)
        {
            for (int i = 0; i <= 8; i++) if (MinePattern.Inside(f.Outline, Vector2.Lerp(a, b, i / 8f))) return true;
            return false;
        }

        /// <summary>
        /// Clears every live mine of <paramref name="f"/> within <paramref name="half"/> m of the segment <paramref name="a"/>–<paramref name="b"/> (candidates by lattice arithmetic, as <c>TestBody</c>): each one
        /// that survives the seeded <paramref name="survival"/> roll stays live; the rest go into <see cref="MinesCleared"/> events (≤ 16 each) and — when <paramref name="burst"/> — burst where they lay.
        /// </summary>
        private unsafe void ClearMines(EntityRepository repo, Entity clearer, Entity field, FieldCache f, Vector2 a, Vector2 b, float half, float survival, bool burst, float z)
        {
            float s = f.Field.SpacingM, ang = f.Field.OrientationDeg;
            var uv0 = MinePattern.ToField(a, f.Anchor, ang);
            var uv1 = MinePattern.ToField(b, f.Anchor, ang);
            float jitter = f.Field.Seed != 0 ? 0.15f * s * 1.5f : 0f;
            float band = half + jitter + 0.01f;
            float vMin = MathF.Min(uv0.Y, uv1.Y) - band, vMax = MathF.Max(uv0.Y, uv1.Y) + band;
            int r0 = (int)MathF.Floor(vMin / s), r1 = (int)MathF.Ceiling(vMax / s);
            float du = uv1.X - uv0.X, dv = uv1.Y - uv0.Y;
            float h2 = half * half;
            for (int r = r0; r <= r1; r++)
            {
                if (r < short.MinValue || r > short.MaxValue) continue;
                float rv = r * s, tA = 0f, tB = 1f;
                if (MathF.Abs(dv) < 1e-6f) { if (MathF.Abs(uv0.Y - rv) > band) continue; }
                else
                {
                    float t0 = (rv - band - uv0.Y) / dv, t1 = (rv + band - uv0.Y) / dv;
                    if (t0 > t1) (t0, t1) = (t1, t0);
                    tA = MathF.Max(0f, t0); tB = MathF.Min(1f, t1);
                    if (tA > tB) continue;
                }
                float uA = uv0.X + du * tA, uB = uv0.X + du * tB;
                float uLo = MathF.Min(uA, uB) - band, uHi = MathF.Max(uA, uB) + band;
                float off = (r & 1) != 0 ? 0.5f : 0f;
                int cLo = (int)MathF.Ceiling(uLo / s - off), cHi = (int)MathF.Floor(uHi / s - off);
                for (int col = cLo; col <= cHi; col++)
                {
                    if (col < short.MinValue || col > short.MaxValue) continue;
                    var type = MinePattern.TypeAt(f.Field.Type, r);
                    if (type == MineType.None) continue;
                    var mp = MinePattern.At(f.Field, f.Anchor, r, col);
                    if (DistanceSquaredToSegment(mp, a, b) > h2) continue;
                    if (!MinePattern.Laid(f.Field, f.Outline, mp)) continue;   // ⭐ CE-4655 — the one test (edge inset)
                    if (survival > 0f && Survives(f, r, col, survival)) continue;
                    long key = KeyOf(field, r, col);
                    if (_firedThisFrame.Contains(key) || _pending.ContainsKey(key) || _gone.Contains(key)) continue;
                    if (f.HasGuid && MinefieldStrikesSystem.IsGone(repo, f.Guid, (short)r, (short)col)) { _gone.Add(key); continue; }
                    _pending[key] = repo.SimulationTime + PendingSeconds;
                    Cleared++;
                    _batchRow[_batchCount] = (short)r; _batchCol[_batchCount] = (short)col;
                    if (++_batchCount == MinesCleared.MaxMines) Flush(repo, field);
                    if (burst)
                    {
                        ReadAmmo(repo);
                        bool anti = type == MineType.AntiTank;
                        // ⭐ MR-17 — the flail's burst: the mine's own munition at the mine, ⛔ never the flail as a target (its chain is out ahead of its hull)
                        repo.Bus.Publish(new DetonationNotification
                        {
                            Shooter = field, Target = Entity.Null, HitX = mp.X, HitY = mp.Y, HitZ = z,
                            Penetration = anti ? _atPen : _apPen, Damage = anti ? _atDmg : _apDmg,
                            Ammo = anti ? Minefields.MunitionAntiTank : Minefields.MunitionAntiPersonnel,
                        });
                    }
                }
            }
            Flush(repo, field);
        }

        private unsafe void Flush(EntityRepository repo, Entity field)
        {
            if (_batchCount == 0) return;
            var ev = new MinesCleared { Field = field, Count = (byte)_batchCount };
            for (int i = 0; i < _batchCount; i++) { ev.Row[i] = _batchRow[i]; ev.Col[i] = _batchCol[i]; }
            repo.Bus.Publish(ev);
            _batchCount = 0;
        }

        /// <summary>The seeded survival roll: hashed per (field seed, row, column) — the same mine survives every pass and every run (MR-15).</summary>
        private static bool Survives(FieldCache c, int row, int col, float chance)
        {
            uint h = (uint)row * 0x85EBCA77u ^ (uint)col * 0xC2B2AE3Du ^ (c.Field.Seed * 0x9E3779B1u + 0x165667B1u) ^ (c.HasGuid ? (uint)c.Guid.GetHashCode() * 0x27D4EB2Fu : 0u);
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFu) / 65535f < chance;
        }

        // ── housekeeping ─────────────────────────────────────────────────────────────────────────────────────────

        private void Expire(float now)
        {
            _expired.Clear();
            foreach (var kv in _pending) if (kv.Value <= now) _expired.Add(kv.Key);
            for (int i = 0; i < _expired.Count; i++) _pending.Remove(_expired[i]);
        }

        private void Sweep(EntityRepository repo)
        {
            _stale.Clear();
            foreach (var kv in _last) if (!repo.IsAlive(kv.Key)) _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++) { _last.Remove(_stale[i]); _mass.Remove(_stale[i]); }
            _stale.Clear();
            foreach (var kv in _clearers) if (!repo.IsAlive(kv.Key)) _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++) _clearers.Remove(_stale[i]);
            _stale.Clear();
            foreach (var kv in _fields) if (!repo.IsAlive(kv.Key)) _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++) _fields.Remove(_stale[i]);
        }
    }
}
