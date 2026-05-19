using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Scenario translator that projects the per-entity <see cref="BTreeTraceWorkingMemory1024"/>
    /// ring buffer into a JSON array for diagnostic dumps (clipboard copy / fdp dump).
    /// </summary>
    /// <remarks>
    /// <see cref="Inject"/> is intentionally a no-op: trace memory is transient execution
    /// state (<c>DataPolicy.NoSave</c>) and must never be reconstructed from a scenario file.
    /// </remarks>
    public sealed class BTreeTraceWorkingMemoryTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(BTreeTraceWorkingMemory1024);

        private readonly BehaviorRegistry _registry;

        public BTreeTraceWorkingMemoryTranslator(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public bool IsExtractionSafe => true;

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(BTreeTraceWorkingMemory1024));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<BTreeTraceWorkingMemory1024>(entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var traceData = ref repo.GetComponentRO<BTreeTraceWorkingMemory1024>(entity);

            BehaviorTreeBlob? blob = null;
            if (repo.HasComponent<BehaviorState>(entity))
            {
                var state = repo.GetComponentRO<BehaviorState>(entity);
                if (_registry.TryGetDefinition(state.ActiveBehaviorHash, out var def))
                    blob = def.BTreeInterpreter?.Blob;
            }

            var recordsArray = new JsonArray();
            int payloadBytes = BTreeTraceWorkingMemory1024.PayloadBytes;
            int stride       = BTreeTraceWorkingMemory1024.RecordStride;

            int startOffset = traceData.RecordCount == BTreeTraceWorkingMemory1024.CapacityRecords
                ? traceData.WritePos : 0;

            // Pin the ECS-resident component to a stable pointer for the loop.
            // GetComponentRO returns a ref into chunk memory which is GC-safe here.
            fixed (byte* bufferPtr = traceData.Buffer)
            {
                for (int i = 0; i < traceData.RecordCount; i++)
                {
                    int offset = (startOffset + (i * stride)) % payloadBytes;
                    BTreeTraceRecord* rec = (BTreeTraceRecord*)(bufferPtr + offset);

                    var recordObj = new JsonObject
                    {
                        ["Timestamp"]  = rec->Timestamp,
                        ["OpCode"]     = rec->OpCode.ToString(),
                        ["InstanceId"] = rec->InstanceId,
                    };

                    switch (rec->OpCode)
                    {
                        case BTreeTraceOpCode.NodeEvaluated:
                            recordObj["NodeIndex"] = rec->NodeIndex;
                            recordObj["NodeName"]  = NodeName(blob, rec->NodeIndex);
                            recordObj["Status"]    = rec->Status.ToString();
                            break;
                        case BTreeTraceOpCode.ScopePushed:
                        case BTreeTraceOpCode.ScopePopped:
                            recordObj["StackDepth"] = rec->StackDepth;
                            break;
                        case BTreeTraceOpCode.WaitStarted:
                        case BTreeTraceOpCode.WaitCompleted:
                            recordObj["NodeIndex"] = rec->NodeIndex;
                            recordObj["NodeName"]  = NodeName(blob, rec->NodeIndex);
                            recordObj["Duration"]  = rec->Duration;
                            break;
                        case BTreeTraceOpCode.ChannelMutated:
                            recordObj["NodeIndex"]     = rec->NodeIndex;
                            recordObj["NodeName"]      = NodeName(blob, rec->NodeIndex);
                            recordObj["Channel"]       = ((ChannelKind)rec->Channel).ToString();
                            recordObj["ActiveAction"]  = rec->ActiveAction;
                            recordObj["ChannelStatus"] = rec->ChannelStatus.ToString();
                            break;
                        case BTreeTraceOpCode.Error:
                            recordObj["NodeIndex"] = rec->NodeIndex;
                            recordObj["NodeName"]  = NodeName(blob, rec->NodeIndex);
                            recordObj["ErrorCode"] = rec->ErrorCode;
                            break;
                    }

                    recordsArray.Add(recordObj);
                }
            }

            var root = new JsonObject
            {
                ["RecordCount"] = traceData.RecordCount,
                ["WritePos"]    = traceData.WritePos,
                ["History"]     = recordsArray,
            };
            return new Dictionary<string, object> { [Key] = root };
        }

        public void Inject(EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver) { }

        public IEnumerable<string> GetOutputDomKeys() { yield return Key; }

        private static string NodeName(BehaviorTreeBlob? blob, ushort nodeIndex)
        {
            if (blob?.DebugMetadata == null) return "?";
            if (nodeIndex >= blob.DebugMetadata.Length) return "?";
            var label = blob.DebugMetadata[nodeIndex].Label;
            return string.IsNullOrEmpty(label) ? "?" : label;
        }
    }
}
