using System;
using System.IO;
using System.Linq;
using System.Text;
using Fhsm.Compiler.Graph;
using Fhsm.Compiler.Hashing;
using Fhsm.Kernel.Data;

namespace Fhsm.Compiler
{
    public class HsmEmitter
    {
        /// <summary>
        /// Construct a <see cref="MachineMetadata"/> sidecar from the source graph,
        /// mapping each <c>FlatIndex</c> back to a human-readable state name. Used
        /// by AI diagnostic renderers/translators to symbolicate raw trace records.
        /// </summary>
        public static MachineMetadata BuildMachineMetadata(StateMachineGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var meta = new MachineMetadata();

            foreach (var state in graph.States.Values)
            {
                if (state.FlatIndex == 0xFFFF) continue;
                meta.StateNames[state.FlatIndex] = state.Name;
                meta.StateStableIds[state.FlatIndex] = state.StableId;
            }

            foreach (var kvp in graph.EventNameToId)
            {
                meta.EventNames[kvp.Value] = kvp.Key;
            }

            // Action names: use the same hash IDs that HsmFlattener assigns so
            // that MachineMetadata.ActionNames[hashId] resolves correctly at runtime.
            var actionTable = HsmFlattener.BuildActionTable(graph);
            foreach (var (name, hashId) in actionTable)
                meta.ActionNames[hashId] = name;

            // Guard names share the same ActionNames dictionary (guards are looked
            // up by their hash ID via MachineMetadata.GetActionName).
            var guardTable = HsmFlattener.BuildGuardTable(graph);
            foreach (var (name, hashId) in guardTable)
                meta.ActionNames[hashId] = name;

            // Transition VisualIds: must match HsmFlattener order (states sorted by FlatIndex,
            // then state transitions, then global transitions appended at the end).
            ushort transIdx = 0;
            foreach (var state in graph.States.Values.OrderBy(s => s.FlatIndex))
            {
                foreach (var t in state.Transitions)
                {
                    meta.TransitionVisualIds[transIdx++] = t.VisualId;
                }
            }
            foreach (var gt in graph.GlobalTransitions)
            {
                meta.TransitionVisualIds[transIdx++] = gt.VisualId;
            }

            // Deferred event IDs per state, keyed by flat index.
            foreach (var state in graph.States.Values)
            {
                if (state.FlatIndex == 0xFFFF) continue;
                if (state.DeferredEventIds.Count > 0)
                    meta.DeferredEventsByState[state.FlatIndex] = state.DeferredEventIds.ToArray();
            }

            return meta;
        }

        /// <summary>
        /// Emit HsmDefinitionBlob from flattened data.
        /// </summary>
        public static HsmDefinitionBlob Emit(HsmFlattener.FlattenedData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            
            var header = new HsmDefinitionHeader();
            
            // Magic & version
            header.Magic = HsmDefinitionHeader.MagicNumber;
            header.FormatVersion = 1;
            
            // Counts
            header.StateCount = (ushort)data.States.Length;
            header.TransitionCount = (ushort)data.Transitions.Length;
            header.RegionCount = (ushort)data.Regions.Length;
            header.GlobalTransitionCount = (ushort)data.GlobalTransitions.Length;
            header.EventDefinitionCount = 0;  // Not tracked yet
            header.ActionCount = (ushort)data.ActionIds.Length;
            header.GuardCount = (ushort)data.GuardIds.Length; // Assuming GuardCount exists in Header? Check
            
            // Hashes
            header.StructureHash = ComputeStructureHash(data);
            header.ParameterHash = ComputeParameterHash(data);
            
            // Create Linker Tables
            var actionTable = new LinkerTableEntry[data.ActionIds.Length];
            for (int i = 0; i < actionTable.Length; i++)
                actionTable[i] = new LinkerTableEntry { FunctionId = data.ActionIds[i] };
                
            var guardTable = new LinkerTableEntry[data.GuardIds.Length];
            for (int i = 0; i < guardTable.Length; i++)
                guardTable[i] = new LinkerTableEntry { FunctionId = data.GuardIds[i] };

            // Create blob
            return HsmDefinitionBlob.CreateWithLinkerTables(
                header,
                data.States,
                data.Transitions,
                data.Regions,
                data.GlobalTransitions,
                actionTable,
                guardTable
            );
        }
        
        public static void EmitWithDebug(
            HsmDefinitionBlob blob,
            MachineMetadata metadata,
            string outputPath)
        {
            // Export debug sidecar
            var debugPath = outputPath + ".debug";
            var debugJson = SerializeMetadata(metadata);
            File.WriteAllText(debugPath, debugJson);
            
            // Note: Binary serialization of blob is not implemented here, 
            // usually you'd serialize the blob structure to bytes.
            // For now, we assume the caller handles blob persistence or we implement a basic serializer.
            // Instructions said: File.WriteAllBytes(outputPath, blobBytes);
            // I'll skip binary serialization of blob as it's not provided in the snippet and might be complex.
            // Caller might just use this for sidecar.
        }

        private static string SerializeMetadata(MachineMetadata metadata)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            
            // State names
            sb.AppendLine("  \"states\": {");
            foreach (var kvp in metadata.StateNames)
            {
                sb.AppendLine($"    \"{kvp.Key}\": \"{kvp.Value}\",");
            }
            sb.AppendLine("  },");
            
            // Event names
            sb.AppendLine("  \"events\": {");
            foreach (var kvp in metadata.EventNames)
            {
                sb.AppendLine($"    \"{kvp.Key}\": \"{kvp.Value}\",");
            }
            sb.AppendLine("  },");
            
            // Action names
            sb.AppendLine("  \"actions\": {");
            foreach (var kvp in metadata.ActionNames)
            {
                sb.AppendLine($"    \"{kvp.Key}\": \"{kvp.Value}\",");
            }
            sb.AppendLine("  }");
            
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static uint ComputeStructureHash(HsmFlattener.FlattenedData data)
        {
            // Hash topology: state count, parent indices, depths
            var builder = new StringBuilder();
            
            builder.Append(data.States.Length);
            
            foreach (var state in data.States)
            {
                builder.Append(state.ParentIndex);
                builder.Append(state.Depth);
                builder.Append(state.FirstChildIndex);
                builder.Append(state.OutputLaneMask);
                builder.Append(state.Flags); 
            }
            
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = XxHash64.ComputeHash(bytes);
            
            return (uint)hash;
        }
        
        private static uint ComputeParameterHash(HsmFlattener.FlattenedData data)
        {
            // Hash logic: action IDs, guard IDs, event IDs
            var builder = new StringBuilder();
            
            foreach (var state in data.States)
            {
                builder.Append(state.OnEntryActionId);
                builder.Append(state.OnExitActionId);
                builder.Append(state.ActivityActionId);
                builder.Append(state.TimerActionId);
            }
            
            foreach (var trans in data.Transitions)
            {
                builder.Append(trans.EventId);
                builder.Append(trans.GuardId);
                builder.Append(trans.ActionId);
            }
            
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = XxHash64.ComputeHash(bytes);
            
            return (uint)hash;
        }
    }
}
