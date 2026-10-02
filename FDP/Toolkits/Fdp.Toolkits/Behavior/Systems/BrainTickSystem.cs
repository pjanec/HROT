using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fbt;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Behavior.Runners;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>O7c</c>-④b — THE ONE SYSTEM THAT STEPS AN ENTITY'S ROOT BRAIN.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.14 *(the design)* · §31.16 *(the as-built)*.
    ///
    /// <para>🔒 <b>User, <c>2026-09-23</c>:</b> <i>"But there will likely be no hsm tick system, will it?
    /// Cant we merge all the occurence traversal and ticking into a single system?"</i></para>
    ///
    /// <para>⛔⛔ <b>It is stronger than "likely": <c>HsmTickSystem&lt;T&gt;</c> COULD NOT SURVIVE.</b> It
    /// was generic over the ECS component that wrapped the instance, so retiring <c>BrainHsm128</c>
    /// leaves it with no <c>T</c>. ⇒ it becomes either a non-generic twin of <c>BTreeTickSystem</c> or
    /// it merges — and two near-identical non-generic systems is the duplication <c>B3</c> already paid
    /// to remove once.</para>
    ///
    /// <para>⭐⭐ <b>TWO ARMS, ONE BODY — and the split is MEASURED, not assumed.</b> §31.14.1 probed
    /// ten structural elements: the terminal-event dedup dictionary, the lifecycle pruning, the
    /// <c>BrainTier</c> discriminator, the registry lookup, the trace-buffer resolution, the
    /// <c>BehaviorFinishedEvent</c> publish and the authority gate are shared by BOTH paradigms and
    /// are the BODY. ⭐ The arms differ in exactly three things: <b>where the state comes from</b>,
    /// <b>which kernel steps it</b>, and <b>how terminality is read</b>.</para>
    ///
    /// <para>⭐⭐⭐ <b>S4 (<c>DESIGN_Unified_Behaviour_Run</c> U-2) — the arms are now RUNNERS.</b> Each tier's kernel, brain
    /// lookup, pause, trace and quirks (HSM's MobilityLost interrupt and hosted children) live in its
    /// <see cref="Runners.IBehaviorRunner"/>; this system keeps ONE arm (<c>Run</c>) for what every tier shares — the
    /// finished guard, the block and its reload restart, the finish and the fault. ⚠ The paragraph above is the O7c
    /// history of how two systems became one.</para>
    ///
    /// <para>⛔ <b><c>BlueprintTickSystem</c> is deliberately NOT merged in</b>, on four measured
    /// grounds (§31.14.2): it is constructed by two roots outside <c>CognitiveRuntimeModule</c>, it
    /// ticks world singletons that belong to no entity, it iterates EVERY slot by kind where the brain
    /// roots look up ONE slot by a computed key, and it carries no authority gate. ⚠ Merging it would
    /// be a second, much weaker argument wearing the first one's clothes.</para>
    ///
    /// <para>Ordering: must run AFTER <see cref="ChannelArbitrationSystem"/> so stale channels are
    /// cleared before a brain writes new actions. The brain order
    /// (arbitration → interrupt → <b>tick</b> → cleanup → pulse) is preserved exactly; this merge
    /// removes a NODE from that chain and never reorders it.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in CognitiveRuntimeModule.
    public unsafe class BrainTickSystem : IEcsModuleSystem, IProfiledSystem
    {
        private readonly BehaviorRegistry _registry;

        /// <summary>
        /// ⭐⭐ One cached query per tier, built on the first tick. 📄 §31.7. ⚠ Index-aligned with
        /// <c>BlueprintTierTable.Ascending</c>; an entry is <c>null</c> where that tier is not
        /// registered on this world, so the walk must skip nulls.
        /// </summary>
        private EntityQuery?[]? _tierQueries;

        /// <summary>
        /// Tracks the <see cref="BehaviorState.InstanceId"/> for which a terminal
        /// <see cref="BehaviorFinishedEvent"/> was last published, keyed by entity index.
        /// ⭐ ONE dictionary for both paradigms, which is correct by construction: an entity has ONE
        /// brain, selected by <c>BehaviorState.BrainTier</c>.
        /// </summary>
        private readonly Dictionary<int, uint> _publishedTerminalForInstanceId = new();

        // Reusable buffers for the stale-entry sweep; avoids per-frame heap allocation.
        private readonly HashSet<int> _seenThisFrame = new();
        private readonly List<int>    _staleKeys     = new();

        /// <summary>
        /// Number of entity indices currently tracked for <see cref="BehaviorFinishedEvent"/>
        /// deduplication. Exposed for test verification only; should drop to zero after a
        /// <c>DestructionOrder</c> for the entity is processed.
        /// </summary>
        internal int TrackedEntityCount => _publishedTerminalForInstanceId.Count;

        public string ProfileName => nameof(BrainTickSystem);

        /// <summary>
        /// ⭐⭐⭐ <b><c>P3</c> step <c>3b</c> — the EXECUTION gate.</b> When true, this system processes only
        /// entities whose cognitive state THIS node owns. 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c>
        /// §3.5.
        ///
        /// <para>⛔⛔ <b>Authority gates REPLICATION, not EXECUTION</b> — every egress translator checks
        /// <c>HasAuthority</c>, but a query does not. ⇒ without this flag, declining the brain state stops
        /// a node PUBLISHING the brain and does not stop it RUNNING one, and two nodes tick the same
        /// tree. ⭐ That is what would have made the whole design cosmetic.</para>
        ///
        /// <para>⚠ <b>Defaults to <c>false</c>, and that is load-bearing rather than cautious.</b> A
        /// promoted ghost owns nothing until a role policy or an explicit grant says otherwise, so turning
        /// this on before the node has a policy would stop it processing every entity it did not create —
        /// reproducing <c>CE-256</c> while fixing it. ⇒ the host turns it on in the same breath as handing
        /// over the policy (step 4).</para>
        ///
        /// <para>⭐ <b>ONE gate now covers both paradigms</b>, which is what the two systems had to keep
        /// in step by hand: <c>BrainTier</c> selects the arm, so gating the walk gates the whole brain.</para>
        /// </summary>
        private readonly bool _gateOnAuthority;

        public BrainTickSystem(BehaviorRegistry registry, bool gateOnAuthority = false,
            Fdp.Toolkit.Blueprints.Systems.IReloadLogSink? reloadLog = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _gateOnAuthority = gateOnAuthority;
            // ⚠ Optional exactly as BlueprintTickSystem's is — no production host constructs a sink for either tier today.
            _reloadLog = reloadLog ?? Fdp.Toolkit.Blueprints.Systems.NullReloadLogSink.Instance;
        }

        private readonly Fdp.Toolkit.Blueprints.Systems.IReloadLogSink _reloadLog;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BrainTickSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // ⭐⭐⭐ DISCOVERY IS THE TIER WALK — the shape BlueprintTickSystem has had since B3, and
            //   which BTree adopted in O7c-②. 📐 There is no brain COMPONENT left to query on: both
            //   roots live in occurrence slots, so the thing to enumerate is "entities carrying a
            //   store", and BrainTier discriminates inside the loop.
            //   📄 §31.7 — BuildTierQueries owns smallest-first ordering, build-once caching and the
            //   skip for a tier this world never registered.
            _tierQueries ??= BlueprintTierTable.BuildTierQueries(
                repo, qb => qb.WithOwnedWhen<BehaviorState>(_gateOnAuthority));

            var tiers = BlueprintTierTable.Ascending;

            // ⭐ CE-446: a blueprint behaviour's tick takes the frame's command buffer, like an Instance tick.
            _ecb = view.GetCommandBuffer();

            // Prune the dedup cache using reliable lifecycle events.
            foreach (var evt in repo.Bus.Read<DestructionOrder>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);
            foreach (var evt in repo.Bus.Read<ClearBehaviorEvent>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);

            SweepStaleDedupEntries(tiers.Count);

            // ⭐ S4 — the MobilityLost interrupt (CE-324) moved into HsmRunner with the rest of the HSM tier's quirks.

            for (int t = 0; t < tiers.Count; t++)
            {
                var q = _tierQueries![t];
                if (q is null) continue;            // tier not registered on this world

                foreach (var entity in q)
                {
                    var behavior = repo.GetComponent<BehaviorState>(entity);

                    // ⭐⭐ BrainTier is THE discriminator, and it always was — an entity carrying an
                    //   occurrence store may be a BTree brain, an HSM brain or a pure blueprint host.
                    //   ⛔ The brain components were a second, redundant discriminator; losing them is
                    //   not losing a filter.
                    if (!_registry.TryGetDefinition(behavior.ActiveBehaviorHash, out var def))
                        continue;

                    if (BehaviorRunners.For(behavior.BrainTier) is { } runner)
                        Run(runner, repo, entity, behavior, def, deltaTime);

                    // ⭐ CE-482 — FAIL LOUD: a run that raised a fault this tick (and did not already finish) ends now, through
                    //   the normal finish — so its commands, channels and owned parts are released like any other end.
                    //   📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D2.
                    if (BehaviorFault.IsPending(repo, entity, behavior.InstanceId))
                        Finish(repo, entity, behavior, NodeStatus.Failure);
                }
            }
        }

        /// <summary>
        /// ⭐⭐ <b>Drop dedup entries for entities that left the walk — and the premise is RESTATED
        /// rather than inherited.</b> 📄 §31.14.6.
        ///
        /// <para>📐 The sweep existed in <c>HsmTickSystem</c> and <b>not at all</b> in
        /// <c>BTreeTickSystem</c> — two systems that should behave identically did not, and a merge
        /// that copied whichever twin it started from would have silently picked a winner.</para>
        ///
        /// <para>⛔ <b>Its old justification is obsolete by this programme:</b> <i>"entities no longer in
        /// the query — brain component removed without a lifecycle event"</i>. 🔴 There is no brain
        /// component any more. ⭐ <b>The premise in slot terms:</b> an entity leaves the walk when its
        /// STORE goes or its <c>BrainTier</c> changes; the dictionary is keyed by <c>entity.Index</c>,
        /// which the ECS <b>reuses</b>, so a stale entry on a recycled index would suppress a genuine
        /// <c>BehaviorFinishedEvent</c> for a DIFFERENT entity. ⇒ that is a correctness argument, and
        /// it applies to the BTree arm exactly as much — <b>the merge FIXES a latent BTree gap rather
        /// than importing an HSM quirk.</b></para>
        /// </summary>
        private void SweepStaleDedupEntries(int tierCount)
        {
            // ⭐ CE-446 step 3: the blueprint layout record is keyed the same way, for the same reason.
            if (_publishedTerminalForInstanceId.Count == 0 && _blueprintLayout.Count == 0) return;

            _seenThisFrame.Clear();
            for (int t = 0; t < tierCount; t++)
            {
                var q = _tierQueries![t];
                if (q is null) continue;
                foreach (var seenEntity in q)
                    _seenThisFrame.Add(seenEntity.Index);
            }

            if (_seenThisFrame.Count == 0)
            {
                _publishedTerminalForInstanceId.Clear();
                _blueprintLayout.Clear();
                return;
            }

            _staleKeys.Clear();
            foreach (var key in _publishedTerminalForInstanceId.Keys)
                if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
            foreach (var key in _staleKeys)
                _publishedTerminalForInstanceId.Remove(key);

            _staleKeys.Clear();
            foreach (var key in _blueprintLayout.Keys)
                if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
            foreach (var key in _staleKeys)
                _blueprintLayout.Remove(key);
        }

        // ══ THE ONE ARM — every tier, through its runner (S4) ═════════════════════════════════

        /// <summary>
        /// ⭐⭐⭐ <b>S4 (<c>DESIGN_Unified_Behaviour_Run</c> §3, U-2) — one arm for every tier.</b> The runner locates the
        /// brain state and steps it; this body owns what every tier shares: the already-finished guard, the block and
        /// its reload restart (<c>CE-452</c>), and the finish (<c>CE-449</c>). ⭐ The same <c>Runner.Tick</c> over the same
        /// (brain, block) pair is what hosting will call (S5) — root = hosted with no host.
        /// </summary>
        private void Run(IBehaviorRunner runner, EntityRepository repo, Entity entity, in BehaviorState behavior,
                         BehaviorDefinition def, float deltaTime)
        {
            // ⭐ Finished runs never tick again: Finish clears (BrainTier = 0), and this guards the same frame.
            if (_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint doneFor)
                && doneFor == behavior.InstanceId)
                return;

            if (!runner.TryGetRootBrain(repo, entity, def, out byte* brain, out int brainBytes))
                return;

            // ⭐⭐⭐ P4-② — THE BLACKBOARD IS THE ROOT PARAMS SLOT, resolved once per entity per tick and handed down
            //   as a `ref byte`. ⛔ The no-block case is gated on a checkable predicate (RootParamsBytes), never a
            //   null-guess; "no block" is the shared SENTINEL, so a projection from it fails loudly (CE-431).
            ref byte block = ref BehaviorBlock.None;
            int blockBytes = RootParamsAccess.RootParamsBytes(def);
            if (blockBytes > 0)
            {
                // ⭐ CE-452: a reload that changed the block's width or layout restarts the behaviour instead of ticking it.
                if (!RestartIfRelaidOut(repo, entity, behavior, def, blockBytes)) return;
                block = ref RootParamsAccess.RootRef(repo, entity);
            }

            var ctx = new BehaviorRunContext
            {
                World = repo, Self = entity, Definition = def, InstanceId = behavior.InstanceId, Ecb = _ecb, DeltaTime = deltaTime,
            };
            var status = runner.Tick(ref ctx, brain, brainBytes, ref block);

            if (status == NodeStatus.Success || status == NodeStatus.Failure)
                Finish(repo, entity, behavior, status);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-449</c> — finishing is TERMINAL, for every tier.</b> 📄 <c>BD1-DESIGN.md</c> §1.0a: root
        /// <c>Success</c>/<c>Failure</c> ⇒ <i>"the entire behavior has concluded"</i>.
        /// <para>
        /// 🔒 User, <c>2026-09-30</c>: <i>"Every behavior must be finishable … must be finishable and clean up resources"</i> ·
        /// <i>"finishing a behavior should cancel the commands exactly same as Clear Behavior does … each channel resets."</i>
        /// ⇒ publish <see cref="BehaviorFinishedEvent"/> once, then run THE clear
        /// (<see cref="BehaviorIngressSystem.Clear"/>): every slot of the behaviour is freed, <c>InstanceId</c> is bumped so
        /// <c>ChannelArbitrationSystem</c> resets each channel to its default, and <c>BrainTier = 0</c> so nothing ticks it again.
        /// </para>
        /// <para>
        /// ⛔ Before this, a BTree re-ran from its root every frame after finishing (the interpreter resets
        /// <c>RunningNodeIndex</c>) and an HSM ran again because this system cleared its <c>Terminated</c> latch. ⚠ A tree that
        /// is MEANT to loop says so with a <c>Repeater</c> at its root.
        /// </para>
        /// </summary>
        private void Finish(EntityRepository repo, Entity entity, in BehaviorState behavior, NodeStatus result)
        {
            if (_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint prev) && prev == behavior.InstanceId)
                return;

            // ⭐ CE-482: a fault the run raised overrides how it ended — even a Success returned in the same tick.
            var fault = BehaviorFault.Take(repo, entity, behavior.InstanceId);
            if (fault != BehaviorFaultCode.None) result = NodeStatus.Failure;
            repo.Bus.Publish(new BehaviorFinishedEvent { Entity = entity, Result = result, FaultCode = fault });
            _publishedTerminalForInstanceId[entity.Index] = behavior.InstanceId;
            BehaviorIngressSystem.Clear(repo, entity, _registry);
        }

        /// <summary>⭐ CE-446: the frame's command buffer, handed to every runner (a blueprint tick records into it).</summary>
        private Fdp.Interfaces.IEntityCommandBuffer? _ecb;

        /// <summary>The layout each running behaviour started with — keyed by <c>entity.Index</c>, valid only for the recorded
        /// <c>(InstanceId, behaviour hash)</c>; <c>Pending</c> = a restart was requested for that instance. Swept with the
        /// terminal dedup.</summary>
        private readonly Dictionary<int, (uint InstanceId, int Behavior, ulong Layout, bool Pending)> _blueprintLayout = new();

        /// <summary>
        /// ⭐⭐ <b><c>CE-452</c> — a hot reload that re-lays-out a RUNNING behaviour's root block RESTARTS it through the ONE
        /// start pipeline, with the parameters it was started with.</b> 📄 <c>Architect_Question_77</c> §5.12.
        ///
        /// <para>
        /// ⭐ Design basis: <c>btree-ai-action-binding/SLICE2-DESIGN.md</c> Flaw 2 — on a layout-changing reload
        /// <i>"re-publish <c>AssignBehaviorEvent</c> for every entity running that behavior"</i> so ingress re-provisions
        /// correctly-sized slots; <c>AI_Editor_Shared_Infrastructure.md</c> §17 — Soft keeps state, Hard restarts.
        /// ⭐ Detected per entity on the tick, because no reload path tells a world which entities run the reloaded
        /// behaviour (<c>DESIGN_Cgf_Editor_Sharing_Slice3</c> §10.3).
        /// </para>
        /// <list type="bullet">
        /// <item>WIDTH changed (every tier) — the block no longer fits the definition; ⛔ ticking would read/write past it.</item>
        /// <item>LAYOUT changed (blueprint tier — the generated <c>StructureHash</c>); a BTree/HSM root has no layout hash yet.</item>
        /// </list>
        /// <para>
        /// ⭐ On either: this tick is SKIPPED and an <c>AssignBehaviorEvent</c> is published with the
        /// <see cref="BehaviorStartRecord"/>'s name + JSON (authored defaults when there is none), so next frame's
        /// <c>BehaviorIngressSystem.Start</c> rebuilds everything — params, own resolver, hosted children (they re-seed from
        /// the rebuilt block), store growth (structural, so ingress-only). ⚠ A restart is a new instance: <c>InstanceId</c>
        /// bumps, so each channel resets once. ⛔ If the instance is STILL here next tick, the restart failed (the parse
        /// threw, or the name is gone) ⇒ it is CLEARED rather than ticked over a block that does not fit.
        /// </para>
        /// <returns><c>false</c> when this tick must be skipped.</returns>
        /// </summary>
        private bool RestartIfRelaidOut(
            EntityRepository repo, Entity entity, in BehaviorState behavior, BehaviorDefinition def, int blockBytes)
        {
            bool known = _blueprintLayout.TryGetValue(entity.Index, out var started)
                         && started.InstanceId == behavior.InstanceId
                         && started.Behavior == behavior.ActiveBehaviorHash;

            if (known && started.Pending)
            {
                _blueprintLayout.Remove(entity.Index);
                BehaviorIngressSystem.Clear(repo, entity, _registry);
                return false;
            }

            // ⚠ Per entity per tick on every brain — written only when the instance or its layout changed.
            if (!known || started.Layout != def.BlueprintStructureHash)
                _blueprintLayout[entity.Index] = (behavior.InstanceId, behavior.ActiveBehaviorHash, def.BlueprintStructureHash, false);

            bool present = RootParamsAccess.TryGetRootBytes(repo, entity, out _, out int length);
            bool widthChanged  = present && length != blockBytes;
            bool layoutChanged = known && started.Layout != def.BlueprintStructureHash;
            if (!widthChanged && !layoutChanged) return true;   // ⚠ absent: RootRef throws with the causes

            string? name = null, json = null;
            if (repo.HasManagedComponent<BehaviorStartRecord>(entity))
            {
                var record = ((ISimulationView)repo).GetManagedComponentRO<BehaviorStartRecord>(entity);
                if (record.InstanceId == behavior.InstanceId) { name = record.BehaviorName; json = record.JsonParams; }
            }
            if (name == null && !_registry.TryGetName(behavior.ActiveBehaviorHash, out name))
            {
                BehaviorIngressSystem.Clear(repo, entity, _registry);
                return false;
            }

            repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = name!, JsonParams = json ?? "{}" });
            _blueprintLayout[entity.Index] = (behavior.InstanceId, behavior.ActiveBehaviorHash, def.BlueprintStructureHash, true);
            _reloadLog.OnHardReset(behavior.ActiveBehaviorHash, entity,
                known ? started.Layout : 0UL, def.BlueprintStructureHash);
            return false;
        }

    }
}
