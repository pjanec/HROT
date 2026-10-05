using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Consumes <see cref="AssignBehaviorEvent"/>s and applies them to the relevant entities:
    /// <list type="number">
    ///   <item>Sets <see cref="BehaviorState.ActiveBehaviorHash"/> and <see cref="BehaviorState.BrainTier"/>.</item>
    ///   <item>Increments <see cref="BehaviorState.InstanceId"/> (deliberate wrapping via <c>unchecked</c>).
    ///         This bumps the preemption token so <see cref="ChannelArbitrationSystem"/> clears stale
    ///         channels on the next simulation tick.</item>
    ///   <item>Resets <see cref="BrainBTreeState.State"/> to <c>default</c> (execution pointer → 0).</item>
    ///   <item>Calls <see cref="BehaviorDefinition.ParseParams"/> to write blackboard parameters.</item>
    /// </list>
    ///
    /// Runs in <see cref="InputSystemGroup"/> so behavior changes are visible to all brain tick
    /// systems (which run in <see cref="SimulationSystemGroup"/>) within the same frame.
    ///
    /// <para>
    /// DEBT-035 fix: all ECS component writes (<see cref="BehaviorState"/>, <see cref="BrainBTreeState"/>)
    /// now happen AFTER <see cref="BehaviorDefinition.ParseParams"/> succeeds.  A parse failure leaves
    /// the entity entirely on its previous behavior — no partial transition.
    /// A stackalloc shadow copy of the blackboard is used so the live component is only updated
    /// when parsing succeeds, keeping the operation atomic from the ECS perspective.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class BehaviorIngressSystem : IEcsModuleSystem
    {
        private readonly BehaviorRegistry _registry;

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-307</c> — the transactional-parse shadow. A GROWABLE scratch buffer, and
        /// deliberately <b>not</b> a bound.</b>
        ///
        /// <para>🔴 <b>What it replaced, and why the replacement is the load-bearing half of
        /// <c>P4</c>-④.</b> This was <c>stackalloc byte[BehaviorConstants.BrainBlackboardByteSize]</c>
        /// — 100 bytes, the width of the component being retired. ⛔ A behaviour whose packed variable
        /// table exceeded 100 bytes would have had <see cref="BehaviorDefinition.ParseParams"/> write
        /// <b>past the end of a stack buffer</b>, and the carry-over seed silently truncate. ⚠ Neither
        /// was reachable — <b>only because <c>BehaviorParameterSizeAnalyzer</c> capped params at
        /// 100</b>. ⇒ 🔒 removing that cap before this was sized per-behaviour would have converted an
        /// impossible fault into a live one, which is why this change lands FIRST.</para>
        ///
        /// <para>⭐ <b>An instance field, not a <c>stackalloc</c>.</b> The width is a RUNTIME value now
        /// (<see cref="RootParamsAccess.RootParamsBytes"/> — 52 for <c>PlatoonHillAttack</c>, 16 for
        /// <c>MoveToLocation</c>), so a compile-time stack allocation cannot express it, and a
        /// <c>stackalloc</c> inside the event loop is the <c>CA2014</c> stack-overflow the original
        /// comment was avoiding. ⚠ Reused across events and frames — hence the unconditional
        /// <c>Clear()</c> at the seed — and it only ever grows, so the steady state allocates nothing.
        /// ⛔ <c>Execute</c> is not re-entrant (Input phase, one thread), which is what makes per-system
        /// scratch state safe here.</para>
        /// </summary>
        private byte[] _shadow = Array.Empty<byte>();

        /// <summary>
        /// The smallest shadow this will hand out. ⛔ <b>Not a cap and not a budget</b> — a floor, so
        /// that pinning the buffer can never yield a <c>null</c> <c>dst</c> for a
        /// <see cref="BehaviorDefinition.ParseParams"/> delegate. ⚠ Reachable only for a behaviour that
        /// declares a parser and a manifest whose extent computes to zero; every shipped behaviour
        /// declares either a manifest with real variables or a <c>BlackboardLayoutType</c>.
        /// </summary>
        private const int MinShadowBytes = 16;

        public BehaviorIngressSystem(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// ⭐ Widens <see cref="_shadow"/> to hold <paramref name="bytes"/> and returns exactly that
        /// window. ⚠ The returned span's <c>Length</c> is the authority for the seed clamp — ⛔ never
        /// <c>_shadow.Length</c>, which may be wider from a previous, larger behaviour.
        /// </summary>
        private Span<byte> EnsureShadow(int bytes)
        {
            int need = Math.Max(bytes, MinShadowBytes);
            if (_shadow.Length < need) _shadow = new byte[need];
            return _shadow.AsSpan(0, need);
        }

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BehaviorIngressSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            _startedByNameThisFrame.Clear();
            var events = repo.Bus.ReadManaged<AssignBehaviorEvent>();

            foreach (var evt in events)
            {
                if (evt == null) continue;
                if (!repo.HasComponent<BehaviorState>(evt.Entity)) continue;

                // DEBT-006: use stable int ID from registry — no GetHashCode().
                if (!_registry.TryGetId(evt.BehaviorName, out int behaviorId)) continue;
                if (!_registry.TryGetDefinition(behaviorId, out var def)) continue;

                // ⭐ CE-3034 — THE ONE GATE (R-188): a lower origin cannot replace a higher one.
                // ⭐ CE-2078 — and a reaction (R-199) pauses the task it replaces.
                if (!Admit(repo, evt.Entity, evt.Origin, evt.Urgency, out bool pause)) continue;
                var paused = pause ? PauseRecord(repo, evt.Entity) : null;
                if (!Start(repo, evt.Entity, evt.BehaviorName, behaviorId, def, evt.JsonParams, evt.Origin, evt.Urgency)) continue;
                AfterAdmitted(repo, evt.Entity, evt.Origin, paused);
                _startedByNameThisFrame[evt.Entity.Index] = behaviorId;
            }

            // ── ClearBehaviorEvent handler ────────────────────────────────────────────────
            // Forcibly resets the active behavior to BehaviorIds.None (brain-death).
            // Published top-down by MissionDirectorSystem (plan exhausted) and
            // MissionControlRequestSystem (CMD_ABORT_ALL).
            var clearEvents = repo.Bus.Read<ClearBehaviorEvent>();
            foreach (var evt in clearEvents)
            {
                if (!repo.HasComponent<BehaviorState>(evt.Entity)) continue;
                if (!Admit(repo, evt.Entity, evt.Origin, ReactionUrgency.NotAReaction, out _)) continue;   // ⭐ CE-3034 — a clear is gated like an assign
                bool endsAReaction = repo.GetComponentRO<BehaviorState>(evt.Entity).Origin == BehaviorOrigin.Reaction;
                Clear(repo, evt.Entity, _registry);
                // ⭐ CE-2078 — a reaction ending ITSELF resumes the task it paused; an order's clear drops it.
                if (evt.Origin == BehaviorOrigin.Self && endsAReaction) ResumePausedTask(repo, evt.Entity);
                else AfterAdmitted(repo, evt.Entity, evt.Origin, null);
            }

            // ── AssignBehaviorHashEvent handler ──────────────────────────────────────────
            // Activates a behavior by integer hash — published by MissionDirectorSystem on a phase advance.
            //
            // ⭐⭐ CE-451 (2026-09-30): THE SAME START PIPELINE as an assign by name. 🔴 This handler used to hand-write a
            //   partial copy — detach, bump, attach the tree state / HSM instance — that never provisioned the store nor
            //   attached the root PARAMS block and never parsed, so a behaviour WITH parameters assigned this way threw in
            //   RootParamsAccess.RootRef on every BTree / blueprint tick. ⇒ now one path: Start(), with no JSON ("{}" —
            //   authored defaults; the event carries none).
            // ⚠ On CGF a phase advance ALSO produces an assign BY NAME carrying the task's parameters
            //   (MissionAdapterSystem → tactical intent → TacticalIntentResolutionSystem). ⇒ when that one already started
            //   THIS behaviour on this entity in this Execute, the hash event is a duplicate and is dropped — the named one
            //   carries the parameters.
            var hashEvents = repo.Bus.Read<AssignBehaviorHashEvent>();
            foreach (var evt in hashEvents)
            {
                if (!repo.HasComponent<BehaviorState>(evt.Entity)) continue;
                if (_startedByNameThisFrame.TryGetValue(evt.Entity.Index, out int byName) && byName == evt.BehaviorHash)
                    continue;
                if (!Admit(repo, evt.Entity, evt.Origin, ReactionUrgency.NotAReaction, out bool pauseByHash)) continue;   // ⭐ CE-3034
                if (_registry.TryGetDefinition(evt.BehaviorHash, out var def)
                    && _registry.TryGetName(evt.BehaviorHash, out var name))
                {
                    var pausedByHash = pauseByHash ? PauseRecord(repo, evt.Entity) : null;
                    // ⭐ CE-456: the phase's own parameters, not "{}" — the hash event has no JSON, but the plan it came from does.
                    if (Start(repo, evt.Entity, name, evt.BehaviorHash, def, MissionPhaseParams(repo, evt.Entity, evt.BehaviorHash), evt.Origin))
                        AfterAdmitted(repo, evt.Entity, evt.Origin, pausedByHash);
                    continue;
                }

                // ⚠ NO DEFINITION ON THIS NODE ⇒ nothing to start. The pre-CE-451 bookkeeping is kept byte for byte —
                //   release the outgoing root slots, record the hash, bump the instance — because a node that does not
                //   host the behaviour still tracks WHICH one is active (rails MissionDirectorSystemTests.*). ⭐ Nothing
                //   ticks it: BrainTickSystem skips a hash with no definition.
                int previousBehaviorId = repo.GetComponentRO<BehaviorState>(evt.Entity).ActiveBehaviorHash;
                if (previousBehaviorId != BehaviorIds.None && previousBehaviorId != evt.BehaviorHash)
                {
                    RootHsmAccess.DetachRoot(repo, evt.Entity, previousBehaviorId);
                    RootStateAccess.DetachRoot(repo, evt.Entity, previousBehaviorId);
                    RootParamsAccess.DetachRoot(repo, evt.Entity, previousBehaviorId);
                }
                // ⭐ CE-485: the run ends here (InstanceId is bumped below) ⇒ its owned parts (EQS sensors) end with it. 📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D4.
                BehaviorOwnedParts.Release(repo, evt.Entity, repo.GetComponentRO<BehaviorState>(evt.Entity).InstanceId);
                ref var unhosted = ref repo.GetComponentRW<BehaviorState>(evt.Entity);
                unhosted.Origin = BehaviorOriginRank.AfterAssign(evt.Origin, unhosted);   // ⭐ CE-3034 — before the hash moves
                unhosted.Urgency = UrgencyAfterAssign(evt.Origin, ReactionUrgency.NotAReaction, unhosted.Urgency);
                unhosted.ActiveBehaviorHash = evt.BehaviorHash;
                unhosted.RunSince = Now(repo);   // ⭐ CE-2080
                unchecked { unhosted.InstanceId++; }
                AfterAdmitted(repo, evt.Entity, evt.Origin, null);
            }

            // ── ⭐ CE-3035 — the SOP slot's assign / clear ─────────────────────────────────────────────────────────────────
            ApplySopEvents(repo);
        }

        /// <summary>⭐ <c>CE-3034</c> — refusals by the gate since this system was built (a test / diagnostics probe).</summary>
        public int RefusedCount { get; private set; }

        /// <summary>
        /// ⭐⭐ <c>CE-3034</c> — THE ONE GATE (R-188, <c>DESIGN_Sensors_And_Doctrine.md</c> §6): may an assign / clear of
        /// <paramref name="origin"/> replace what <paramref name="entity"/> runs? Rule in <see cref="BehaviorOriginRank.Admits"/>.
        /// ⚠ The INTERNAL finish (<c>BrainTickSystem</c> → <see cref="Clear"/>) is not gated — a behaviour ending never needs
        /// permission. A refusal is counted and logged; it never throws.
        /// </summary>
        private bool Admit(EntityRepository repo, Entity entity, BehaviorOrigin origin, ReactionUrgency urgency, out bool pause)
        {
            ref readonly var running = ref repo.GetComponentRO<BehaviorState>(entity);
            if (AdmitsWithReactions(repo, entity, origin, urgency, running, out pause)) return true;
            RefusedCount++;
            if (origin == BehaviorOrigin.Sop) WakeSop(repo, entity);   // ⭐ CE-3035 / R-195 — a refused SOP assignment wakes it
            Fdp.Core.Logging.FdpLog<BehaviorIngressSystem>.Info(
                "[BehaviorIngress] refused {0} assignment for entity #{1}: it runs behaviour {2} at {3}.",
                origin, entity.Index, running.ActiveBehaviorHash, running.Origin);
            return false;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2078</c> — the gate with the four SOP rules (R-199, <c>docs/DESIGN_Decision_Layer.md</c> §4.1):
        /// <list type="number">
        /// <item>a task beats the SOP's idle choice — the rank rule (<see cref="BehaviorOriginRank.Admits(BehaviorOrigin, in BehaviorState)"/>);</item>
        /// <item>a reaction PAUSES the task (<paramref name="pause"/>) unless the ROE says <see cref="RoeReactions.StayOnTask"/>;
        ///   over an empty slot or the SOP's idle choice it simply starts;</item>
        /// <item>a running reaction yields only to a MORE urgent reaction, or to an order that may replace the task it paused;
        ///   the SOP's own idle choice never replaces it;</item>
        /// <item>at most one thing is paused — a reaction replacing a reaction leaves the paused task as it was.</item>
        /// </list>
        /// <see cref="BehaviorOrigin.Self"/> is always admitted (a reaction restarting itself stays a reaction).
        /// </summary>
        internal static bool AdmitsWithReactions(EntityRepository repo, Entity entity, BehaviorOrigin origin,
            ReactionUrgency urgency, in BehaviorState running, out bool pause)
        {
            pause = false;
            bool slotEmpty = running.ActiveBehaviorHash == BehaviorIds.None;
            if (origin == BehaviorOrigin.Reaction)
            {
                if (slotEmpty || running.Origin == BehaviorOrigin.Sop) return true;              // ② nothing to pause
                if (running.Origin == BehaviorOrigin.Reaction)                                   // ③ only a MORE urgent one
                    return EffectiveUrgency(urgency) > running.Urgency;
                if (RoeOf.Reactions(repo, entity) == RoeReactions.StayOnTask) return false;      // ② the order forbids it
                pause = true;                                                                    // ② the task waits
                return true;
            }
            if (!slotEmpty && running.Origin == BehaviorOrigin.Reaction && origin != BehaviorOrigin.Self)
            {
                if (origin == BehaviorOrigin.Sop) return false;                                  // ③ the idle choice waits
                var paused = PausedTaskOf(repo, entity);
                return paused == null                                                            // ③ an order: weighed against
                    || BehaviorOriginRank.Of(origin) >= BehaviorOriginRank.Of(paused.Origin);    //   the task it would end
            }
            return BehaviorOriginRank.Admits(origin, running);                                   // ① the rank rule
        }

        /// <summary>⭐ <c>CE-2080</c> — sim time now (<see cref="GlobalTime.TotalTime"/>; 0 on a world without the clock).</summary>
        private static double Now(EntityRepository repo)
            => repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;

        /// <summary>⭐ <c>CE-2078</c> — a reaction published with no urgency counts as <see cref="ReactionUrgency.Alert"/>.</summary>
        private static ReactionUrgency EffectiveUrgency(ReactionUrgency urgency)
            => urgency == ReactionUrgency.NotAReaction ? ReactionUrgency.Alert : urgency;

        /// <summary>⭐ <c>CE-2078</c> — the urgency the slot carries after an admitted assignment: a reaction's own;
        /// <see cref="BehaviorOrigin.Self"/> keeps the running one; anything else is not a reaction.</summary>
        private static ReactionUrgency UrgencyAfterAssign(BehaviorOrigin origin, ReactionUrgency urgency, ReactionUrgency running)
            => origin switch
            {
                BehaviorOrigin.Reaction => EffectiveUrgency(urgency),
                BehaviorOrigin.Self     => running,
                _                       => ReactionUrgency.NotAReaction,
            };

        /// <summary>⭐ <c>CE-2078</c> — what a reaction is about to pause: the running task's start record and origin, read
        /// BEFORE the start replaces them. <c>null</c> when the task was stamped directly (no start record) — it cannot be
        /// restarted, so it is simply replaced.</summary>
        private static PausedTask? PauseRecord(EntityRepository repo, Entity entity)
        {
            ref readonly var running = ref repo.GetComponentRO<BehaviorState>(entity);
            if (!repo.HasManagedComponent<BehaviorStartRecord>(entity)) return null;
            var record = ((ISimulationView)repo).GetManagedComponentRO<BehaviorStartRecord>(entity);
            if (record == null || record.InstanceId != running.InstanceId) return null;
            return new PausedTask { BehaviorName = record.BehaviorName, JsonParams = record.JsonParams, Origin = running.Origin };
        }

        /// <summary>⭐ <c>CE-2078</c> — after an admitted start / clear: a reaction that paused a task records it; an ORDER
        /// (not <see cref="BehaviorOrigin.Self"/>, not a reaction) ends whatever was paused — it replaced the task.</summary>
        private void AfterAdmitted(EntityRepository repo, Entity entity, BehaviorOrigin origin, PausedTask? paused)
        {
            if (paused != null)
            {
                if (!ReferenceEquals(_pausedRegisteredOn, repo))
                {
                    repo.RegisterManagedComponent<PausedTask>();
                    _pausedRegisteredOn = repo;
                }
                repo.SetManagedComponent(entity, paused);
                return;
            }
            if (origin != BehaviorOrigin.Self && origin != BehaviorOrigin.Reaction) DropPausedTask(repo, entity);
        }

        private EntityRepository? _pausedRegisteredOn;

        /// <summary>⭐ <c>CE-2078</c> — the task a reaction paused on <paramref name="entity"/>, or <c>null</c>.</summary>
        public static PausedTask? PausedTaskOf(EntityRepository repo, Entity entity)
            => repo.HasManagedComponent<PausedTask>(entity) ? ((ISimulationView)repo).GetManagedComponentRO<PausedTask>(entity) : null;

        private static void DropPausedTask(EntityRepository repo, Entity entity)
        {
            if (repo.HasManagedComponent<PausedTask>(entity)) repo.SetManagedComponent<PausedTask>(entity, null!);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2078</c> (R-199 ②) — a reaction ENDED (finished, or cleared itself): restart the task it paused, through
        /// the gate, with the parameters and the origin it had — published, so next frame's ingress starts it like any order.
        /// ⚠ Restart, not resume (<c>CE-2081</c>): the task begins again from its root. A no-op when nothing was paused.
        /// </summary>
        internal static void ResumePausedTask(EntityRepository repo, Entity entity)
        {
            var paused = PausedTaskOf(repo, entity);
            if (paused == null) return;
            DropPausedTask(repo, entity);
            if (!repo.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) return;
            repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = entity, BehaviorName = paused.BehaviorName, JsonParams = paused.JsonParams, Origin = paused.Origin,
            });
        }

        /// <summary>
        /// ⭐ <b><c>CE-456</c> — the parameters of the mission phase an <see cref="AssignBehaviorHashEvent"/> starts.</b>
        /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.16.7.
        /// <para>The hash event's ONE producer is <c>MissionDirectorSystem</c> advancing a <see cref="MissionPlanQueue"/>, and
        /// the event carries no JSON (it is unmanaged). The plan it advanced still holds the text: the entity's
        /// <see cref="ActiveMissionPlan"/> task at the queue's current phase. ⭐ This is what makes a phase behaviour run on its
        /// task's parameters on EVERY host — ⛔ before, only CGF did, through <c>MissionAdapterSystem</c>'s named assign
        /// (which the hash assign then yields to); everywhere else the phase ran on its authored defaults.</para>
        /// <para>Returns <c>"{}"</c> when there is no plan, the phase is out of range, or the task names a DIFFERENT behaviour
        /// (a plan edited under the queue) — never another behaviour's parameters.</para>
        /// </summary>
        internal string MissionPhaseParams(EntityRepository repo, Entity entity, int behaviorHash)
        {
            if (!repo.IsComponentTypeRegistered<MissionPlanQueue>() || !repo.HasComponent<MissionPlanQueue>(entity)
                || !repo.HasManagedComponent<ActiveMissionPlan>(entity))
                return "{}";
            int phase = repo.GetComponentRO<MissionPlanQueue>(entity).CurrentPhase;
            var tasks = ((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(entity)?.Plan?.Tasks;
            if (tasks == null || phase >= tasks.Count) return "{}";
            var task = tasks[phase];
            if (string.IsNullOrWhiteSpace(task.BehaviorName)
                || !_registry.TryGetId(task.BehaviorName, out int taskHash) || taskHash != behaviorHash)
                return "{}";
            return string.IsNullOrWhiteSpace(task.BehaviorParams) ? "{}" : task.BehaviorParams;
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-452</c> / <c>CE-451</c> — THE ONE START PIPELINE.</b> Every way a root behaviour starts runs this:
        /// an assign by name, an assign by hash (<c>CE-451</c> — it used to hand-write a partial copy that provisioned no
        /// params block), and a hot-reload restart (<c>CE-452</c> — which re-publishes an assign by name, so it lands here).
        /// 🔒 User, <c>2026-09-30</c>: <i>"i hope you are reusing whatever init/setup code there is"</i>.
        /// <para>Parse into the shadow → commit <c>BehaviorState</c> → provision → detach the previous behaviour's slots →
        /// attach the root params / tree state / HSM instance → record what it was started with.</para>
        /// </summary>
        /// <returns><c>false</c> when the parse failed — the entity stays on its previous behaviour entirely.</returns>
        private unsafe bool Start(
            EntityRepository repo, Entity entity, string behaviorName, int behaviorId, BehaviorDefinition def, string json,
            BehaviorOrigin origin, ReactionUrgency urgency = ReactionUrgency.NotAReaction)
        {
            // DEBT-035 fix: attempt ParseParams BEFORE writing BehaviorState/BrainBTreeState.
            // Strategy: parse into stack memory and commit only on success, so a ParseParams
            // failure leaves the entity 100% on the old behavior.
            //
            // ⭐⭐⭐ P3-C — THE CLEAN CUT (2026-09-21). 🔴 The shadow used to be copied FROM and
            //   committed back TO a per-entity BrainBlackboard component. Both halves are gone:
            //   the parsed bytes now land in the entity's ROOT PARAMS OCCURRENCE SLOT, which is
            //   attached further down once the store is provisioned (§29.6 / §29.7 P3-C).
            //
            // ⛔ NOT a dual write. R-132's second producer, and in this codebase a temporary one
            //   becomes permanent — the user ruled CLEAN CUT for exactly that reason.
            // ⭐⭐⭐ CE-307 — THE SHADOW IS SIZED BY THE BEHAVIOUR, NOT BY A CONSTANT.
            //   Hoisted here because the shadow's width IS this number: the region the parser may
            //   write is the packed variable table's extent, and that is what lands in the slot
            //   below. ⛔ It used to be BrainBlackboardByteSize (100) — the OLD component's width —
            //   which made a >100-byte behaviour a STACK SMASH in ParseParams and a silent
            //   truncation in the carry-over. Impossible only because the analyzer capped at 100.
            // ⭐⭐⭐ CE-416 ② (2026-10-02) — ONE predicate for "this behaviour has a root block": its WIDTH.
            //   🔴 This used to be gated on `ParseParams != null` while BrainTickSystem (and RootParamsAccess)
            //   gate on RootParamsBytes(def) > 0 — so a behaviour with a layout and no parser (the curated
            //   `JoinFormation`) got NO block here and the very next tick THREW "no ROOT PARAMS slot". Measured
            //   through the real curated registrar, ingress and tick. ⇒ the block exists iff it has a width;
            //   the PARSE is the optional part (baked defaults, or zeros, when there is no parser).
            // ⭐ CE-3035: storage is keyed by behaviour, so the task and the SOP must be different behaviours.
            if (IsTheSop(repo, entity, behaviorId))
            {
                RefusedCount++;
                Fdp.Core.Logging.FdpLog<BehaviorIngressSystem>.Info(
                    "[BehaviorIngress] refused task {0} for entity #{1}: it is the unit's SOP.", behaviorName, entity.Index);
                return false;
            }
            if (!ParseIntoShadow(repo, entity, def, json, out int rootBytes, out Span<byte> shadow)) return false;

            // ParseParams succeeded (or was not required). Commit behavior transition.

            // 1. Update BehaviorState.
            // Read previous behavior hash before overwriting (needed for S2-2 detach).
            int previousBehaviorId = repo.GetComponentRW<BehaviorState>(entity).ActiveBehaviorHash;
            // ⭐ CE-485: the run ends here (InstanceId is bumped below) ⇒ its owned parts (EQS sensors) end with it. 📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D4.
            //   ⚠ Also on a re-assign of the SAME behaviour: a new run asks its own questions.
            BehaviorOwnedParts.Release(repo, entity, repo.GetComponentRO<BehaviorState>(entity).InstanceId);
            ref var behavior = ref repo.GetComponentRW<BehaviorState>(entity);
            // ⭐ CE-3034: record WHO started it (Self keeps the running origin) — read while the old run is still current.
            behavior.Urgency = UrgencyAfterAssign(origin, urgency, behavior.Urgency);   // ⭐ CE-2078 — before Origin moves
            behavior.RunSince = Now(repo);                                               // ⭐ CE-2080
            behavior.Origin = BehaviorOriginRank.AfterAssign(origin, behavior);
            behavior.ActiveBehaviorHash = behaviorId;
            // Intentional unsigned wrap — InstanceId is a monotonic preemption token.
            unchecked { behavior.InstanceId++; }
            behavior.BrainTier = def.BrainTier;
            // ⛔ P4-① (2026-09-22): the Blackboard1024 add is GONE with the component. It was
            //    gated on `def.HeavyDtoType != null`, which is null at every production site and
            //    in all 30 shipped assets ⇒ this branch never ran. 📄 §30.13.

            // S2-2: Synchronously provision stateful working-state partition slots.
            // Must happen BEFORE the same frame's Simulation tick (§10 Flaw 1 fix).
            // ⭐ CE-431 + S5b — the manifest as provisioned: hosted slots sized from their child, and every nested
            //   descendant under its own key. The sweep below must see the SAME list, or it detaches the nested slots.
            var effectiveSlots = def.StatefulWorkingSlots is { Count: > 0 } ? HostedSubtree.EffectiveSlots(def.StatefulWorkingSlots) : null;
            if (def.StatefulWorkingSlots != null && def.StatefulWorkingSlots.Count > 0)
            {
                // Detach previous behavior's slots to avoid leaking them.
                if (previousBehaviorId != BehaviorIds.None &&
                    previousBehaviorId != behaviorId &&
                    _registry.TryGetDefinition(previousBehaviorId, out var prevDef) &&
                    prevDef.StatefulWorkingSlots != null && prevDef.StatefulWorkingSlots.Count > 0)
                {
                    DetachStatefulSlots(repo, entity, HostedSubtree.EffectiveSlots(prevDef.StatefulWorkingSlots));   // ⭐ S5b — nested slots too
                }

                // A3/D1': declare WHAT these occurrences are, so O0's walker can filter on a
                // declared Kind instead of on a BlueprintRegistry miss (F7 -- an accident, not
                // a filter). The behaviour's tier IS the kind for its stateful working slots.
                // O7b-3: the behaviour's HOSTED occurrences need room in the same tier.
                // CE-302: and so does the ROOT PARAMS slot attached a few lines below.
                _registry.TryGetHostedOccurrenceDemand(behaviorName, out var hosted);
                hosted = WithDescendantDemand(hosted, def);   // ⭐ S5b — hosted children's lazy occurrences need room too
                // ⭐ CE-431: hosted child slots sized from the CHILD's definition — known only now.
                ProvisionStatefulSlots(repo, entity, effectiveSlots!, KindOf(def),
                                       hosted, RootParamsCost(def), RootBrainStateCost(def));
            }
            else if (HasSop(repo, entity))
            {
                // ⭐ CE-3035: the SOP's slots share the store ⇒ size by FREE space, which counts them (capacity does not).
                _registry.TryGetHostedOccurrenceDemand(behaviorName, out var hosted);
                ProvisionStatefulSlots(repo, entity, Array.Empty<StatefulSlotInfo>(), KindOf(def),
                                       hosted, RootParamsCost(def), RootBrainStateCost(def));
            }
            else
            {
                _registry.TryGetHostedOccurrenceDemand(behaviorName, out var hosted);
                EnsureOccurrenceStore(repo, entity, def, hosted, RootParamsCost(def), RootBrainStateCost(def));
            }

            // E3a: drop the PREVIOUS assign's lazily-attached hosted occurrences, so their params
            // re-seed from the JSON just parsed. ⛔ Omitting this makes new JSON a no-op (§28.4).
            DetachHostedOccurrenceSlots(repo, entity, effectiveSlots, _registry);

            // ⭐ CE-302: and the PREVIOUS behaviour's ROOT PARAMS slot, which the sweep above
            //   cannot reach on a BTree brain — its kind is BTree, not Hsm/Blueprint. ⛔ Without
            //   this, every behaviour change leaks one slot, and an entity reassigned a few times
            //   exhausts MaxSlots (3 on the 256 tier) and then silently loses its params.
            if (previousBehaviorId != BehaviorIds.None && previousBehaviorId != behaviorId)
            {
                RootParamsAccess.DetachRoot(repo, entity, previousBehaviorId);
                // ⭐⭐ O7c-② / CE-319: the root TREE STATE slot leaks the same way and for the same
                //   reason — its kind is BTree, so DetachHostedOccurrenceSlots cannot see it either.
                RootStateAccess.DetachRoot(repo, entity, previousBehaviorId);
            }

            // ⭐⭐⭐ P3 — THE ROOT BEHAVIOUR'S PARAMS GET THEIR OWN SLOT.
            //   §29.6: ONE slot holds the WHOLE packed table, exactly as BehaviorParameters does,
            //   so every per-state seed offset (E3b-0) keeps meaning what it means. ⛔ It is NOT a
            //   scatter — scattering would destroy that indexing.
            //
            // ⚠ It must run AFTER provisioning: the occurrence store is what we attach into, and
            //   ProvisionStatefulSlots/EnsureOccurrenceStore above is what guarantees one exists.
            //
            // 🔴🔴 AND AFTER DetachHostedOccurrenceSlots — measured 2026-09-21, this ordering is
            //   LOAD-BEARING and the first version had it wrong. An HSM behaviour's root slot is
            //   attached with OccurrenceKind.Hsm (KindOf follows the brain tier, A3/D1′), and the
            //   sweep detaches exactly "kind Hsm|Blueprint and not named by the manifest" ⇒ it
            //   swept the root slot away on the very same assign that created it. ⛔ Harmless only
            //   while the blackboard commit still ran; after P3-C it is total params loss on every
            //   HSM brain. ⚠ Do NOT move this block back above the sweep.
            if (rootBytes > 0)   // ⭐ CE-416 ②: the block's existence, not the parser's (see the shadow above)
            {
                // ⚠ CE-307: `rootBytes` is the SAME value the shadow was sized from, hoisted to
                //   the top of this iteration. ⛔ Recomputing it here would let the two drift.
                if (rootBytes > 0)
                {
                    byte* rootParams = RootParamsAccess.ResolveOrAttachRoot(
                        repo, entity, behaviorId, rootBytes, KindOf(def), out _);

                    // ⛔⛔ A null here USED TO BE TOLERABLE — the blackboard still carried the
                    //   params, so the entity ran correctly and the miss was invisible. 🔴 After
                    //   P3-C the slot is the ONLY home, so tolerating it means the behaviour runs
                    //   on an all-zero params region: it does not crash, it just quietly does the
                    //   wrong thing. ⇒ THROW, and name the two causes.
                    // ⚠ This is the one place the toolkit's narrow contract (E-cap §27.2 — skip
                    //   when the tier components are not registered) becomes visible to a host. It
                    //   is deliberate: skipping is fine for a behaviour that merely MIGHT host an
                    //   occurrence, and is not fine for one that HAS parameters.
                    if (rootParams == null)
                        throw new InvalidOperationException(
                            $"Behaviour '{behaviorName}' parses {rootBytes} bytes of parameters, " +
                            $"but entity {entity.Index} has nowhere to put them. Either the " +
                            "BlueprintBlackboard* tier components are not registered on this world " +
                            "(Hrot registers them in HrotSharedComponentRegistry; a bare test world " +
                            "needs BlueprintTierTable.RegisterAll), or the entity's store had no " +
                            "room, which means the tier demand under-counted (CE-302).");

                    fixed (byte* src = shadow)
                        Buffer.MemoryCopy(src, rootParams, rootBytes, rootBytes);
                }
            }

            // 2. ⭐⭐⭐ O7c-② / CE-319 — ATTACH the root tree state, then reset the cursor so the new
            //    behaviour starts from the root. 📄 §31.
            //
            //    ⚠ ORDERING, and it is the same rule CE-302 paid for on the params path: this runs
            //      AFTER provisioning (the store must exist to attach into) and AFTER
            //      DetachHostedOccurrenceSlots (which sweeps kind Hsm|Blueprint — a BTree root slot
            //      is invisible to it, but the ordering is kept uniform so the two root slots cannot
            //      drift apart).
            //
            //    ⭐ TryAttach ZEROES a fresh payload, so the reset is redundant on first attach — but
            //      NOT on the idempotent path, where re-assigning the SAME behaviour finds its slot
            //      already there. ⛔ The component version reset unconditionally; so does this.
            if (RootStateAccess.RootStateBytes(def) > 0)
            {
                RootStateAccess.ResolveOrAttachRoot(repo, entity, behaviorId, KindOf(def), out _,
                    RootStateAccess.RootStateBytes(def));   // ⭐ S2 — a blueprint's Exec is not a 64-byte tree cursor
                RootStateAccess.ResetState(repo, entity);
            }
            ResetHostedTreeStates(repo, entity, def);

            // 3. ⭐⭐⭐ O7c-④ — ATTACH the root HSM instance, then BIND it to the new behaviour's
            //    topology. 📄 §31.14.
            //
            //    BHU-016 / CRITICAL FIX, unchanged in substance: InstanceHeader.MachineId must
            //    equal the new blob's MachineId (CE-2001) or HsmKernelCore.ValidateInstance rejects the
            //    instance on every subsequent tick — silently, by `continue`.
            //
            //    🔴🔴 ORDERING IS LOAD-BEARING, AND IN THE OPPOSITE DIRECTION FROM THE BTREE ROOT.
            //      This slot declares OccurrenceKind.Hsm, so DetachHostedOccurrenceSlots — which
            //      sweeps exactly "kind Hsm|Blueprint and not named by the manifest" — CAN see it,
            //      and a root key is never in a manifest. ⇒ attaching before that sweep would
            //      remove the slot on the very assign that created it, which is the defect the
            //      root PARAMS path already paid for once on HSM brains. ⛔ Do NOT move this block
            //      above DetachHostedOccurrenceSlots.
            if (def.BrainTier == BehaviorConstants.BrainTierHsm && def.HsmDefinition != null)
            {
                RootHsmAccess.ResolveOrAttachRoot(
                    repo, entity, behaviorId,
                    RootHsmAccess.InstanceBytes(def.HsmDefinition), KindOf(def), out _);
                RootHsmAccess.ResetInstance(repo, entity, def.HsmDefinition);
            }

            // ⭐ CE-452: what it was started with, for a hot-reload restart. ⭐ Registered on first use (idempotent), as
            //   SetSingletonManaged does — a behaviour host must not have to know this component exists.
            if (!ReferenceEquals(_startRecordRegisteredOn, repo))
            {
                repo.RegisterManagedComponent<BehaviorStartRecord>();
                _startRecordRegisteredOn = repo;
            }
            repo.SetManagedComponent(entity, new BehaviorStartRecord
            {
                BehaviorName = behaviorName,
                JsonParams   = json,
                InstanceId   = repo.GetComponentRO<BehaviorState>(entity).InstanceId,
            });
            return true;
        }

        /// <summary>
        /// ⭐ <c>CE-3035</c> — STAGE 0–2 of every start (extracted, not copied, so the task slot and the SOP slot parse the
        /// same way): clear → bake defaults → parse / resolve the JSON into the reused shadow. <c>false</c> = the parse
        /// failed and nothing may be committed. 📄 <c>DESIGN_Parameter_Model.md</c> §P.
        /// </summary>
        private unsafe bool ParseIntoShadow(EntityRepository repo, Entity entity, BehaviorDefinition def, string json,
                                            out int rootBytes, out Span<byte> shadow)
        {
            rootBytes = RootParamsAccess.RootParamsBytes(def);
            shadow = rootBytes > 0 ? EnsureShadow(rootBytes) : default;

            if (rootBytes > 0 && def.ParseParams == null)
            {
                shadow.Clear();
                if (def.BakeDefaults != null)
                    fixed (byte* dst = shadow) def.BakeDefaults(dst, shadow.Length);
            }
            else if (def.ParseParams != null)
            {
                // ⭐⭐⭐ CE-421 + CE-426 (2026-09-29) — STAGE 0: THE SHADOW STARTS EMPTY, ALWAYS.
                //   🔒 User, 2026-09-28: "why would re-assigning the same behaviour deserve special
                //   handling, this happens rarely (certainly not every tick or two)" ⇒ always
                //   Clear() → BAKE the whole block's defaults → OVERLAY the JSON → RESOLVE. An
                //   unmentioned variable lands on its AUTHORED DEFAULT — predictable, inspectable in
                //   the editor, and identical on a first assign and on a re-assign (Q76 §12.3).
                // ⛔⛔ HISTORY — this used to SEED the shadow from the previous root slot, so a variable
                //   with no default that the JSON did not mention kept the PREVIOUS behaviour's bytes,
                //   reinterpreted as its own type (CE-421). CE-437 then kept the whole block on a
                //   same-behaviour re-assign — exactly the gate CE-421's ruling had rejected, built
                //   without reading that row. Both are gone.
                // 📐 Measured before deleting: every production publisher of AssignBehaviorEvent sends
                //   a COMPLETE parameter set (MissionAdapter via TacticalIntentResolution, the three
                //   maneuver mappers) — none relies on a partial re-assign keeping untouched values.
                // ⚠ Clear() is still load-bearing on its own: the buffer is REUSED across events and
                //   frames, so a stale event's bytes would otherwise leak into this one.
                shadow.Clear();

                // Attempt parse on the shadow.
                bool parseOk;
                fixed (byte* dst = shadow)
                {
                    try
                    {
                        // ⭐ G1/E7 — `host` is null: this is a ROOT behaviour, which is its
                        //   defined value (DESIGN_Parameter_Model.md §3.4). A HOSTED occurrence
                        //   will pass its host's variable access here, at E7a, without another
                        //   signature change.
                        // ⭐⭐ CE-331 (2026-09-23): the parser is TOLD how much room it has.
                        //   ⚠ `shadow.Length`, not `rootBytes` — the shadow IS the writable
                        //   region, and handing anything wider would license the overrun this
                        //   parameter exists to stop.
                        def.ParseParams(json, dst, shadow.Length, repo, entity);
                        parseOk = true;
                    }
                    catch (Exception ex)
                    {
                        // Suppress — do NOT rethrow; a parse failure must not crash the loop.
                        _ = ex;
                        parseOk = false;
                    }
                }

                if (!parseOk) return false; // ParseParams failed — the caller keeps the previous behaviour entirely.
            }
            return true;
        }

        // ════ ⭐⭐ CE-3035 — THE SOP SLOT (R-189, R-198) ═══════════════════════════════════════════════════════════════════
        //   📄 docs/DESIGN_Sensors_And_Doctrine.md §6–§7 · docs/DESIGN_Decision_Layer.md §4. The SOP is a SECOND behaviour on the
        //   unit: its own SopState, its own storage (keyed by its own behaviour — the task and the SOP must differ), its own
        //   run tokens (high bit), started through THIS pipeline's parse and the same root attach, and run by the same runners
        //   inside a BrainSlotScope. It acts only by ASSIGNING behaviours (Do when idle / React, CE-2079).

        /// <summary>⭐ <c>CE-3035</c> — refused SOP assignments / clears since this system was built (a test / diagnostics probe).</summary>
        public int SopRefusedCount { get; private set; }

        private EntityRepository? _sopRecordRegisteredOn;

        private void ApplySopEvents(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<SopState>()) return;

            foreach (var evt in repo.Bus.ReadManaged<AssignSopEvent>())
            {
                if (evt == null || !repo.IsAlive(evt.Entity)) continue;
                if (!_registry.TryGetId(evt.BehaviorName, out int id) || !_registry.TryGetDefinition(id, out var def))
                {
                    SopRefusedCount++;
                    Fdp.Core.Logging.FdpLog<BehaviorIngressSystem>.Info(
                        "[BehaviorIngress] refused SOP '{0}' for entity #{1}: no such behaviour on this node.", evt.BehaviorName, evt.Entity.Index);
                    continue;
                }
                StartSop(repo, evt.Entity, evt.BehaviorName, id, def, evt.JsonParams ?? "{}", evt.Origin);
            }

            foreach (var evt in repo.Bus.Read<ClearSopEvent>())
            {
                if (!repo.HasComponent<SopState>(evt.Entity)) continue;
                var sop = repo.GetComponentRO<SopState>(evt.Entity);
                if (!BehaviorOriginRank.Admits(evt.Origin, sop.SopHash, sop.SopOrigin)) { SopRefusedCount++; continue; }
                EndSop(repo, evt.Entity, _registry);
            }
        }

        /// <summary>⭐ <c>CE-2084</c> (§4.5 ①) — an SOP must not drive a channel: a behaviour KNOWN to command one is refused
        /// at assign time (unknown is allowed; the runtime <c>ChannelGuard</c> is the backstop).</summary>
        public static bool DrivesAChannel(BehaviorDefinition def) => def.WritesChannels is { Count: > 0 };

        // ── ⭐ CE-3043 — the editor's PAUSED edits: the SAME gate and start pipeline, applied at once ────────────────────
        //   📄 docs/DESIGN_Sensors_And_Doctrine.md §7.6. Nothing ticks while the editor is paused, so an author's change
        //   must not wait for a frame; ⛔ these are NOT a second start path — each is exactly what the matching event does.

        /// <summary>What an <see cref="AssignBehaviorEvent"/> does, now. <c>false</c> = refused (gate, unknown name, parse).</summary>
        public bool AssignNow(EntityRepository repo, Entity entity, string behaviorName, string json, BehaviorOrigin origin)
        {
            if (!repo.IsAlive(entity) || !repo.HasComponent<BehaviorState>(entity)) return false;
            if (!_registry.TryGetId(behaviorName, out int id) || !_registry.TryGetDefinition(id, out var def)) return false;
            if (!Admit(repo, entity, origin, ReactionUrgency.NotAReaction, out bool pause)) return false;
            var paused = pause ? PauseRecord(repo, entity) : null;
            if (!Start(repo, entity, behaviorName, id, def, string.IsNullOrWhiteSpace(json) ? "{}" : json, origin)) return false;
            AfterAdmitted(repo, entity, origin, paused);
            return true;
        }

        /// <summary>What a <see cref="ClearBehaviorEvent"/> does, now.</summary>
        public bool ClearNow(EntityRepository repo, Entity entity, BehaviorOrigin origin)
        {
            if (!repo.IsAlive(entity) || !repo.HasComponent<BehaviorState>(entity)) return false;
            if (!Admit(repo, entity, origin, ReactionUrgency.NotAReaction, out _)) return false;
            Clear(repo, entity, _registry);
            AfterAdmitted(repo, entity, origin, null);
            return true;
        }

        /// <summary>What an <see cref="AssignSopEvent"/> does, now.</summary>
        public bool AssignSopNow(EntityRepository repo, Entity entity, string behaviorName, string json, BehaviorOrigin origin)
        {
            if (!repo.IsAlive(entity) || !repo.IsComponentTypeRegistered<SopState>()) return false;
            if (!_registry.TryGetId(behaviorName, out int id) || !_registry.TryGetDefinition(id, out var def)) return false;
            return StartSop(repo, entity, behaviorName, id, def, string.IsNullOrWhiteSpace(json) ? "{}" : json, origin);
        }

        /// <summary>What a <see cref="ClearSopEvent"/> does, now.</summary>
        public bool ClearSopNow(EntityRepository repo, Entity entity, BehaviorOrigin origin)
        {
            if (!repo.IsAlive(entity) || !repo.IsComponentTypeRegistered<SopState>() || !repo.HasComponent<SopState>(entity)) return false;
            var sop = repo.GetComponentRO<SopState>(entity);
            if (!BehaviorOriginRank.Admits(origin, sop.SopHash, sop.SopOrigin)) { SopRefusedCount++; return false; }
            EndSop(repo, entity, _registry);
            return true;
        }

        /// <summary>
        /// ⭐ <c>CE-3035</c> — start <paramref name="def"/> in the unit's SOP slot: gate (against the SOP's own origin), refuse
        /// the unit's current task, parse (the same <see cref="ParseIntoShadow"/>), end the previous SOP run, make room beside
        /// the task (by FREE space), attach the SOP's roots, reset them inside its <see cref="BrainSlotScope"/>, record the start.
        /// </summary>
        internal unsafe bool StartSop(EntityRepository repo, Entity entity, string behaviorName, int behaviorId,
                                       BehaviorDefinition def, string json, BehaviorOrigin origin)
        {
            if (!repo.HasComponent<SopState>(entity)) repo.AddComponent(entity, new SopState());
            var before = repo.GetComponentRO<SopState>(entity);

            if (!BehaviorOriginRank.Admits(origin, before.SopHash, before.SopOrigin))
            {
                SopRefusedCount++;
                Fdp.Core.Logging.FdpLog<BehaviorIngressSystem>.Info(
                    "[BehaviorIngress] refused {0} SOP '{1}' for entity #{2}: its SOP was set at {3}.", origin, behaviorName, entity.Index, before.SopOrigin);
                return false;
            }
            if (repo.HasComponent<BehaviorState>(entity) && repo.GetComponentRO<BehaviorState>(entity).ActiveBehaviorHash == behaviorId)
            {
                SopRefusedCount++;
                Fdp.Core.Logging.FdpLog<BehaviorIngressSystem>.Info(
                    "[BehaviorIngress] refused SOP '{0}' for entity #{1}: it is the unit's current task.", behaviorName, entity.Index);
                return false;
            }
            if (DrivesAChannel(def))   // ⭐ CE-2084
            {
                SopRefusedCount++;
                Fdp.Core.Logging.FdpLog<BehaviorIngressSystem>.Warn(
                    "[BehaviorIngress] refused SOP '{0}' for entity #{1}: it commands a channel ({2}) — an SOP must not move or fire the unit; use React to start a behaviour that does.",
                    behaviorName, entity.Index, string.Join(", ", def.WritesChannels!));
                return false;
            }
            if (!ParseIntoShadow(repo, entity, def, json, out int rootBytes, out Span<byte> shadow))
            {
                SopRefusedCount++;
                return false;
            }

            EndSop(repo, entity, _registry);   // the previous SOP run ends here (its parts and slots go with it)
            uint run = SopTokens.Next();

            _registry.TryGetHostedOccurrenceDemand(behaviorName, out var hosted);
            hosted = WithDescendantDemand(hosted, def);
            var effectiveSlots = def.StatefulWorkingSlots is { Count: > 0 } ? HostedSubtree.EffectiveSlots(def.StatefulWorkingSlots) : null;
            ProvisionStatefulSlots(repo, entity, effectiveSlots ?? (IReadOnlyList<StatefulSlotInfo>)Array.Empty<StatefulSlotInfo>(),
                                   KindOf(def), hosted, RootParamsCost(def), RootBrainStateCost(def));

            if (rootBytes > 0)
            {
                byte* rootParams = RootParamsAccess.ResolveOrAttachRoot(repo, entity, behaviorId, rootBytes, KindOf(def), out _);
                if (rootParams == null)
                {
                    SopRefusedCount++;
                    Fdp.Core.Logging.FdpLog<BehaviorIngressSystem>.Warn(
                        "[BehaviorIngress] SOP '{0}' for entity #{1}: no room for its parameters beside the task.", behaviorName, entity.Index);
                    return false;
                }
                fixed (byte* src = shadow)
                    Buffer.MemoryCopy(src, rootParams, rootBytes, rootBytes);
            }

            ref var sop = ref repo.GetComponentRW<SopState>(entity);
            sop.SopHash       = behaviorId;
            sop.SopInstanceId = run;
            sop.SopBrainTier  = def.BrainTier;
            sop.SopOrigin     = BehaviorOriginRank.AfterAssign(origin, before.SopOrigin);
            sop.SopFaulted    = 0;
            sop.SopWake       = 1;   // decide at once
            sop.SopNextTick   = 0;

            using (BrainSlotScope.Enter(entity, behaviorId, run))
            {
                if (RootStateAccess.RootStateBytes(def) > 0)
                {
                    RootStateAccess.ResolveOrAttachRoot(repo, entity, behaviorId, KindOf(def), out _, RootStateAccess.RootStateBytes(def));
                    RootStateAccess.ResetState(repo, entity);
                }
                ResetHostedTreeStates(repo, entity, def);
                if (def.BrainTier == BehaviorConstants.BrainTierHsm && def.HsmDefinition != null)
                {
                    RootHsmAccess.ResolveOrAttachRoot(repo, entity, behaviorId,
                        RootHsmAccess.InstanceBytes(def.HsmDefinition), KindOf(def), out _);
                    RootHsmAccess.ResetInstance(repo, entity, def.HsmDefinition);
                }
            }

            if (!ReferenceEquals(_sopRecordRegisteredOn, repo))
            {
                if (!repo.TryGetTable(typeof(SopStartRecord), out _)) repo.RegisterManagedComponent<SopStartRecord>();
                _sopRecordRegisteredOn = repo;
            }
            repo.SetManagedComponent(entity, new SopStartRecord { BehaviorName = behaviorName, JsonParams = json, InstanceId = run });
            return true;
        }

        /// <summary>⭐ <c>CE-3035</c> — end the unit's SOP run (the unit keeps <see cref="SopState"/>, empty): its owned parts
        /// and its slots go. Not gated — the gate is the caller's.</summary>
        internal static void EndSop(EntityRepository repo, Entity entity, BehaviorRegistry registry)
        {
            if (!repo.HasComponent<SopState>(entity)) return;
            var sop = repo.GetComponentRO<SopState>(entity);
            if (sop.SopHash == BehaviorIds.None) return;

            BehaviorOwnedParts.Release(repo, entity, sop.SopInstanceId);
            if (registry.TryGetDefinition(sop.SopHash, out var def) && def.StatefulWorkingSlots is { Count: > 0 })
                DetachStatefulSlots(repo, entity, HostedSubtree.EffectiveSlots(def.StatefulWorkingSlots));
            RootHsmAccess.DetachRoot(repo, entity, sop.SopHash);
            RootStateAccess.DetachRoot(repo, entity, sop.SopHash);
            RootParamsAccess.DetachRoot(repo, entity, sop.SopHash);

            repo.GetComponentRW<SopState>(entity) = default;
            if (repo.HasManagedComponent<SopStartRecord>(entity))
                repo.SetManagedComponent<SopStartRecord>(entity, null!);
        }

        private static bool HasSop(EntityRepository repo, Entity entity)
            => repo.IsComponentTypeRegistered<SopState>() && repo.HasComponent<SopState>(entity)
               && repo.GetComponentRO<SopState>(entity).SopHash != BehaviorIds.None;

        private static bool IsTheSop(EntityRepository repo, Entity entity, int behaviorId)
            => HasSop(repo, entity) && repo.GetComponentRO<SopState>(entity).SopHash == behaviorId;

        private static void WakeSop(EntityRepository repo, Entity entity)
        {
            if (HasSop(repo, entity)) repo.GetComponentRW<SopState>(entity).SopWake = 1;
        }

        /// <summary>⭐ <c>CE-3035</c> — is <paramref name="key"/> one of the SOP slot's own storage slots (its roots, its manifest)?
        /// The task's sweeps leave those alone. ⚠ The SOP's LAZILY attached hosted occurrences are not named anywhere, so a
        /// task start still resets them — an SOP tree re-reads from its root each wake, so this costs at most a hosted
        /// child's progress.</summary>
        private static bool IsHeldBySop(EntityRepository repo, Entity entity, BehaviorRegistry? registry, int key)
        {
            if (!HasSop(repo, entity)) return false;
            int h = repo.GetComponentRO<SopState>(entity).SopHash;
            if (key == RootParamsAccess.KeyForBehaviour(h) || key == RootStateAccess.KeyForBehaviour(h)
                || key == RootHsmAccess.KeyForBehaviour(h))
                return true;
            return registry != null && registry.TryGetDefinition(h, out var d) && d.StatefulWorkingSlots is { Count: > 0 }
                   && IsNamedByManifest(HostedSubtree.EffectiveSlots(d.StatefulWorkingSlots), key);
        }

        private EntityRepository? _startRecordRegisteredOn;

        /// <summary>
        /// ⭐ S5b — the root's own lazy-occurrence demand PLUS every hosted descendant's (one per occurrence). ⛔ Nothing can
        /// grow the store mid-tick, so a hosted HSM child whose states attach occurrences lazily must be counted HERE.
        /// Returns the input unchanged when nothing is hosted.
        /// </summary>
        private HostedOccurrenceDemand? WithDescendantDemand(HostedOccurrenceDemand? own, BehaviorDefinition def)
        {
            var children = HostedSubtree.HostedDescendants(def.StatefulWorkingSlots);
            if (children.Count == 0) return own;
            int bytes = own?.PayloadBytes ?? 0, count = own?.SlotCount ?? 0;
            bool any = own is not null;
            foreach (var child in children)
            {
                if (!_registry.TryGetHostedOccurrenceDemand(child.Name, out var d) || d is null) continue;
                bytes += d.PayloadBytes; count += d.SlotCount; any = true;
            }
            return any ? new HostedOccurrenceDemand(bytes, count) : null;
        }

        /// <summary>Entities started BY NAME in the current <c>Execute</c> → the behaviour started (<c>CE-451</c>).</summary>
        private readonly Dictionary<int, int> _startedByNameThisFrame = new();

        /// <summary>
        /// 🔴🔴🔴 <b><c>E-cap</c> — A BEHAVIOUR WITH NO MANIFEST STILL NEEDS A STORE.</b>
        /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §27.
        ///
        /// <para>⛔⛔ <b>Why this is not speculative housekeeping.</b> A hosted occurrence attaches its
        /// working state <b>lazily</b>, on first dispatch (§24.8) — and it <b>cannot add a tier
        /// component to do so</b>, because that is a STRUCTURAL change and must never happen inside a
        /// tick. ⇒ without a store already present, the very first dispatch of an HSM- or BTree-hosted
        /// blueprint <b>throws</b>.</para>
        ///
        /// <para>🔴 <b>Measured: that gap was real and shipped.</b> <c>ProvisionStatefulSlots</c> ran
        /// only when <c>def.StatefulWorkingSlots</c> was non-empty, so a behaviour that hosts a
        /// blueprint but declares no stateful slots of its own got no store at all. <c>O7b</c>'s HSM
        /// path passed its tests only because the fixture adds one by hand — ⚠ <b>the production path
        /// would have thrown</b>, which is exactly the shape §26.1's rule exists to catch.</para>
        ///
        /// <para>⭐ <b>The cost is the smallest tier</b> — <c>SelectTierForPayload(0, 0)</c> — which is
        /// why <c>O3b</c> added the 256 tier: §5a measured it as <b>load-bearing, not an
        /// optimisation</b>. ⚠ Entities whose behaviour never hosts anything do carry it; that is
        /// accepted, and it is reversible once a manifest can say who actually hosts (<c>O7b-3</c>).</para>
        ///
        /// <para>⚠ <b>Brain tiers only.</b> A behaviour on no brain tier cannot host an occurrence, so it gets
        /// nothing — ⛔ this is not "a store for every entity". ⭐ <c>CE-446</c>: a blueprint behaviour hosts
        /// nothing but still needs the store for its root block.</para>
        /// </summary>
        private static unsafe void EnsureOccurrenceStore(
            EntityRepository repo, Entity entity, BehaviorDefinition def,
            HostedOccurrenceDemand? hosted = null, int rootParamsCost = 0, int rootStateCost = 0)
        {
            // ⭐ CE-446: a blueprint behaviour hosts nothing, but its ROOT BLOCK is attached into this store.
            if (def.BrainTier != BehaviorConstants.BrainTierBTree &&
                def.BrainTier != BehaviorConstants.BrainTierHsm &&
                def.BrainTier != BehaviorConstants.BrainTierBlueprint)
                return;

            // ⭐⭐ O7b-3: size it for what this behaviour will actually host. ⚠ A null demand means
            //   "nobody computed one" (§27.7), and the smallest tier is the same answer E-cap gave —
            //   so this is additive, never a regression.
            // ⭐ CE-302: the root params slot is attached after this returns, so its cost is added here
            //   or it is never counted at all.
            // ⭐ O7c-②: the ROOT TREE STATE slot is additive here on exactly the same footing as the
            //   root params slot — both attach after this returns, so both are counted before it.
            int demandPayload = HostedPayloadCost(hosted) + rootParamsCost + rootStateCost;
            int demandSlots   = (hosted?.SlotCount ?? 0)
                              + (rootParamsCost > 0 ? 1 : 0)
                              + (rootStateCost  > 0 ? 1 : 0);

            int currentTier = GetCurrentTierSize(repo, entity);
            if (currentTier != 0)
            {
                // 🔴🔴 O7c-④a — "already has one" IS NO LONGER ENOUGH, and the reason is new with
                //   this slice. Until now every cost this branch could be asked for was a CONSTANT:
                //   the root tree state is always 64, the root params always MaxBehaviorParamByteSize.
                //   ⇒ a store that fitted the first assign fitted every later one, and returning early
                //   was correct by arithmetic rather than by luck.
                //
                //   ⭐ The root HSM instance breaks that: its width is SelectTier(blob) — 64, 128 or
                //   256 — so reassigning an entity from a one-region machine to a three-region one
                //   RAISES the demand. 📐 Measured: 256 bytes aligned plus a 16-byte slot entry is
                //   272, and the smallest tier's payload is 176. ⇒ the attach a few lines later would
                //   simply return null and the machine would never run — no throw, no log.
                //
                //   ⚠ THE GUARD IS DELIBERATELY "DOES IT FIT THE TIER AT ALL", NOT "IS THERE ROOM
                //   RIGHT NOW". Free space understates: the PREVIOUS behaviour's slots are reclaimed
                //   by DetachHostedOccurrenceSlots AFTER this returns. ⇒ comparing against capacity
                //   promotes only an entity whose tier could never hold the demand, and leaves every
                //   entity that fits exactly where it is. 📌 That is what keeps this off CE-318's
                //   ground: no BTree entity's tier moves, so the golden cannot shift under it.
                ref readonly var header =
                    ref Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity));

                if (demandPayload <= header.PayloadSize && demandSlots <= header.MaxSlots) return;

                int grownTier = SelectTierForPayload(demandPayload, demandSlots);
                if (grownTier <= currentTier) return;   // the ladder has nothing bigger to offer
                if (!BlueprintTierTable.ByTotalSize(grownTier).IsRegistered(repo)) return;

                UpgradeTier(repo, entity, currentTier, grownTier);
                return;
            }

            int targetTier = SelectTierForPayload(demandPayload, demandSlots);

            // ⛔⛔ DO NOT WIDEN THE TOOLKIT'S CONTRACT. Registering the tier components is Hrot-wide
            //   (HrotSharedComponentRegistry, CE-161) but this system lives in Fdp.Toolkits, which
            //   hosts may use WITHOUT them. ⇒ a host that never hosts an occurrence must not start
            //   failing at assign just because this provisioning was added.
            // ⭐ The skip is SAFE because it is not silent where it matters: a host that skips here
            //   and then DOES host an occurrence gets OccurrenceWorkingState's loud "carries no
            //   occurrence store", whose message names tier registration as a cause.
            if (!BlueprintTierTable.ByTotalSize(targetTier).IsRegistered(repo)) return;

            AddAndInitializeTier(repo, entity, targetTier);
        }

        /// <summary>
        /// 🔴🔴 <b><c>F14b</c> — the EXTERNAL reset path: a hosted occurrence's cursor must follow
        /// the host's.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §21.2.
        ///
        /// <para>⛔⛔ <b>Why the two <c>D4</c> halves do not cover this.</b>
        /// <c>HostedSubtree.Tick</c> resets a child that COMPLETED, and the deactivator resets a child
        /// the host ABANDONED mid-tick — both reached through <c>Interpreter.SweepExitedNodes</c>.
        /// ⚠ This system zeroes <c>BrainBTreeState.State</c> <b>without a tick</b>, so neither fires:
        /// the host restarts at the root while a hosted child resumes mid-tree. ⭐ Before <c>O4</c> the
        /// child shared the host's 64-byte state, so zeroing the host cleared it by ACCIDENT — own
        /// state removes that, and this is the bill (the same bill <c>F14</c> paid for the sweep).</para>
        ///
        /// <para>⭐ <b>Only the hosted tree-state slots</b> (<c>HostedSubtree.IsTreeStateSlot</c>).
        /// ⛔ Author working state is deliberately NOT cleared: <c>AttachSlotsToMemory</c>'s idempotent
        /// arm preserves it across a no-op re-assign on purpose, and a cursor is not working state.</para>
        ///
        /// <para>⚠ <b><c>ClearBehaviorEvent</c> (<c>:204</c>) needs no call</b> — measured: it
        /// <c>DetachStatefulSlots</c> first, and <c>TryAttach</c> ZEROES the payload it hands out
        /// (<c>SlotAttachZeroingTests</c>), so the next assign gets a clean cursor either way.</para>
        /// </summary>
        /// <summary>
        /// 🔴🔴🔴 <b><c>E3a</c> — drop the LAZILY-ATTACHED hosted occurrences on a behaviour assign, so
        /// their params are re-seeded from the JSON this assign just parsed.</b> 📄 §28.4.
        ///
        /// <para>⛔⛔ <b>This is NOT housekeeping — omitting it is a REGRESSION.</b> Before <c>E3a</c> an
        /// HSM thunk read its params from <c>BrainBlackboard</c> <b>live</b>, so a re-assign with new
        /// JSON took effect on the very next dispatch. ⭐ After <c>E3a</c> the slot holds a <b>copy</b>
        /// ⇒ without this, <b>new JSON would silently stop taking effect</b> and the occurrence would
        /// run forever on the first assign's values.</para>
        ///
        /// <para>⭐⭐ <b><c>A3</c>/<c>D1′</c>'s <c>Kind</c> nibble is what makes this PRECISE.</b> A
        /// lazily-attached hosted occurrence declares <see cref="OccurrenceKind.Hsm"/> or
        /// <see cref="OccurrenceKind.Blueprint"/>; a manifest slot is provisioned with the behaviour's
        /// own kind. ⛔ Detaching by kind ALONE would also take manifest slots — hence <i>kind AND not
        /// named by the manifest</i>. ⚠ Without the nibble this sweep could not be written at all,
        /// which is what <c>F7</c> predicted.</para>
        ///
        /// <para>⚠ <b>Walks DOWNWARD.</b> <c>TryDetach</c> dense-compacts the slot table
        /// (<c>:188-199</c>), so an ascending walk would skip the entry that slid into the hole.</para>
        /// </summary>
        private static unsafe void DetachHostedOccurrenceSlots(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo>? manifest, BehaviorRegistry? registry)
        {
            byte* store = OccurrenceStoreAccess.TryGetStore(repo, entity, out _);
            if (store == null) return;

            for (int i = BlueprintBlackboardPartitions.GetSlotCount(store) - 1; i >= 0; i--)
            {
                var kind = BlueprintBlackboardPartitions.GetSlotKind(store, i);
                if (kind != OccurrenceKind.Hsm && kind != OccurrenceKind.Blueprint) continue;

                int key = BlueprintBlackboardPartitions.GetSlot(store, i).BlueprintId;
                if (IsNamedByManifest(manifest, key)) continue;   // provisioned, not lazily attached
                if (IsHeldBySop(repo, entity, registry, key)) continue;   // ⭐ CE-3035 — the SOP slot's storage is not the task's to sweep

                BlueprintBlackboardPartitions.TryDetach(store, key);
            }
        }

        private static bool IsNamedByManifest(IReadOnlyList<StatefulSlotInfo>? manifest, int slotKey)
        {
            if (manifest == null) return false;
            for (int i = 0; i < manifest.Count; i++)
                if (manifest[i].SlotKey == slotKey) return true;
            return false;
        }

        private static void ResetHostedTreeStates(EntityRepository repo, Entity entity, BehaviorDefinition? def)
        {
            var slots = def?.StatefulWorkingSlots;
            if (slots == null) return;

            for (int i = 0; i < slots.Count; i++)
                if (HostedSubtree.IsTreeStateSlot(slots[i]))
                    HostedSubtree.Reset(repo, entity, slots[i].SlotKey);
        }

        // ── S2-2: stateful slot provisioning helpers ─────────────────────────────

        /// <summary>
        /// S2-2/S2-3: Synchronously provisions BlueprintBlackboard* tier and eagerly allocates
        /// every stateful working-state slot from the behavior manifest.
        /// Selects the smallest tier that can fit the manifest slots (considering existing
        /// occupancy when an entity already carries a tier). Performs tier upgrade inline
        /// (synchronous structural mutation is safe in Input phase, outside the Simulation lock).
        ///
        /// S2-3 addition: when an existing tier carries slots from this manifest that will be
        /// detached-and-reattached (size/hash mismatch), their current payload is treated as
        /// "to-be-freed" when computing available space, so tier selection is correct even when
        /// a WorkingState grows on a hard reload.
        /// </summary>
        /// <summary>
        /// A3/<c>D1′</c> — the <see cref="OccurrenceKind"/> a behaviour's stateful working slots get.
        /// ⚠ <see cref="OccurrenceKind.Invalid"/> for an unknown tier is deliberate: an undeclared
        /// occurrence must read <i>"nobody declared one"</i>, never a guess.
        /// </summary>
        /// <summary>
        /// <c>O7b-3</c> — the store bytes a behaviour's HOSTED occurrences will cost, in the SAME
        /// arithmetic <see cref="ProvisionStatefulSlots"/> uses for a manifest slot: aligned payload
        /// plus one <c>BlueprintSlotEntry</c> each.
        ///
        /// <para>⭐ The demand already carries its payload ALIGNED (<c>HostedOccurrenceDemand.Of</c>),
        /// so only the slot-entry overhead is added here. ⛔ Two places computing this would be two
        /// places to get it wrong — that is why the split is stated on the record and not invented at
        /// each call site.</para>
        ///
        /// <para>⚠ A <c>null</c> demand costs ZERO, which reproduces <c>E-cap</c>'s smallest tier
        /// exactly. ⛔ Not an error: <i>"nobody computed one"</i> is a real state (§27.7).</para>
        /// </summary>
        // ⭐⭐ CE-318 (2026-09-23): the slot entries are NOT added here any more — the slot table is
        //   carved out of the store once, up front, so PayloadSize has already had them removed.
        //   The slot AXIS is still counted, by `requiredSlots += hosted.SlotCount` at the call site.
        //   📄 BlueprintBlackboardPartitions.PayloadCost.
        private static int HostedPayloadCost(HostedOccurrenceDemand? hosted)
            => hosted is null ? 0 : hosted.PayloadBytes;

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-302</c> — the store bytes the ROOT PARAMS slot costs</b>, in the same
        /// arithmetic <see cref="HostedPayloadCost"/> uses: aligned payload plus one
        /// <c>BlueprintSlotEntry</c>. <b>Zero means "this behaviour has no root params".</b>
        ///
        /// <para>⛔⛔ <b>Why this had to exist before <c>P3-C</c>'s clean cut.</b> The root params slot
        /// is attached AFTER provisioning, so nothing that sizes the tier ever saw it. ⭐ While the
        /// blackboard commit still ran that was harmless — a full store simply returned <c>null</c>
        /// and the entity kept using <c>BrainBlackboard</c>. ⇒ once the blackboard is gone, the same
        /// <c>null</c> is <b>lost params</b>, so the room has to be reserved up front.</para>
        ///
        /// <para>⚠ <b>The slot count is implicitly ONE.</b> §29.6: the root params region is ONE slot
        /// holding the WHOLE packed table — scattering it per state would destroy <c>E3b-0</c>'s seed
        /// offsets, which index INTO that table. ⇒ callers add <c>cost &gt; 0 ? 1 : 0</c> slots, and
        /// there is no shape in which this returns a cost for more than one.</para>
        ///
        /// <para>⚠ <b>Gated on <c>ParseParams</c>, exactly as the attach is.</b> A behaviour with a
        /// manifest but no parser never attaches a root slot, so reserving for it would push entities
        /// up a tier for nothing.</para>
        /// </summary>
        /// <summary>
        /// ⭐⭐ <b><c>O7c</c>-② / <c>CE-319</c> — what the ROOT TREE STATE slot costs the tier demand.</b>
        /// 📄 §31.
        ///
        /// <para>⛔⛔ <b>The exact shape of <see cref="RootParamsCost"/>, and for the same reason:</b>
        /// the slot attaches AFTER provisioning returns, and nothing later can grow the tier — that is a
        /// structural change inside a tick. ⇒ its cost is counted HERE or it is never counted at all,
        /// which is precisely the defect <c>CE-302</c> was.</para>
        ///
        /// <para>⚠ <b>Asks the DEFINITION, never a probe.</b> A behaviour is BTree-tier or it is not;
        /// "the lookup failed" cannot tell "this behaviour has no tree" from "the slot should exist and
        /// does not" — <c>CE-307</c>'s lesson on the params path.</para>
        /// </summary>
        private static int RootStateCost(BehaviorDefinition? def)
        {
            int bytes = RootStateAccess.RootStateBytes(def);
            if (bytes <= 0) return 0;

            // ⭐⭐ CE-318: PAYLOAD only — the caller adds the slot on its own axis.
            return BlueprintBlackboardPartitions.PayloadCost(bytes);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7c</c>-④ — what the ROOT BRAIN'S EXECUTION STATE costs the tier demand,
        /// whichever paradigm the brain is.</b> 📄 §31.14.
        ///
        /// <para>⭐⭐ <b>ONE cost, because it is ONE slot.</b> <see cref="RootStateCost"/> is non-zero
        /// only for a BTree-tier behaviour and <see cref="RootHsmCost"/> only for an HSM-tier one —
        /// <c>BehaviorState.BrainTier</c> is a single discriminator, so exactly one of the two can pay.
        /// ⛔ Summing them is therefore not "reserve for both"; it is the one-line spelling of
        /// <i>"reserve for whichever brain this is"</i>, and it is why the provisioners take a single
        /// parameter rather than growing a second.</para>
        /// </summary>
        private static int RootBrainStateCost(BehaviorDefinition? def)
            => RootStateCost(def) + RootHsmCost(def);

        /// <summary>
        /// ⭐⭐ <b>What the ROOT HSM INSTANCE slot costs the tier demand.</b>
        ///
        /// <para>🔴 <b>The one place this differs from every other cost in this file: the width is a
        /// RUNTIME value</b> — <c>HsmInstanceManager.SelectTier(blob)</c> returns 64, 128 or 256 from
        /// the machine's own shape. ⛔ Not a <c>sizeof</c>, and not 128 just because the retired
        /// <c>BrainHsm128</c> component happened to be that wide. ⚠ A 64-byte machine now reserves 64,
        /// which is the first time the kernel's smallest tier has been reachable at all.</para>
        ///
        /// <para>⛔⛔ <b>Counted HERE or never</b> — the slot attaches AFTER provisioning returns and
        /// nothing later can grow the tier, which is a structural change inside a tick. That is
        /// precisely the defect <c>CE-302</c> was on the params path.</para>
        /// </summary>
        private static int RootHsmCost(BehaviorDefinition? def)
        {
            int bytes = RootHsmAccess.RootHsmBytes(def);
            if (bytes <= 0) return 0;

            // ⭐⭐ CE-318: PAYLOAD only — the caller adds the slot on its own axis.
            return BlueprintBlackboardPartitions.PayloadCost(bytes);
        }

        private static int RootParamsCost(BehaviorDefinition def)
        {
            // ⭐ CE-416 ②: costed whenever the block EXISTS (the attach's predicate), parser or not.
            if (def == null) return 0;

            int bytes = RootParamsAccess.RootParamsBytes(def);
            if (bytes <= 0) return 0;

            // ⭐⭐ CE-318: PAYLOAD only — the caller adds the slot on its own axis.
            return BlueprintBlackboardPartitions.PayloadCost(bytes);
        }

        private static OccurrenceKind KindOf(BehaviorDefinition def) => def.BrainTier switch
        {
            BehaviorConstants.BrainTierBTree => OccurrenceKind.BTree,
            BehaviorConstants.BrainTierHsm   => OccurrenceKind.Hsm,
            // ⭐ CE-446: the root block of a blueprint behaviour — a kind nothing walks or sweeps.
            BehaviorConstants.BrainTierBlueprint => OccurrenceKind.BlueprintBehavior,
            _                                => OccurrenceKind.Invalid,
        };

        private static unsafe void ProvisionStatefulSlots(
            EntityRepository repo, Entity entity,
            IReadOnlyList<StatefulSlotInfo> slots,
            OccurrenceKind kind,
            HostedOccurrenceDemand? hosted = null,
            int rootParamsCost = 0,
            int rootStateCost = 0)
        {
            // Compute aggregate required payload for the new manifest:
            // each slot at alignment-padded size + one BlueprintSlotEntry header per slot.
            int requiredPayload = 0;
            // ⭐⭐ CE-318: PAYLOAD only; `requiredSlots` below carries the slot axis.
            foreach (var s in slots)
                requiredPayload += BlueprintBlackboardPartitions.PayloadCost(s.PayloadSize);
            int requiredSlots = slots.Count;

            // ⭐⭐⭐ O7b-3: the behaviour's HOSTED occurrences need room too, and they attach LAZILY —
            //   so nothing here will ever see them, and nothing later can grow the tier (a structural
            //   change inside a tick). ⇒ their demand is ADDITIVE to the manifest's, in BOTH branches
            //   below. ⛔ Sizing only the `EnsureOccurrenceStore` branch is the cheap wrong fix: it
            //   never runs for a behaviour that declares stateful slots of its own (rail O7_R18).
            requiredPayload += HostedPayloadCost(hosted);
            requiredSlots   += hosted?.SlotCount ?? 0;

            // ⭐⭐ CE-302: and the ROOT PARAMS slot, attached after this returns (§29.6 — ONE slot for
            //   the whole packed table). ⛔ Same reason as the hosted demand above: nothing later can
            //   grow the tier, because that is a structural change inside a tick.
            requiredPayload += rootParamsCost;
            requiredSlots   += rootParamsCost > 0 ? 1 : 0;

            // ⭐⭐ O7c-② / CE-319: and the ROOT TREE STATE slot. ⛔ Counted in THIS branch as well as in
            //   EnsureOccurrenceStore's — sizing only one of them is the cheap wrong fix that rail
            //   O7_R18 exists to catch: this branch is the one that runs for a behaviour which declares
            //   stateful slots of its own, and EnsureOccurrenceStore never runs for it at all.
            requiredPayload += rootStateCost;
            requiredSlots   += rootStateCost > 0 ? 1 : 0;

            // Determine the entity's current tier (0 = none, else TotalSize).
            int currentTier = GetCurrentTierSize(repo, entity);

            if (currentTier == 0)
            {
                // No tier present: select smallest tier whose abstract capacity fits the manifest.
                int targetTier = SelectTierForPayload(requiredPayload, requiredSlots);
                AddAndInitializeTier(repo, entity, targetTier);
            }
            else
            {
                // S2-3: compute how much space will be *freed* by detaching existing manifest slots
                // that are already attached (they will be detach+reattach'd if size/hash differs,
                // or are idempotently kept if identical). Only detach candidates contribute freed space.
                int toBeFreedPayload = GetManifestSlotsToBeFreedPayload(repo, entity, slots);
                int toBeReusedSlots  = GetManifestSlotsAlreadyAttachedCount(repo, entity, slots);

                // Effective free space: current free + what will be freed by detach.
                // Effective required slots: requiredSlots - already-attached (those reuse their slot entries).
                int freePayload       = GetTierFreePayload(repo, entity) + toBeFreedPayload;
                int freeSlots         = GetTierFreeSlotCount(repo, entity) + toBeReusedSlots;
                bool tierFits         = freePayload >= requiredPayload && freeSlots >= requiredSlots;

                if (!tierFits)
                {
                    // Current tier cannot accommodate manifest: compute total needed
                    // (existing used - freed-by-detach + new manifest) and select the smallest tier.
                    int usedPayload  = GetTierUsedPayload(repo, entity) - toBeFreedPayload;
                    int usedSlots    = GetTierUsedSlotCount(repo, entity) - toBeReusedSlots;
                    int totalPayload = usedPayload + requiredPayload;
                    int totalSlots   = usedSlots   + requiredSlots;
                    int targetTier   = SelectTierForPayload(totalPayload, totalSlots);

                    if (targetTier > currentTier)
                        UpgradeTier(repo, entity, currentTier, targetTier);
                    // If targetTier == currentTier (shouldn't happen since tierFits was false),
                    // we proceed; TryAttach will fail silently (not enough space).
                }
                // If tierFits: leave existing tier in place.
            }

            // Eager-allocate every manifest slot (idempotent for same-size+hash; detach+reattach for mismatch).
            AttachManifestSlots(repo, entity, slots, kind);
        }

        // ═══ O3a / B3 — THE SIX LADDERS THAT WERE NEVER TIER BRANCHING ═════════════════════════
        //  📄 DESIGN_Occurrence_Scoped_Storage.md §17.1 N3.
        //  ⛔⛔ Each of these took (repo, entity, tierSize) and re-spelled the three-way probe to
        //     reach ONE entity's store — while `tierSize` was GetCurrentTierSize(repo, entity), i.e.
        //     OccurrenceStoreAccess.GetStoreSize, computed on that same entity one line earlier at
        //     the single call site (:287). ⇒ they asked a question the seam already answers.
        //  ⭐⭐ And BlueprintBlackboardHeader is SELF-DESCRIBING — MaxSlots, SlotCount, PayloadSize
        //     and PayloadFree all live in the 32-byte header — so even the per-tier PayloadSize
        //     constant GetTierUsedPayload used was redundant: the store states its own capacity.
        //  ✅ Provably equivalent: every one of these is called only inside the `currentTier != 0`
        //     branch (:289), so TryGetStore can never return null here. The tierSize parameter is
        //     kept ONLY where it still documents the caller's intent — see StoreOf.

        /// <summary>
        /// The entity's store pointer. ⛔ Valid for the CALLING expression only — see the
        /// <c>OccurrenceStoreAccess</c> LIFETIME RULE.
        /// </summary>
        private static unsafe byte* StoreOf(EntityRepository repo, Entity entity)
            => Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess
                   .TryGetStore(repo, entity, out _);

        /// <summary>Returns the current free payload bytes in the entity's tier.</summary>
        private static unsafe int GetTierFreePayload(EntityRepository repo, Entity entity)
            => Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity)).PayloadFree;

        /// <summary>Returns the number of free slot entries (MaxSlots - SlotCount) in the entity's tier.</summary>
        private static unsafe int GetTierFreeSlotCount(EntityRepository repo, Entity entity)
        {
            ref var h = ref Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity));
            return h.MaxSlots - h.SlotCount;
        }

        /// <summary>Returns the used payload bytes = (PayloadSize - PayloadFree) in the entity's tier.</summary>
        private static unsafe int GetTierUsedPayload(EntityRepository repo, Entity entity)
        {
            // ⭐ O3a: was a three-constant ternary over BlueprintBlackboard*.PayloadSize. The header
            //   records PayloadSize per tier, so the store answers for itself.
            ref var h = ref Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity));
            return h.PayloadSize - h.PayloadFree;
        }

        /// <summary>
        /// S2-3: Sums the aligned PayloadSize of manifest slots that are already attached
        /// AND will be detached+reattached (PayloadSize or StructureHash mismatch).
        /// This is the amount of space that will be freed before reattachment, so the
        /// tier-fit calculation in ProvisionStatefulSlots can use it as "available extra space".
        /// </summary>
        private static unsafe int GetManifestSlotsToBeFreedPayload(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo> slots)
            => ComputeToBeFreedPayload(StoreOf(repo, entity), slots);

        private static unsafe int ComputeToBeFreedPayload(byte* mem, IReadOnlyList<StatefulSlotInfo> slots)
        {
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
            byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
            int freed = 0;
            foreach (var s in slots)
            {
                for (int i = 0; i < header.SlotCount; i++)
                {
                    ref var entry = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (entry.BlueprintId == s.SlotKey)
                    {
                        // Will this slot be detached? Only if size or hash mismatches.
                        int alignedManifestSize = AlignUp(s.PayloadSize, BlueprintBlackboardPartitions.Alignment);
                        if (entry.PayloadSize != alignedManifestSize ||
                            entry.StructureHash != (uint)s.StructureHash)
                        {
                            freed += entry.PayloadSize; // this size will be returned to free list
                        }
                        break;
                    }
                }
            }
            return freed;
        }

        /// <summary>
        /// S2-3: Counts manifest slots already attached in the tier regardless of mismatch.
        /// These slots will either be kept (idempotent) or freed+reattached, but either way
        /// they do not consume an additional slot entry beyond what's already allocated.
        /// This count is used to adjust the free-slot-entry count when computing tier fit.
        /// </summary>
        private static unsafe int GetManifestSlotsAlreadyAttachedCount(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo> slots)
            => ComputeAlreadyAttachedCount(StoreOf(repo, entity), slots);

        private static unsafe int ComputeAlreadyAttachedCount(byte* mem, IReadOnlyList<StatefulSlotInfo> slots)
        {
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
            byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
            int count = 0;
            foreach (var s in slots)
            {
                for (int i = 0; i < header.SlotCount; i++)
                {
                    ref var entry = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (entry.BlueprintId == s.SlotKey)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        /// <summary>Returns the used slot count (SlotCount) in the entity's tier.</summary>
        private static unsafe int GetTierUsedSlotCount(EntityRepository repo, Entity entity)
            => Unsafe.AsRef<BlueprintBlackboardHeader>(StoreOf(repo, entity)).SlotCount;

        /// <summary>
        /// ⭐⭐⭐ <b>THE one clear</b> — brain-death for one entity: detach the outgoing behaviour's stateful, hosted and root
        /// slots, then set <c>ActiveBehaviorHash = None</c>, bump <c>InstanceId</c> (⇒ <c>ChannelArbitrationSystem</c> resets
        /// every channel to its default, <c>ActiveAction = 0</c>) and <c>BrainTier = 0</c>.
        /// <para>
        /// ⭐ <c>CE-449</c>: called by the <see cref="ClearBehaviorEvent"/> handler AND by <c>BrainTickSystem</c> the moment a
        /// behaviour of any tier finishes (user, <c>2026-09-30</c>: <i>"finishing a behavior should cancel the commands
        /// exactly same as Clear Behavior does"</i>). ⛔ Inline, not a published event: ingress runs assign-by-name BEFORE
        /// clear, so a published clear would wipe a behaviour assigned in the same frame. ⚠ Non-structural (every detach is a
        /// slot-table edit), so it is safe inside the tick's walk.
        /// </para>
        /// </summary>
        internal static void Clear(EntityRepository repo, Entity entity, BehaviorRegistry registry)
        {

            // S3-5: detach the outgoing behavior's stateful slots BEFORE clearing.
            // The switch path (AssignBehaviorEvent) already detaches on switch, but a clear-
            // without-successor previously only nulled ActiveBehaviorHash, leaking the slots
            // until the next assign. Capture the previous behavior id and reclaim its slots.
            // DetachStatefulSlots frees by the manifest's SlotKey, which is scope-aware (S3-4),
            // so this reclaims Node- and Behavior-scoped slots alike.
            int previousBehaviorId = repo.GetComponentRW<BehaviorState>(entity).ActiveBehaviorHash;
            if (previousBehaviorId != BehaviorIds.None &&
                registry.TryGetDefinition(previousBehaviorId, out var prevDef) &&
                prevDef.StatefulWorkingSlots != null && prevDef.StatefulWorkingSlots.Count > 0)
            {
                DetachStatefulSlots(repo, entity, HostedSubtree.EffectiveSlots(prevDef.StatefulWorkingSlots));   // ⭐ S5b — nested slots too
            }

            // E3a: a clear-without-successor must reclaim the lazily-attached hosted occurrences
            // too — the same leak S3-5 fixed for manifest slots.
            DetachHostedOccurrenceSlots(repo, entity, manifest: null, registry);

            // 🔴🔴 O7c-② — THE ROOT SLOTS MUST BE DETACHED **BEFORE** THE HASH IS CLEARED, AND BOTH
            //    OF THEM. 📄 §31.
            //
            //    ⛔⛔ Every root-slot key is COMPUTED from ActiveBehaviorHash, so once the line below
            //      sets it to None the key is 0 and BOTH DetachRoot and ResetState become silent
            //      no-ops. ⚠ The first draft of this change put the reset after the clear and it
            //      would have leaked the slot on every brain-death — caught by reading the handler,
            //      not by a test, because a leaked slot has no visible effect until MaxSlots (3 on
            //      the 256 tier) runs out and params silently stop attaching.
            //
            //    🔴 AND THE PARAMS LINE IS A PRE-EXISTING LEAK THIS CHANGE FIXES, not one it caused:
            //      CE-302 added DetachRoot to the ASSIGN path only, so a clear-without-successor has
            //      been leaking the root params slot ever since. ⚠ Fixed here rather than filed,
            //      because adding its exact twin while leaving it in place would be worse than
            //      either doing both or neither.
            //    ⭐ O7c-④: THE ROOT HSM SLOT NEEDS NO LINE HERE, AND THAT IS MEASURED, NOT FORGOTTEN.
            //      It declares OccurrenceKind.Hsm, so DetachHostedOccurrenceSlots(manifest: null) a
            //      few lines above already reclaimed it — the same sweep that cannot see the two
            //      BTree-kind slots below. ⚠ If that sweep's kind filter ever narrows, this is the
            //      block that has to grow a RootHsmAccess.DetachRoot call.
            if (previousBehaviorId != BehaviorIds.None)
            {
                RootStateAccess.ResetState(repo, entity);   // zero while the key still resolves
                RootStateAccess.DetachRoot(repo, entity, previousBehaviorId);
                RootParamsAccess.DetachRoot(repo, entity, previousBehaviorId);
            }

            // ⭐ CE-485: the run ends here (InstanceId is bumped below) ⇒ its owned parts (EQS sensors) end with it. 📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D4.
            BehaviorOwnedParts.Release(repo, entity, repo.GetComponentRO<BehaviorState>(entity).InstanceId);
            ref var behavior = ref repo.GetComponentRW<BehaviorState>(entity);
            behavior.ActiveBehaviorHash = BehaviorIds.None;
            unchecked { behavior.InstanceId++; }
            behavior.BrainTier = 0;
            behavior.Origin = BehaviorOrigin.Unmarked;   // ⭐ CE-3034 — an empty slot admits anything
            behavior.Urgency = ReactionUrgency.NotAReaction;   // ⭐ CE-2078
            behavior.RunSince = Now(repo);                       // ⭐ CE-2080 — emptiness is a "run" too

            // ⭐ CE-452: no behaviour ⇒ nothing to restart.
            if (repo.HasManagedComponent<BehaviorStartRecord>(entity))
                repo.SetManagedComponent<BehaviorStartRecord>(entity, null!);
        }


        /// <summary>
        /// S2-2: Detaches the previous behavior's stateful slots from the entity's tier.
        /// Called before attaching new behavior's slots to prevent slot leaks.
        /// </summary>
        private static unsafe void DetachStatefulSlots(
            EntityRepository repo, Entity entity,
            IReadOnlyList<StatefulSlotInfo> slots)
        {
            // A2: the three-tier ladder, once, in OccurrenceStoreAccess.
            // ⛔ The pointer is valid for THIS CALL only — see the seam's LIFETIME RULE.
            byte* mem = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess
                            .TryGetStore(repo, entity, out _);
            if (mem == null) return;

            foreach (var s in slots)
                BlueprintBlackboardPartitions.TryDetach(mem, s.SlotKey);
        }

        /// <summary>Returns the TotalSize constant of the entity's active tier, or 0 if none.</summary>
        private static int GetCurrentTierSize(EntityRepository repo, Entity entity)
        {
            // A2: identical to OccurrenceStoreAccess.GetStoreSize — delegated, not re-spelled.
            return Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.GetStoreSize(repo, entity);
        }

        // ═══ O3a / B3 — THE THREE THAT GENUINELY NEED A TIER TYPE ══════════════════════════════
        //  📄 DESIGN_Occurrence_Scoped_Storage.md §17.2. Select / Add / Upgrade are the sites where
        //  a COMPONENT TYPE must be named, so these go to BlueprintTierTable rather than the seam.
        //  ⛔⛔ UpgradeTier was the QUADRATIC one: 3 tiers ⇒ 3 arms, 4 tiers ⇒ 6. It is now one body.

        /// <summary>
        /// Selects the TotalSize of the smallest tier whose abstract capacity fits the given
        /// payload and slot count. Falls through to the largest tier if nothing smaller fits.
        /// </summary>
        private static int SelectTierForPayload(int requiredPayload, int requiredSlots)
            => BlueprintTierTable.Select(requiredPayload, requiredSlots).TotalSize;

        /// <summary>Adds a fresh tier component of the given size and initializes its allocator.</summary>
        private static unsafe void AddAndInitializeTier(EntityRepository repo, Entity entity, int tierSize)
        {
            var spec = BlueprintTierTable.ByTotalSize(tierSize);
            spec.Add(repo, entity);
            BlueprintBlackboardPartitions.Initialize(
                spec.Memory(repo, entity), spec.TotalSize, (byte)spec.MaxSlots);
        }

        /// <summary>
        /// Upgrades from a smaller tier to a larger one synchronously:
        /// AddComponent(larger), CopyToLargerTier, RemoveComponent(smaller).
        /// Preserves existing slots and their payloads.
        ///
        /// <para>⛔⛔ <c>CopyToLargerTier</c> is where <c>H1</c> lives — it copies the header's
        /// <c>Reserved</c>, which since <c>A3</c> carries the per-slot <c>Kind</c> nibble array. Rail
        /// <c>A3_R2</c> pins it. ⚠ This method must keep going THROUGH that helper; a hand-rolled
        /// copy here would zero every slot's kind and the tick walker would then skip the entity.</para>
        /// </summary>
        private static unsafe void UpgradeTier(EntityRepository repo, Entity entity, int srcTierSize, int dstTierSize)
        {
            // No downgrade path (current >= target means no-op, handled by caller).
            if (dstTierSize <= srcTierSize) return;

            BlueprintTierTable.Promote(
                repo, entity,
                BlueprintTierTable.ByTotalSize(srcTierSize),
                BlueprintTierTable.ByTotalSize(dstTierSize));
        }

        /// <summary>
        /// Attaches each manifest slot to the entity's active tier.
        /// Skips slots already attached (TryAttach is not idempotent; guard via TryGetSlotOffset).
        /// </summary>
        private static unsafe void AttachManifestSlots(
            EntityRepository repo, Entity entity, IReadOnlyList<StatefulSlotInfo> slots,
            OccurrenceKind kind)
        {
            // A2: the three-tier ladder, once, in OccurrenceStoreAccess.
            // ⛔ The pointer is valid for THIS CALL only — see the seam's LIFETIME RULE.
            byte* mem = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess
                            .TryGetStore(repo, entity, out _);
            if (mem != null)
                AttachSlotsToMemory(mem, slots, kind);
        }

        /// <summary>
        /// S2-3: Ghost-slot-safe slot attachment.
        /// For each manifest slot:
        /// <list type="bullet">
        ///   <item>Not attached → attach (as before).</item>
        ///   <item>Attached with SAME PayloadSize AND StructureHash → leave it (idempotent;
        ///         working state is preserved — no churn on soft reload / no-op re-assign).</item>
        ///   <item>Attached with DIFFERENT PayloadSize OR StructureHash → TryDetach then
        ///         TryAttach at the manifest size/hash. The resized slot's working state
        ///         resets (expected on a structural reload). Adjacent slots remain intact
        ///         because TryDetach dense-compacts the slot table and returns payload to
        ///         the free list before TryAttach takes new space.</item>
        /// </list>
        /// Caller (ProvisionStatefulSlots) must have already ensured the tier has enough total
        /// space to satisfy the manifest (accounting for slots that will be freed before reattach).
        /// </summary>
        private static unsafe void AttachSlotsToMemory(
            byte* mem, IReadOnlyList<StatefulSlotInfo> slots, OccurrenceKind kind)
        {
            foreach (var s in slots)
            {
                if (!BlueprintBlackboardPartitions.TryGetSlotOffset(mem, s.SlotKey, out int existingOffset))
                {
                    // Not attached — attach fresh.
                    BlueprintBlackboardPartitions.TryAttach(mem, s.SlotKey, s.PayloadSize, s.StructureHash, kind, out _);
                    continue;
                }

                // Already attached — locate the slot entry to compare size and hash.
                ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                int slotCount = header.SlotCount;
                byte* slotTable = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();

                bool mismatch = false;
                for (int i = 0; i < slotCount; i++)
                {
                    ref var entry = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (entry.BlueprintId == s.SlotKey)
                    {
                        // Compare manifest PayloadSize (may be unaligned) against the aligned
                        // allocated size stored in the entry, and the hash.
                        int alignedManifestSize = AlignUp(s.PayloadSize, BlueprintBlackboardPartitions.Alignment);
                        if (entry.PayloadSize == alignedManifestSize &&
                            entry.StructureHash == (uint)s.StructureHash)
                        {
                            // Same size AND same hash → idempotent; preserve working state.
                            mismatch = false;
                        }
                        else
                        {
                            mismatch = true;
                        }
                        break;
                    }
                }

                if (mismatch)
                {
                    // S2-3 ghost-slot fix: detach the old (possibly wrong-sized) slot and
                    // re-attach at the manifest-specified size. This correctly re-provisions
                    // a slot that grew (or otherwise changed layout) on a hard reload.
                    // TryDetach dense-compacts the slot table — adjacent slots remain intact.
                    BlueprintBlackboardPartitions.TryDetach(mem, s.SlotKey);
                    BlueprintBlackboardPartitions.TryAttach(mem, s.SlotKey, s.PayloadSize, s.StructureHash, kind, out _);
                }
                // else: same size + hash → idempotent leave-it path (no churn, working state preserved).
            }
        }

        /// <summary>Aligns a size up to the given alignment boundary.</summary>
        private static int AlignUp(int size, int alignment)
            => (size + alignment - 1) & ~(alignment - 1);

        // ⛔⛔⛔ O7c-④b (2026-09-23): ResetHsmComponents IS DELETED, with the system that needed it.
        //   📄 DESIGN_Occurrence_Scoped_Storage.md §31.16.
        //
        //   📐 It hand-rolled the kernel's own reset against a TYPE — `(HsmInstance128*)hdr`, four
        //     ActiveLeafIds, EventCount, InterruptSlotUsed — a third producer of a fact the kernel
        //     already owns, and correct only for a 128-byte machine.
        //   ⭐ RootHsmAccess.ResetInstance replaces it and routes to the size-driven
        //     HsmInstanceManager.Initialize added by O7c-③: the width comes from the SLOT, so a 64-
        //     or 256-byte machine is bound correctly for the first time.
    }
}
