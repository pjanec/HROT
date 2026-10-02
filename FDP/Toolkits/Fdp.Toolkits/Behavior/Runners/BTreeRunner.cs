using System;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Runners
{
    /// <summary>
    /// ⭐⭐ <b>S4 — the behaviour-tree runner.</b> The brain state is the 64-byte <see cref="BehaviorTreeState"/> cursor in
    /// the root STATE slot (O7c-② / CE-319); the kernel is the definition's interpreter; terminality is the root's
    /// returned status. ⭐ Moved verbatim from <c>BrainTickSystem</c>'s former BTree arm.
    /// </summary>
    public sealed unsafe class BTreeRunner : IBehaviorRunner
    {
        public static readonly BTreeRunner Instance = new();
        private BTreeRunner() { }

        public bool TryGetRootBrain(EntityRepository world, Entity self, BehaviorDefinition def,
                                    out byte* brain, out int brainBytes)
        {
            brain = null; brainBytes = 0;
            if (def.BTreeInterpreter == null)
            {
                // ⭐ CE-482: a definition that cannot run is a FAULT, not a silent skip.
                BehaviorFault.Raise(world, self, BehaviorFaultCode.NoDefinition,
                    $"Behavior '{def.Name}' has no BTree interpreter.");
                return false;
            }

            // ⭐⭐⭐ THE CURSOR COMES FROM THE ENTITY'S ROOT STATE SLOT (O7c-② / CE-319).
            //   ⛔ RequireStateRef THROWS on a miss rather than handing back a scratch cursor: a BTree-tier entity that
            //   reaches the tick with no slot means ingress never provisioned one, and ticking a stack local would
            //   restart the tree every frame — forever, silently.
            //   ⚠ LIFETIME: valid for this tick; slots attach lazily without moving an existing payload, and only
            //   CopyToLargerTier (ingress-only) moves payloads.
            ref var state = ref RootStateAccess.RequireStateRef(world, self);
            brain = (byte*)Unsafe.AsPointer(ref state);
            brainBytes = sizeof(BehaviorTreeState);
            return true;
        }

        public int BrainBytes(BehaviorDefinition def) => sizeof(BehaviorTreeState);

        public void Start(BehaviorDefinition def, byte* brain, int brainBytes)
            => new Span<byte>(brain, brainBytes).Clear();

        public NodeStatus Tick(ref BehaviorRunContext ctx, byte* brain, int brainBytes, ref byte block)
        {
            ref var btState = ref Unsafe.AsRef<BehaviorTreeState>(brain);
            var repo = ctx.World;
            var entity = ctx.Self;

            // Held by the debugger: skip the interpreter (no trace spam, no state mutation).
            if ((btState.InstanceFlags & BehaviorInstanceFlags.Paused) != 0)
                return NodeStatus.Running;

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
                    traceMem.LastInstanceId = ctx.InstanceId;
                    tracePtr = (BTreeTraceWorkingMemory1024*)Unsafe.AsPointer(ref traceMem);
                }
            }

            ushort startWritePos = tracePtr != null ? tracePtr->WritePos : (ushort)0;

            // Stack-allocate context -- zero heap allocation.
            var context = new BTreeContext
            {
                Self         = entity,
                World        = repo,
                _deltaTime   = ctx.DeltaTime,
                _frameCount  = (int)repo.SimulationTick,
                _floatParams = Array.Empty<float>(),
                _intParams   = Array.Empty<int>(),
                _instanceId  = ctx.InstanceId,
                TraceBuffer  = tracePtr,
                _occurrenceKey = ctx.OccurrenceKey,
            };

            var interpreter = ctx.Definition.BTreeInterpreter!;
            var rootResult = interpreter.Tick(ref block, ref btState, ref context);

            if (tracePtr != null && emitToLog
                && BehaviorTraceLog.Instance is { IsTraceEnabled: true } emitter)
            {
                int bytesWritten = tracePtr->WritePos - startWritePos;
                if (bytesWritten < 0)
                    bytesWritten += BTreeTraceWorkingMemory1024.PayloadBytes;
                int recordsWritten = bytesWritten / BTreeTraceWorkingMemory1024.RecordStride;
                if (recordsWritten > 0)
                    EmitRecordsToLog(entity, repo, tracePtr, startWritePos, recordsWritten, interpreter.Blob, emitter);
            }

            return rootResult;
        }

        /// <summary>
        /// Decode the per-frame BTree trace delta into BehaviorLog strings. Allocates strings, but is only entered after
        /// explicit <c>EmitToLog</c> + <c>IsTraceEnabled</c> gates, so the steady-state path remains allocation-free.
        /// </summary>
        private static void EmitRecordsToLog(
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
    }
}
