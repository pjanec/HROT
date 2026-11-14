using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a vehicle's contact with a barrier did (<see cref="ContactBreachEvent.Kind"/>).</summary>
    public enum ContactBreachKind : byte
    {
        /// <summary>A small barrier broken; the vehicle drives on through the gap.</summary>
        Broke = 0,
        /// <summary>A building wall bumped open for infantry (a Passage); the vehicle stays outside.</summary>
        Bumped = 1,
        /// <summary>The barrier is too strong for this vehicle (the driver's cue).</summary>
        TooStrong = 2,
        /// <summary>A push under way (full throttle in contact): <see cref="ContactBreachEvent.Progress"/> 0..1 — the cue's bar.</summary>
        Pushing = 3,
    }

    /// <summary>
    /// ⭐ <c>CE-4600</c> (B-3; 📄 docs/DESIGN_Vehicle_Breaching.md §3.1, §7.2 V-17) — a vehicle broke a barrier, bumped a building wall, pushes one, or met one
    /// too strong for it: what the feedback reads (dust and crash sound at <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/>, the driver's HUD cue). LOCAL to the
    /// deciding node; the breach itself travels as its record.
    /// </summary>
    [EventId(TerrainEventIds.ContactBreach)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ContactBreachEvent
    {
        public Entity Vehicle;
        public byte Kind;
        public float X, Y, Z;
        public float Progress;
    }

    /// <summary>⭐ <c>CE-4600</c> — <c>contact-breach.json</c> (the file's own comment is the reference): who breaks what by contact.</summary>
    public sealed class ContactBreachTable
    {
        public sealed record Row(string Name, bool Tracked, float MinMassKg, float SmallMaxStrength, bool Building, float MinRamSpeedMs, float PushSeconds, float SpeedKept, float CostHealth)
        {
            /// <summary>⭐ CE-4603 (B-6) — this row fells a lone tree trunk by ram or push.</summary>
            public bool Trees { get; init; }
        }

        public IReadOnlyList<Row> Rows { get; init; } = Array.Empty<Row>();
        public float CutMarginM { get; init; } = 0.5f;
        public float MinDriveThroughM { get; init; } = 4.2f;
        public float BumpWidthM { get; init; } = 1.2f;
        public float BumpHeightM { get; init; } = 2.2f;

        private static readonly Lazy<ContactBreachTable> s_shared = new(() =>
        {
            using var s = typeof(ContactBreachTable).Assembly.GetManifestResourceStream("Fdp.Toolkit.Terrain.contact-breach.json")
                ?? throw new InvalidOperationException("contact-breach.json is not embedded");
            using var r = new System.IO.StreamReader(s);
            return Parse(r.ReadToEnd());
        });

        public static ContactBreachTable Shared => s_shared.Value;

        public static ContactBreachTable Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var rows = new List<Row>();
            foreach (var r in root.GetProperty("rows").EnumerateArray())
                rows.Add(new Row(r.GetProperty("name").GetString() ?? "", r.GetProperty("tracked").GetBoolean(), r.GetProperty("minMassKg").GetSingle(),
                    r.GetProperty("smallMaxStrength").GetSingle(), r.GetProperty("building").GetBoolean(), r.GetProperty("minRamSpeedMs").GetSingle(),
                    r.GetProperty("pushSeconds").GetSingle(), r.GetProperty("speedKept").GetSingle(), r.GetProperty("costHealth").GetSingle())
                    { Trees = r.TryGetProperty("trees", out var t) && t.GetBoolean() });
            return new ContactBreachTable
            {
                Rows = rows, CutMarginM = root.GetProperty("cutMarginM").GetSingle(), MinDriveThroughM = root.GetProperty("minDriveThroughM").GetSingle(),
                BumpWidthM = root.GetProperty("bumpWidthM").GetSingle(), BumpHeightM = root.GetProperty("bumpHeightM").GetSingle(),
            };
        }

        /// <summary>The first row a vehicle qualifies for, or null (it breaks nothing).</summary>
        public Row? For(bool tracked, float massKg)
        {
            foreach (var r in Rows) if ((!r.Tracked || tracked) && massKg >= r.MinMassKg) return r;
            return null;
        }
    }
}

namespace CarKinem.Controllers
{
    /// <summary>
    /// ⭐⭐ <c>CE-4600</c> (B-3; 📄 docs/DESIGN_Vehicle_Breaching.md §3.2, §4 B-D3/B-D4, §7.3 D-3, §7.2 V-3, V-9, V-12, V-15, V-21; R-4238, R-4243) — THE CONTACT
    /// RULE, called by the hull guard at its refusal for a hull <see cref="HullBreakGate.MayBreak"/> lets break (a player drives it). It names the barrier (a wall
    /// panel, or the solid piece the step rose into — an obstacle's box), decides by <see cref="ContactBreachTable"/> — a RAM (the speed INTO it) or a PUSH (full
    /// throttle held in contact) — and:
    /// <list type="bullet">
    /// <item>a SMALL barrier broken: the step goes on with a speed loss (never a zero — V-3), the cut is the hull at its crossing angle (V-9), on every panel it
    /// overlaps (V-12); for a GRACE the hull passes the barrier it broke (the record lands a few ticks later — V-3), and a stub of it breaks at any speed (V-21);</item>
    /// <item>a BUILDING wall (a tank, a ram only): a door-sized Passage, the hull held (it never enters — B-D4, V-5);</item>
    /// <item>too strong / no row: the hull held, a <see cref="ContactBreachKind.TooStrong"/> cue.</item>
    /// </list>
    /// ⭐ The write is a FOLLOW-UP (V-15): inside the per-entity phase it is queued on the batch (<see cref="Fdp.Core.Jobs.BatchContext.Later"/>) and played
    /// serially — a <see cref="BuildingDamageEvent"/> for the ONE breach writer (<see cref="BuildingDamageSystem"/>, which finds or creates the record), the obstacle
    /// damage through <see cref="Fdp.Toolkit.Combat.ObstacleDamage.Damage"/>, the <see cref="ContactBreachEvent"/>. The per-hull state (push seconds, grace, the last
    /// cue) is keyed by the hull's own entity: each job writes only its own key.
    /// </summary>
    public static class ContactBreach
    {
        /// <summary>A hull passes a barrier it broke for this long while the record is on its way (s).</summary>
        public const float GraceSeconds = 3f;
        /// <summary>A PUSH is the throttle at least this far forward (or back).</summary>
        public const float PushThrottle = 0.9f;

        private readonly record struct Contact(int Key, float Seconds);
        private static readonly ConcurrentDictionary<int, Contact> s_push = new(), s_grace = new(), s_bumped = new();
        private static readonly ConcurrentDictionary<int, (int Key, byte Kind)> s_cue = new();
        private static readonly ConcurrentDictionary<long, Pending> s_pending = new();
        private static long s_ticket;

        private sealed class Pending
        {
            public Entity Vehicle;
            public readonly List<BuildingDamageEvent> Breaches = new();
            public int ObstacleIndex = -1;
            public (int X, int Y)? Tree;   // ⭐ CE-4603 — a felled trunk's key
            public ContactBreachEvent Cue;
            public bool HasCue;
        }

        /// <summary>Forgets every hull's contact state (a test's fixture; a new world).</summary>
        public static void Reset() { s_push.Clear(); s_grace.Clear(); s_bumped.Clear(); s_cue.Clear(); }

        /// <summary>
        /// The guard held <paramref name="vehicle"/>'s step at <paramref name="hit"/>: may it go on? True ⇒ the step is taken, its speed × <paramref name="speedKept"/>.
        /// <paramref name="velocity"/> is the step's planar velocity (before the refusal zeroes it), <paramref name="throttle"/> the driver's forward axis.
        /// </summary>
        public static bool OnContact(EntityRepository repo, Entity vehicle, TerrainWorld world, in VehicleParams hull, in BarrierHit hit,
                                     Vector2 here, Vector2 fwd, float hereZ, Vector2 velocity, float throttle, float dt, out float speedKept)
        {
            speedKept = 1f;
            var table = ContactBreachTable.Shared;
            if (!Identify(world, in hit, hereZ, out int key, out int panel, out int obstacle, out float strength, out var tree)) return false;
            int me = vehicle.Index;

            // ⭐ V-3 / V-21 — a barrier this hull broke a moment ago: it passes (the record is on its way), and a stub of it is part of the same break
            if (s_grace.TryGetValue(me, out var g) && g.Key == key && g.Seconds > 0f)
            {
                s_grace[me] = g with { Seconds = g.Seconds - dt };
                return true;
            }

            float massKg = 0f; bool tracked = false;
            if (Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(repo, vehicle, out var template)
                && template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.VehicleParametersDto>() is { } dto) massKg = dto.Mass;
            tracked = Fdp.Toolkit.Motion.Locomotions.Of(repo, vehicle) == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Tracked;
            var row = table.For(tracked, massKg);
            bool building = panel >= 0 && world.Panels[panel].Building >= 0;
            float into = MathF.Max(0f, -Vector2.Dot(velocity, hit.Normal));   // ⭐ B-D2 — only the speed INTO it

            if (row == null || (building && !row.Building) || (tree != null ? !row.Trees : !building && strength > row.SmallMaxStrength))
            {
                Cue(repo, vehicle, key, ContactBreachKind.TooStrong, hit.At, hereZ, 0f);
                return false;
            }

            if (building)
            {
                // one bump per wall while the hull stays at it — and ⛔ never a pass: a hull never enters a building (V-5)
                if (s_bumped.TryGetValue(me, out var bumped) && bumped.Key == key && bumped.Seconds > 0f) { s_bumped[me] = bumped with { Seconds = bumped.Seconds - dt }; return false; }
                if (into < row.MinRamSpeedMs) return false;   // ⭐ D-3 — a building's bump is a RAM only
                var p = world.Panels[panel];
                float along = Along(p, hit.At), w = MathF.Min(table.BumpWidthM, hull.Width);
                var pending = NewPending(vehicle);
                pending.Breaches.Add(Event(world, panel, along - w * 0.5f, along + w * 0.5f, p.BaseZ, MathF.Min(p.TopZ, p.BaseZ + table.BumpHeightM), vehicle));
                pending.Cue = Make(vehicle, ContactBreachKind.Bumped, hit.At, hereZ, 1f); pending.HasCue = true;
                s_bumped[me] = new Contact(key, GraceSeconds);   // one bump, not one per tick of contact (the hull stays held)
                Play(repo, vehicle, pending);
                return false;
            }

            bool breaks = into >= row.MinRamSpeedMs;
            if (!breaks && MathF.Abs(throttle) >= PushThrottle)   // ⭐ D-3 — a PUSH: full throttle held in contact
            {
                float seconds = (s_push.TryGetValue(me, out var push) && push.Key == key ? push.Seconds : 0f) + dt;
                s_push[me] = new Contact(key, seconds);
                breaks = seconds >= row.PushSeconds;
                if (!breaks) { Cue(repo, vehicle, key, ContactBreachKind.Pushing, hit.At, hereZ, seconds / row.PushSeconds); return false; }
            }
            if (!breaks) return false;

            s_push.TryRemove(me, out _);
            var broke = NewPending(vehicle);
            if (panel >= 0) Cut(world, table, in hull, panel, hit.At, fwd, vehicle, broke.Breaches);
            else if (tree != null) broke.Tree = tree;   // ⭐ CE-4603 (B-6) — the trunk is felled
            else broke.ObstacleIndex = obstacle;
            broke.Cue = Make(vehicle, ContactBreachKind.Broke, hit.At, hereZ, 1f); broke.HasCue = true;
            s_grace[me] = new Contact(key, GraceSeconds);
            Play(repo, vehicle, broke);
            speedKept = MathF.Max(0.05f, row.SpeedKept);   // a break costs speed, never a zero (V-3)
            return true;
        }

        /// <summary>
        /// The barrier <paramref name="hit"/> names: a wall panel (<paramref name="panel"/>) or a static obstacle's box (<paramref name="obstacle"/> = its entity
        /// index); its contact <paramref name="key"/> and STRENGTH = resistance per metre × thickness × height (B-D3). False: nothing breakable (a solid building, a door).
        /// </summary>
        public static bool Identify(TerrainWorld world, in BarrierHit hit, float hereZ, out int key, out int panel, out int obstacle, out float strength)
            => Identify(world, in hit, hereZ, out key, out panel, out obstacle, out strength, out _);

        /// <summary>⭐ CE-4603 (B-6) — and a lone tree trunk (<paramref name="tree"/> = its key; its contact key a hash of it).</summary>
        public static bool Identify(TerrainWorld world, in BarrierHit hit, float hereZ, out int key, out int panel, out int obstacle, out float strength, out (int X, int Y)? tree)
        {
            key = 0; panel = -1; obstacle = -1; strength = 0f; tree = null;
            int pi = hit.Rise ? world.SolidUnder(hit.At.X, hit.At.Y, hereZ) : hit.Piece;
            if ((uint)pi >= (uint)world.Prisms.Count) return false;
            var prism = world.Prisms[pi];
            float perMetre = prism.Material?.ResistanceMmRhaPerMetre ?? 1500f;
            if (prism.Kind == TerrainPrismKind.Tree)
            {
                var k = TreeDamages.KeyOf(prism);
                tree = k;
                key = (int)((2u << 29) | ((uint)(k.X * 73856093 ^ k.Y * 19349663) & 0x1FFFFFFFu));
                return true;
            }
            if (prism.Panel >= 0)
            {
                panel = prism.Panel;
                var p = world.Panels[panel];
                strength = perMetre * p.Thickness * (p.TopZ - p.BaseZ);
                key = panel;
                return true;
            }
            if (prism.Label is { } label && label.StartsWith(TerrainObstacles.LabelPrefix, StringComparison.Ordinal)
                && int.TryParse(label.AsSpan(TerrainObstacles.LabelPrefix.Length), out obstacle))
            {
                float thin = float.MaxValue;
                for (int i = 0; i < prism.Footprint.Length; i++) thin = MathF.Min(thin, Vector2.Distance(prism.Footprint[i], prism.Footprint[(i + 1) % prism.Footprint.Length]));
                strength = perMetre * thin * (prism.TopZ - prism.BaseZ);
                key = (1 << 30) | obstacle;
                return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ V-9 / V-12 — the drive-through cut: the hull's footprint across the wall at its crossing angle + the margin each side, at least the drive-through width,
        /// from the wall's base to its top (a Passage, V-6) — on the panel hit AND every free-wall panel the cut overlaps (a joint, a corner).
        /// </summary>
        public static void Cut(TerrainWorld world, ContactBreachTable table, in VehicleParams hull, int panel, Vector2 at, Vector2 fwd, Entity by, List<BuildingDamageEvent> into)
        {
            var p = world.Panels[panel];
            float len = p.Length;
            if (len <= 1e-3f) return;
            var dir = (p.B - p.A) / len;
            var side = new Vector2(-fwd.Y, fwd.X);
            float extent = MathF.Abs(hull.Length * Vector2.Dot(fwd, dir)) + MathF.Abs(hull.Width * Vector2.Dot(side, dir));
            float width = MathF.Max(extent + 2f * table.CutMarginM, table.MinDriveThroughM);
            float along = Along(p, at);
            var e1 = p.A + dir * (along - width * 0.5f); var e2 = p.A + dir * (along + width * 0.5f);
            var near = new List<int>();
            world.BuildingPanels.Near((e1 + e2) * 0.5f, width * 0.5f + 1f, near);
            if (!near.Contains(panel)) near.Add(panel);
            foreach (int qi in near)
            {
                var q = world.Panels[qi];
                if (q.Building >= 0) continue;   // a drive-through cut opens free walls; a building is the bump's
                float ql = q.Length;
                if (ql <= 1e-3f) continue;
                var qd = (q.B - q.A) / ql; var qn = new Vector2(-qd.Y, qd.X);
                float d1 = MathF.Abs(Vector2.Dot(e1 - q.A, qn)), d2 = MathF.Abs(Vector2.Dot(e2 - q.A, qn));
                if (qi != panel && MathF.Max(d1, d2) > q.Thickness * 0.5f + 1f) continue;   // not along this cut
                float t1 = Vector2.Dot(e1 - q.A, qd), t2 = Vector2.Dot(e2 - q.A, qd);
                float from = MathF.Max(0f, MathF.Min(t1, t2)), to = MathF.Min(ql, MathF.Max(t1, t2));
                if (to - from < 0.05f) continue;
                into.Add(Event(world, qi, from, to, q.BaseZ, q.TopZ, by));
            }
        }

        private static float Along(TerrainWallPanel p, Vector2 at)
        {
            var d = p.B - p.A; float len = d.Length();
            return len > 1e-6f ? Vector2.Dot(at - p.A, d / len) : 0f;
        }

        private static BuildingDamageEvent Event(TerrainWorld world, int panel, float from, float to, float bottom, float top, Entity by)
        {
            var p = world.Panels[panel];
            from = MathF.Max(0f, from); to = MathF.Min(p.Length, to);
            return new BuildingDamageEvent
            {
                Building = WallGroups.Of(world, panel), Panel = panel, From = from, To = to, Bottom = bottom, Top = top,
                Kind = (byte)BreachRule.Classify(in p, world.Breaches, from, to, bottom, top), Shooter = by,
            };
        }

        private static ContactBreachEvent Make(Entity v, ContactBreachKind kind, Vector2 at, float z, float progress)
            => new() { Vehicle = v, Kind = (byte)kind, X = at.X, Y = at.Y, Z = z, Progress = progress };

        /// <summary>A cue that only changes the driver's HUD: once per (barrier, kind) per hull, not every tick of contact (a push's bar every tick).</summary>
        private static void Cue(EntityRepository repo, Entity v, int key, ContactBreachKind kind, Vector2 at, float z, float progress)
        {
            if (kind != ContactBreachKind.Pushing && s_cue.TryGetValue(v.Index, out var last) && last.Key == key && last.Kind == (byte)kind) return;
            s_cue[v.Index] = (key, (byte)kind);
            var p = NewPending(v);
            p.Cue = Make(v, kind, at, z, progress); p.HasCue = true;
            Play(repo, v, p);
        }

        private static Pending NewPending(Entity v) => new() { Vehicle = v };

        /// <summary>⭐ V-15 — now when not inside a per-entity phase, else a follow-up played serially after it, in batch order.</summary>
        private static void Play(EntityRepository repo, Entity v, Pending p)
        {
            if (!ParallelReadCheck.Defers(repo)) { Apply(repo, p); return; }
            long ticket = System.Threading.Interlocked.Increment(ref s_ticket);
            s_pending[ticket] = p;
            ParallelReadCheck.CurrentBatch!.Later(FollowUp.Instance, v, 0, ticket);
        }

        private sealed class FollowUp : Fdp.Core.Jobs.IPhaseFollowUp
        {
            public static readonly FollowUp Instance = new();
            public void Apply(EntityRepository repo, Entity e, int code, long arg) { if (s_pending.TryRemove(arg, out var p)) ContactBreach.Apply(repo, p); }
        }

        private static void Apply(EntityRepository repo, Pending p)
        {
            if (p.Breaches.Count > 0 && repo.Bus.IsRegistered<BuildingDamageEvent>())
                foreach (var b in p.Breaches) repo.Bus.Publish(b);
            if (p.ObstacleIndex >= 0 && repo.IsComponentTypeRegistered<StaticObstacle>())
                foreach (var ob in repo.Query().With<StaticObstacle>().Build())
                    if (ob.Index == p.ObstacleIndex)
                    {
                        float hp = repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && repo.HasComponent<Fdp.Toolkit.Combat.Components.Health>(ob)
                            ? repo.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(ob).Current : 0f;
                        Fdp.Toolkit.Combat.ObstacleDamage.Damage(repo, ob, hp + 1f, p.Vehicle);   // crushed: the obstacle's whole Health
                        break;
                    }
            if (p.Tree is { } t && repo.Bus.IsRegistered<TreeFellEvent>()) repo.Bus.Publish(new TreeFellEvent { X = t.X, Y = t.Y, By = p.Vehicle });   // ⭐ CE-4603
            if (p.HasCue && repo.Bus.IsRegistered<ContactBreachEvent>()) repo.Bus.Publish(p.Cue);
        }
    }
}
