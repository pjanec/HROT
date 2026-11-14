using System;
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
using Fdp.Toolkit.Squad.Command;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-7023</c> / <c>CE-7005</c> (City Battle §6, §11 C-1b, C-4) — the company's attack plan: up to <see cref="MaxObjectives"/>
    /// objectives taken in order, each roster slot's role, the phase, the current objective's base-of-fire and reserve points, and the tempo
    /// clock (when the objective was started, which escalation step is done). Mirrors <see cref="PlatoonAttackPlan"/> one level up.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CompanyAttackPlan
    {
        public const int Capacity = CommandPicture.MaxSubordinates;
        public const int MaxObjectives = 4;
        public const byte NoRole = 0, Assault = 1, Support = 2, Reserve = 3;
        public const byte PhasePlan = 0, PhaseSupport = 1, PhaseAssault = 2, PhaseConsolidate = 3, PhaseAbort = 4;

        public float Radius;
        public float Speed;
        public float Deadline;
        public byte Made;
        public byte Phase;
        public byte Aborted;
        public Vector3 Start;
        public int Objectives;
        public int Current;
        public fixed float OX[MaxObjectives];
        public fixed float OY[MaxObjectives];
        public fixed float OZ[MaxObjectives];
        public fixed byte Role[Capacity];
        public fixed byte Dirty[Capacity];
        public Vector3 SupportAt;
        public Vector3 ReserveAt;
        /// <summary>⭐ C-4 — when the current objective's attack began (sim s), and the escalation steps taken on it (0 none · 1 fires asked ·
        /// 2 the reserve committed · 3 bypassed).</summary>
        public SimInstant ObjectiveSince;
        public byte Escalation;

        public Vector3 Objective(int i) => new(OX[i], OY[i], OZ[i]);

        public int SlotOf(byte role, int n)
        {
            for (int s = 0; s < n && s < Capacity; s++) if (Role[s] == role) return s;
            return -1;
        }
    }

    /// <summary>⭐ <c>CE-7023</c> — what <see cref="CompanyAttackBehavior.KeepAttack"/> keeps: how many orders it gave, how many fire requests.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CompanyAttackState
    {
        public int Orders;
        public int FireRequests;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-7023</c> (City Battle §6, §7, §11 C-1b) — <c>CompanyAttack</c>: the company takes its objectives (the phase lines) in order over
    /// its PLATOONS. Slot 0 assaults, slot 1 is the base of fire, slot 2 the reserve (a fourth slot — the support element — keeps its own task).
    /// CSupport puts the base of fire up 250 m short of the objective (<c>PlatoonDefend</c>, its arc on the objective) and the reserve 400 m short;
    /// on the base of fire's Set, CAssault sends the assault platoon in (<c>PlatoonAttack</c> on the objective's area); its Set ⇒ the next
    /// objective (the base of fire moves up); the last one taken ⇒ CConsolidate (every platoon defends the objective) ⇒ CWon. The watch region
    /// relieves a failing assault (TaskFailed or Black) with a fresh reserve, and breaks the attack off when none is left.
    /// ⭐ <c>CE-7005</c> tempo (§3a): every objective has a deadline — at half of it the company asks for fire support on the objective, at all
    /// of it the reserve is committed beside the assault, at 1.5 × it the objective is bypassed for the next (the last one is never bypassed).
    /// Nothing here reads another brain — only the company's picture (what its platoons reported).
    /// </summary>
    public static class CompanyAttackBehavior
    {
        public const float DefaultRadius = 80f, DefaultDeadlineSeconds = 480f;
        public const float SupportStandOffMetres = 250f, ReserveStandOffMetres = 400f, SupportArcHalfDegrees = 30f, SupportRangeMetres = 450f;
        public const float ConsolidateRadius = 80f;
        public const BehaviorFaultCode AbortedFault = BehaviorFaultCode.Custom + 7023;

        [StructLayout(LayoutKind.Sequential)]
        public struct CompanyAttackBlackboard
        {
            public CompanyAttackPlan Plan;
            public CompanyAttackState Keep;
        }

        [BehaviorResolver(BehaviorNames.CompanyAttack)]
        public static unsafe void Resolve(in CompanyAttackParamsJsonDto authored, ref CompanyAttackBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            block.Plan.Radius = authored.Radius > 0f ? authored.Radius : DefaultRadius;
            block.Plan.Speed = authored.Speed;
            block.Plan.Deadline = authored.DeadlineSeconds > 0f ? authored.DeadlineSeconds : DefaultDeadlineSeconds;
            Vector3 o1 = authored.Objective1, o2 = authored.Objective2, o3 = authored.Objective3, o4 = authored.Objective4;
            int n = 0;
            if (o1 != Vector3.Zero) Put(ref block.Plan, n++, o1);
            if (n > 0 && o2 != Vector3.Zero) Put(ref block.Plan, n++, o2);
            if (n > 1 && o3 != Vector3.Zero) Put(ref block.Plan, n++, o3);
            if (n > 2 && o4 != Vector3.Zero) Put(ref block.Plan, n++, o4);
            block.Plan.Objectives = n;
        }

        private static unsafe void Put(ref CompanyAttackPlan p, int i, Vector3 o) { p.OX[i] = o.X; p.OY[i] = o.Y; p.OZ[i] = o.Z; }

        [Behavior(BehaviorNames.CompanyAttack, Params = typeof(CompanyAttackParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.CompanyAttack, Curated = true, Group = "Library/Orders", ParamsType = typeof(CompanyAttackBlackboard))]
        public static StateMachineGraph BuildCompanyAttack()
        {
            var builder = new HsmBuilder("CompanyAttack_HSM");
            var root = builder.State("Attacking").Initial().Parallel();
            StateBuilder plan = null!, support = null!, assault = null!, next = null!, consolidate = null!, breaking = null!, broke = null!;
            StateBuilder watch = null!, relieve = null!, abort = null!;
            root.Child("CPlan", c => plan = c.InRegion(0).Initial()
                .Activity<CompanyAttackBlackboard, CompanyAttackPlan, CompanyAttackState>(bb => bb.Plan, bb => bb.Keep, PlanAttack));
            root.Child("CSupport", c => support = c.InRegion(0)
                .OnEntry<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, EnterSupport)
                .Activity<CompanyAttackBlackboard, CompanyAttackPlan, CompanyAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("CAssault", c => assault = c.InRegion(0)
                .OnEntry<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, EnterAssault)
                .Activity<CompanyAttackBlackboard, CompanyAttackPlan, CompanyAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("CNext", c => next = c.InRegion(0).OnEntry<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, NextObjective));
            root.Child("CConsolidate", c => consolidate = c.InRegion(0)
                .OnEntry<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, EnterConsolidate)
                .Activity<CompanyAttackBlackboard, CompanyAttackPlan, CompanyAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("CWon", c => c.InRegion(0).Final());
            root.Child("CBreaking", c => breaking = c.InRegion(0)
                .OnEntry<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, EnterBreaking)
                .Activity<CompanyAttackBlackboard, CompanyAttackPlan, CompanyAttackState>(bb => bb.Plan, bb => bb.Keep, KeepAttack));
            root.Child("CBroke", c => broke = c.InRegion(0).OnEntry<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, Broke));
            root.Child("CEnd", c => c.InRegion(0).Final());
            root.Child("CWatch", c => watch = c.InRegion(1).Initial());
            root.Child("CRelieve", c => relieve = c.InRegion(1).OnEntry<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, Relieve));
            root.Child("CAbort", c => abort = c.InRegion(1).OnEntry<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, Abort));

            plan.On(0).GoTo("CWon").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, NothingToAttack).Polled();
            plan.On(0).GoTo("CSupport").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, Planned).Polled();
            foreach (var st in new[] { support, assault, consolidate })
                st.On(0).GoTo("CBreaking").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, IsAborted).Polled();
            support.On(0).GoTo("CAssault").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, SupportSet).Polled();
            assault.On(0).GoTo("CNext").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, ObjectiveTaken).Polled();
            next.On(0).GoTo("CSupport").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, MoreObjectives).Polled();
            next.On(0).GoTo("CConsolidate").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, NoMoreObjectives).Polled();
            consolidate.On(0).GoTo("CWon").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, AllSet).Polled();
            breaking.On(0).GoTo("CBroke").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, AllSet).Polled();
            broke.On(0).GoTo("CEnd").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, Handled).Polled();
            watch.On(0).GoTo("CAbort").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, AssaultLost).Polled();
            watch.On(0).GoTo("CRelieve").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, AssaultFailing).Polled();
            relieve.On(0).GoTo("CWatch").Guard<CompanyAttackBlackboard, CompanyAttackPlan>(bb => bb.Plan, Handled).Polled();
            return builder.Build();
        }

        // ── the plan ─────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CPlan's activity: the roles (slot 0 assault, 1 base of fire, 2 reserve) and the start (the company's centre).</summary>
        [SharedAiAction]
        public static unsafe NodeStatus PlanAttack(ref CompanyAttackPlan p, ref CompanyAttackState ws, Entity self, EntityRepository world)
        {
            if (p.Made != 0) return NodeStatus.Running;
            int n = RosterCount(world, self);
            if (n == 0) return NodeStatus.Running;
            p.Start = StaffComposerSystem.CentreOf(world, self);
            for (int s = 0; s < n && s < CompanyAttackPlan.Capacity; s++)
                p.Role[s] = s == 0 ? CompanyAttackPlan.Assault : s == 1 ? CompanyAttackPlan.Support : s == 2 ? CompanyAttackPlan.Reserve : CompanyAttackPlan.NoRole;
            p.Made = 1;
            return NodeStatus.Running;
        }

        // ── the phases ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CSupport's entry: the current objective's points, its clock started, every role re-ordered.</summary>
        [SharedAiAction]
        public static NodeStatus EnterSupport(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            Points(ref p);
            p.ObjectiveSince = (SimInstant)CommandPictureOps.Now(world);
            p.Escalation = 0;
            SetPhase(ref p, CompanyAttackPlan.PhaseSupport);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus EnterAssault(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, CompanyAttackPlan.PhaseAssault);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus NextObjective(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            p.Current++;
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus EnterConsolidate(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            p.Current = Math.Max(0, p.Objectives - 1);
            SetPhase(ref p, CompanyAttackPlan.PhaseConsolidate);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus EnterBreaking(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, CompanyAttackPlan.PhaseAbort);
            return NodeStatus.Success;
        }

        /// <summary>The company is back on its start: the run ends FAILED.</summary>
        [SharedAiAction]
        public static NodeStatus Broke(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            BehaviorFault.Raise(world, self, AbortedFault, "company attack broken off: no platoon left to assault");
            return NodeStatus.Success;
        }

        private static unsafe void SetPhase(ref CompanyAttackPlan p, byte phase)
        {
            p.Phase = phase;
            for (int s = 0; s < CompanyAttackPlan.Capacity; s++) if (p.Role[s] != CompanyAttackPlan.NoRole) p.Dirty[s] = 1;
        }

        /// <summary>The base of fire stands <see cref="SupportStandOffMetres"/> short of the objective on the line from where the company came
        /// from (the start, then the objective before it); the reserve <see cref="ReserveStandOffMetres"/> short.</summary>
        private static void Points(ref CompanyAttackPlan p)
        {
            var o = p.Objective(p.Current);
            var from = p.Current == 0 ? p.Start : p.Objective(p.Current - 1);
            var back = new Vector2(from.X - o.X, from.Y - o.Y);
            var dir = back.LengthSquared() > 1e-4f ? Vector2.Normalize(back) : -Vector2.UnitY;
            p.SupportAt = o + new Vector3(dir * SupportStandOffMetres, 0f);
            p.ReserveAt = o + new Vector3(dir * ReserveStandOffMetres, 0f);
        }

        /// <summary>The ONE order writer: a slot whose order of this phase does not stand, or that is dirty, is ordered again; and the tempo
        /// clock (C-4) is checked here, once per tick of the phase.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus KeepAttack(ref CompanyAttackPlan p, ref CompanyAttackState ws, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return NodeStatus.Running;
            Tempo(ref p, ref ws, self, world);
            int n = Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity);
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            for (int slot = 0; slot < n; slot++)
            {
                if (p.Role[slot] == CompanyAttackPlan.NoRole) continue;
                var platoon = CommandPictureOps.Subordinate(world, self, slot);
                if (platoon.IsNull || !world.IsAlive(platoon)) continue;
                if (p.Dirty[slot] == 0 && CommandOrders.Stands(world, self, platoon)) continue;
                p.Dirty[slot] = 0;
                if (!OrderFor(ref p, slot, json, out var behaviour, out var order)) continue;
                if (CommandOrders.StillRunning(world, self, platoon, behaviour, order)) continue;
                CommandOrders.Assign(world, self, platoon, behaviour, order, newOrder: true);
                ws.Orders++;
            }
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-7005</c> (§3a, tempo) — the objective's deadline: at ½ ask for fire support on it (once), at 1× commit the reserve
        /// beside the assault (once), at 1.5× bypass it for the next objective (never the last). Only in CSupport / CAssault.</summary>
        private static unsafe void Tempo(ref CompanyAttackPlan p, ref CompanyAttackState ws, Entity self, EntityRepository world)
        {
            if (p.Phase != CompanyAttackPlan.PhaseSupport && p.Phase != CompanyAttackPlan.PhaseAssault) return;
            double spent = CommandPictureOps.Now(world) - p.ObjectiveSince;
            if (p.Escalation < 1 && spent >= p.Deadline * 0.5)
            {
                p.Escalation = 1;
                if (AskFires(world, self, p.Objective(p.Current), FireSupportRound.Smoke)) ws.FireRequests++;   // ⭐ CE-7025 — smoke on it first
            }
            if (p.Escalation < 2 && spent >= p.Deadline)
            {
                p.Escalation = 2;
                int n = Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity);
                int r = FreeReserve(ref p, self, world, n);
                if (r >= 0) { p.Role[r] = CompanyAttackPlan.Assault; p.Dirty[r] = 1; }
            }
            if (p.Escalation < 3 && spent >= p.Deadline * 1.5 && p.Current < p.Objectives - 1)
            {
                p.Escalation = 3;
                p.Current++;
                Points(ref p);
                p.ObjectiveSince = (SimInstant)CommandPictureOps.Now(world);
                p.Escalation = 0;
                SetPhase(ref p, CompanyAttackPlan.PhaseSupport);
            }
        }

        /// <summary>The company's REQUEST FireSupport on <paramref name="at"/> with <paramref name="round"/> (smoke = the
        /// <see cref="ElementEquipment.Smoke"/> flag), up to its
        /// headquarters (the staff sends it after the report delay).</summary>
        public static bool AskFires(EntityRepository world, Entity self, Vector3 at, FireSupportRound round = FireSupportRound.HighExplosive)
        {
            SimInstant now = (SimInstant)CommandPictureOps.Now(world);
            return ReportQueue.Enqueue(world, self, new CommandReport
            {
                Kind = ReportKind.Request, Subtype = (byte)RequestKind.FireSupport, At = at, Radius = 50f, Count = 1,
                Equipment = round == FireSupportRound.Smoke ? ElementEquipment.Smoke : ElementEquipment.NoEquipment, ObservedAt = now,
            }, now + CommandNodes.ReportDelaySeconds);
        }

        /// <summary>The order a slot of <paramref name="slot"/>'s role gets in the plan's phase (false = none: it keeps what it has).</summary>
        private static unsafe bool OrderFor(ref CompanyAttackPlan p, int slot, JsonSerializerOptions json, out string behaviour, out string order)
        {
            behaviour = order = "";
            byte role = p.Role[slot];
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            switch (p.Phase)
            {
                case CompanyAttackPlan.PhaseSupport:
                case CompanyAttackPlan.PhaseAssault:
                    if (role == CompanyAttackPlan.Support) return Defend(p.SupportAt, o, SupportArcHalfDegrees, SupportRangeMetres, p.Speed, json, out behaviour, out order);
                    if (role == CompanyAttackPlan.Reserve) return Defend(p.ReserveAt, o, 0f, 0f, p.Speed, json, out behaviour, out order);
                    if (role == CompanyAttackPlan.Assault && p.Phase == CompanyAttackPlan.PhaseAssault)
                    {
                        behaviour = BehaviorNames.PlatoonAttack;
                        order = JsonSerializer.Serialize(new PlatoonAttackParamsJsonDto { Position = o, SearchRadius = p.Radius, Speed = p.Speed }, json);
                        return true;
                    }
                    return false;   // the assault waits for the base of fire
                case CompanyAttackPlan.PhaseConsolidate:
                    return Defend(o, o, 0f, 0f, p.Speed, json, out behaviour, out order);
                case CompanyAttackPlan.PhaseAbort:
                    return Defend(p.Start, p.Start, 0f, 0f, p.Speed, json, out behaviour, out order);
                default:
                    return false;
            }
        }

        /// <summary>A <c>PlatoonDefend</c> at <paramref name="at"/>; an arc ± <paramref name="halfArc"/> toward <paramref name="toward"/> when
        /// given (the base of fire's), else the platoon's own sector.</summary>
        private static bool Defend(Vector3 at, Vector3 toward, float halfArc, float range, float speed, JsonSerializerOptions json, out string behaviour, out string order)
        {
            behaviour = BehaviorNames.PlatoonDefend;
            var dto = new PlatoonDefendParamsJsonDto { Position = at, Speed = speed };
            if (halfArc > 0f)
            {
                float bearing = SectorSplit.Norm(MathF.Atan2(toward.X - at.X, toward.Y - at.Y) * (180f / MathF.PI));
                dto.Left = (Bearing)SectorSplit.Norm(bearing - halfArc);
                dto.Right = (Bearing)SectorSplit.Norm(bearing + halfArc);
                dto.MaxRange = range;
            }
            order = JsonSerializer.Serialize(dto, json);
            return true;
        }

        // ── the guards ───────────────────────────────────────────────────────────────────────────────────────────────

        [SharedAiCondition]
        public static bool Planned(ref CompanyAttackPlan p, Entity self, EntityRepository world) => p.Made != 0 && p.Objectives > 0;

        [SharedAiCondition]
        public static bool NothingToAttack(ref CompanyAttackPlan p, Entity self, EntityRepository world) => p.Made != 0 && p.Objectives == 0;

        [SharedAiCondition]
        public static bool IsAborted(ref CompanyAttackPlan p, Entity self, EntityRepository world) => p.Aborted != 0;

        /// <summary>CSupport → CAssault: the base of fire reports its line Set — or there is none (a two-platoon company keeps a reserve? no:
        /// slot 1 is the base of fire; one platoon ⇒ none).</summary>
        [SharedAiCondition]
        public static unsafe bool SupportSet(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            int s = p.SlotOf(CompanyAttackPlan.Support, Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity));
            return s < 0 || !Alive(world, self, s) || CommandPictureOps.ProgressOf(world, self, s) == ProgressKind.Set;
        }

        /// <summary>CAssault → CNext: an assault platoon reports its attack done (Set) — a committed reserve's Set counts too.</summary>
        [SharedAiCondition]
        public static unsafe bool ObjectiveTaken(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity);
            for (int s = 0; s < n; s++)
                if (p.Role[s] == CompanyAttackPlan.Assault && Alive(world, self, s) && CommandPictureOps.ProgressOf(world, self, s) == ProgressKind.Set) return true;
            return false;
        }

        [SharedAiCondition]
        public static bool MoreObjectives(ref CompanyAttackPlan p, Entity self, EntityRepository world) => p.Current < p.Objectives;

        [SharedAiCondition]
        public static bool NoMoreObjectives(ref CompanyAttackPlan p, Entity self, EntityRepository world) => p.Current >= p.Objectives;

        /// <summary>Every live platoon with a role reports Set on its current order (consolidation, or the way back).</summary>
        [SharedAiCondition]
        public static unsafe bool AllSet(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity), live = 0;
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] == CompanyAttackPlan.NoRole || !Alive(world, self, s)) continue;
                if (CommandPictureOps.ProgressOf(world, self, s) != ProgressKind.Set) return false;
                live++;
            }
            return live > 0;
        }

        // ── the control region ───────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CWatch → CRelieve: the assault failed (TaskFailed or Black) in the assault phase and a reserve is free.</summary>
        [SharedAiCondition]
        public static bool AssaultFailing(ref CompanyAttackPlan p, Entity self, EntityRepository world)
            => p.Made != 0 && p.Aborted == 0 && p.Phase == CompanyAttackPlan.PhaseAssault && AssaultFailed(ref p, self, world)
               && FreeReserve(ref p, self, world, Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity)) >= 0;

        /// <summary>CWatch → CAbort: the assault failed and no reserve is left.</summary>
        [SharedAiCondition]
        public static bool AssaultLost(ref CompanyAttackPlan p, Entity self, EntityRepository world)
            => p.Made != 0 && p.Aborted == 0 && p.Phase == CompanyAttackPlan.PhaseAssault && AssaultFailed(ref p, self, world)
               && FreeReserve(ref p, self, world, Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity)) < 0;

        /// <summary>CRelieve's entry: the free reserve becomes the assault, every failed assault a spent reserve (NoRole: it consolidates later).</summary>
        [SharedAiAction]
        public static unsafe NodeStatus Relieve(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity);
            int r = FreeReserve(ref p, self, world, n);
            if (r < 0) return NodeStatus.Success;
            for (int s = 0; s < n; s++)
                if (p.Role[s] == CompanyAttackPlan.Assault && Failed(world, self, s)) { p.Role[s] = CompanyAttackPlan.Support; p.Dirty[s] = 1; }
            p.Role[r] = CompanyAttackPlan.Assault;
            p.Dirty[r] = 1;
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus Abort(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            p.Aborted = 1;
            return NodeStatus.Success;
        }

        [SharedAiCondition]
        public static bool Handled(ref CompanyAttackPlan p, Entity self, EntityRepository world) => true;

        /// <summary>Every live assault platoon has failed (none left assaulting).</summary>
        private static unsafe bool AssaultFailed(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), CompanyAttackPlan.Capacity), assaults = 0;
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != CompanyAttackPlan.Assault) continue;
                assaults++;
                if (!Failed(world, self, s)) return false;
            }
            return assaults > 0;
        }

        private static bool Failed(EntityRepository world, Entity self, int slot)
        {
            if (!Alive(world, self, slot)) return true;
            if (CommandPictureOps.ProgressOf(world, self, slot) == ProgressKind.TaskFailed) return true;
            var colour = new SubordinateColourParams { Slot = slot, AtLeast = StatusColour.Black };
            return PictureConditions.SubordinateColour(ref colour, self, world);
        }

        private static unsafe int FreeReserve(ref CompanyAttackPlan p, Entity self, EntityRepository world, int n)
        {
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != CompanyAttackPlan.Reserve || !Alive(world, self, s)) continue;
                var colour = new SubordinateColourParams { Slot = s, AtLeast = StatusColour.Black };
                if (!PictureConditions.SubordinateColour(ref colour, self, world)) return s;
            }
            return -1;
        }

        internal static bool Alive(EntityRepository world, Entity self, int slot)
        {
            var unit = CommandPictureOps.Subordinate(world, self, slot);
            return !unit.IsNull && world.IsAlive(unit);
        }

        internal static int RosterCount(EntityRepository world, Entity self)
            => world.HasComponent<UnitRoster>(self) ? world.GetComponentRO<UnitRoster>(self).Count : 0;
    }
}

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-7024</c> (City Battle §6, §11 C-1c) — the company's defence plan: the line (its middle, the direction the threat comes from,
    /// frontage, depth), each slot's role and point, whether the company has fallen back, which reserve is committed where.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CompanyDefencePlan
    {
        public const int Capacity = CommandPicture.MaxSubordinates;
        public const byte NoRole = 0, Line = 1, Reserve = 2;

        public Vector3 Position;
        /// <summary>Unit vector (x, y) toward the threat.</summary>
        public Vector2 Toward;
        public float Frontage;
        public float Depth;
        public float Speed;
        /// <summary>⭐ <c>CE-7007</c> — shoot and move handed down (s; ≤ 0 off).</summary>
        public float ShootAndMove;
        public byte Made;
        /// <summary>1 once the company fell back to its next line.</summary>
        public byte Falling;
        public fixed byte Role[Capacity];
        public fixed byte Dirty[Capacity];
        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];
        /// <summary>The block a committed reserve went to (−1 none).</summary>
        public int ReserveTo;
        /// <summary>⭐ <c>CE-7004</c> — the reserve's slot sent to RETAKE a lost block (−1 none); <see cref="ReserveTo"/> is that block.</summary>
        public int CounterBy;
        /// <summary>1 once the company counterattacked (once per defence).</summary>
        public byte Countered;
        /// <summary>1 once the counterattack took the block back (its Set): the reserve then defends it.</summary>
        public byte Retaken;

        public Vector3 Point(int slot) => new(X[slot], Y[slot], Z[slot]);
        public void SetPoint(int slot, Vector3 at) { X[slot] = at.X; Y[slot] = at.Y; Z[slot] = at.Z; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CompanyDefendState
    {
        public int Orders;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-7024</c> (City Battle §6, §11 C-1c) — <c>CompanyDefend</c>: the company holds a line over its PLATOONS. With three or more the
    /// last is the reserve (half the depth behind the middle), every other a BLOCK (<c>PlatoonDefend</c>) spread evenly across the frontage,
    /// each with an arc of ± <see cref="BlockArcHalfDegrees"/>° on the threat and its own fall-back point <see cref="CompanyDefencePlan.Depth"/>
    /// behind. The watch region reinforces the first failing block (TaskFailed, Black, or falling back) with the reserve; when HALF the blocks
    /// fail and the reserve is already in (or there is none) the whole company falls back to the next line (once) — the platoon's <c>HalfTheLineFailing</c> rule one level up. The defence
    /// never ends by itself (a new order replaces it). Nothing here reads another brain — only the picture.
    /// </summary>
    public static class CompanyDefendBehavior
    {
        public const float DefaultFrontage = 300f, DefaultDepth = 250f, BlockArcHalfDegrees = 40f;
        /// <summary>⭐ <c>CE-7007</c> (lane default R-7002) — a company's defence shoots and moves by default (the showcase's "never just sitting").</summary>
        public const float DefaultShootAndMoveSeconds = 25f;

        [StructLayout(LayoutKind.Sequential)]
        public struct CompanyDefendBlackboard
        {
            public CompanyDefencePlan Plan;
            public CompanyDefendState Keep;
        }

        [BehaviorResolver(BehaviorNames.CompanyDefend)]
        public static void Resolve(in CompanyDefendParamsJsonDto authored, ref CompanyDefendBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            Vector3 at = authored.Position, threat = authored.Threat;
            block.Plan.Position = at;
            var d = threat == Vector3.Zero ? Vector2.UnitY : new Vector2(threat.X - at.X, threat.Y - at.Y);
            block.Plan.Toward = d.LengthSquared() > 1e-4f ? Vector2.Normalize(d) : Vector2.UnitY;
            block.Plan.Frontage = authored.Frontage > 0f ? authored.Frontage : DefaultFrontage;
            block.Plan.Depth = authored.Depth > 0f ? authored.Depth : DefaultDepth;
            block.Plan.Speed = authored.Speed;
            block.Plan.ShootAndMove = authored.ShootAndMoveSeconds == 0f ? DefaultShootAndMoveSeconds : authored.ShootAndMoveSeconds;   // ⭐ CE-7007
            block.Plan.ReserveTo = -1;
            block.Plan.CounterBy = -1;
        }

        [Behavior(BehaviorNames.CompanyDefend, Params = typeof(CompanyDefendParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.CompanyDefend, Curated = true, Group = "Library/Combat", ParamsType = typeof(CompanyDefendBlackboard))]
        public static StateMachineGraph BuildCompanyDefend()
        {
            var builder = new HsmBuilder("CompanyDefend_HSM");
            var root = builder.State("Defending").Initial().Parallel();
            StateBuilder plan = null!, hold = null!, fallBack = null!, watch = null!, reinforce = null!;
            root.Child("KPlan", c => plan = c.InRegion(0).Initial()
                .Activity<CompanyDefendBlackboard, CompanyDefencePlan, CompanyDefendState>(bb => bb.Plan, bb => bb.Keep, PlanDefence));
            root.Child("KHold", c => hold = c.InRegion(0)
                .Activity<CompanyDefendBlackboard, CompanyDefencePlan, CompanyDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence));
            root.Child("KFallBack", c => fallBack = c.InRegion(0)
                .OnEntry<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, FallBack)
                .Activity<CompanyDefendBlackboard, CompanyDefencePlan, CompanyDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence));
            root.Child("KWatch", c => watch = c.InRegion(1).Initial());
            root.Child("KReinforce", c => reinforce = c.InRegion(1).OnEntry<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, Reinforce));
            StateBuilder counter = null!;
            root.Child("KCounter", c => counter = c.InRegion(1).OnEntry<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, Counterattack));
            plan.On(0).GoTo("KHold").Guard<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, Planned).Polled();
            hold.On(0).GoTo("KFallBack").Guard<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, HalfTheBlocksFailing).Polled();
            // ⭐ CE-7004 — checked FIRST: a LOST block with a strong reserve is retaken; a merely failing one is reinforced
            watch.On(0).GoTo("KCounter").Guard<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, BlockLostWithStrongReserve).Polled();
            counter.On(0).GoTo("KWatch").Guard<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, Handled).Polled();
            watch.On(0).GoTo("KReinforce").Guard<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, BlockFailingWithReserve).Polled();
            reinforce.On(0).GoTo("KWatch").Guard<CompanyDefendBlackboard, CompanyDefencePlan>(bb => bb.Plan, Handled).Polled();
            return builder.Build();
        }

        /// <summary>KPlan's activity: roles and points — the blocks across the frontage, the reserve behind the middle.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus PlanDefence(ref CompanyDefencePlan p, ref CompanyDefendState ws, Entity self, EntityRepository world)
        {
            if (p.Made != 0) return NodeStatus.Running;
            int n = Math.Min(CompanyAttackBehavior.RosterCount(world, self), CompanyDefencePlan.Capacity);
            if (n == 0) return NodeStatus.Running;
            int lines = n >= 3 ? n - 1 : n;
            var across = new Vector2(p.Toward.Y, -p.Toward.X);   // the line runs across the threat
            for (int s = 0; s < n; s++)
            {
                if (s < lines)
                {
                    p.Role[s] = CompanyDefencePlan.Line;
                    float t = lines == 1 ? 0f : ((s + 0.5f) / lines - 0.5f) * p.Frontage;
                    p.SetPoint(s, p.Position + new Vector3(across * t, 0f));
                }
                else
                {
                    p.Role[s] = CompanyDefencePlan.Reserve;
                    p.SetPoint(s, p.Position - new Vector3(p.Toward * (p.Depth * 0.5f), 0f));
                }
                p.Dirty[s] = 1;
            }
            p.Made = 1;
            return NodeStatus.Running;
        }

        /// <summary>KFallBack's entry: every point moves <see cref="CompanyDefencePlan.Depth"/> back, every slot re-ordered.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus FallBack(ref CompanyDefencePlan p, Entity self, EntityRepository world)
        {
            p.Falling = 1;
            var back = new Vector3(p.Toward * p.Depth, 0f);
            for (int s = 0; s < CompanyDefencePlan.Capacity; s++)
            {
                if (p.Role[s] == CompanyDefencePlan.NoRole) continue;
                p.SetPoint(s, p.Point(s) - back);
                p.Dirty[s] = 1;
            }
            p.ReserveTo = -1;
            return NodeStatus.Success;
        }

        /// <summary>KReinforce's entry: the reserve goes to the first failing block's point (just behind it).</summary>
        [SharedAiAction]
        public static unsafe NodeStatus Reinforce(ref CompanyDefencePlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(CompanyAttackBehavior.RosterCount(world, self), CompanyDefencePlan.Capacity);
            int r = FreeReserve(ref p, self, world, n), b = FirstFailingBlock(ref p, self, world, n);
            if (r < 0 || b < 0) return NodeStatus.Success;
            p.SetPoint(r, p.Point(b) - new Vector3(p.Toward * 40f, 0f));
            p.ReserveTo = b;
            p.Dirty[r] = 1;
            return NodeStatus.Success;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-7004</c> (City Battle §3a, §11 C-3; handoff default: "a company objective is lost and the reserve is ≥ 70 % ⇒ attack it, once") —
        /// KCounter's entry: the free reserve is sent to RETAKE the first lost block (<c>PlatoonAttack</c> on its point, <see cref="KeepDefence"/>);
        /// the block then counts as held (<see cref="CompanyDefencePlan.ReserveTo"/>), and once retaken the reserve defends it.
        /// </summary>
        [SharedAiAction]
        public static unsafe NodeStatus Counterattack(ref CompanyDefencePlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(CompanyAttackBehavior.RosterCount(world, self), CompanyDefencePlan.Capacity);
            int r = StrongReserve(ref p, self, world, n), b = FirstLostBlock(ref p, self, world, n);
            if (r < 0 || b < 0) return NodeStatus.Success;
            p.CounterBy = r;
            p.ReserveTo = b;
            p.Countered = 1;
            p.Retaken = 0;
            p.SetPoint(r, p.Point(b));
            p.Dirty[r] = 1;
            return NodeStatus.Success;
        }

        /// <summary>The ONE order writer: every dirty (or no longer standing) slot gets its block's <c>PlatoonDefend</c>.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus KeepDefence(ref CompanyDefencePlan p, ref CompanyDefendState ws, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return NodeStatus.Running;
            int n = Math.Min(CompanyAttackBehavior.RosterCount(world, self), CompanyDefencePlan.Capacity);
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            for (int slot = 0; slot < n; slot++)
            {
                if (p.Role[slot] == CompanyDefencePlan.NoRole) continue;
                var platoon = CommandPictureOps.Subordinate(world, self, slot);
                if (platoon.IsNull || !world.IsAlive(platoon)) continue;
                // ⭐ CE-7004 — the counterattack's Set (its order is new, so an older Set does not count): the block is retaken ⇒ defend it
                if (slot == p.CounterBy && p.Retaken == 0 && p.Dirty[slot] == 0 && CommandPictureOps.ProgressOf(world, self, slot) == ProgressKind.Set)
                {
                    p.Retaken = 1;
                    p.Dirty[slot] = 1;
                }
                if (p.Dirty[slot] == 0 && CommandOrders.Stands(world, self, platoon)) continue;
                p.Dirty[slot] = 0;
                var at = p.Point(slot);
                var dto = new PlatoonDefendParamsJsonDto { Position = at, Speed = p.Speed, FallBackTo = at - new Vector3(p.Toward * p.Depth, 0f),
                                                          ShootAndMoveSeconds = Math.Max(0f, p.ShootAndMove) };
                if (p.Role[slot] == CompanyDefencePlan.Line || p.ReserveTo >= 0)
                {
                    float bearing = SectorSplit.Norm(MathF.Atan2(p.Toward.X, p.Toward.Y) * (180f / MathF.PI));
                    dto.Left = SectorSplit.Norm(bearing - BlockArcHalfDegrees);
                    dto.Right = SectorSplit.Norm(bearing + BlockArcHalfDegrees);
                }
                // ⭐ CE-7004 — the counterattacking reserve ATTACKS the lost block until it reports Set (retaken), then defends it as a block
                if (slot == p.CounterBy && p.Retaken == 0)
                {
                    var attack = JsonSerializer.Serialize(new PlatoonAttackParamsJsonDto { Position = at, Speed = p.Speed }, json);
                    if (CommandOrders.StillRunning(world, self, platoon, BehaviorNames.PlatoonAttack, attack)) continue;
                    CommandOrders.Assign(world, self, platoon, BehaviorNames.PlatoonAttack, attack, newOrder: true);
                    ws.Orders++;
                    continue;
                }
                var order = JsonSerializer.Serialize(dto, json);
                if (CommandOrders.StillRunning(world, self, platoon, BehaviorNames.PlatoonDefend, order)) continue;
                CommandOrders.Assign(world, self, platoon, BehaviorNames.PlatoonDefend, order, newOrder: true);
                ws.Orders++;
            }
            return NodeStatus.Running;
        }

        [SharedAiCondition]
        public static bool Planned(ref CompanyDefencePlan p, Entity self, EntityRepository world) => p.Made != 0;

        /// <summary>KHold → KFallBack: half the blocks (or more) are failing AND no reserve is free to put in (it went in first).</summary>
        [SharedAiCondition]
        public static unsafe bool HalfTheBlocksFailing(ref CompanyDefencePlan p, Entity self, EntityRepository world)
        {
            if (p.Made == 0 || p.Falling != 0) return false;
            int n = Math.Min(CompanyAttackBehavior.RosterCount(world, self), CompanyDefencePlan.Capacity), blocks = 0, failing = 0;
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != CompanyDefencePlan.Line) continue;
                blocks++;
                if (s != p.ReserveTo && Failing(world, self, s)) failing++;   // the block the reserve went into counts as held
            }
            return blocks > 0 && failing * 2 >= blocks && (p.ReserveTo >= 0 || FreeReserve(ref p, self, world, n) < 0);   // the reserve goes in first
        }

        /// <summary>KWatch → KReinforce: a block is failing and the reserve is still free.</summary>
        [SharedAiCondition]
        public static bool BlockFailingWithReserve(ref CompanyDefencePlan p, Entity self, EntityRepository world)
        {
            if (p.Made == 0 || p.ReserveTo >= 0) return false;
            int n = Math.Min(CompanyAttackBehavior.RosterCount(world, self), CompanyDefencePlan.Capacity);
            return FreeReserve(ref p, self, world, n) >= 0 && FirstFailingBlock(ref p, self, world, n) >= 0;
        }

        /// <summary>⭐ CE-7004 — KWatch → KCounter: once per defence, a block is LOST and the reserve is still free and at ≥ 70 % strength.</summary>
        [SharedAiCondition]
        public static bool BlockLostWithStrongReserve(ref CompanyDefencePlan p, Entity self, EntityRepository world)
        {
            if (p.Made == 0 || p.Countered != 0 || p.ReserveTo >= 0) return false;
            int n = Math.Min(CompanyAttackBehavior.RosterCount(world, self), CompanyDefencePlan.Capacity);
            return StrongReserve(ref p, self, world, n) >= 0 && FirstLostBlock(ref p, self, world, n) >= 0;
        }

        [SharedAiCondition]
        public static bool Handled(ref CompanyDefencePlan p, Entity self, EntityRepository world) => true;

        /// <summary>⭐ CE-7004 — a block is LOST when its platoon is gone or gave it up (TaskFailed) — not merely Black or falling back (that is
        /// failing: the reserve reinforces it).</summary>
        private static unsafe int FirstLostBlock(ref CompanyDefencePlan p, Entity self, EntityRepository world, int n)
        {
            for (int s = 0; s < n; s++)
                if (p.Role[s] == CompanyDefencePlan.Line
                    && (!CompanyAttackBehavior.Alive(world, self, s) || CommandPictureOps.ProgressOf(world, self, s) == ProgressKind.TaskFailed)) return s;
            return -1;
        }

        /// <summary>⭐ CE-7004 — the free reserve at ≥ 70 % strength (its colour Green or Amber: <c>StaffComposerSystem.AmberFraction</c> 0.7).</summary>
        private static unsafe int StrongReserve(ref CompanyDefencePlan p, Entity self, EntityRepository world, int n)
        {
            int r = FreeReserve(ref p, self, world, n);
            if (r < 0) return -1;
            var weak = new SubordinateColourParams { Slot = r, AtLeast = StatusColour.Red };
            return PictureConditions.SubordinateColour(ref weak, self, world) ? -1 : r;
        }

        private static unsafe int FirstFailingBlock(ref CompanyDefencePlan p, Entity self, EntityRepository world, int n)
        {
            for (int s = 0; s < n; s++) if (p.Role[s] == CompanyDefencePlan.Line && Failing(world, self, s)) return s;
            return -1;
        }

        private static unsafe int FreeReserve(ref CompanyDefencePlan p, Entity self, EntityRepository world, int n)
        {
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != CompanyDefencePlan.Reserve || !CompanyAttackBehavior.Alive(world, self, s)) continue;
                var colour = new SubordinateColourParams { Slot = s, AtLeast = StatusColour.Black };
                if (!PictureConditions.SubordinateColour(ref colour, self, world)) return s;
            }
            return -1;
        }

        /// <summary>A block fails when its platoon is gone, its task failed, it reports Black, or it is falling back on its own.</summary>
        private static bool Failing(EntityRepository world, Entity self, int slot)
        {
            if (!CompanyAttackBehavior.Alive(world, self, slot)) return true;
            var progress = CommandPictureOps.ProgressOf(world, self, slot);
            if (progress == ProgressKind.TaskFailed || progress == ProgressKind.FallingBack) return true;
            var colour = new SubordinateColourParams { Slot = slot, AtLeast = StatusColour.Black };
            return PictureConditions.SubordinateColour(ref colour, self, world);
        }
    }
}
