using Hrot.AI.Behaviors.Orders;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Combat;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2277</c> DA-F (AT-1…AT-12) — the platoon's attack plan: the order (area, preset), the objectives (≤ <see cref="MaxObjectives"/>),
    /// each roster slot's role, the phase <c>KeepAttack</c> orders for and the current objective's three points.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8f.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PlatoonAttackPlan
    {
        public const int Capacity = CommandPicture.MaxSubordinates;
        public const int MaxObjectives = 6;
        public const byte NoRole = 0, Assault = 1, Support = 2, Reserve = 3;
        public const byte PhasePlan = 0, PhaseSupport = 1, PhaseApproach = 2, PhaseAssault = 3, PhaseConsolidate = 4, PhaseAbort = 5;

        public Vector3 Position;
        public float SearchRadius;
        public float Speed;
        /// <summary>0 = AttackPosition (support, approach, assault) · 1 = ClearBuilding (no support phase — PL-1) · 2 = ReactToFireFromBuilding
        /// (⭐ CE-2284 UM-12 — the squad fired on supports from where it stands, no wait for its Set).</summary>
        public byte Preset;
        /// <summary>⭐ UM-12 — the slot that sent the CONTACT the reaction answers (−1 = none: slot order).</summary>
        public int Reporter;
        public byte Made;
        public byte Asked;
        public SimInstant AskedAt;
        public byte Phase;
        /// <summary>AT-10 — 1 once the attack is broken off.</summary>
        public byte Aborted;
        /// <summary>Where the platoon stood at the plan: the approach comes from here, the abort goes back here.</summary>
        public Vector3 Start;
        public int Objectives;
        public int Current;
        public fixed float OX[MaxObjectives];
        public fixed float OY[MaxObjectives];
        public fixed float OZ[MaxObjectives];
        /// <summary>1 = the objective is a building (cleared); 0 = an open position (assaulted through).</summary>
        public fixed byte OBuilding[MaxObjectives];
        public fixed byte Role[Capacity];
        public fixed byte Dirty[Capacity];
        /// <summary>AT-11 — 1 = this reserve is out on a casevac.</summary>
        public fixed byte Casevac[Capacity];
        /// <summary>⭐ CE-4608 — 1 = this slot was the assault and was relieved to the reserve (spent): in the assault it HOLDS the objective's foothold.</summary>
        public fixed byte Spent[Capacity];
        /// <summary>⭐ <c>CE-5051</c> (D-12 extended, user `2026-11-26`) — this slot started the support phase beyond the support line: 1 = MOVING to its
        /// ATTACK POSITION (<see cref="PlatoonAttackBehavior.AttackPositionOf"/>), 2 = there, waiting in cover (0 = it waits where it stood).</summary>
        public fixed byte Forward[Capacity];
        public Vector3 SupportAt;
        public Vector3 AssaultAt;
        public Vector3 ReserveAt;
        /// <summary>⭐ <c>CE-2436</c> AU-1 (A-1, A-3) — the posts the base of fire keeps under fire: the current objective's reported posts, then
        /// (SHIFTED at the assault) its depth and flanks; <see cref="Posts"/> of them.</summary>
        public Vector3 Post0, Post1, Post2, Post3;
        public int Posts;
        /// <summary>⭐ AU-1 (DA-11) — 1 once the assault reported its FOOTHOLD: the base of fire lifts (defends, returns fire).</summary>
        public byte Lifted;
        /// <summary>⭐ <c>CE-2436</c> AU-2 (A-7) — 1 = the objective's posts are REPORTED (in contact): the approach is fire and movement, not bounding.</summary>
        public byte Contact;

        public Vector3 Objective(int i) => new(OX[i], OY[i], OZ[i]);

        public int SlotOf(byte role, int n)
        {
            for (int s = 0; s < n && s < Capacity; s++) if (Role[s] == role) return s;
            return -1;
        }
    }

    /// <summary>⭐ <c>CE-2277</c> — what <see cref="PlatoonAttackBehavior.KeepAttack"/> keeps: how many orders it gave.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlatoonAttackState
    {
        public int Orders;
    }

    /// <summary>
    /// ⭐ <c>CE-2277</c> (AT-2) — pure: the objectives of an AREA from a building survey and the picture's elements — every fresh element inside
    /// the area: inside a surveyed building's room (or within <see cref="BuildingMatchMetres"/> of its centre) ⇒ that building, else its own
    /// point; one per building, open points within <see cref="SameOpenMetres"/> merged; nearest <c>start</c> first. No element ⇒ the area's
    /// enterable buildings (a sweep).
    /// </summary>
    public static class AttackObjectives
    {
        public const float BuildingMatchMetres = 12f, SameOpenMetres = 30f;

        public readonly record struct Objective(Vector3 Point, bool Building);

        public static List<Objective> Plan(WorldSurveyPayload? answer, IReadOnlyList<Vector3> elements, Vector3 centre, float radius, Vector3 start, int max)
        {
            var list = new List<Objective>();
            var taken = new HashSet<int>();
            foreach (var at in elements)
            {
                if (Flat(at, centre) > radius) continue;
                int b = BuildingAt(answer, at);
                if (b >= 0)
                {
                    if (!taken.Add(b)) continue;
                    var bc = answer!.Buildings.Find(x => x.Building == b)!;
                    list.Add(new Objective(new Vector3(bc.CentreX, bc.CentreY, at.Z), true));
                    continue;
                }
                bool merged = false;
                foreach (var o in list) if (!o.Building && Flat(o.Point, at) <= SameOpenMetres) { merged = true; break; }
                if (!merged) list.Add(new Objective(at, false));
            }
            if (list.Count == 0 && answer != null)
                foreach (var bc in answer.Buildings)
                    if (!bc.Solid && Flat(new Vector3(bc.CentreX, bc.CentreY, 0f), centre) <= radius + BuildingMatchMetres)
                        list.Add(new Objective(new Vector3(bc.CentreX, bc.CentreY, 0f), true));
            list.Sort((a, b) => Flat(a.Point, start).CompareTo(Flat(b.Point, start)));
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        /// <summary>The enterable surveyed building <paramref name="at"/> is in: inside one of its indoor rooms, else near its centre; −1.</summary>
        public static int BuildingAt(WorldSurveyPayload? answer, Vector3 at)
        {
            if (answer == null) return -1;
            foreach (var room in answer.Rooms)
                if (room.Indoors && room.Corners.Length >= 8 && InQuad(room.Corners, at.X, at.Y)
                    && answer.Buildings.Find(x => x.Building == room.Building) is { Solid: false })
                    return room.Building;
            int best = -1; float bestD = BuildingMatchMetres;
            foreach (var bc in answer.Buildings)
            {
                if (bc.Solid) continue;
                float d = Flat(new Vector3(bc.CentreX, bc.CentreY, 0f), at);
                if (d <= bestD) { bestD = d; best = bc.Building; }
            }
            return best;
        }

        private static bool InQuad(float[] c, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = 3; i < 4; j = i++)
            {
                float xi = c[i * 2], yi = c[i * 2 + 1], xj = c[j * 2], yj = c[j * 2 + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2277</c> DA-F (AT-1…AT-12, Platoon §4, R-2060) — <c>PlatoonAttack</c>, the platoon attacks an AREA over its squads: APlan
    /// lists the objectives (a survey of the area + the picture's elements) and gives each slot a role; for each objective ASupport puts the
    /// base of fire up (the support <c>SquadDefend</c>s 120 m off, its sector on the objective, FireAtWill), AApproach bounds the assault to a
    /// flank point 40 m off (the reserve behind it), AAssault lifts the support's fire and sends the assault in (<c>ClearBuilding</c> or
    /// <c>FireAndMovement</c>), ANext takes the next one; AConsolidate holds the last. The control region relieves a Black assault with a
    /// reserve, answers a Casevac, and breaks the attack off when nobody is left to assault. One activity (<see cref="KeepAttack"/>) gives
    /// every order. Nothing here reads another brain — only the picture.
    /// </summary>
    public static class PlatoonAttackBehavior
    {
        public const float DefaultSearchRadius = 60f;
        public const float SupportStandOffMetres = 120f, AssaultStandOffMetres = 40f, ReserveStandOffMetres = 100f;
        /// <summary>⭐ <c>CE-8323</c> — a base of fire this close to the objective when the attack starts supports from where it stands (m): within
        /// 50 m of the stand-off, moving up gains little range and costs a walk in the open — the cover it already holds is worth more.</summary>
        public const float SupportFromHereMetres = SupportStandOffMetres + 50f;

        /// <summary>⭐ <c>CE-2436</c> AU-6 (R-2114) — an assault whose approach failed is ordered again while at least this many of its men stand
        /// (fire and movement needs two halves).</summary>
        public const int ApproachMinMen = 2;
        public const float FlankDegrees = 70f, SupportArcHalfDegrees = 25f, SupportRangeMetres = 220f;
        public const float SupportPostRadius = 25f, ConsolidateRadius = 60f;
        /// <summary>⭐ D-12 (CE-4666, user-approved 2026-11-19) — how far round where it stands a squad WAITING for the base of fire looks for its covered posts (m).</summary>
        public const float WaitRadius = 15f;
        /// <summary>⭐ <c>CE-5051</c> — a waiting squad farther than the support line plus this (m) moves up to its attack position first. 📐 120, not
        /// 40: at 40 the village-street-fight squads (161 m out) were sent 40 m up the open street and the scenario went from all-pass to 7 reds
        /// (A/B, the extension off ⇒ all pass); stechovice-battle's 550 m is what the move is for.</summary>
        public const float AttackPositionMarginMetres = 120f;
        /// <summary>⭐ <c>CE-5051</c> — a squad whose men's centre is this close to its attack position has arrived there (m).</summary>
        public const float AttackPositionArriveMetres = 20f;

        /// <summary>
        /// ⭐⭐ <c>CE-5051</c> (D-12 extended; user `2026-11-26`: <i>"yes, build the D-12 extension"</i>; doctrine ATP 3-21.8 — the platoon moves
        /// together to its attack position, then splits) — where slot <paramref name="slot"/> waits while the base of fire sets when it started far
        /// out: on ITS OWN axis (the bearing from the objective to its assault / reserve point), at the support line's distance. 📐 stechovice-battle:
        /// the assault and the reserve waited 83 s at the start line 550 m from the village.
        /// </summary>
        public static unsafe Vector3 AttackPositionOf(in PlatoonAttackPlan p, int slot)
        {
            var o = p.Objective(Math.Min(p.Current, Math.Max(0, p.Objectives - 1)));
            var axisPoint = p.Role[slot] == PlatoonAttackPlan.Reserve ? p.ReserveAt : p.AssaultAt;
            var d = new Vector2(axisPoint.X - o.X, axisPoint.Y - o.Y);
            if (d.LengthSquared() < 1e-4f) d = new Vector2(p.SupportAt.X - o.X, p.SupportAt.Y - o.Y);
            if (d.LengthSquared() < 1e-4f) return axisPoint;
            d = Vector2.Normalize(d) * SupportStandOffMetres;
            return new Vector3(o.X + d.X, o.Y + d.Y, axisPoint.Z);
        }
        public const float ElementFreshSeconds = 120f;
        /// <summary>⭐ <c>CE-2436</c> AU-1 — a reported element this close to the objective is one of its posts (m); the SHIFT puts fire this far
        /// beyond it (the depth) and to its flanks (m).</summary>
        public const float PostMatchMetres = 25f, ShiftDepthMetres = 30f, ShiftFlankMetres = 25f;
        public const int AreaSite = 0x22770001;
        public const float SurveyTimeoutSeconds = 5f;
        /// <summary>⭐ UM-12 — the reaction's search round the firing element (m), and how fresh that element must be (s).</summary>
        public const float ReactSearchRadius = 15f, ReactFreshSeconds = 30f;
        public const BehaviorFaultCode AbortedFault = BehaviorFaultCode.Custom + 277;

        [StructLayout(LayoutKind.Sequential)]
        public struct PlatoonAttackBlackboard : IPlayRoles
        {
            public PlatoonAttackPlan Plan;
            public PlatoonAttackState Keep;

            /// <summary>⭐ CE-4686 ② — the slot's role, read from the plan (never changed).</summary>
            public unsafe string? RoleOf(int slot) => slot < 0 || slot >= PlatoonAttackPlan.Capacity ? null : Plan.Role[slot] switch
            {
                PlatoonAttackPlan.Assault => PlayRoleWords.Assault, PlatoonAttackPlan.Support => PlayRoleWords.BaseOfFire,
                PlatoonAttackPlan.Reserve => PlayRoleWords.Reserve, _ => null,
            };
        }

        [BehaviorResolver(BehaviorNames.PlatoonAttack)]
        public static void Resolve(in PlatoonAttackParamsJsonDto authored, ref PlatoonAttackBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            block.Plan.Position = authored.Position;
            // ⭐ CE-8307 (R-8392) — the neutral AttackOrder's entity: the area round it when no place is given
            if ((Vector3)authored.Position == Vector3.Zero && authored.Target.Ref.TryResolve(world, out var t) && world.HasComponent<Fdp.Core.SimTransform>(t))
                block.Plan.Position = (Vector3)world.GetComponentRO<Fdp.Core.SimTransform>(t).Position;
            block.Plan.SearchRadius = authored.SearchRadius > 0f ? authored.SearchRadius : DefaultSearchRadius;
            block.Plan.Speed = authored.Speed;
            block.Plan.Preset = (byte)authored.Preset;
        }

        /// <summary>⭐ <c>CE-2284</c> U-2 (UM-12) — the SOP's reaction: the same play, preset 2, round the newest firing element (found at the plan).</summary>
        [BehaviorResolver(BehaviorNames.ReactToFireFromBuilding)]
        public static void ResolveReact(in NoParams authored, ref PlatoonAttackBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            block.Plan.Preset = (byte)PlatoonAttackPreset.ReactToFireFromBuilding;
            block.Plan.SearchRadius = ReactSearchRadius;
        }

        [Behavior(BehaviorNames.ReactToFireFromBuilding, Conduct = Fdp.Toolkit.Behavior.Conduct.Halts | Fdp.Toolkit.Behavior.Conduct.SeeksCover,   /* CE-4621 M-3 (CD-1) */ Params = typeof(NoParams), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.ReactToFireFromBuilding, Curated = true, Group = "Library/Reactions", ParamsType = typeof(PlatoonAttackBlackboard))]
        public static StateMachineGraph BuildReactToFireFromBuilding() => Build("ReactToFireFromBuilding_HSM");

        [Behavior(BehaviorNames.PlatoonAttack, Params = typeof(PlatoonAttackParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Attack), Categories = BehaviorCategory.Commander)]   // ⭐ CE-8307 (R-8392): it fulfils Attack (a split platoon re-resolves to it)
        [HsmDefinition(BehaviorNames.PlatoonAttack, Curated = true, Group = "Library/Orders", ParamsType = typeof(PlatoonAttackBlackboard))]
        public static StateMachineGraph BuildPlatoonAttack() => Build("PlatoonAttack_HSM");

        /// <summary>The one play's graph, under either name (§10: one play, presets).</summary>
        private static StateMachineGraph Build(string name)
        {
            var builder = new HsmBuilder(name);
            var root = builder.State("Attacking").Initial().Parallel();
            StateBuilder plan = null!, support = null!, approach = null!, assault = null!, next = null!, consolidate = null!, breaking = null!, broke = null!;
            StateBuilder watch = null!, relieve = null!, answer = null!, abort = null!;
            root.Child("APlan", c => plan = c.InRegion(0).Initial()
                .Activity<PlatoonAttackBlackboard, PlatoonAttackPlan, PlatoonAttackState>(bb => bb.Plan, bb => bb.Keep, PlanAttack));
            root.Child("ASupport", c => support = c.InRegion(0)
                .OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, EnterSupport)
                .Activity<PlatoonAttackBlackboard, PlatoonAttackPlan, PlatoonAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("AApproach", c => approach = c.InRegion(0)
                .OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, EnterApproach)
                .Activity<PlatoonAttackBlackboard, PlatoonAttackPlan, PlatoonAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("AAssault", c => assault = c.InRegion(0)
                .OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, EnterAssault)
                .Activity<PlatoonAttackBlackboard, PlatoonAttackPlan, PlatoonAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("ANext", c => next = c.InRegion(0).OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, NextObjective));
            root.Child("AConsolidate", c => consolidate = c.InRegion(0)
                .OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, EnterConsolidate)
                .Activity<PlatoonAttackBlackboard, PlatoonAttackPlan, PlatoonAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("AWon", c => c.InRegion(0).Final());
            root.Child("ABreaking", c => breaking = c.InRegion(0)
                .OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, EnterBreaking)
                .Activity<PlatoonAttackBlackboard, PlatoonAttackPlan, PlatoonAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("ABroke", c => broke = c.InRegion(0).OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, Broke));
            root.Child("AEnd", c => c.InRegion(0).Final());
            root.Child("AWatch", c => watch = c.InRegion(1).Initial());
            root.Child("ARelieve", c => relieve = c.InRegion(1).OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, Relieve));
            root.Child("AAnswer", c => answer = c.InRegion(1).OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, AnswerCasevac));
            root.Child("AAbort", c => abort = c.InRegion(1).OnEntry<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, Abort));

            plan.On(0).GoTo("AWon").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, NothingToAttack).Polled();
            plan.On(0).GoTo("ASupport").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, Planned).Polled();
            foreach (var st in new[] { support, approach, assault, consolidate })
                st.On(0).GoTo("ABreaking").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, IsAborted).Polled();
            support.On(0).GoTo("AApproach").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, SupportSet).Polled();
            approach.On(0).GoTo("AAssault").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, AssaultSet).Polled();
            assault.On(0).GoTo("ANext").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, AssaultDone).Polled();
            next.On(0).GoTo("ASupport").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, MoreObjectives).Polled();
            next.On(0).GoTo("AConsolidate").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, NoMoreObjectives).Polled();
            consolidate.On(0).GoTo("AWon").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, AllSet).Polled();
            breaking.On(0).GoTo("ABroke").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, AllBack).Polled();
            broke.On(0).GoTo("AEnd").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, Handled).Polled();
            watch.On(0).GoTo("AAbort").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, AssaultLost).Polled();
            watch.On(0).GoTo("ARelieve").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, AssaultFailing).Polled();
            watch.On(0).GoTo("AAnswer").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, CasevacPending).Polled();
            relieve.On(0).GoTo("AWatch").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, Handled).Polled();
            answer.On(0).GoTo("AWatch").Guard<PlatoonAttackBlackboard, PlatoonAttackPlan>(bb => bb.Plan, Handled).Polled();
            return builder.Build();
        }

        // ── the plan (AT-2, AT-3) ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>APlan's activity: one survey of the area (≤ <see cref="SurveyTimeoutSeconds"/>), then the objectives and the roles.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus PlanAttack(ref PlatoonAttackPlan p, ref PlatoonAttackState ws, Entity self, EntityRepository world)
        {
            if (p.Made != 0) return NodeStatus.Running;
            int n = RosterCount(world, self);
            if (n == 0) return NodeStatus.Running;
            double now = CommandPictureOps.Now(world);
            if (p.Asked == 0 && p.Preset == (byte)PlatoonAttackPreset.ReactToFireFromBuilding && p.Position == Vector3.Zero)
            {
                // UM-12 — the reaction answers the newest firing element; none (it went stale) ⇒ nothing to attack
                if (!FiringElement(world, self, (Fdp.Core.SimInstant)now, out var at, out p.Reporter)) { p.Made = 1; p.Objectives = 0; return NodeStatus.Running; }
                p.Position = at;
            }
            if (p.Asked == 0)
            {
                WorldSurveyChildSensor.Ensure(world, self, AreaSite, WorldSurveyRequest.OfArea(new Vector2(p.Position.X, p.Position.Y), p.SearchRadius));
                p.Asked = 1;
                p.AskedAt = (SimInstant)now;
                return NodeStatus.Running;
            }
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, AreaSite);
            var read = WorldSurveyChildSensor.Read(world, child, now);
            bool ready = read.Ready && !read.Stale && read.Payload != null;
            if (!ready && now - p.AskedAt < SurveyTimeoutSeconds) return NodeStatus.Running;
            p.Start = StaffComposerSystem.CentreOf(world, self);
            var objectives = AttackObjectives.Plan(ready ? read.Payload : null, Elements(world, self, (Fdp.Core.SimInstant)now), p.Position, p.SearchRadius,
                                                   p.Start, PlatoonAttackPlan.MaxObjectives);
            WorldSurveyChildSensor.Release(world, child);
            if (objectives.Count == 0) objectives.Add(new AttackObjectives.Objective(p.Position, false));   // nothing known: the area's middle
            p.Objectives = objectives.Count;
            for (int i = 0; i < objectives.Count; i++)
            {
                var o = objectives[i].Point;
                p.OX[i] = o.X; p.OY[i] = o.Y; p.OZ[i] = o.Z;
                p.OBuilding[i] = objectives[i].Building ? (byte)1 : (byte)0;
            }
            for (int s = 0; s < n && s < PlatoonAttackPlan.Capacity; s++)   // AT-3
                p.Role[s] = s == 0 ? PlatoonAttackPlan.Assault : s == 1 ? PlatoonAttackPlan.Support : PlatoonAttackPlan.Reserve;
            if (p.Preset == (byte)PlatoonAttackPreset.ReactToFireFromBuilding) ReactRoles(ref p, world, self, n);
            p.Made = 1;
            return NodeStatus.Running;
        }

        /// <summary>⭐ UM-12 — the building is cleared: the firing elements the reaction answered (within its search round) leave the picture,
        /// so the SOP does not start the same reaction again when this one ends (📐 rail: the same CONTACT re-triggered it at once).</summary>
        private static void Answered(ref PlatoonAttackPlan p, EntityRepository world, Entity self)
        {
            if (!world.HasComponent<CommandPicture>(self)) return;
            ref var picture = ref world.GetComponentRW<CommandPicture>(self);
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref var e = ref picture.Elements.Items[i];
                if (e.Activity == ElementActivity.Firing && Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(p.Position.X, p.Position.Y)) <= p.SearchRadius) e.Count = 0;
            }
        }

        /// <summary>⭐ UM-12 — the squad fired on is the SUPPORT; the first other live squad the ASSAULT; the rest the reserve.</summary>
        private static unsafe void ReactRoles(ref PlatoonAttackPlan p, EntityRepository world, Entity self, int n)
        {
            int support = p.Reporter >= 0 && p.Reporter < n && Alive(world, self, p.Reporter) ? p.Reporter : -1;
            int assault = -1;
            for (int s = 0; s < n && s < PlatoonAttackPlan.Capacity; s++)
            {
                if (s == support || !Alive(world, self, s)) { p.Role[s] = s == support ? PlatoonAttackPlan.Support : PlatoonAttackPlan.NoRole; continue; }
                if (assault < 0) { assault = s; p.Role[s] = PlatoonAttackPlan.Assault; }
                else p.Role[s] = PlatoonAttackPlan.Reserve;
            }
        }

        /// <summary>⭐ UM-12 — the newest element in the picture reported FIRING within <see cref="ReactFreshSeconds"/> (a squad's CONTACT, CE-2283
        /// M-2), and the roster slot of the squad that sent it (−1 = not one of ours).</summary>
        public static bool FiringElement(EntityRepository world, Entity self, SimInstant now, out Vector3 at, out int slot)
        {
            at = default; slot = -1;
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            SimInstant newest = (SimInstant)double.NegativeInfinity;
            long from = 0;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || e.Activity != ElementActivity.Firing || now - e.ObservedAt > ReactFreshSeconds || e.ObservedAt <= newest) continue;
                newest = e.ObservedAt; at = e.At; from = e.FromNetId;
            }
            if (double.IsNegativeInfinity(newest)) return false;
            int n = RosterCount(world, self);
            for (int s = 0; s < n; s++)
                if (CommandPictureOps.NetIdOf(world, CommandPictureOps.Subordinate(world, self, s)) == from) { slot = s; break; }
            return true;
        }

        private static List<Vector3> Elements(EntityRepository world, Entity self, SimInstant now)
        {
            var list = new List<Vector3>();
            if (!world.HasComponent<CommandPicture>(self)) return list;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count > 0 && now - e.ObservedAt <= ElementFreshSeconds) list.Add(e.At);
            }
            return list;
        }

        // ── the phases (AT-4…AT-8) ──────────────────────────────────────────────────────────────────────────────────────

        [SharedAiAction]
        public static unsafe NodeStatus EnterSupport(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            Points(ref p, world, self);
            int here = p.SlotOf(PlatoonAttackPlan.Support, PlatoonAttackPlan.Capacity);
            if (here >= 0)
            {
                var stands = StaffComposerSystem.CentreOf(world, CommandPictureOps.Subordinate(world, self, here));
                var o = p.Objective(p.Current);
                float dx = stands.X - o.X, dy = stands.Y - o.Y;
                // UM-12 — the squad in contact supports from where it stands. ⭐⭐ CE-8323 — and so does a base of fire ALREADY within
                //   SupportFromHereMetres of the objective: it fights from the cover it holds instead of walking into the open to a point on the
                //   axis. 📐 live village-street-mg-flank: the support point (411,244) lay across the street from the squad (378,272, 160 m from the
                //   gun); it walked through the street to it, then its post plan found the house it had left and walked back — a man crossed twice
                if (p.Preset == (byte)PlatoonAttackPreset.ReactToFireFromBuilding || dx * dx + dy * dy <= SupportFromHereMetres * SupportFromHereMetres)
                    p.SupportAt = stands;
            }
            // ⭐ CE-5051 (D-12 extended) — which waiting squads start beyond the support line: they move up to their attack position
            {
                var obj = p.Objective(p.Current);
                for (int s = 0; s < PlatoonAttackPlan.Capacity; s++)
                {
                    p.Forward[s] = 0;
                    if (p.Role[s] != PlatoonAttackPlan.Assault && p.Role[s] != PlatoonAttackPlan.Reserve) continue;
                    var sub = CommandPictureOps.Subordinate(world, self, s);
                    if (sub.IsNull || !world.IsAlive(sub)) continue;
                    var at = MenCentre(world, sub);
                    float far = SupportStandOffMetres + AttackPositionMarginMetres;
                    if ((at.X - obj.X) * (at.X - obj.X) + (at.Y - obj.Y) * (at.Y - obj.Y) > far * far) p.Forward[s] = 1;
                }
            }
            SetPhase(ref p, PlatoonAttackPlan.PhaseSupport);
            ObjectivePosts(ref p, world, self);   // ⭐ CE-2436 AU-1 (A-1) — the base of fire's targets: the objective's reported posts
            p.Lifted = 0;
            int support = p.SlotOf(PlatoonAttackPlan.Support, PlatoonAttackPlan.Capacity);
            Fire(world, self, support, RoeFire.FireAtWill);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus EnterApproach(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, PlatoonAttackPlan.PhaseApproach);
            return NodeStatus.Success;
        }

        /// <summary>⭐⭐ <c>CE-2436</c> AU-1 (A-3, replacing AT-6's lift) — the assault goes in and the base of fire SHIFTS to the objective's depth
        /// and flanks (the order changes, its fire never stops); it LIFTS only at the assault's FOOTHOLD (DA-11, <see cref="KeepAttack"/>).
        /// 📌 Measured: AT-6 set the support to ReturnFire here, so the base of fire went nearly quiet as the assault crossed the open.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus EnterAssault(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, PlatoonAttackPlan.PhaseAssault);
            ShiftPosts(ref p, world, self);
            return NodeStatus.Success;
        }

        /// <summary>⭐ AU-1 (A-1) — the current objective's reported posts (fresh picture elements within <see cref="PostMatchMetres"/>, nearest
        /// first, ≤ 4); none reported ⇒ the objective itself.</summary>
        private static void ObjectivePosts(ref PlatoonAttackPlan p, EntityRepository world, Entity self)
        {
            var o = p.Objective(Math.Min(p.Current, Math.Max(0, p.Objectives - 1)));
            p.Posts = 0;
            foreach (var e in Elements(world, self, CommandPictureOps.Now(world)))
                if (Flat(e, o) <= PostMatchMetres) AddPost(ref p, e);
            p.Contact = p.Posts > 0 ? (byte)1 : (byte)0;   // AU-2 (A-7)
            if (p.Posts == 0) AddPost(ref p, o);
        }

        /// <summary>⭐ AU-1 (A-3) — the SHIFT: reported elements beyond the objective (within the attack's area, farther than
        /// <see cref="PostMatchMetres"/> from it), then its depth (<see cref="ShiftDepthMetres"/> beyond, away from the start) and its two flanks
        /// (<see cref="ShiftFlankMetres"/> across) — ≤ 4.</summary>
        private static void ShiftPosts(ref PlatoonAttackPlan p, EntityRepository world, Entity self)
        {
            var o = p.Objective(Math.Min(p.Current, Math.Max(0, p.Objectives - 1)));
            var toStart = new Vector2(p.Start.X - o.X, p.Start.Y - o.Y);
            var dir = toStart.LengthSquared() > 1e-4f ? Vector2.Normalize(toStart) : -Vector2.UnitY;
            var across = new Vector2(-dir.Y, dir.X);
            p.Posts = 0;
            foreach (var e in Elements(world, self, CommandPictureOps.Now(world)))
            {
                float d = Flat(e, o);
                if (d > PostMatchMetres && d <= p.SearchRadius + 40f && Vector2.Dot(new Vector2(e.X - o.X, e.Y - o.Y), dir) < 0f) AddPost(ref p, e);
            }
            AddPost(ref p, o - new Vector3(dir * ShiftDepthMetres, 0f));
            AddPost(ref p, o + new Vector3(across * ShiftFlankMetres, 0f));
            AddPost(ref p, o - new Vector3(across * ShiftFlankMetres, 0f));
        }

        private static void AddPost(ref PlatoonAttackPlan p, Vector3 at)
        {
            if (p.Posts >= 4) return;
            switch (p.Posts) { case 0: p.Post0 = at; break; case 1: p.Post1 = at; break; case 2: p.Post2 = at; break; default: p.Post3 = at; break; }
            p.Posts++;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));

        /// <summary>⭐ <c>CE-2436</c> AU-1 (DA-11) — the LIFT: the assault reported its FOOTHOLD (or the building clear, or — an open position —
        /// Set on it; its Set at the assault position is gone by then: the assault's new order restarts its picture row) ⇒
        /// the base of fire stops its area fire and returns fire only, defending its point.</summary>
        private static unsafe void Lift(ref PlatoonAttackPlan p, EntityRepository world, Entity self, int n)
        {
            if (p.Lifted != 0 || p.Phase != PlatoonAttackPlan.PhaseAssault) return;
            int a = p.SlotOf(PlatoonAttackPlan.Assault, n);
            if (a < 0) return;
            var progress = CommandPictureOps.ProgressOf(world, self, a);
            if (progress != ProgressKind.Foothold && progress != ProgressKind.BuildingClear && progress != ProgressKind.Set) return;
            p.Lifted = 1;
            int s = p.SlotOf(PlatoonAttackPlan.Support, n);
            if (s < 0) return;
            p.Dirty[s] = 1;
            Fire(world, self, s, RoeFire.ReturnFire);
        }

        [SharedAiAction]
        public static NodeStatus NextObjective(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            // ⭐ CE-4610 — objective k taken is phase line k (1-based) reached, reported up
            CommandNodes.ReportProgress(world, self, ProgressKind.PhaseLine, p.Objective(p.Current), p.Current + 1);
            p.Current++;
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static unsafe NodeStatus EnterConsolidate(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, PlatoonAttackPlan.PhaseConsolidate);
            // ⭐ CE-4610 (Platoon Attack §4: "the platoon's staff sends PROGRESS ObjectiveSecure up") — the last objective taken
            if (p.Objectives > 0) CommandNodes.ReportProgress(world, self, ProgressKind.ObjectiveSecure, p.Objective(Math.Max(0, p.Current - 1)));
            if (p.Preset == (byte)PlatoonAttackPreset.ReactToFireFromBuilding) Answered(ref p, world, self);
            Fire(world, self, p.SlotOf(PlatoonAttackPlan.Support, PlatoonAttackPlan.Capacity), RoeFire.FireAtWill);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus EnterBreaking(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, PlatoonAttackPlan.PhaseAbort);
            return NodeStatus.Success;
        }

        /// <summary>AT-10 — the line is back: the run ends FAILED (a fault overrides the HSM's success).</summary>
        [SharedAiAction]
        public static NodeStatus Broke(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            BehaviorFault.Raise(world, self, AbortedFault, "attack broken off: no squad left to assault");
            return NodeStatus.Success;
        }

        private static unsafe void SetPhase(ref PlatoonAttackPlan p, byte phase)
        {
            p.Phase = phase;
            for (int s = 0; s < PlatoonAttackPlan.Capacity; s++) if (p.Role[s] != PlatoonAttackPlan.NoRole) p.Dirty[s] = 1;
        }

        /// <summary>AT-4, AT-5 — the current objective's support point (toward the start), and its assault and reserve points on the flank
        /// with fewer reported elements.</summary>
        private static void Points(ref PlatoonAttackPlan p, EntityRepository world, Entity self)
        {
            var o = p.Objective(p.Current);
            var toStart = new Vector2(p.Start.X - o.X, p.Start.Y - o.Y);
            var dir = toStart.LengthSquared() > 1e-4f ? Vector2.Normalize(toStart) : -Vector2.UnitY;
            p.SupportAt = o + new Vector3(dir * SupportStandOffMetres, 0f);
            float side = FewerOnLeft(world, self, o, dir) ? 1f : -1f;
            float a = side * FlankDegrees * MathF.PI / 180f;
            var flank = new Vector2(dir.X * MathF.Cos(a) - dir.Y * MathF.Sin(a), dir.X * MathF.Sin(a) + dir.Y * MathF.Cos(a));
            p.AssaultAt = o + new Vector3(flank * AssaultStandOffMetres, 0f);
            p.ReserveAt = o + new Vector3(flank * ReserveStandOffMetres, 0f);
        }

        /// <summary>True when the left of the line objective → start holds no more reported elements than its right (ties go left).</summary>
        private static bool FewerOnLeft(EntityRepository world, Entity self, Vector3 o, Vector2 dir)
        {
            int left = 0, right = 0;
            foreach (var e in Elements(world, self, CommandPictureOps.Now(world)))
            {
                float cross = dir.X * (e.Y - o.Y) - dir.Y * (e.X - o.X);
                if (cross > 1f) left++; else if (cross < -1f) right++;
            }
            return left <= right;
        }

        /// <summary>The ONE order writer (AT-12): a slot whose order of this phase does not stand, or that is dirty, is ordered again.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus KeepAttack(ref PlatoonAttackPlan p, ref PlatoonAttackState ws, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return NodeStatus.Running;
            int n = Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity);
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            for (int slot = 0; slot < n; slot++)
            {
                if (p.Role[slot] == PlatoonAttackPlan.NoRole) continue;
                var squad = CommandPictureOps.Subordinate(world, self, slot);
                if (squad.IsNull || !world.IsAlive(squad)) continue;
                if (p.Casevac[slot] != 0)   // AT-11 — out on a casevac: back once that task ended
                {
                    if (!CasevacAnswers.Returned(world, self, slot)) continue;
                    p.Casevac[slot] = 0;
                    p.Dirty[slot] = 1;
                }
                // ⭐⭐ CE-2436 AU-6 (R-2114, lane decision) — an ASSAULT whose approach FAILED (fire and movement ends when one of its two halves has
                //   nobody left) but which still STANDS is ordered again: the new order splits the survivors into two halves afresh. ⛔ Its order
                //   "stood" (Stands names the run, not the task), and AssaultSet waits for a Set ⇒ the squad idled at 50 m until it went Black.
                //   📌 Live (cc-battalion run #9): each assault squad in turn stopped 47 / 34 / 8 m short with no task, and the objective was never taken
                if (p.Dirty[slot] == 0 && p.Phase == PlatoonAttackPlan.PhaseApproach && p.Role[slot] == PlatoonAttackPlan.Assault
                    && CommandPictureOps.ProgressOf(world, self, slot) == ProgressKind.TaskFailed
                    && Fdp.Toolkit.Squad.CombatEffective.StandingMembers(world, squad) >= ApproachMinMen)
                    p.Dirty[slot] = 1;
                // ⭐⭐ CE-4669 — the RESERVE that has reached its point (its bound Set) waits there DEFENDING, never on a finished order: its order
                //   "stood" (Stands names the run, not the task) ⇒ 📐 live pc-defend-house: Blue Squad 3's BoundingOverwatch ended Success at
                //   160 s and the squad sat with no task to the end (`doing = nothing`). Re-sending an unchanged SquadDefend is a no-op below.
                // ⭐ D-12 — a slot a phase change just dirtied has not been ordered its bound yet: a Set it reports is its PREVIOUS order's (the
                //   wait's SquadDefend, AT-14) — 📌 live (pc-corridor-fight): read as "the bound arrived", the reserve was sent to defend its far
                //   point and WALKED there in a knot across the open instead of bounding
                bool reserveArrived = p.Role[slot] == PlatoonAttackPlan.Reserve && p.Spent[slot] == 0 && p.Dirty[slot] == 0
                    && (p.Phase == PlatoonAttackPlan.PhaseApproach || p.Phase == PlatoonAttackPlan.PhaseAssault)
                    && CommandPictureOps.ProgressOf(world, self, slot) == ProgressKind.Set;
                if (reserveArrived) p.Dirty[slot] = 1;
                // ⭐⭐ CE-5051 — a squad MOVING to its attack position that has arrived: now it waits there in cover. 📐 live (stechovice-battle): a
                //   SquadDefend on the far point never marched there — its post plan searched cover round where the men stood, and they drifted
                //   130 m the wrong way — so the move comes first, the wait on arrival
                if (p.Phase == PlatoonAttackPlan.PhaseSupport && p.Forward[slot] == 1)
                {
                    var c = MenCentre(world, squad);
                    var ap = AttackPositionOf(in p, slot);
                    if ((c.X - ap.X) * (c.X - ap.X) + (c.Y - ap.Y) * (c.Y - ap.Y) <= AttackPositionArriveMetres * AttackPositionArriveMetres)
                    { p.Forward[slot] = 2; p.Dirty[slot] = 1; }
                    // ⭐ CE-5051 — no way to its attack position (its march FAILED: SquadMarchPlay.NoWayLeadsToFail): it waits where it stands, as
                    //   D-12 before the extension. 📐 live (stechovice-battle): the point lay on ground no path reaches
                    else if (p.Dirty[slot] == 0 && CommandPictureOps.ProgressOf(world, self, slot) == ProgressKind.TaskFailed)
                    { p.Forward[slot] = 0; p.Dirty[slot] = 1; }
                }
                if (p.Dirty[slot] == 0 && CommandOrders.Stands(world, self, squad)) continue;
                p.Dirty[slot] = 0;
                var standsAt = MenCentre(world, squad);   // ⭐ D-12 — where a waiting squad takes cover: where its MEN stand
                if (!OrderFor(ref p, slot, json, out var behaviour, out var order, out var kind, out var neutral, reserveArrived, standsAt)) continue;
                // ⭐ CE-2346 (R-2066 D) — a phase change re-dirties every slot; the support's SquadDefend is the same order in the next
                //   phase, and re-sending it restarted the squad from "move to the point" (measured: men walked back round the berm)
                // ⭐ agg-behaviors R-8305 / R-8315 — every arm is routed by kind with the TWO payloads (a stochastic squad gets Order(kind, neutral); a physical one
                //   exactly the play's own json); SquadRecover (elsewhere) stays Assign — no stochastic row
                if (CommandOrders.StillRoutedRunning(world, self, squad, kind, neutral, behaviour, order)) continue;
                CommandOrders.Route(world, self, squad, kind, neutral, behaviour, order, newOrder: true);
                if (behaviour == BehaviorNames.SquadDefend || behaviour == BehaviorNames.SupportByFire) Sector(ref p, slot, world, squad, standsAt);   // AU-1: the base of fire keeps its sector (DA-27) · D-12 a waiting squad faces the objective
                ws.Orders++;
            }
            Lift(ref p, world, self, n);   // ⭐ AU-1 — after the orders: the assault's new order has restarted its progress row
            return NodeStatus.Running;
        }

        /// <summary>⭐ D-12 — where a squad's live men stand (their mean), never the squad entity's own transform: 📌 live (`pc-corridor-fight`) that
        /// one stays where the squad was authored, so a wait ordered "where it stands" on the attack's second objective marched the squad 120 m
        /// back across the open. No live man ⇒ <see cref="StaffComposerSystem.CentreOf"/>.</summary>
        private static Vector3 MenCentre(EntityRepository world, Entity squad)
        {
            if (!world.HasComponent<UnitRoster>(squad)) return StaffComposerSystem.CentreOf(world, squad);
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(squad);
            var sum = Vector3.Zero; int n = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = CommandPictureOps.Subordinate(world, squad, i);
                if (m.IsNull || !world.IsAlive(m) || !Injury.IsInTheFight(world, m) || !world.HasComponent<SimTransform>(m)) continue;
                sum += world.GetComponentRO<SimTransform>(m).Position; n++;
            }
            return n > 0 ? sum / n : StaffComposerSystem.CentreOf(world, squad);
        }

        /// <summary>The order a slot of <paramref name="slot"/>'s role gets in the plan's phase (false = none: it keeps what it has).</summary>
        private static unsafe bool OrderFor(ref PlatoonAttackPlan p, int slot, JsonSerializerOptions json, out string behaviour, out string order, out OrderKind kind, out string neutral, bool reserveArrived = false, Vector3 standsAt = default)
        {
            behaviour = order = neutral = "";
            kind = 0;
            byte role = p.Role[slot];
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            switch (p.Phase)
            {
                case PlatoonAttackPlan.PhaseSupport:
                case PlatoonAttackPlan.PhaseApproach:
                case PlatoonAttackPlan.PhaseAssault:
                    // ⭐ CE-4608 (Building Clearing §6a — "the remnant holds the foothold") — a relieved assault squad stops clearing and defends
                    //   the objective it was in (⛔ no order left its ClearBuilding running on)
                    if (role == PlatoonAttackPlan.Reserve && p.Phase == PlatoonAttackPlan.PhaseAssault && p.Spent[slot] != 0)
                        return Defend(o, ConsolidateRadius, p.Speed, json, out behaviour, out order, out kind, out neutral);
                    if (role == PlatoonAttackPlan.Support && p.Preset != (byte)PlatoonAttackPreset.ClearBuilding)
                        return p.Lifted != 0 || p.Posts == 0
                            ? Defend(p.SupportAt, SupportPostRadius, p.Speed, json, out behaviour, out order, out kind, out neutral)
                            : SupportByFire(ref p, json, out behaviour, out order, out kind, out neutral);   // ⭐ CE-2436 AU-1 (A-1, A-3)
                    if (role == PlatoonAttackPlan.Assault && p.Phase == PlatoonAttackPlan.PhaseAssault)
                    {
                        if (p.OBuilding[p.Current] != 0)
                        {
                            behaviour = BehaviorNames.ClearBuilding;
                            order = JsonSerializer.Serialize(new ClearBuildingParamsJsonDto { Building = o, Speed = p.Speed }, json);
                            kind = OrderKind.ClearBuilding; neutral = Attack(o, p.Speed, json);
                        }
                        else
                        {
                            behaviour = BehaviorNames.FireAndMovement;
                            order = JsonSerializer.Serialize(new FireAndMovementParamsJsonDto { Objective = o, Speed = p.Speed }, json);
                            kind = OrderKind.FireAndMovement; neutral = Attack(o, p.Speed, json);
                        }
                        return true;
                    }
                    // ⭐⭐ D-12 (CE-4666, user-approved 2026-11-19) — the assault and the reserve WAIT for the base of fire IN COVER where they stand: a
                    //   SquadDefend there (the consolidation's order, AT-8), its sector on the objective, so each man takes a covered post; AT-5's
                    //   approach replaces it at the support's Set. 📌 Live (pc-corridor-fight): with no order both squads stood idle in the open ~90 s.
                    //   ⛔ Was: return false (no order: they kept whatever the platoon's approach left them doing)
                    if (p.Phase == PlatoonAttackPlan.PhaseSupport)
                    {
                        if (p.Forward[slot] == 1)   // ⭐ CE-5051 — far out: MOVE to the attack position first (tactical movement), then wait there
                            return Move(AttackPositionOf(in p, slot), p.Speed, json, out behaviour, out order, out kind, out neutral);
                        return Defend(p.Forward[slot] == 2 ? AttackPositionOf(in p, slot) : standsAt, WaitRadius, p.Speed, json, out behaviour, out order, out kind, out neutral);
                    }
                    if (reserveArrived && role == PlatoonAttackPlan.Reserve)   // ⭐ CE-4669 — at its point: it holds there, ready to relieve
                        return Defend(p.ReserveAt, SupportPostRadius, p.Speed, json, out behaviour, out order, out kind, out neutral);
                    // ⭐⭐ CE-2436 AU-2 (A-7) — in contact the assault CLOSES by fire and movement (rushes between cover, its own half covering,
                    //   the crossing smoke) — bounding only before contact. 📌 measured: bounding under the line's fire ≈ 0.66 m/s over 113 m
                    if (role == PlatoonAttackPlan.Assault)
                        return p.Contact != 0 ? Rush(p.AssaultAt, p.Speed, json, out behaviour, out order, out kind, out neutral) : Bound(p.AssaultAt, p.Speed, json, out behaviour, out order, out kind, out neutral);
                    if (role == PlatoonAttackPlan.Reserve && p.Phase == PlatoonAttackPlan.PhaseApproach)
                        return Bound(p.ReserveAt, p.Speed, json, out behaviour, out order, out kind, out neutral);
                    return false;
                case PlatoonAttackPlan.PhaseConsolidate:
                    return Defend(o, ConsolidateRadius, p.Speed, json, out behaviour, out order, out kind, out neutral);
                case PlatoonAttackPlan.PhaseAbort:
                    behaviour = BehaviorNames.BreakContact;
                    order = neutral = JsonSerializer.Serialize(new BreakContactOrder { To = p.Start, Speed = p.Speed }, json);
                    kind = OrderKind.BreakContact;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>⭐ AU-2 (A-7) — fire and movement to <paramref name="at"/>.</summary>
        private static bool Rush(Vector3 at, float speed, JsonSerializerOptions json, out string behaviour, out string order, out OrderKind kind, out string neutral)
        {
            behaviour = BehaviorNames.FireAndMovement;
            order = JsonSerializer.Serialize(new FireAndMovementParamsJsonDto { Objective = at, Speed = speed }, json);
            kind = OrderKind.FireAndMovement; neutral = Attack(at, speed, json);
            return true;
        }

        /// <summary>⭐ R-8315 — the neutral Attack order on a place (the objective): what a stochastic squad is ordered for ClearBuilding / FireAndMovement / Bound.</summary>
        private static string Attack(Vector3 at, float speed, JsonSerializerOptions json)
            => JsonSerializer.Serialize(new AttackOrder { Position = at, Speed = speed }, json);

        /// <summary>⭐ AU-1 (A-1) — the base of fire's order: hold at the support point, the plan's posts under sustained fire.</summary>
        private static bool SupportByFire(ref PlatoonAttackPlan p, JsonSerializerOptions json, out string behaviour, out string order, out OrderKind kind, out string neutral)
        {
            behaviour = BehaviorNames.SupportByFire;
            // ⭐ R-8315 / CE-8302 (R-8341) — a stochastic squad: Order(SupportByFire, AttackOrder HoldBack on the SUPPORT POINT — where the physical squad stands;
            //   the phase fires at whatever is in reach, so the posts are not its to aim)
            kind = OrderKind.SupportByFire;
            neutral = JsonSerializer.Serialize(new AttackOrder { Position = p.SupportAt, Speed = p.Speed, HoldBack = true }, json);
            order = JsonSerializer.Serialize(new SupportByFireParamsJsonDto
            {
                Position = p.SupportAt, Speed = p.Speed,
                Post1 = p.Posts > 0 ? p.Post0 : default, Post2 = p.Posts > 1 ? p.Post1 : default,
                Post3 = p.Posts > 2 ? p.Post2 : default, Post4 = p.Posts > 3 ? p.Post3 : default,
            }, json);
            return true;
        }

        private static bool Defend(Vector3 at, float radius, float speed, JsonSerializerOptions json, out string behaviour, out string order, out OrderKind kind, out string neutral)
        {
            behaviour = BehaviorNames.SquadDefend;
            order = neutral = JsonSerializer.Serialize(new DefendOrder { Position = at, SearchRadius = radius, Speed = speed }, json);   // ⭐ R-8300 — the neutral Defend order
            kind = OrderKind.Defend;
            return true;
        }

        /// <summary>⭐ <c>CE-5051</c> — a squad MOVE (the squad's own march, traveling): the neutral Move order is also the play's.</summary>
        private static bool Move(Vector3 to, float speed, JsonSerializerOptions json, out string behaviour, out string order, out OrderKind kind, out string neutral)
        {
            behaviour = BehaviorNames.InfantrySquadMove;
            order = neutral = JsonSerializer.Serialize(new MoveOrder { Location = to, Speed = speed }, json);
            kind = OrderKind.Move;
            return true;
        }

        private static bool Bound(Vector3 to, float speed, JsonSerializerOptions json, out string behaviour, out string order, out OrderKind kind, out string neutral)
        {
            behaviour = BehaviorNames.BoundingOverwatch;
            order = JsonSerializer.Serialize(new BoundingOverwatchParamsJsonDto { Objective = to, Speed = speed }, json);
            kind = OrderKind.Bound; neutral = Attack(to, speed, json);
            return true;
        }

        /// <summary>AT-4 — the support's sector: the objective ± <see cref="SupportArcHalfDegrees"/> from its point; every other order none.</summary>
        private static unsafe void Sector(ref PlatoonAttackPlan p, int slot, EntityRepository world, Entity squad, Vector3 standsAt)
        {
            if (!world.Bus.IsRegistered<SetEngagementAreaEvent>()) return;
            bool waiting = p.Phase == PlatoonAttackPlan.PhaseSupport && p.Role[slot] != PlatoonAttackPlan.Support;   // ⭐ D-12 — faces the objective from where it waits
            if ((p.Role[slot] != PlatoonAttackPlan.Support && !waiting) || p.Phase == PlatoonAttackPlan.PhaseConsolidate)
            {
                EngagementAreas.WithdrawSuperior(world, squad);
                return;
            }
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            var from = waiting ? (p.Forward[slot] == 2 ? AttackPositionOf(in p, slot) : standsAt) : p.SupportAt;   // ⭐ CE-5051 — from its attack position
            float bearing = SectorSplit.Norm(MathF.Atan2(o.X - from.X, o.Y - from.Y) * (180f / MathF.PI));
            world.Bus.Publish(new SetEngagementAreaEvent
            {
                Entity = squad, Origin = BehaviorOrigin.Superior, ArcSet = 1, ArcOrigin = from,
                LeftBearing = SectorSplit.Norm(bearing - SupportArcHalfDegrees), RightBearing = SectorSplit.Norm(bearing + SupportArcHalfDegrees),
                MaxRange = SupportRangeMetres,
            });
        }

        private static void Fire(EntityRepository world, Entity self, int slot, RoeFire fire)
        {
            var unit = CommandPictureOps.Subordinate(world, self, slot);
            if (unit.IsNull || !world.Bus.IsRegistered<SetRoeEvent>()) return;
            world.Bus.Publish(new SetRoeEvent { Entity = unit, Origin = BehaviorOrigin.Superior, Fire = fire });
        }

        // ── the guards ───────────────────────────────────────────────────────────────────────────────────────────────

        [SharedAiCondition]
        public static bool Planned(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => p.Made != 0 && p.Objectives > 0;

        [SharedAiCondition]
        public static bool NothingToAttack(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => p.Made != 0 && p.Objectives == 0;

        [SharedAiCondition]
        public static bool IsAborted(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => p.Aborted != 0;

        /// <summary>ASupport → AApproach: the base of fire is up (its Set) — or there is none (ClearBuilding, one squad).</summary>
        [SharedAiCondition]
        public static unsafe bool SupportSet(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int s = p.SlotOf(PlatoonAttackPlan.Support, Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity));
            return p.Preset != 0 || s < 0 || !Alive(world, self, s) || CommandPictureOps.ProgressOf(world, self, s) == ProgressKind.Set;
        }

        /// <summary>AApproach → AAssault: the assault is at its assault position (its Set).</summary>
        [SharedAiCondition]
        public static unsafe bool AssaultSet(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int a = p.SlotOf(PlatoonAttackPlan.Assault, Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity));
            return a >= 0 && CommandPictureOps.ProgressOf(world, self, a) == ProgressKind.Set;
        }

        /// <summary>AAssault → ANext (AT-7): the building is clear (BuildingClear, or its task's Set), the position taken (Set), or the
        /// assault could not (TaskFailed: a blocked building — the next one).</summary>
        [SharedAiCondition]
        public static unsafe bool AssaultDone(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int a = p.SlotOf(PlatoonAttackPlan.Assault, Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity));
            if (a < 0) return false;
            var progress = CommandPictureOps.ProgressOf(world, self, a);
            return progress == ProgressKind.Set || progress == ProgressKind.BuildingClear || progress == ProgressKind.TaskFailed;
        }

        [SharedAiCondition]
        public static bool MoreObjectives(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => p.Current < p.Objectives;

        [SharedAiCondition]
        public static bool NoMoreObjectives(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => p.Current >= p.Objectives;

        /// <summary>AConsolidate → AWon (AT-8): every live squad with a role reports Set on its consolidation order.</summary>
        [SharedAiCondition]
        public static bool AllSet(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => Every(ref p, self, world, ProgressKind.Set, ProgressKind.Set);

        /// <summary>ABreaking → ABroke (AT-10): every live squad's break contact ended (Set or TaskFailed).</summary>
        [SharedAiCondition]
        public static bool AllBack(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => Every(ref p, self, world, ProgressKind.Set, ProgressKind.TaskFailed);

        private static unsafe bool Every(ref PlatoonAttackPlan p, Entity self, EntityRepository world, ProgressKind a, ProgressKind b)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity), live = 0;
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] == PlatoonAttackPlan.NoRole || !Alive(world, self, s)) continue;
                var progress = CommandPictureOps.ProgressOf(world, self, s);
                if (progress != a && progress != b) return false;
                live++;
            }
            return live > 0;
        }

        // ── the control region (AT-9…AT-11) ──────────────────────────────────────────────────────────────────────────

        /// <summary>AWatch → ARelieve (AT-9): the assault reports Black and a reserve is free.</summary>
        [SharedAiCondition]
        public static bool AssaultFailing(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
            => p.Made != 0 && p.Aborted == 0 && AssaultBlack(ref p, self, world) && FreeRelief(ref p, self, world) >= 0;

        /// <summary>AWatch → AAbort (AT-10): the assault reports Black and no reserve is left.</summary>
        [SharedAiCondition]
        public static bool AssaultLost(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
            => p.Made != 0 && p.Aborted == 0 && p.Phase != PlatoonAttackPlan.PhaseConsolidate
               && AssaultBlack(ref p, self, world) && FreeRelief(ref p, self, world) < 0;

        /// <summary>ARelieve's entry: the free reserve becomes the assault, the spent assault a reserve; both ordered again.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus Relieve(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity);
            int a = p.SlotOf(PlatoonAttackPlan.Assault, n), r = FreeRelief(ref p, self, world);
            if (a < 0 || r < 0) return NodeStatus.Success;
            p.Role[a] = PlatoonAttackPlan.Reserve;
            p.Role[r] = PlatoonAttackPlan.Assault;
            p.Spent[a] = 1; p.Spent[r] = 0;   // ⭐ CE-4608
            p.Dirty[a] = p.Dirty[r] = 1;
            return NodeStatus.Success;
        }

        /// <summary>AAbort's entry (AT-10): break off — every squad goes back to the start (ABreaking orders it).</summary>
        [SharedAiAction]
        public static NodeStatus Abort(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            p.Aborted = 1;
            return NodeStatus.Success;
        }

        /// <summary>AWatch → AAnswer (AT-11): a Casevac waits and a reserve is free.</summary>
        [SharedAiCondition]
        public static bool CasevacPending(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
            => p.Made != 0 && p.Aborted == 0 && CommandPictureOps.FirstRequest(world, self, RequestKind.Casevac) >= 0 && FreeReserve(ref p, self, world) >= 0;

        /// <summary>AAnswer's entry (AT-11, RQ-6): the free reserve fetches the casualty to its own point (<see cref="CasevacAnswers"/>).</summary>
        [SharedAiAction]
        public static unsafe NodeStatus AnswerCasevac(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int reserve = FreeReserve(ref p, self, world);
            if (reserve < 0) return NodeStatus.Success;
            var to = CommandPictureOps.Subordinate(world, self, reserve) is var unit && world.HasComponent<SimTransform>(unit)
                ? (Vector3)world.GetComponentRO<SimTransform>(unit).Position : p.ReserveAt;
            if (CasevacAnswers.Answer(world, self, reserve, to, p.Speed)) p.Casevac[reserve] = 1;
            return NodeStatus.Success;
        }

        [SharedAiCondition]
        public static bool Handled(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => true;

        private static unsafe bool AssaultBlack(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int a = p.SlotOf(PlatoonAttackPlan.Assault, Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity));
            return a >= 0 && SlotLost(self, world, a);
        }

        /// <summary>
        /// ⭐⭐ CE-7015 (AQ101 §R E — the rout) — a squad is LOST when it is gone, reports Black, OR too few of its men still STAND (casualties and
        /// the broken alike). ⭐ <c>CE-2436</c> R-2112 — ONE predicate for the assault (AT-9 / AT-10) AND for whoever would relieve it: a broken
        /// reserve or base of fire is no relief (⛔ <see cref="FreeReserve"/> read the colour alone, so a broken old assault, never Black, came back
        /// as the "free reserve" and the roles swapped instead of AT-10 breaking off — rail-found).
        /// </summary>
        private static unsafe bool SlotLost(Entity self, EntityRepository world, int slot)
        {
            if (!Alive(world, self, slot)) return true;
            var unit = CommandPictureOps.Subordinate(world, self, slot);
            if (!unit.IsNull && !Fdp.Toolkit.Squad.CombatEffective.IsStanding(world, unit)) return true;
            var colour = new SubordinateColourParams { Slot = slot, AtLeast = StatusColour.Black };
            return PictureConditions.SubordinateColour(ref colour, self, world);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-6 (R-2112, lane decision) — who can relieve a lost assault: a free reserve (<see cref="FreeReserve"/>), else the BASE
        /// OF FIRE when it is not Black — the platoon shifts its support forward rather than break off with a fit squad in hand (the doctrine's
        /// answer when the reserve is committed). 📌 Live (cc-battalion run #5, t ≈ 400 s): the reserve was out on a casevac when the assault went
        /// Black at the building, and the platoon aborted beside an intact base of fire against a Red line down to 1–2 men a platoon. −1 = nobody:
        /// AT-10 breaks off.
        /// </summary>
        private static unsafe int FreeRelief(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int r = FreeReserve(ref p, self, world);
            if (r >= 0) return r;
            int n = Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity);
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != PlatoonAttackPlan.Support || p.Casevac[s] != 0 || SlotLost(self, world, s)) continue;
                return s;
            }
            return -1;
        }

        private static unsafe int FreeReserve(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity);
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != PlatoonAttackPlan.Reserve || p.Casevac[s] != 0 || SlotLost(self, world, s)) continue;
                return s;
            }
            return -1;
        }

        private static bool Alive(EntityRepository world, Entity self, int slot)
        {
            var squad = CommandPictureOps.Subordinate(world, self, slot);
            return !squad.IsNull && world.IsAlive(squad);
        }

        private static int RosterCount(EntityRepository world, Entity self)
            => world.HasComponent<UnitRoster>(self) ? world.GetComponentRO<UnitRoster>(self).Count : 0;

        private static readonly int AttackHash = BehaviorHash.FromName(BehaviorNames.PlatoonAttack);
        private static readonly int ReactHash = BehaviorHash.FromName(BehaviorNames.ReactToFireFromBuilding);

        /// <summary>⭐ AT-13 (as DA-55 for the defence) — the platoon's task is NOT an attack. <c>BasicPlatoonSop</c>'s hold row asks it: an
        /// attacking platoon expects contact — its base of fire is up for it and its control region relieves and breaks off — so pausing the
        /// task to order every squad <c>Hold</c> would stop the attack at its first SPOT (measured live, <c>pc-defend-house</c>).</summary>
        [SharedAiCondition]
        public static bool NotAttacking(Entity self, EntityRepository world)
            => !world.HasComponent<BehaviorState>(self)
               || (world.GetComponentRO<BehaviorState>(self).ActiveBehaviorHash is var h && h != AttackHash && h != ReactHash);   // ⭐ CE-2284 — the preset too
    }

    /// <summary>
    /// ⭐ <c>CE-2305</c> RQ-3/RQ-4, shared by <c>CE-2277</c> AT-11 — answering a Casevac with a squad: the oldest Casevac request in the
    /// picture is answered — the squad <c>SquadRecover</c>s its casualty to <c>to</c> and puts him down (a request naming nobody is answered with
    /// no order); the squad is back once that task ended (PROGRESS Set or TaskFailed). One rule for <c>PlatoonDefend</c> and <c>PlatoonAttack</c>.
    /// </summary>
    public static class CasevacAnswers
    {
        /// <summary>True when <paramref name="slot"/>'s squad was ordered (it is now out on the casevac).</summary>
        public static bool Answer(EntityRepository world, Entity self, int slot, Vector3 to, float speed)
        {
            int index = CommandPictureOps.FirstRequest(world, self, RequestKind.Casevac);
            if (index < 0) return false;
            int casualty = world.GetComponentRO<CommandPicture>(self).Requests.Items[index].SubjectNetId;
            CommandPictureOps.Answered(world, self, index);
            if (casualty == 0) return false;
            var unit = CommandPictureOps.Subordinate(world, self, slot);
            if (unit.IsNull) return false;
            var order = new SquadRecoverParamsJsonDto { Casualty = new Fdp.Toolkit.Replication.EntityRef(casualty), To = to, PutDown = true, Speed = speed };
            CommandOrders.Assign(world, self, unit, BehaviorNames.SquadRecover,
                                 JsonSerializer.Serialize(order, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), newOrder: true);
            return true;
        }

        /// <summary>The squad's casevac task ended (its PROGRESS Set or TaskFailed).</summary>
        public static bool Returned(EntityRepository world, Entity self, int slot)
        {
            var progress = CommandPictureOps.ProgressOf(world, self, slot);
            return progress == ProgressKind.Set || progress == ProgressKind.TaskFailed;
        }
    }
}
