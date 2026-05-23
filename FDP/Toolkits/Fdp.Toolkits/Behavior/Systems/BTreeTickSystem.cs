using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Steps the <see cref="BrainBTreeState"/> for every entity whose
    /// <see cref="BehaviorState.BrainTier"/> equals <see cref="BehaviorConstants.BrainTierBTree"/>.
    ///
    /// Ordering: must run AFTER <see cref="ChannelArbitrationSystem"/> so that stale
    /// channels are cleared before the BTree writes new actions.
    ///
    /// Zero allocation per tick: <see cref="BTreeContext"/> is a stack-allocated struct.
    ///
    /// Publishes <see cref="BehaviorFinishedEvent"/> exactly once per terminal behavior
    /// transition (Success or Failure). A secondary tick on an already-terminal behavior
    /// does not re-publish; the event is suppressed until the behavior's
    /// <see cref="BehaviorState.InstanceId"/> changes (i.e. a new behavior is assigned).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in CognitiveRuntimeModule.
    public unsafe class BTreeTickSystem : IEcsModuleSystem
    {
        private readonly BehaviorRegistry _registry;

        /// <summary>
        /// Tracks the <see cref="BehaviorState.InstanceId"/> for which a terminal
        /// <see cref="BehaviorFinishedEvent"/> was last published, keyed by entity index.
        /// Prevents repeated publication when the same behavior evaluation stays terminal
        /// across consecutive ticks.
        /// </summary>
        private readonly Dictionary<int, uint> _publishedTerminalForInstanceId = new();

        /// <summary>
        /// Number of entity indices currently tracked in the terminal-event deduplication
        /// dictionary. Exposed for test verification only.
        /// </summary>
        internal int TrackedEntityCount => _publishedTerminalForInstanceId.Count;

        public BTreeTickSystem(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BTreeTickSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var q = repo.Query()
                .With<BehaviorState>()
                .With<BrainBTreeState>()
                .With<BrainBlackboard>()
                .Build();

            // Prune deduplication cache using reliable lifecycle events.
            foreach (var evt in repo.Bus.Read<DestructionOrder>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);
            foreach (var evt in repo.Bus.Read<ClearBehaviorEvent>())
                _publishedTerminalForInstanceId.Remove(evt.Entity.Index);

            foreach (var entity in q)
            {
                var behavior = repo.GetComponent<BehaviorState>(entity);

                // Only process BTree-tier entities.
                if (behavior.BrainTier != BehaviorConstants.BrainTierBTree)
                    continue;

                // If the behavior is not registered, skip silently.
                if (!_registry.TryGetDefinition(behavior.ActiveBehaviorHash, out var def)
                    || def.BTreeInterpreter == null)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"[BTreeTickSystem] Behavior hash {behavior.ActiveBehaviorHash} not registered; entity {entity.Index} skipped.");
#endif
                    continue;
                }

                ref var btState    = ref repo.GetComponentRW<BrainBTreeState>(entity);

                // Entity is held by the debugger. Skip ticking the interpreter
                // to prevent trace log spam and state mutation.
                if ((btState.State.InstanceFlags & Fbt.BehaviorInstanceFlags.Paused) != 0)
                    continue;

                ref var blackboard = ref repo.GetComponentRW<BrainBlackboard>(entity);

                // Resolve the optional per-entity trace ring buffer. Skipped (and the
                // chunk version stays clean) unless DebugState.EnableTraceBuffer is set.
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

                // Snapshot the cursor BEFORE stepping so we can decode the per-frame delta
                // afterwards if EmitToLog is on.
                ushort startWritePos = tracePtr != null ? tracePtr->WritePos : (ushort)0;

                // Stack-allocate context -- zero heap allocation.
                var context = new BTreeContext
                {
                    Self        = entity,
                    World       = repo,
                    _deltaTime  = deltaTime,
                    _frameCount = (int)repo.GlobalVersion,
                    _floatParams = Array.Empty<float>(),
                    _intParams   = Array.Empty<int>(),
                    _instanceId  = behavior.InstanceId,
                    TraceBuffer  = tracePtr,
                };

                var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);

                // Optional NLog emission for traces written THIS frame. Gated by:
                //   - tracePtr != null (otherwise nothing was recorded)
                //   - DebugState.EmitToLog (per-entity opt-in)
                //   - BehaviorTraceLog.Instance and IsTraceEnabled (NLog target on)
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

                // Publish BehaviorFinishedEvent exactly once per terminal transition per
                // behavior instance. Suppress re-publication when the same InstanceId has
                // already triggered the event (e.g. the BTree stays at Success across ticks).
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
        }

        /// <summary>
        /// Decode the per-frame trace delta into BehaviorLog strings. Allocates strings,
        /// but only entered after explicit <c>EmitToLog</c> + <c>IsTraceEnabled</c> gates,
        /// so the steady-state simulation path remains allocation-free.
        /// </summary>
        private static unsafe void EmitBTreeRecordsToLog(
            Entity entity,
            EntityRepository repo,
            BTreeTraceWorkingMemory1024* traceData,
            ushort startWritePos,
            int recordCount,
            Fbt.BehaviorTreeBlob blob,
            IBehaviorTraceLogEmitter emitter)
        {
            int payloadBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
            int stride       = BTreeTraceWorkingMemory1024.RecordStride;
            byte* bufferPtr  = (byte*)Unsafe.AsPointer(ref traceData->Buffer[0]);

            for (int i = 0; i < recordCount; i++)
            {
                int offset = (startWritePos + (i * stride)) % payloadBytes;
                var rec = (Fdp.Toolkit.Behavior.Diagnostics.BTreeTraceRecord*)(bufferPtr + offset);

                string nodeLabel = "?";
                if (blob.DebugMetadata != null && rec->NodeIndex < blob.DebugMetadata.Length)
                {
                    var lbl = blob.DebugMetadata[rec->NodeIndex].Label;
                    if (!string.IsNullOrEmpty(lbl)) nodeLabel = lbl;
                }

                string msg = rec->OpCode switch
                {
                    Fbt.BTreeTraceOpCode.NodeEvaluated =>
                        $"Node [{rec->NodeIndex}] {nodeLabel} -> {rec->Status}",
                    Fbt.BTreeTraceOpCode.WaitStarted =>
                        $"Wait started [{rec->NodeIndex}] {nodeLabel} duration={rec->Duration:F2}s",
                    Fbt.BTreeTraceOpCode.WaitCompleted =>
                        $"Wait completed [{rec->NodeIndex}] {nodeLabel}",
                    Fbt.BTreeTraceOpCode.ChannelMutated =>
                        $"Channel mutated [{rec->NodeIndex}] {nodeLabel}: ch={(Fbt.Kernel.ChannelKind)rec->Channel} action={rec->ActiveAction} status={rec->ChannelStatus}",
                    Fbt.BTreeTraceOpCode.Error =>
                        $"ERROR [{rec->NodeIndex}] {nodeLabel}: code={rec->ErrorCode}",
                    Fbt.BTreeTraceOpCode.ScopePushed =>
                        $"Scope pushed depth={rec->StackDepth}",
                    Fbt.BTreeTraceOpCode.ScopePopped =>
                        $"Scope popped depth={rec->StackDepth}",
                    _ => $"OpCode {rec->OpCode}",
                };

                emitter.EmitTrace(entity, repo, msg, "BTreeTrace");
            }
        }
    }
}
