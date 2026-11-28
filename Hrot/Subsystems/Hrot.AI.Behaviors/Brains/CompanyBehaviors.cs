using Hrot.AI.Behaviors.Orders;
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
using Fdp.Toolkit.Perception.Survey;
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
        /// <summary>⭐ <c>CE-2436</c> AU-4 (A-4) — a VEHICLE subordinate: supports by fire from the base of fire's line, never assaults.</summary>
        public const byte Vehicle = 4;
        /// <summary>⭐ <c>CE-2436</c> AU-5 (A-6) — the DRONE TEAM: its recon drone watches the objective, its armed drones strike the posts.</summary>
        public const byte Drones = 5;
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
        /// <summary>⭐ <c>CE-2436</c> AU-3 (A-1) — the posts the company's base of fire keeps under fire (the current objective's reported elements;
        /// none ⇒ the objective); <see cref="Posts"/> of them.</summary>
        public Vector3 Post0, Post1, Post2, Post3;
        public int Posts;
        /// <summary>⭐ <c>CE-2436</c> A-9 — the covered position's survey for the current objective: asked · when · settled (moved, or none found).</summary>
        public byte CoverAsked, CoverDone;
        public SimInstant CoverAskedAt;

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
        /// <summary>⭐ <c>CE-2436</c> AU-3 (A-5) — a sustained mission's rounds per gun; the screen stands this far from the objective toward where the
        /// company came from (m); an element this close to the objective is one of its posts (m).</summary>
        public const int SustainedRounds = 8;
        public const float ScreenMetres = 50f, PostMatchMetres = 40f;
        public const BehaviorFaultCode AbortedFault = BehaviorFaultCode.Custom + 7023;

        [StructLayout(LayoutKind.Sequential)]
        public struct CompanyAttackBlackboard : IPlayRoles
        {
            /// <summary>⭐ CE-4686 ② — the slot's role, read from the plan (never changed).</summary>
            public unsafe string? RoleOf(int slot) => slot < 0 || slot >= CompanyAttackPlan.Capacity ? null : Plan.Role[slot] switch
            {
                CompanyAttackPlan.Assault => PlayRoleWords.Assault, CompanyAttackPlan.Support => PlayRoleWords.BaseOfFire,
                CompanyAttackPlan.Reserve => PlayRoleWords.Reserve, CompanyAttackPlan.Vehicle => PlayRoleWords.VehicleSupport,
                CompanyAttackPlan.Drones => PlayRoleWords.Drones, _ => null,
            };

            public CompanyAttackPlan Plan;
            public CompanyAttackState Keep;
        }

        [BehaviorResolver(BehaviorNames.CompanyAttack)]
        public static unsafe void Resolve(in CompanyAttackParamsJsonDto authored, ref CompanyAttackBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            block.Plan.Radius = authored.Radius > 0f ? authored.Radius : authored.SearchRadius > 0f ? authored.SearchRadius : DefaultRadius;
            block.Plan.Speed = authored.Speed;
            block.Plan.Deadline = authored.DeadlineSeconds > 0f ? authored.DeadlineSeconds : DefaultDeadlineSeconds;
            Vector3 o1 = authored.Objective1, o2 = authored.Objective2, o3 = authored.Objective3, o4 = authored.Objective4;
            // ⭐ CE-8307 (R-8392) — the neutral AttackOrder: its place (else its entity's) is the first objective when none is authored
            if (o1 == Vector3.Zero) o1 = authored.Position;
            if (o1 == Vector3.Zero && authored.Target.Ref.TryResolve(world, out var t) && world.HasComponent<Fdp.Core.SimTransform>(t))
                o1 = (Vector3)world.GetComponentRO<Fdp.Core.SimTransform>(t).Position;
            int n = 0;
            if (o1 != Vector3.Zero) Put(ref block.Plan, n++, o1);
            if (n > 0 && o2 != Vector3.Zero) Put(ref block.Plan, n++, o2);
            if (n > 1 && o3 != Vector3.Zero) Put(ref block.Plan, n++, o3);
            if (n > 2 && o4 != Vector3.Zero) Put(ref block.Plan, n++, o4);
            block.Plan.Objectives = n;
        }

        private static unsafe void Put(ref CompanyAttackPlan p, int i, Vector3 o) { p.OX[i] = o.X; p.OY[i] = o.Y; p.OZ[i] = o.Z; }

        [Behavior(BehaviorNames.CompanyAttack, Params = typeof(CompanyAttackParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Attack), Categories = BehaviorCategory.Commander)]   // ⭐ CE-8307 (R-8392)
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
            int k = 0;   // ⭐ CE-2436 AU-4 — the platoons take the roles in order; a VEHICLE subordinate supports by fire (never the assault)
            for (int s = 0; s < n && s < CompanyAttackPlan.Capacity; s++)
            {
                var sub = CommandPictureOps.Subordinate(world, self, s);
                if (!sub.IsNull && Fdp.Toolkit.Motion.Locomotions.IsVehicle(world, sub)) { p.Role[s] = CompanyAttackPlan.Vehicle; continue; }
                if (DroneTeamBehavior.IsDroneTeam(world, sub)) { p.Role[s] = CompanyAttackPlan.Drones; continue; }   // ⭐ AU-5 — never a platoon role
                p.Role[s] = k == 0 ? CompanyAttackPlan.Assault : k == 1 ? CompanyAttackPlan.Support : k == 2 ? CompanyAttackPlan.Reserve : CompanyAttackPlan.NoRole;
                k++;
            }
            p.Made = 1;
            return NodeStatus.Running;
        }

        // ── the phases ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CSupport's entry: the current objective's points, its clock started, every role re-ordered.</summary>
        [SharedAiAction]
        public static NodeStatus EnterSupport(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            Points(ref p);
            CompanyPosts(ref p, world, self);   // ⭐ CE-2436 AU-3 (A-1) — the base of fire's targets
            p.ObjectiveSince = (SimInstant)CommandPictureOps.Now(world);
            p.Escalation = 0;
            p.CoverAsked = 0; p.CoverDone = 0;   // ⭐ A-9 — each objective's base of fire looks for its own cover
            SetPhase(ref p, CompanyAttackPlan.PhaseSupport);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus EnterAssault(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, CompanyAttackPlan.PhaseAssault);
            // ⭐ CE-4615 (R-2100) — the base of fire is up and the assault goes in: fire (HE) on the objective. 📐 Live on cc-battalion
            //   the only call was the tempo's SMOKE (CE-7025), so the assault met an untouched line and never took it.
            // ⭐⭐ CE-2436 AU-3 (A-5) — a SUSTAINED mission on the REPORTED posts for the approach (rounds over time, not one 3-round burst)
            AskFires(world, self, PostsCentre(ref p, world, self), FireSupportRound.HighExplosive, SustainedRounds);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus NextObjective(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            // ⭐ CE-4610 — objective k taken is phase line k (1-based) reached: reported up, so the battalion sees the company's progress
            CommandNodes.ReportProgress(world, self, ProgressKind.PhaseLine, p.Objective(p.Current), p.Current + 1);
            // ⭐ CE-2436 AU-3 (A-5) — the SHIFT beyond: the objective is taken, the indirect fire moves on to the next one (the depth of the
            //   attack); the last one taken asks nothing more (the company consolidates on it)
            if (p.Current + 1 < p.Objectives) AskFires(world, self, p.Objective(p.Current + 1), FireSupportRound.HighExplosive, SustainedRounds);
            p.Current++;
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus EnterConsolidate(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            p.Current = Math.Max(0, p.Objectives - 1);
            SetPhase(ref p, CompanyAttackPlan.PhaseConsolidate);
            // ⭐ CE-4610 (Platoon Attack §4 Consolidate) — the last objective is taken: ObjectiveSecure up (reached only through CNext, never a bypass)
            if (p.Objectives > 0) CommandNodes.ReportProgress(world, self, ProgressKind.ObjectiveSecure, p.Objective(p.Current));
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

        /// <summary>⭐ <c>CE-2436</c> A-9 — the cover survey's site, its radius round the fixed support point (m), how long it is waited for (s), and
        /// the nearest a covered position may be to the objective (m).</summary>
        public const int CoverSite = 0x24360009;
        public const float CoverSearchMetres = 100f, CoverTimeoutSeconds = 5f, CoverMinMetres = 120f;

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> A-9 (Assault Under Fire A-9, R-4254) — the company's base of fire takes a COVERED position: one world survey round the
        /// fixed support point (<see cref="SupportStandOffMetres"/>), and the enterable building there chosen by <see cref="CoverChoice.Pick"/> becomes
        /// the support point (its squads man its windows — <c>SquadDefend</c>'s post search). Asked BESIDE the plan, never waited for: until the
        /// answer the base of fire moves to the fixed point; with no building, or no answer in <see cref="CoverTimeoutSeconds"/>, it stays there.
        /// Only while the base of fire is still going up (the support phase).
        /// </summary>
        private static unsafe void Cover(ref CompanyAttackPlan p, Entity self, EntityRepository world)
        {
            if (p.Phase != CompanyAttackPlan.PhaseSupport || p.CoverDone != 0) return;
            double now = CommandPictureOps.Now(world);
            if (p.CoverAsked == 0)
            {
                WorldSurveyChildSensor.Ensure(world, self, CoverSite, WorldSurveyRequest.OfArea(new Vector2(p.SupportAt.X, p.SupportAt.Y), CoverSearchMetres));
                p.CoverAsked = 1;
                p.CoverAskedAt = (SimInstant)now;
                return;
            }
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, CoverSite);
            var read = WorldSurveyChildSensor.Read(world, child, now);
            bool ready = read.Ready && !read.Stale && read.Payload != null;
            if (!ready && now - p.CoverAskedAt < CoverTimeoutSeconds) return;
            p.CoverDone = 1;
            if (!child.IsNull) WorldSurveyChildSensor.Release(world, child);
            if (!ready) return;
            var o = p.Objective(p.Current);
            if (!CoverChoice.Pick(read.Payload!, p.SupportAt, o, CoverMinMetres, SupportRangeMetres, SupportArcHalfDegrees, out var at)) return;
            p.SupportAt = new Vector3(at.X, at.Y, p.SupportAt.Z);
            int s = p.SlotOf(CompanyAttackPlan.Support, CompanyAttackPlan.Capacity);
            if (s >= 0) p.Dirty[s] = 1;   // re-ordered onto the cover
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

        /// <summary>⭐ AU-3 (A-1) — the company base of fire's posts: fresh picture elements within <see cref="PostMatchMetres"/> of the current
        /// objective (≤ 4); none ⇒ the objective.</summary>
        private static unsafe void CompanyPosts(ref CompanyAttackPlan p, EntityRepository world, Entity self)
        {
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            p.Posts = 0;
            if (world.HasComponent<CommandPicture>(self))
            {
                ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
                var now = (SimInstant)CommandPictureOps.Now(world);
                for (int i = 0; i < picture.Elements.Count && p.Posts < 4; i++)
                {
                    ref readonly var e = ref picture.Elements.Items[i];
                    if (e.Count == 0 || now - e.ObservedAt > PlatoonAttackBehavior.ElementFreshSeconds) continue;
                    if (Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(o.X, o.Y)) > PostMatchMetres) continue;
                    AddPost(ref p, e.At);
                }
            }
            if (p.Posts == 0) AddPost(ref p, o);
        }

        private static void AddPost(ref CompanyAttackPlan p, Vector3 at)
        {
            switch (p.Posts) { case 0: p.Post0 = at; break; case 1: p.Post1 = at; break; case 2: p.Post2 = at; break; default: p.Post3 = at; break; }
            p.Posts++;
        }

        /// <summary>⭐ AU-4 (A-4) — vehicles stand this far to each side of the base of fire's point (m), alternating by slot.</summary>
        public const float VehicleSpreadMetres = 35f;

        private static bool VehicleSupport(ref CompanyAttackPlan p, int slot, JsonSerializerOptions json, out string behaviour, out string order)
        {
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            var toObj = new Vector2(o.X - p.SupportAt.X, o.Y - p.SupportAt.Y);
            var dir = toObj.LengthSquared() > 1e-4f ? Vector2.Normalize(toObj) : Vector2.UnitY;
            var across = new Vector2(-dir.Y, dir.X) * ((slot & 1) == 0 ? VehicleSpreadMetres : -VehicleSpreadMetres);
            behaviour = BehaviorNames.VehicleSupportByFire;
            // ⭐ AU-4 blind fire (R-2124) — the reported posts the vehicle fires on while it sees nothing itself; ONLY while the base of fire
            //   works (Support): from the assault on our own men are on them — lifted, as the base of fire lifts
            order = JsonSerializer.Serialize(new VehicleSupportByFireParamsJsonDto { Position = p.SupportAt + new Vector3(across, 0f), Posts = p.Posts > 0 && p.Phase == CompanyAttackPlan.PhaseSupport ? PostsOf(ref p, o) : Vector3.Zero }, json);
            return true;
        }

        /// <summary>⭐ AU-5 (A-6) — the drone team's task: watch <paramref name="watch"/>, strike <paramref name="strike"/> ((0, 0, 0) = none).</summary>
        private static bool DroneTask(Vector3 watch, Vector3 strike, JsonSerializerOptions json, out string behaviour, out string order)
        {
            behaviour = BehaviorNames.DroneTeam;
            order = JsonSerializer.Serialize(new DroneTeamParamsJsonDto { Watch = watch, Strike = strike }, json);
            return true;
        }

        /// <summary>The centre of the reported posts the base of fire holds (the objective when none is reported).</summary>
        private static Vector3 PostsOf(ref CompanyAttackPlan p, Vector3 objective)
        {
            if (p.Posts <= 0) return objective;
            var sum = p.Post0;
            if (p.Posts > 1) sum += p.Post1;
            if (p.Posts > 2) sum += p.Post2;
            if (p.Posts > 3) sum += p.Post3;
            return sum / System.Math.Min(p.Posts, 4);
        }

        private static bool SupportByFire(ref CompanyAttackPlan p, JsonSerializerOptions json, out string behaviour, out string order)
        {
            behaviour = BehaviorNames.PlatoonSupportByFire;
            order = JsonSerializer.Serialize(new PlatoonSupportByFireParamsJsonDto
            {
                Position = p.SupportAt, Speed = p.Speed,
                Post1 = p.Posts > 0 ? p.Post0 : default, Post2 = p.Posts > 1 ? p.Post1 : default,
                Post3 = p.Posts > 2 ? p.Post2 : default, Post4 = p.Posts > 3 ? p.Post3 : default,
            }, json);
            return true;
        }

        /// <summary>⭐ AU-3 (A-5) — where the reported posts are: the centre of the company picture's fresh elements within
        /// <see cref="PostMatchMetres"/> of the current objective; none ⇒ the objective.</summary>
        private static Vector3 PostsCentre(ref CompanyAttackPlan p, EntityRepository world, Entity self)
        {
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            if (!world.HasComponent<CommandPicture>(self)) return o;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            var now = (SimInstant)CommandPictureOps.Now(world);
            Vector3 sum = Vector3.Zero; int n = 0;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || now - e.ObservedAt > PlatoonAttackBehavior.ElementFreshSeconds) continue;
                if (Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(o.X, o.Y)) > PostMatchMetres) continue;
                sum += (Vector3)e.At; n++;
            }
            return n > 0 ? sum / n : o;
        }

        /// <summary>⭐ AU-3 (A-5) — the smoke SCREEN: <see cref="ScreenMetres"/> from the current objective toward where the company came from —
        /// on the crossing the assault makes, not on the objective.</summary>
        private static Vector3 ScreenPoint(ref CompanyAttackPlan p)
        {
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            var from = p.Current == 0 ? p.Start : p.Objective(p.Current - 1);
            var back = new Vector2(from.X - o.X, from.Y - o.Y);
            var dir = back.LengthSquared() > 1e-4f ? Vector2.Normalize(back) : -Vector2.UnitY;
            return o + new Vector3(dir * ScreenMetres, 0f);
        }

        /// <summary>The ONE order writer: a slot whose order of this phase does not stand, or that is dirty, is ordered again; and the tempo
        /// clock (C-4) is checked here, once per tick of the phase.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus KeepAttack(ref CompanyAttackPlan p, ref CompanyAttackState ws, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return NodeStatus.Running;
            Tempo(ref p, ref ws, self, world);
            Cover(ref p, self, world);   // ⭐ CE-2436 A-9 — the base of fire onto a covered position (asked beside, never waited for)
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
                // ⭐ agg-behaviors R-8305 / R-8315 — the Defend, PlatoonAttack and PlatoonSupportByFire arms are routed by kind with the TWO payloads (a stochastic
                //   platoon gets Order(kind, neutral); a physical one exactly the play's own json); the vehicle / drone arms stay Assign (kind 0)
                var kind = NeutralOf(ref p, behaviour, order, json, out var neutral);
                if (CommandOrders.StillRoutedRunning(world, self, platoon, kind, neutral, behaviour, order)) continue;
                CommandOrders.Route(world, self, platoon, kind, neutral, behaviour, order, newOrder: true);
                CommandOrders.AskSitrep(world, platoon);   // ⭐ CE-4615 — the colour the relief / LOST guards read
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
                // ⭐ CE-7025 — smoke first; ⭐ CE-2436 AU-3 (A-5) — as a SCREEN on the crossing, between the objective and the assault's route,
                //   ⛔ never on the objective (📐 measured: smoke on it blinds the company's own base of fire)
                if (AskFires(world, self, ScreenPoint(ref p), FireSupportRound.Smoke)) ws.FireRequests++;
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
        public static bool AskFires(EntityRepository world, Entity self, Vector3 at, FireSupportRound round = FireSupportRound.HighExplosive, int rounds = 1)
        {
            SimInstant now = (SimInstant)CommandPictureOps.Now(world);
            return ReportQueue.Enqueue(world, self, new CommandReport
            {
                Kind = ReportKind.Request, Subtype = (byte)RequestKind.FireSupport, At = at, Radius = 50f, Count = (byte)Math.Clamp(rounds, 1, 20),
                Equipment = round == FireSupportRound.Smoke ? ElementEquipment.Smoke : ElementEquipment.NoEquipment, ObservedAt = now,
            }, now + CommandNodes.ReportDelaySeconds);
        }

        /// <summary>⭐ R-8315 — the NEUTRAL form of the order <see cref="OrderFor"/> chose, and its kind (0 = no neutral kind: a vehicle or the drone team stays <c>Assign</c>).
        /// Defend is the one payload; the assault is an <see cref="AttackOrder"/> on the objective; the base of fire an <see cref="AttackOrder"/> with
        /// <see cref="AttackOrder.HoldBack"/> on the SUPPORT POSITION (⭐ CE-8302: <c>SupportAt</c>, where the physical base of fire stands) — a stochastic unit's support by fire never closes.</summary>
        private static OrderKind NeutralOf(ref CompanyAttackPlan p, string behaviour, string order, JsonSerializerOptions json, out string neutral)
        {
            neutral = order;
            var o = p.Objective(Math.Min(p.Current, p.Objectives - 1));
            if (behaviour == BehaviorNames.PlatoonDefend) return OrderKind.Defend;
            if (behaviour == BehaviorNames.PlatoonAttack)
            {
                neutral = JsonSerializer.Serialize(new AttackOrder { Position = o, SearchRadius = p.Radius, Speed = p.Speed }, json);
                return OrderKind.Attack;
            }
            if (behaviour == BehaviorNames.PlatoonSupportByFire)
            {
                // ⭐ CE-8302 (R-8341) — the base of fire stands on the SUPPORT POSITION (SupportAt, 250 m short, or the covered building A-9 chose), the same
                //   point PlatoonSupportByFire gets: a HoldBack on the posts walked it to 100 m of the enemy line (F-15)
                neutral = JsonSerializer.Serialize(new AttackOrder { Position = p.SupportAt, Speed = p.Speed, HoldBack = true }, json);
                return OrderKind.SupportByFire;
            }
            return 0;
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
                    // ⭐⭐ CE-2436 AU-4 (A-4) — a vehicle supports by fire from the base of fire's line, ±VehicleSpreadMetres to its sides, never forward
                    if (role == CompanyAttackPlan.Vehicle) return VehicleSupport(ref p, slot, json, out behaviour, out order);
                    // ⭐⭐ CE-2436 AU-5 (A-6) — the drone team: the recon drone over the objective, the armed drones on the reported posts
                    if (role == CompanyAttackPlan.Drones) return DroneTask(o, PostsOf(ref p, o), json, out behaviour, out order);
                    // ⭐⭐ CE-2436 AU-3 (A-1) — the company's base of fire FIRES on the reported posts (every squad SupportByFire), seen or not
                    if (role == CompanyAttackPlan.Support) return SupportByFire(ref p, json, out behaviour, out order);
                    if (role == CompanyAttackPlan.Reserve) return Defend(p.ReserveAt, o, 0f, 0f, p.Speed, json, out behaviour, out order);
                    if (role == CompanyAttackPlan.Assault && p.Phase == CompanyAttackPlan.PhaseAssault)
                    {
                        behaviour = BehaviorNames.PlatoonAttack;
                        order = JsonSerializer.Serialize(new PlatoonAttackParamsJsonDto { Position = o, SearchRadius = p.Radius, Speed = p.Speed }, json);
                        return true;
                    }
                    return false;   // the assault waits for the base of fire
                case CompanyAttackPlan.PhaseConsolidate:
                case CompanyAttackPlan.PhaseAbort:
                    if (role == CompanyAttackPlan.Vehicle) return VehicleSupport(ref p, slot, json, out behaviour, out order);   // AU-4 — stays back, covering
                    if (role == CompanyAttackPlan.Drones) return DroneTask(o, Vector3.Zero, json, out behaviour, out order);   // AU-5 — watches, no strike on our own
                    return p.Phase == CompanyAttackPlan.PhaseConsolidate
                        ? Defend(o, o, 0f, 0f, p.Speed, json, out behaviour, out order)
                        : Defend(p.Start, p.Start, 0f, 0f, p.Speed, json, out behaviour, out order);
                default:
                    return false;
            }
        }

        /// <summary>A <c>PlatoonDefend</c> at <paramref name="at"/>; an arc ± <paramref name="halfArc"/> toward <paramref name="toward"/> when
        /// given (the base of fire's), else the platoon's own sector.</summary>
        private static bool Defend(Vector3 at, Vector3 toward, float halfArc, float range, float speed, JsonSerializerOptions json, out string behaviour, out string order)
        {
            behaviour = BehaviorNames.PlatoonDefend;
            // ⭐ agg-behaviors R-8300 — the NEUTRAL Defend order. ⚠ <paramref name="halfArc"/> is 0 at every caller today, so the arc (and its MaxRange,
            //   which the neutral DTO does not hold) is never written: a non-zero arc would need MaxRange added to DefendOrder first
            var dto = new DefendOrder { Position = at, Speed = speed };
            if (halfArc > 0f)
            {
                float bearing = SectorSplit.Norm(MathF.Atan2(toward.X - at.X, toward.Y - at.Y) * (180f / MathF.PI));
                dto.Left = (Bearing)SectorSplit.Norm(bearing - halfArc);
                dto.Right = (Bearing)SectorSplit.Norm(bearing + halfArc);
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
                if (p.Role[s] == CompanyAttackPlan.NoRole || p.Role[s] == CompanyAttackPlan.Vehicle || p.Role[s] == CompanyAttackPlan.Drones
                    || !Alive(world, self, s)) continue;   // AU-4 / AU-5: a vehicle or the drone team reports no Set   // AU-4: a vehicle reports no Set
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
        public struct CompanyDefendBlackboard : IPlayRoles
        {
            public CompanyDefencePlan Plan;
            public CompanyDefendState Keep;

            /// <summary>⭐ CE-4686 ② — the slot's role, read from the plan (never changed).</summary>
            public unsafe string? RoleOf(int slot) => slot < 0 || slot >= CompanyDefencePlan.Capacity ? null : Plan.Role[slot] switch
            {
                CompanyDefencePlan.Line => PlayRoleWords.Line, CompanyDefencePlan.Reserve => PlayRoleWords.Reserve, _ => null,
            };
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

        [Behavior(BehaviorNames.CompanyDefend, Params = typeof(CompanyDefendParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Defend), Categories = BehaviorCategory.Commander)]
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
                // ⭐ agg-behaviors R-8300/R-8305 — the NEUTRAL Defend order (a physical PlatoonDefend reads it strictly; a stochastic platoon is routed by kind)
                var dto = new DefendOrder { Position = at, Speed = p.Speed, FallBackTo = at - new Vector3(p.Toward * p.Depth, 0f),
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
                    // ⭐ R-8315 — a stochastic platoon is ordered Attack by KIND (the neutral AttackOrder on the lost block's position); a physical one exactly as before
                    var neutralAttack = JsonSerializer.Serialize(new AttackOrder { Position = at, Speed = p.Speed }, json);
                    if (CommandOrders.StillRoutedRunning(world, self, platoon, OrderKind.Attack, neutralAttack, BehaviorNames.PlatoonAttack, attack)) continue;
                    CommandOrders.Route(world, self, platoon, OrderKind.Attack, neutralAttack, BehaviorNames.PlatoonAttack, attack, newOrder: true);
                    CommandOrders.AskSitrep(world, platoon);
                    ws.Orders++;
                    continue;
                }
                var order = JsonSerializer.Serialize(dto, json);
                if (CommandOrders.StillRoutedRunning(world, self, platoon, OrderKind.Defend, order, BehaviorNames.PlatoonDefend, order)) continue;   // ⭐ X2-H — resolution-blind
                CommandOrders.Route(world, self, platoon, OrderKind.Defend, BehaviorNames.PlatoonDefend, order, newOrder: true);
                CommandOrders.AskSitrep(world, platoon);   // ⭐ CE-4615 — a block's colour: Black ⇒ LOST ⇒ the counter-attack (CE-7004)
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
            if (progress == ProgressKind.TaskFailed) return true;   // ⭐ CE-4610 — FallingBack retired: a fall-back's end is Set (DA E-3a), a failure TaskFailed
            var colour = new SubordinateColourParams { Slot = slot, AtLeast = StatusColour.Black };
            return PictureConditions.SubordinateColour(ref colour, self, world);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2436</c> A-9 — pure: the covered position for a base of fire — among the surveyed ENTERABLE buildings, those between
    /// <c>min</c> and <c>max</c> metres of the objective and within <c>arcHalfDegrees</c> of the line from the objective to the fixed support point
    /// (the side the company came from), the one nearest that fixed point; its centre. False when none qualifies.
    /// </summary>
    public static class CoverChoice
    {
        public static bool Pick(Fdp.Toolkit.Perception.Survey.WorldSurveyPayload answer, Vector3 fixedPoint, Vector3 objective, float min, float max,
                                float arcHalfDegrees, out Vector3 at)
        {
            at = fixedPoint;
            var line = new Vector2(fixedPoint.X - objective.X, fixedPoint.Y - objective.Y);
            if (line.LengthSquared() < 1e-4f) return false;
            line = Vector2.Normalize(line);
            float cosArc = MathF.Cos(arcHalfDegrees * MathF.PI / 180f), best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < answer.Buildings.Count; i++)
            {
                var b = answer.Buildings[i];
                if (b.Solid) continue;
                var c = new Vector2(b.CentreX, b.CentreY);
                var fromObj = c - new Vector2(objective.X, objective.Y);
                float d = fromObj.Length();
                if (d < min || d > max) continue;
                if (Vector2.Dot(fromObj / d, line) < cosArc) continue;
                float toFixed = Vector2.Distance(c, new Vector2(fixedPoint.X, fixedPoint.Y));
                if (toFixed < best) { best = toFixed; at = new Vector3(c, 0f); found = true; }
            }
            return found;
        }
    }
}
