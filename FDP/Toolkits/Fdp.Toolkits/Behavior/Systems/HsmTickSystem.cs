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
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Minimal unmanaged bridge passed to <see cref="HsmKernel.Update{TInstance,TContext}"/>.
    /// Must satisfy <c>where TContext : unmanaged</c> — cannot hold managed references.
    /// <c>WorldHandle</c> is an <see cref="System.IntPtr"/> (unmanaged) holding the GCHandle
    /// table index for the <see cref="EntityRepository"/>; recover with
    /// <c>GCHandle.FromIntPtr(bridge->WorldHandle).Target</c>.
    /// See DEBT-007-HSM-ANALYSIS.md for full explanation.
    /// </summary>
    public unsafe struct HsmKernelBridge
    {
        public Entity Self;
        public IntPtr WorldHandle;   // IntPtr is unmanaged; holds GCHandle table index

        /// <summary>
        /// Optional pointer to a stack-local <see cref="HsmTraceContext"/> built each
        /// tick over the entity's <c>HsmTraceWorkingMemory1024</c> component. Null
        /// when tracing is disabled. User-authored HSM actions/guards may write
        /// domain errors via <c>bridge.TraceContext-&gt;WriteError(...)</c>.
        /// </summary>
        public HsmTraceContext* TraceContext;
    }

    /// <summary>
    /// Generic system that steps FastHSM instances of type <typeparamref name="T"/> for
    /// every entity whose <see cref="BehaviorState.BrainTier"/> equals
    /// <see cref="BehaviorConstants.BrainTierHsm"/>.
    ///
    /// Register twice in the world:
    /// <code>
    ///   group.AddSystem(new HsmTickSystem&lt;BrainHsm64&gt;(registry));
    ///   group.AddSystem(new HsmTickSystem&lt;BrainHsm128&gt;(registry));
    /// </code>
    ///
    /// Ordering: must run AFTER <see cref="ChannelArbitrationSystem"/>.
    /// </summary>
    /// <typeparam name="T">
    /// ECS component that wraps an HSM instance (<see cref="BrainHsm64"/> or
    /// <see cref="BrainHsm128"/>).  The component's memory layout must start with the
    /// corresponding <c>HsmInstance64/128</c> so that
    /// <see cref="HsmKernel.Update{TInstance,TContext}"/> can identify the tier from
    /// <c>sizeof(T)</c>.
    /// </typeparam>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in CognitiveRuntimeModule.
    public class HsmTickSystem<T> : IEcsModuleSystem, IProfiledSystem 
        where T : unmanaged
    {
        private readonly BehaviorRegistry _registry;

        /// <summary>
        /// Tracks the <see cref="BehaviorState.InstanceId"/> for which a terminal
        /// <see cref="BehaviorFinishedEvent"/> was last published, keyed by entity index.
        /// Prevents repeated publication when the same HSM behavior stays terminated
        /// across consecutive ticks.
        /// </summary>
        private readonly Dictionary<int, uint> _publishedTerminalForInstanceId = new();

        // Reusable buffers for stale-entry sweeping; avoids per-frame heap allocation.
        private readonly HashSet<int> _seenThisFrame = new();
        private readonly List<int>    _staleKeys     = new();

        // Exposed for unit-testing: number of entities currently being tracked for
        // deduplication of BehaviorFinishedEvent. Should drop to zero after a
        // DestructionOrder for the entity is processed.
        internal int TrackedEntityCount => _publishedTerminalForInstanceId.Count;

        public string ProfileName => $"HsmTickSystem<{typeof(T).Name}>";

        public HsmTickSystem(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(HsmTickSystem<T>)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var q = repo.Query()
                .With<BehaviorState>()
                .With<T>()
                .Build();

            // Prune deduplication cache using reliable lifecycle events.
            foreach (var evt in repo.Bus.Read<DestructionOrder>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);
            foreach (var evt in repo.Bus.Read<ClearBehaviorEvent>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);

            // Sweep stale entries: entities no longer in the query (brain component removed
            // without a lifecycle event, e.g. dynamic reclassing or direct RemoveComponent).
            if (_publishedTerminalForInstanceId.Count > 0)
            {
                if (q.IsEmpty)
                {
                    _publishedTerminalForInstanceId.Clear();
                }
                else
                {
                    _seenThisFrame.Clear();
                    foreach (var seenEntity in q)
                        _seenThisFrame.Add(seenEntity.Index);

                    _staleKeys.Clear();
                    foreach (var key in _publishedTerminalForInstanceId.Keys)
                        if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
                    foreach (var key in _staleKeys)
                        _publishedTerminalForInstanceId.Remove(key);
                }
            }

            // Early-exit: skip per-entity overhead when no HSM entities are present.
            if (q.IsEmpty)
                return;

            var mobilityLostEvent = new HsmEvent { EventId = BehaviorConstants.EventId_MobilityLost };

            foreach (var entity in q)
            {
                var behavior = repo.GetComponent<BehaviorState>(entity);

                // Only process HSM-tier entities.
                if (behavior.BrainTier != BehaviorConstants.BrainTierHsm)
                    continue;

                // Skip if behavior is unknown or has no HSM definition.
                if (!_registry.TryGetDefinition(behavior.ActiveBehaviorHash, out var def)
                    || def.HsmDefinition == null)
                    continue;

                ref var component = ref repo.GetComponentRW<T>(entity);

                // BHU-009: Inject MobilityLost interrupt if the interrupt register is set.
                if (repo.HasComponent<BrainBlackboard>(entity))
                {
                    ref var bb = ref repo.GetComponentRW<BrainBlackboard>(entity);
                    if (bb.Interrupt_MobilityLost == 1)
                    {
                        T* instPtr = (T*)Unsafe.AsPointer(ref component);
                        HsmEventQueue.TryEnqueue(instPtr, mobilityLostEvent);
                    }
                }

                // Resolve the optional per-entity HSM trace context. Skipped (and the
                // chunk version stays clean) unless DebugState.EnableTraceBuffer is set.
                HsmTraceContext traceCtx = default;
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

                        // Honor the per-instance gate inside the kernel: set the bit
                        // alongside enabling the buffer.
                        ref var hdrSet = ref Unsafe.As<T, InstanceHeader>(ref component);
                        hdrSet.Flags |= InstanceFlags.DebugTrace;
                    }
                    else if ((dbg.Behavior & BehaviorDebugFlags.EnableTraceBuffer) == 0)
                    {
                        // Clear the gate when the bit flips off so a stale instance flag
                        // does not keep producing dead traces.
                        ref var hdrClr = ref Unsafe.As<T, InstanceHeader>(ref component);
                        hdrClr.Flags &= unchecked((InstanceFlags)(byte)~(byte)InstanceFlags.DebugTrace);
                    }
                }

                // Snapshot the cursor BEFORE stepping so we can decode the per-frame delta.
                ushort startWritePos = hsmTracePtr != null ? hsmTracePtr->WritePos : (ushort)0;

                // DEBT-007 full resolution: WorldHandle carries the GCHandle IntPtr so that
                // action delegates can recover the EntityRepository via GCHandle.FromIntPtr.
                // IntPtr is an unmanaged value type -- satisfies 'where TContext : unmanaged'.
                var bridge = new HsmKernelBridge
                {
                    Self         = entity,
                    WorldHandle  = repo.UnmanagedHandle,  // one property read per entity per tick
                    TraceContext = traceCtxPtr,
                };

                // sizeof(T) determines the tier (64 / 128 / 256) inside HsmKernelCore.
                var dummyPage = new CommandPage();
                HsmKernel.Update(def.HsmDefinition, ref component, bridge, deltaTime, ref dummyPage, traceCtxPtr);

                // Optional NLog emission for this frame's records.
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

                // BHU-007: Detect terminal state and publish BehaviorFinishedEvent exactly once
                // per behavior instance. The Terminated flag is cleared so new behavior
                // assignments don't fire a spurious second event.
                ref var hdr = ref Unsafe.As<T, InstanceHeader>(ref component);
                if ((hdr.Flags & InstanceFlags.Terminated) != 0)
                {
                    int  entityIdx  = entity.Index;
                    uint instanceId = behavior.InstanceId;
                    if (!_publishedTerminalForInstanceId.TryGetValue(entityIdx, out uint prev)
                        || prev != instanceId)
                    {
                        _publishedTerminalForInstanceId[entityIdx] = instanceId;
                        repo.Bus.Publish(new BehaviorFinishedEvent { Entity = entity });
                        // Terminal latch fix: clear flag so a re-assigned behavior won't
                        // inherit the Terminated state from the previous one.
                        hdr.Flags &= unchecked((InstanceFlags)(byte)~(byte)InstanceFlags.Terminated);
                        hdr.Phase  = InstancePhase.Idle;
                    }
                }
            }
        }

        private static unsafe void EmitHsmRecordsToLog(
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
