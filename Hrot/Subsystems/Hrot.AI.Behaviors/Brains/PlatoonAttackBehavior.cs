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
        public double AskedAt;
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
        public Vector3 SupportAt;
        public Vector3 AssaultAt;
        public Vector3 ReserveAt;

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
        public const float FlankDegrees = 70f, SupportArcHalfDegrees = 25f, SupportRangeMetres = 220f;
        public const float SupportPostRadius = 25f, ConsolidateRadius = 60f;
        public const float ElementFreshSeconds = 120f;
        public const int AreaSite = 0x22770001;
        public const float SurveyTimeoutSeconds = 5f;
        /// <summary>⭐ UM-12 — the reaction's search round the firing element (m), and how fresh that element must be (s).</summary>
        public const float ReactSearchRadius = 15f, ReactFreshSeconds = 30f;
        public const BehaviorFaultCode AbortedFault = BehaviorFaultCode.Custom + 277;

        [StructLayout(LayoutKind.Sequential)]
        public struct PlatoonAttackBlackboard
        {
            public PlatoonAttackPlan Plan;
            public PlatoonAttackState Keep;
        }

        [BehaviorResolver(BehaviorNames.PlatoonAttack)]
        public static void Resolve(in PlatoonAttackParamsJsonDto authored, ref PlatoonAttackBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            block.Plan.Position = authored.Position;
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

        [Behavior(BehaviorNames.ReactToFireFromBuilding, Params = typeof(NoParams), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.ReactToFireFromBuilding, Curated = true, Group = "Library/Platoon", ParamsType = typeof(PlatoonAttackBlackboard))]
        public static StateMachineGraph BuildReactToFireFromBuilding() => Build("ReactToFireFromBuilding_HSM");

        [Behavior(BehaviorNames.PlatoonAttack, Params = typeof(PlatoonAttackParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.PlatoonAttack, Curated = true, Group = "Library/Platoon", ParamsType = typeof(PlatoonAttackBlackboard))]
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
                if (!FiringElement(world, self, (float)now, out var at, out p.Reporter)) { p.Made = 1; p.Objectives = 0; return NodeStatus.Running; }
                p.Position = at;
            }
            if (p.Asked == 0)
            {
                WorldSurveyChildSensor.Ensure(world, self, AreaSite, WorldSurveyRequest.OfArea(new Vector2(p.Position.X, p.Position.Y), p.SearchRadius));
                p.Asked = 1;
                p.AskedAt = now;
                return NodeStatus.Running;
            }
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, AreaSite);
            var read = WorldSurveyChildSensor.Read(world, child, now);
            bool ready = read.Ready && !read.Stale && read.Payload != null;
            if (!ready && now - p.AskedAt < SurveyTimeoutSeconds) return NodeStatus.Running;
            p.Start = StaffComposerSystem.CentreOf(world, self);
            var objectives = AttackObjectives.Plan(ready ? read.Payload : null, Elements(world, self, (float)now), p.Position, p.SearchRadius,
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
        public static bool FiringElement(EntityRepository world, Entity self, float now, out Vector3 at, out int slot)
        {
            at = default; slot = -1;
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            float newest = float.NegativeInfinity;
            long from = 0;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || e.Activity != ElementActivity.Firing || now - e.ObservedAt > ReactFreshSeconds || e.ObservedAt <= newest) continue;
                newest = e.ObservedAt; at = e.At; from = e.FromNetId;
            }
            if (float.IsNegativeInfinity(newest)) return false;
            int n = RosterCount(world, self);
            for (int s = 0; s < n; s++)
                if (CommandPictureOps.NetIdOf(world, CommandPictureOps.Subordinate(world, self, s)) == from) { slot = s; break; }
            return true;
        }

        private static List<Vector3> Elements(EntityRepository world, Entity self, float now)
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
            if (p.Preset == (byte)PlatoonAttackPreset.ReactToFireFromBuilding)   // UM-12 — the squad in contact supports from where it stands
            {
                int s = p.SlotOf(PlatoonAttackPlan.Support, PlatoonAttackPlan.Capacity);
                if (s >= 0) p.SupportAt = StaffComposerSystem.CentreOf(world, CommandPictureOps.Subordinate(world, self, s));
            }
            SetPhase(ref p, PlatoonAttackPlan.PhaseSupport);
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

        /// <summary>AT-6 — the lift: the support only returns fire now; then the assault goes in.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus EnterAssault(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, PlatoonAttackPlan.PhaseAssault);
            Fire(world, self, p.SlotOf(PlatoonAttackPlan.Support, PlatoonAttackPlan.Capacity), RoeFire.ReturnFire);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus NextObjective(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            p.Current++;
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static unsafe NodeStatus EnterConsolidate(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, PlatoonAttackPlan.PhaseConsolidate);
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
                if (p.Dirty[slot] == 0 && CommandOrders.Stands(world, self, squad)) continue;
                p.Dirty[slot] = 0;
                if (!OrderFor(ref p, slot, json, out var behaviour, out var order)) continue;
                // ⭐ CE-2346 (R-2066 D) — a phase change re-dirties every slot; the support's SquadDefend is the same order in the next
                //   phase, and re-sending it restarted the squad from "move to the point" (measured: men walked back round the berm)
                if (CommandOrders.StillRunning(world, self, squad, behaviour, order)) continue;
                CommandOrders.Assign(world, self, squad, behaviour, order, newOrder: true);
                if (behaviour == BehaviorNames.SquadDefend) Sector(ref p, slot, world, squad);
                ws.Orders++;
            }
            return NodeStatus.Running;
        }

        /// <summary>The order a slot of <paramref name="slot"/>'s role gets in the plan's phase (false = none: it keeps what it has).</summary>
        private static unsafe bool OrderFor(ref PlatoonAttackPlan p, int slot, JsonSerializerOptions json, out string behaviour, out string order)
        {
            behaviour = order = "";
            byte role = p.Role[slot];
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            switch (p.Phase)
            {
                case PlatoonAttackPlan.PhaseSupport:
                case PlatoonAttackPlan.PhaseApproach:
                case PlatoonAttackPlan.PhaseAssault:
                    if (role == PlatoonAttackPlan.Support && p.Preset != (byte)PlatoonAttackPreset.ClearBuilding)
                        return Defend(p.SupportAt, SupportPostRadius, p.Speed, json, out behaviour, out order);
                    if (role == PlatoonAttackPlan.Assault && p.Phase == PlatoonAttackPlan.PhaseAssault)
                    {
                        if (p.OBuilding[p.Current] != 0)
                        {
                            behaviour = BehaviorNames.ClearBuilding;
                            order = JsonSerializer.Serialize(new ClearBuildingParamsJsonDto { Building = o, Speed = p.Speed }, json);
                        }
                        else
                        {
                            behaviour = BehaviorNames.FireAndMovement;
                            order = JsonSerializer.Serialize(new FireAndMovementParamsJsonDto { Objective = o, Speed = p.Speed }, json);
                        }
                        return true;
                    }
                    if (p.Phase == PlatoonAttackPlan.PhaseSupport) return false;   // the assault and the reserve wait for the base of fire
                    if (role == PlatoonAttackPlan.Assault)
                        return Bound(p.AssaultAt, p.Speed, json, out behaviour, out order);
                    if (role == PlatoonAttackPlan.Reserve && p.Phase == PlatoonAttackPlan.PhaseApproach)
                        return Bound(p.ReserveAt, p.Speed, json, out behaviour, out order);
                    return false;
                case PlatoonAttackPlan.PhaseConsolidate:
                    return Defend(o, ConsolidateRadius, p.Speed, json, out behaviour, out order);
                case PlatoonAttackPlan.PhaseAbort:
                    behaviour = BehaviorNames.BreakContact;
                    order = JsonSerializer.Serialize(new BreakContactParamsJsonDto { To = p.Start, Speed = p.Speed }, json);
                    return true;
                default:
                    return false;
            }
        }

        private static bool Defend(Vector3 at, float radius, float speed, JsonSerializerOptions json, out string behaviour, out string order)
        {
            behaviour = BehaviorNames.SquadDefend;
            order = JsonSerializer.Serialize(new SquadDefendParamsJsonDto { Position = at, SearchRadius = radius, Speed = speed }, json);
            return true;
        }

        private static bool Bound(Vector3 to, float speed, JsonSerializerOptions json, out string behaviour, out string order)
        {
            behaviour = BehaviorNames.BoundingOverwatch;
            order = JsonSerializer.Serialize(new BoundingOverwatchParamsJsonDto { Objective = to, Speed = speed }, json);
            return true;
        }

        /// <summary>AT-4 — the support's sector: the objective ± <see cref="SupportArcHalfDegrees"/> from its point; every other order none.</summary>
        private static unsafe void Sector(ref PlatoonAttackPlan p, int slot, EntityRepository world, Entity squad)
        {
            if (!world.Bus.IsRegistered<SetEngagementAreaEvent>()) return;
            if (p.Role[slot] != PlatoonAttackPlan.Support || p.Phase == PlatoonAttackPlan.PhaseConsolidate)
            {
                EngagementAreas.WithdrawSuperior(world, squad);
                return;
            }
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            float bearing = SectorSplit.Norm(MathF.Atan2(o.X - p.SupportAt.X, o.Y - p.SupportAt.Y) * (180f / MathF.PI));
            world.Bus.Publish(new SetEngagementAreaEvent
            {
                Entity = squad, Origin = BehaviorOrigin.Superior, ArcSet = 1, ArcOrigin = p.SupportAt,
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
            => p.Made != 0 && p.Aborted == 0 && AssaultBlack(ref p, self, world) && FreeReserve(ref p, self, world) >= 0;

        /// <summary>AWatch → AAbort (AT-10): the assault reports Black and no reserve is left.</summary>
        [SharedAiCondition]
        public static bool AssaultLost(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
            => p.Made != 0 && p.Aborted == 0 && p.Phase != PlatoonAttackPlan.PhaseConsolidate
               && AssaultBlack(ref p, self, world) && FreeReserve(ref p, self, world) < 0;

        /// <summary>ARelieve's entry: the free reserve becomes the assault, the spent assault a reserve; both ordered again.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus Relieve(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity);
            int a = p.SlotOf(PlatoonAttackPlan.Assault, n), r = FreeReserve(ref p, self, world);
            if (a < 0 || r < 0) return NodeStatus.Success;
            p.Role[a] = PlatoonAttackPlan.Reserve;
            p.Role[r] = PlatoonAttackPlan.Assault;
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
                ? world.GetComponentRO<SimTransform>(unit).Position : p.ReserveAt;
            if (CasevacAnswers.Answer(world, self, reserve, to, p.Speed)) p.Casevac[reserve] = 1;
            return NodeStatus.Success;
        }

        [SharedAiCondition]
        public static bool Handled(ref PlatoonAttackPlan p, Entity self, EntityRepository world) => true;

        private static unsafe bool AssaultBlack(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int a = p.SlotOf(PlatoonAttackPlan.Assault, Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity));
            if (a < 0) return false;
            if (!Alive(world, self, a)) return true;
            var colour = new SubordinateColourParams { Slot = a, AtLeast = StatusColour.Black };
            return PictureConditions.SubordinateColour(ref colour, self, world);
        }

        private static unsafe int FreeReserve(ref PlatoonAttackPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonAttackPlan.Capacity);
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != PlatoonAttackPlan.Reserve || p.Casevac[s] != 0 || !Alive(world, self, s)) continue;
                var colour = new SubordinateColourParams { Slot = s, AtLeast = StatusColour.Black };
                if (!PictureConditions.SubordinateColour(ref colour, self, world)) return s;
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
            long casualty = world.GetComponentRO<CommandPicture>(self).Requests.Items[index].SubjectNetId;
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
