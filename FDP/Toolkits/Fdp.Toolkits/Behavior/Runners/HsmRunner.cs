using System;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior.Runners
{
    /// <summary>
    /// ⭐⭐ <b>S4 — the hierarchical-state-machine runner.</b> The brain state is the HSM instance in the root HSM slot
    /// (O7c-④a, sized from the blob); the kernel is <c>HsmKernel.Update</c>; terminality is the <c>Terminated</c> flag in
    /// the instance header, read as <c>Success</c> (decision U-4). ⭐ Moved verbatim from <c>BrainTickSystem</c>'s former
    /// HSM arm, with its two tier quirks: the MobilityLost interrupt and the hosted children (E5).
    /// </summary>
    public sealed unsafe class HsmRunner : IBehaviorRunner
    {
        public static readonly HsmRunner Instance = new();
        private HsmRunner() { }

        /// <summary>
        /// ⭐⭐⭐ CE-324 — <c>Interrupt</c> PRIORITY. <c>EventPriority.Low</c> is 0, so a default-built event went into the
        /// SHARED ring (capacity 1 on the 128 tier) instead of the reserved interrupt slot, and one queued normal event
        /// made the vehicle-disabled interrupt fail to enqueue. 📄 §31.23.
        /// </summary>
        private static readonly HsmEvent MobilityLostEvent = new()
        {
            EventId  = BehaviorConstants.EventId_MobilityLost,
            Priority = EventPriority.Interrupt,
        };

        public bool TryGetRootBrain(EntityRepository world, Entity self, BehaviorDefinition def,
                                    out byte* brain, out int brainBytes)
        {
            brain = null; brainBytes = 0;
            if (def.HsmDefinition == null) return false;

            // ⛔⛔ A MISS IS A SKIP HERE, NOT A THROW — measured, not chosen (§31.16.2). Provisioning for an HSM brain
            //   happens at INGRESS only, so an entity that spawned with a default HSM behaviour and never received an
            //   assign legitimately has no instance — exactly the zeroed-component state ValidateInstance used to skip.
            if (!RootHsmAccess.TryGetInstance(world, self, out byte* instance, out int instanceSize))
                return false;
            brain = instance;
            brainBytes = instanceSize;
            return true;
        }

        public int BrainBytes(BehaviorDefinition def) => RootHsmAccess.InstanceBytes(def.HsmDefinition);

        public void Start(BehaviorDefinition def, byte* brain, int brainBytes)
        {
            // ⭐ The same init ingress runs for a root (RootHsmAccess.ResetInstance): it stamps MachineId, without which
            //   HsmKernelCore.ValidateInstance skips the instance every tick.
            if (def.HsmDefinition is { } blob) HsmInstanceManager.Initialize(brain, brainBytes, blob);
            else new Span<byte>(brain, brainBytes).Clear();
        }

        public NodeStatus Tick(ref BehaviorRunContext ctx, byte* instance, int instanceSize, ref byte block)
        {
            var repo = ctx.World;
            var entity = ctx.Self;
            var def = ctx.Definition;
            var header = (InstanceHeader*)instance;

            // BHU-009: inject the MobilityLost interrupt if the interrupt register is set.
            if (repo.HasComponent<BrainInterrupts>(entity))
            {
                ref var bb = ref repo.GetComponentRW<BrainInterrupts>(entity);
                if (bb.Interrupt_MobilityLost == 1
                    && !HsmEventQueue.TryEnqueue(instance, instanceSize, MobilityLostEvent))
                {
                    // ⛔⛔ CE-324: the return value is NOT discarded. False means the reserved interrupt slot still holds an
                    //   UNCONSUMED interrupt. Reported rather than fixed up; DEBUG-only because this is the per-entity path.
#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"[HsmRunner] entity {entity.Index}: MobilityLost interrupt DROPPED — " +
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
                    traceMem.LastInstanceId = ctx.InstanceId;
                    hsmTracePtr            = (HsmTraceWorkingMemory1024*)Unsafe.AsPointer(ref traceMem);
                    traceCtx.Buffer        = (byte*)Unsafe.AsPointer(ref traceMem.Buffer[0]);
                    traceCtx.WritePos      = (ushort*)Unsafe.AsPointer(ref traceMem.WritePos);
                    traceCtx.RecordCount   = (ushort*)Unsafe.AsPointer(ref traceMem.RecordCount);
                    traceCtx.CapacityBytes = HsmTraceWorkingMemory1024.PayloadBytes;
                    traceCtx.MaxRecords    = HsmTraceWorkingMemory1024.CapacityRecords;
                    traceCtx.FilterLevel   = ResolveTraceLevel(dbg.Behavior);
                    traceCtx.CurrentTick   = (ushort)repo.SimulationTick;
                    traceCtx.InstanceId    = ctx.InstanceId;
                    traceCtxPtr = &traceCtx;

                    // Honor the per-instance gate inside the kernel.
                    header->Flags |= InstanceFlags.DebugTrace;
                }
                else if ((dbg.Behavior & BehaviorDebugFlags.EnableTraceBuffer) == 0)
                {
                    // Clear the gate when the bit flips off so a stale instance flag does not keep producing dead traces.
                    header->Flags &= unchecked((InstanceFlags)(byte)~(byte)InstanceFlags.DebugTrace);
                }
            }

            ushort startWritePos = hsmTracePtr != null ? hsmTracePtr->WritePos : (ushort)0;

            // DEBT-007: WorldHandle carries the GCHandle IntPtr so action delegates can recover the EntityRepository.
            var bridge = new HsmKernelBridge
            {
                Self         = entity,
                WorldHandle  = repo.UnmanagedHandle,
                TraceContext = traceCtxPtr,
                OccurrenceKey = ctx.OccurrenceKey,   // ⭐ CE-2002 — this machine's own occurrences nest under it
            };

            // ⭐⭐⭐ THE SIZE COMES FROM THE SLOT, NEVER FROM A TYPE (§9.4): a sizeof(TInstance) larger than the slot
            //   would read into the NEXT OCCURRENCE'S bytes.
            var dummyPage = new CommandPage();
            HsmKernel.Update(def.HsmDefinition!, instance, instanceSize, &bridge, ctx.DeltaTime, &dummyPage, traceCtxPtr);

            // ⭐⭐⭐ E5 — the hosted children, ticked HERE (DESIGN_Occurrence_Scoped_Storage.md §32.3, "go with b").
            //   ⛔ AFTER Update, deliberately: the active-leaf set must be THIS frame's.
            TickHostedChildren(ref ctx, instance, instanceSize, ref block);

            if (hsmTracePtr != null && emitToLog
                && BehaviorTraceLog.Instance is { IsTraceEnabled: true } emitter)
            {
                int bytesWritten = hsmTracePtr->WritePos - startWritePos;
                if (bytesWritten < 0)
                    bytesWritten += HsmTraceWorkingMemory1024.PayloadBytes;
                int recordsWritten = bytesWritten / HsmTraceWorkingMemory1024.RecordStride;
                if (recordsWritten > 0)
                    EmitRecordsToLog(entity, repo, hsmTracePtr, startWritePos, recordsWritten, def.HsmMetadata, emitter);
            }

            // ⭐ TERMINALITY, HSM FORM: a flag in the instance header, not a returned status. ⛔ CE-449: the latch is NOT
            //   cleared here — the finish's clear detaches the whole instance, so no later assign inherits it.
            return (header->Flags & InstanceFlags.Terminated) != 0 ? NodeStatus.Success : NodeStatus.Running;
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>E5</c> — tick the BTree each ACTIVE hosting state owns, and reset the ones whose host is no longer
        /// active.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.3, §32.5.
        ///
        /// <para>🔴🔴 <b>Why here and not a generated <c>[HsmAction]</c>.</b> <c>HsmKernelCore</c> advances an instance by
        /// ONE PHASE PER TICK and <c>Idle</c> leaves only on a non-empty queue ⇒ an <c>ActivityAction</c> on a quiescent
        /// machine runs exactly once (<c>CE-334</c>), and a hosted BTree must advance every frame.</para>
        ///
        /// <para>⭐⭐ <b>The <c>else</c> arm IS <c>F14</c></b>: the set of hosting states is iterated every frame, so
        /// <i>"my host is not active"</i> is directly observable — no deactivator needed.</para>
        ///
        /// <para>⭐ <paramref name="hostBlock"/> is the HOST's block — the SUPPLY source for a bound site (CE-431); each
        /// child ticks against its OWN block inside <c>HostedSubtree.TickHosted</c>.</para>
        /// </summary>
        private static void TickHostedChildren(ref BehaviorRunContext ctx, byte* instance, int instanceSize, ref byte hostBlock)
        {
            var blob = ctx.Definition.HsmDefinition;
            if (blob is null) return;

            if (!HsmHostedSubtrees.TryGetFor(blob, out var hosted))
                return;   // ⭐ the common case — no shipped asset hosts anything

            ushort* activeLeafIds = HsmKernel.GetActiveLeafIds(instance, instanceSize, out int regionCount);
            if (activeLeafIds == null) return;

            var context = new BTreeContext
            {
                Self         = ctx.Self,
                World        = ctx.World,
                _deltaTime   = ctx.DeltaTime,
                _frameCount  = (int)ctx.World.SimulationTick,
                _floatParams = Array.Empty<float>(),
                _intParams   = Array.Empty<int>(),
                _instanceId  = ctx.InstanceId,
                TraceBuffer  = null,
                _occurrenceKey = ctx.OccurrenceKey,   // ⭐ S5b — this machine's children nest under it
            };

            for (int i = 0; i < hosted.Length; i++)
            {
                var entry = hosted[i];

                if (!IsStateActive(blob, activeLeafIds, regionCount, entry.StateIndex))
                {
                    // ⭐ F14 — the host abandoned a child that may still be Running. Unconditional and cheap.
                    HostedSubtree.Reset(ctx.World, ctx.Self, entry.TreeStateSlotKey, ctx.OccurrenceKey);
                    continue;
                }

                // ⚠ A missing binding is SKIPPED (the HSM arm skips where the BTree arm throws, §31.16.2).
                if (!HostedChildren.TryGetDefinition(entry.TreeStateSlotKey, out _)) continue;

                // ⭐⭐ The status is DISCARDED: Q33 §1.5.4 rules a hosted subtree NON-BLOCKING.
                HostedSubtree.TickHosted(ref hostBlock, ref context, entry.TreeStateSlotKey, entry.Binding);
            }
        }

        /// <summary>
        /// ⭐ Is <paramref name="stateIndex"/> on the active path of any region — as a leaf or as an ancestor of one?
        /// ⭐⭐ The ancestor walk is not optional: a hosting COMPOSITE is never itself a leaf (<c>HsmKernelCore.cs:444-456</c>).
        /// ⚠ <c>0xFFFF</c> is the kernel's "no active leaf" sentinel and the root's <c>ParentIndex</c> terminator.
        /// </summary>
        private static bool IsStateActive(
            HsmDefinitionBlob blob, ushort* activeLeafIds, int regionCount, ushort stateIndex)
        {
            for (int r = 0; r < regionCount; r++)
            {
                ushort current = activeLeafIds[r];
                if (current == 0xFFFF) continue;

                // ⚠ Bounded by the state count: a malformed ParentIndex cycle must not hang the tick.
                int guard = 0;
                while (current != 0xFFFF && guard++ <= blob.Header.StateCount)
                {
                    if (current == stateIndex) return true;
                    current = blob.GetState(current).ParentIndex;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TraceLevel ResolveTraceLevel(BehaviorDebugFlags flags)
        {
            // Highest-tier wins (Tier3 implies Tier2 implies Tier1).
            if ((flags & BehaviorDebugFlags.HsmTraceTier3) != 0) return TraceLevel.Tier3;
            if ((flags & BehaviorDebugFlags.HsmTraceTier2) != 0) return TraceLevel.Tier2;
            if ((flags & BehaviorDebugFlags.HsmTraceTier1) != 0) return TraceLevel.Tier1;
            // EnableTraceBuffer with no tier ⇒ Tier1, so the buffer is not silent.
            return TraceLevel.Tier1;
        }

        /// <summary>Decode the per-frame HSM trace delta into BehaviorLog strings. Same gating as the BTree decoder.</summary>
        private static void EmitRecordsToLog(
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
