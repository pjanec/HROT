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

        public BrainTickSystem(BehaviorRegistry registry, bool gateOnAuthority = false)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _gateOnAuthority = gateOnAuthority;
        }

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

            // Prune the dedup cache using reliable lifecycle events.
            foreach (var evt in repo.Bus.Read<DestructionOrder>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);
            foreach (var evt in repo.Bus.Read<ClearBehaviorEvent>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);

            SweepStaleDedupEntries(tiers.Count);

            // ⭐⭐⭐ CE-324 (2026-09-23) — `Interrupt` PRIORITY, AND ITS ABSENCE WAS A REAL DEFECT.
            //   🔴 `EventPriority.Low` is 0, so `new HsmEvent { EventId = … }` built a LOW-priority
            //     event and MobilityLost — the one interrupt this system injects — went into the
            //     SHARED NORMAL/LOW RING instead of the reserved interrupt slot that exists for it.
            //   ⛔⛔ On the 128 tier that ring holds exactly ONE event (`Tier2_Ring_Capacity = 1`), so
            //     a single queued normal event was enough to make the vehicle-disabled interrupt
            //     fail to enqueue — and `EnqueueTier2` reports that by returning false, which this
            //     call site discarded.
            //   ⭐ The reserved slot CANNOT be crowded out by normal traffic, which is the guarantee
            //     §9.4 claimed the system already had. It does now. 📄 §31.23.
            var mobilityLostEvent = new HsmEvent
            {
                EventId  = BehaviorConstants.EventId_MobilityLost,
                Priority = EventPriority.Interrupt,
            };

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

                    if (behavior.BrainTier == BehaviorConstants.BrainTierBTree)
                        TickBTree(repo, entity, behavior, def, deltaTime);
                    else if (behavior.BrainTier == BehaviorConstants.BrainTierHsm)
                        TickHsm(repo, entity, behavior, def, deltaTime, mobilityLostEvent);
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
            if (_publishedTerminalForInstanceId.Count == 0) return;

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
                return;
            }

            _staleKeys.Clear();
            foreach (var key in _publishedTerminalForInstanceId.Keys)
                if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
            foreach (var key in _staleKeys)
                _publishedTerminalForInstanceId.Remove(key);
        }

        // ══ ARM 1 — BEHAVIOUR TREE ══════════════════════════════════════════════════════════

        private void TickBTree(
            EntityRepository repo, Entity entity, in BehaviorState behavior,
            BehaviorDefinition def, float deltaTime)
        {
            if (def.BTreeInterpreter == null)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine(
                    $"[BrainTickSystem] Behavior hash {behavior.ActiveBehaviorHash} has no BTree interpreter; entity {entity.Index} skipped.");
#endif
                return;
            }

            // ⭐⭐⭐ THE CURSOR COMES FROM THE ENTITY'S ROOT STATE SLOT (O7c-② / CE-319).
            //   ⛔ RequireStateRef THROWS on a miss rather than handing back a scratch cursor: a
            //   BTree-tier entity that reaches the tick with no slot means ingress never provisioned
            //   one, and ticking a stack local would restart the tree every frame — forever,
            //   silently, looking like a behaviour that never progresses.
            //
            //   ⚠ LIFETIME: this ref points INTO the tier component, under OccurrenceStoreAccess's
            //   rule — valid for this call, invalid across anything that adds or removes a component
            //   on this entity. ⛔ Nothing in this method does: slots attach LAZILY during the tick,
            //   and TryAttach bump-allocates or reuses a free block without moving an existing
            //   payload. ⭐ Only CopyToLargerTier moves payloads, and that is structural —
            //   ingress-only, never mid-tick.
            ref var btState = ref RootStateAccess.RequireStateRef(repo, entity);

            // Entity is held by the debugger. Skip ticking the interpreter to prevent
            // trace log spam and state mutation.
            if ((btState.InstanceFlags & BehaviorInstanceFlags.Paused) != 0)
                return;

            // ⭐⭐⭐ P4-② — THE BLACKBOARD IS THE ROOT PARAMS SLOT, RESOLVED ONCE PER ENTITY PER TICK
            //   and handed down as a `ref byte`.
            //
            // ⛔⛔ THE NO-PARAMS CASE IS GATED ON A CHECKABLE PREDICATE, NOT A NULL-GUESS.
            //   A behaviour that declares no parameters has NO root slot — ingress attaches one only
            //   when rootBytes > 0 — so RootRef would THROW. ⚠ Asking "did the lookup fail?" cannot
            //   tell "this behaviour has no params" from "the slot should exist and does not".
            byte __noParamsScratch = 0;
            ref byte blackboard = ref __noParamsScratch;
            if (RootParamsAccess.RootParamsBytes(def) > 0)
                blackboard = ref RootParamsAccess.RootRef(repo, entity);

            // Resolve the optional per-entity trace ring buffer.
            BTreeTraceWorkingMemory1024* tracePtr = null;
            bool emitToLog = false;
            if (repo.HasComponent<DebugState>(entity))
            {
                ref readonly var dbg = ref repo.GetComponentRO<DebugState>(entity);
                emitToLog = (dbg.Behavior & BehaviorDebugFlags.EmitToLog) != 0;
                if ((dbg.Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0
                    && repo.HasComponent<BTreeTraceWorkingMemory1024>(entity))
                {
                    ref var traceMem = ref repo.GetComponentRW<BTreeTraceWorkingMemory1024>(entity);
                    traceMem.LastInstanceId = behavior.InstanceId;
                    tracePtr = (BTreeTraceWorkingMemory1024*)Unsafe.AsPointer(ref traceMem);
                }
            }

            ushort startWritePos = tracePtr != null ? tracePtr->WritePos : (ushort)0;

            // Stack-allocate context -- zero heap allocation.
            var context = new BTreeContext
            {
                Self         = entity,
                World        = repo,
                _deltaTime   = deltaTime,
                _frameCount  = (int)repo.SimulationTick,
                _floatParams = Array.Empty<float>(),
                _intParams   = Array.Empty<int>(),
                _instanceId  = behavior.InstanceId,
                TraceBuffer  = tracePtr,
            };

            var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState, ref context);

            if (tracePtr != null && emitToLog
                && BehaviorTraceLog.Instance is { IsTraceEnabled: true } emitter)
            {
                int bytesWritten = tracePtr->WritePos - startWritePos;
                if (bytesWritten < 0)
                    bytesWritten += BTreeTraceWorkingMemory1024.PayloadBytes;
                int recordsWritten = bytesWritten / BTreeTraceWorkingMemory1024.RecordStride;
                if (recordsWritten > 0)
                    EmitBTreeRecordsToLog(entity, repo, tracePtr, startWritePos, recordsWritten,
                        def.BTreeInterpreter.Blob, emitter);
            }

            // ⭐ TERMINALITY, BTREE FORM: the root's returned status. Published exactly once per
            //   terminal transition per behaviour instance.
            if (rootResult == NodeStatus.Success || rootResult == NodeStatus.Failure)
            {
                if (!_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint prevInstanceId)
                    || prevInstanceId != behavior.InstanceId)
                {
                    repo.Bus.Publish(new BehaviorFinishedEvent
                    {
                        Entity = entity,
                        Result = rootResult
                    });
                    _publishedTerminalForInstanceId[entity.Index] = behavior.InstanceId;
                }
            }
        }

        // ══ ARM 2 — HIERARCHICAL STATE MACHINE ══════════════════════════════════════════════

        private void TickHsm(
            EntityRepository repo, Entity entity, in BehaviorState behavior,
            BehaviorDefinition def, float deltaTime, in HsmEvent mobilityLostEvent)
        {
            if (def.HsmDefinition == null) return;

            // ⭐⭐⭐ THE INSTANCE COMES FROM THE ENTITY'S ROOT HSM SLOT, WITH ITS SIZE (O7c-④a).
            //
            //   ⛔⛔ A MISS IS A SKIP HERE, NOT A THROW, AND THE ASYMMETRY WITH THE BTREE ARM IS
            //     MEASURED RATHER THAN CHOSEN. 📄 §31.16.2. Provisioning for an HSM brain happens at
            //     INGRESS only — the translator cannot size the slot, because TkbTranslatorSet.Base()
            //     holds no BehaviorRegistry and therefore cannot reach the blob. ⇒ an entity that
            //     spawned with a default HSM behaviour and never received an assign legitimately has
            //     no instance, and that is EXACTLY the state the component version was in: a zeroed
            //     BrainHsm128 has MachineId == 0, which HsmKernelCore.ValidateInstance rejects by
            //     `continue`. ⭐ Skipping reproduces that behaviour byte for byte; throwing would turn
            //     a long-standing silent no-op into a crash.
            //
            //   ⚠ Every BTree behaviour has a cursor and the translator DOES provision it, so a miss
            //     there is a genuine fault — which is why RequireStateRef throws and this does not.
            if (!RootHsmAccess.TryGetInstance(repo, entity, out byte* instance, out int instanceSize))
                return;

            var header = (InstanceHeader*)instance;

            // BHU-009: inject the MobilityLost interrupt if the interrupt register is set.
            if (repo.HasComponent<BrainInterrupts>(entity))
            {
                ref var bb = ref repo.GetComponentRW<BrainInterrupts>(entity);
                if (bb.Interrupt_MobilityLost == 1
                    && !HsmEventQueue.TryEnqueue(instance, instanceSize, mobilityLostEvent))
                {
                    // ⛔⛔ CE-324: THE RETURN VALUE IS NO LONGER DISCARDED. A false here means the
                    //   reserved interrupt slot still holds an UNCONSUMED interrupt — the kernel
                    //   drains it inside the Update below, so within one tick this should not
                    //   happen. ⚠ It is reported rather than fixed up: silently dropping the event
                    //   that tells a disabled vehicle to stop is exactly the silent-non-execution
                    //   shape this programme keeps filing (CE-315, CE-321, CE-323).
                    //   ⚠ DEBUG-only on purpose: this sits in the per-entity, per-tick path, and a
                    //   production log here would spam once per frame for as long as the condition
                    //   holds. The rail O7_R58 is what proves the enqueue succeeds.
#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"[BrainTickSystem] entity {entity.Index}: MobilityLost interrupt DROPPED — " +
                        $"the reserved interrupt slot was still occupied (instance {instanceSize} B).");
#endif
                }
            }

            // Resolve the optional per-entity HSM trace context.
            HsmTraceContext  traceCtx    = default;
            HsmTraceContext* traceCtxPtr = null;
            HsmTraceWorkingMemory1024* hsmTracePtr = null;
            bool emitToLog = false;
            if (repo.HasComponent<DebugState>(entity))
            {
                ref readonly var dbg = ref repo.GetComponentRO<DebugState>(entity);
                emitToLog = (dbg.Behavior & BehaviorDebugFlags.EmitToLog) != 0;
                if ((dbg.Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0
                    && repo.HasComponent<HsmTraceWorkingMemory1024>(entity))
                {
                    ref var traceMem = ref repo.GetComponentRW<HsmTraceWorkingMemory1024>(entity);
                    traceMem.LastInstanceId = behavior.InstanceId;
                    hsmTracePtr            = (HsmTraceWorkingMemory1024*)Unsafe.AsPointer(ref traceMem);
                    traceCtx.Buffer        = (byte*)Unsafe.AsPointer(ref traceMem.Buffer[0]);
                    traceCtx.WritePos      = (ushort*)Unsafe.AsPointer(ref traceMem.WritePos);
                    traceCtx.RecordCount   = (ushort*)Unsafe.AsPointer(ref traceMem.RecordCount);
                    traceCtx.CapacityBytes = HsmTraceWorkingMemory1024.PayloadBytes;
                    traceCtx.MaxRecords    = HsmTraceWorkingMemory1024.CapacityRecords;
                    traceCtx.FilterLevel   = ResolveTraceLevel(dbg.Behavior);
                    traceCtx.CurrentTick   = (ushort)repo.SimulationTick;
                    traceCtx.InstanceId    = behavior.InstanceId;
                    traceCtxPtr = &traceCtx;

                    // Honor the per-instance gate inside the kernel.
                    header->Flags |= InstanceFlags.DebugTrace;
                }
                else if ((dbg.Behavior & BehaviorDebugFlags.EnableTraceBuffer) == 0)
                {
                    // Clear the gate when the bit flips off so a stale instance flag does not keep
                    // producing dead traces.
                    header->Flags &= unchecked((InstanceFlags)(byte)~(byte)InstanceFlags.DebugTrace);
                }
            }

            ushort startWritePos = hsmTracePtr != null ? hsmTracePtr->WritePos : (ushort)0;

            // DEBT-007 full resolution: WorldHandle carries the GCHandle IntPtr so that action
            // delegates can recover the EntityRepository via GCHandle.FromIntPtr.
            var bridge = new HsmKernelBridge
            {
                Self         = entity,
                WorldHandle  = repo.UnmanagedHandle,
                TraceContext = traceCtxPtr,
            };

            // ⭐⭐⭐ THE SIZE COMES FROM THE SLOT, NEVER FROM A TYPE. §9.4: with occurrence payloads
            //   packed adjacently, a generic overload whose sizeof(TInstance) exceeds the slot reads
            //   into the NEXT OCCURRENCE'S bytes — no compiler check, no runtime check.
            var dummyPage = new CommandPage();
            HsmKernel.Update(
                def.HsmDefinition, instance, instanceSize, &bridge, deltaTime, &dummyPage, traceCtxPtr);

            if (hsmTracePtr != null && emitToLog
                && BehaviorTraceLog.Instance is { IsTraceEnabled: true } emitter)
            {
                int bytesWritten = hsmTracePtr->WritePos - startWritePos;
                if (bytesWritten < 0)
                    bytesWritten += HsmTraceWorkingMemory1024.PayloadBytes;
                int recordsWritten = bytesWritten / HsmTraceWorkingMemory1024.RecordStride;
                if (recordsWritten > 0)
                    EmitHsmRecordsToLog(entity, repo, hsmTracePtr, startWritePos, recordsWritten,
                        def.HsmMetadata, emitter);
            }

            // ⭐ TERMINALITY, HSM FORM: a flag in the instance header, not a returned status.
            //   BHU-007 — publish exactly once per behaviour instance, then clear the latch so a
            //   re-assigned behaviour does not inherit Terminated from the previous one.
            if ((header->Flags & InstanceFlags.Terminated) != 0)
            {
                int  entityIdx  = entity.Index;
                uint instanceId = behavior.InstanceId;
                if (!_publishedTerminalForInstanceId.TryGetValue(entityIdx, out uint prev)
                    || prev != instanceId)
                {
                    _publishedTerminalForInstanceId[entityIdx] = instanceId;
                    repo.Bus.Publish(new BehaviorFinishedEvent { Entity = entity });
                    header->Flags &= unchecked((InstanceFlags)(byte)~(byte)InstanceFlags.Terminated);
                    header->Phase  = InstancePhase.Idle;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TraceLevel ResolveTraceLevel(BehaviorDebugFlags flags)
        {
            // Highest-tier wins (Tier3 implies Tier2 implies Tier1).
            if ((flags & BehaviorDebugFlags.HsmTraceTier3) != 0) return TraceLevel.Tier3;
            if ((flags & BehaviorDebugFlags.HsmTraceTier2) != 0) return TraceLevel.Tier2;
            if ((flags & BehaviorDebugFlags.HsmTraceTier1) != 0) return TraceLevel.Tier1;
            // Default when EnableTraceBuffer is on but no tier specified — pick Tier1
            // (transitions + events + state changes) so the buffer is not silent.
            return TraceLevel.Tier1;
        }

        // ══ TRACE DECODING — unchanged, one copy each ═══════════════════════════════════════

        /// <summary>
        /// Decode the per-frame BTree trace delta into BehaviorLog strings. Allocates strings, but is
        /// only entered after explicit <c>EmitToLog</c> + <c>IsTraceEnabled</c> gates, so the
        /// steady-state simulation path remains allocation-free.
        /// </summary>
        private static void EmitBTreeRecordsToLog(
            Entity entity,
            EntityRepository repo,
            BTreeTraceWorkingMemory1024* traceData,
            ushort startWritePos,
            int recordCount,
            BehaviorTreeBlob blob,
            IBehaviorTraceLogEmitter emitter)
        {
            int payloadBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
            int stride       = BTreeTraceWorkingMemory1024.RecordStride;
            byte* bufferPtr  = (byte*)Unsafe.AsPointer(ref traceData->Buffer[0]);

            for (int i = 0; i < recordCount; i++)
            {
                int offset = (startWritePos + (i * stride)) % payloadBytes;
                var rec = (BTreeTraceRecord*)(bufferPtr + offset);

                string nodeLabel = "?";
                if (blob.DebugMetadata != null && rec->NodeIndex < blob.DebugMetadata.Length)
                {
                    var lbl = blob.DebugMetadata[rec->NodeIndex].Label;
                    if (!string.IsNullOrEmpty(lbl)) nodeLabel = lbl;
                }

                string msg = rec->OpCode switch
                {
                    BTreeTraceOpCode.NodeEvaluated =>
                        $"Node [{rec->NodeIndex}] {nodeLabel} -> {rec->Status}",
                    BTreeTraceOpCode.WaitStarted =>
                        $"Wait started [{rec->NodeIndex}] {nodeLabel} duration={rec->Duration:F2}s",
                    BTreeTraceOpCode.WaitCompleted =>
                        $"Wait completed [{rec->NodeIndex}] {nodeLabel}",
                    BTreeTraceOpCode.ChannelMutated =>
                        $"Channel mutated [{rec->NodeIndex}] {nodeLabel}: ch={(Fbt.Kernel.ChannelKind)rec->Channel} action={rec->ActiveAction} status={rec->ChannelStatus}",
                    BTreeTraceOpCode.Error =>
                        $"ERROR [{rec->NodeIndex}] {nodeLabel}: code={rec->ErrorCode}",
                    BTreeTraceOpCode.ScopePushed =>
                        $"Scope pushed depth={rec->StackDepth}",
                    BTreeTraceOpCode.ScopePopped =>
                        $"Scope popped depth={rec->StackDepth}",
                    _ => $"OpCode {rec->OpCode}",
                };

                emitter.EmitTrace(entity, repo, msg, "BTreeTrace");
            }
        }

        /// <summary>Decode the per-frame HSM trace delta into BehaviorLog strings. Same gating.</summary>
        private static void EmitHsmRecordsToLog(
            Entity entity,
            EntityRepository repo,
            HsmTraceWorkingMemory1024* traceData,
            ushort startWritePos,
            int recordCount,
            MachineMetadata? meta,
            IBehaviorTraceLogEmitter emitter)
        {
            int payloadBytes = HsmTraceWorkingMemory1024.PayloadBytes;
            int stride       = HsmTraceWorkingMemory1024.RecordStride;
            byte* bufferPtr  = (byte*)Unsafe.AsPointer(ref traceData->Buffer[0]);

            for (int i = 0; i < recordCount; i++)
            {
                int offset = (startWritePos + (i * stride)) % payloadBytes;
                var rec = (TraceRecord*)(bufferPtr + offset);

                string msg = rec->OpCode switch
                {
                    TraceOpCode.StateEnter =>
                        $"State enter [{rec->StateIndex}] {meta?.GetStateName(rec->StateIndex) ?? "?"}",
                    TraceOpCode.StateExit =>
                        $"State exit [{rec->StateIndex}] {meta?.GetStateName(rec->StateIndex) ?? "?"}",
                    TraceOpCode.Transition =>
                        $"Transition {meta?.GetStateName(rec->StateIndex) ?? "?"} -> {meta?.GetStateName(rec->TargetStateIndex) ?? "?"} on {meta?.GetEventName(rec->TriggerEventId) ?? "?"}",
                    TraceOpCode.EventHandled =>
                        $"Event handled [{rec->EventId}] {meta?.GetEventName(rec->EventId) ?? "?"}",
                    TraceOpCode.ActionExecuted =>
                        $"Action [{rec->ActionId}] {meta?.GetActionName(rec->ActionId) ?? "?"}",
                    TraceOpCode.GuardEvaluated =>
                        $"Guard [{rec->GuardId}] {meta?.GetActionName(rec->GuardId) ?? "?"} -> {(rec->GuardResult != 0 ? "PASS" : "FAIL")}",
                    TraceOpCode.Error =>
                        $"ERROR code={rec->ErrorCode}",
                    _ => $"OpCode {rec->OpCode}",
                };

                emitter.EmitTrace(entity, repo, msg, "HsmTrace");
            }
        }
    }
}
