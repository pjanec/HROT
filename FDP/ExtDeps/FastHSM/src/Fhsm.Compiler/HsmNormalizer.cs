using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler.Graph;

namespace Fhsm.Compiler
{
    public class HsmNormalizer
    {
        /// <summary>
        /// Normalize graph: assign indices, compute depths, resolve initial states.
        /// </summary>
        public static void Normalize(StateMachineGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            
            // 1. Assign FlatIndex (BFS for cache locality)
            AssignFlatIndices(graph);
            
            // 2. Compute depths
            ComputeDepths(graph.RootState);
            
            // 3. Resolve initial states
            ResolveInitialStates(graph);
            
            // 4. Assign history slots (CRITICAL: sort by StableId)
            AssignHistorySlots(graph);
            
            // 5. Compute transition ranges
            ComputeTransitionRanges(graph);
        }
        
        private static void AssignFlatIndices(StateMachineGraph graph)
        {
            // BFS traversal from root
            var queue = new Queue<StateNode>();
            ushort index = 0;
            
            graph.RootState.FlatIndex = index++;
            queue.Enqueue(graph.RootState);
            
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                
                foreach (var child in state.Children)
                {
                    child.FlatIndex = index++;
                    queue.Enqueue(child);
                }
            }
        }
        
        private static void ComputeDepths(StateNode node, byte depth = 0)
        {
            node.Depth = depth;
            
            foreach (var child in node.Children)
            {
                ComputeDepths(child, (byte)(depth + 1));
            }
        }
        
        private static void ResolveInitialStates(StateMachineGraph graph)
        {
            // For each composite state (has children), ensure one is marked Initial
            foreach (var state in graph.States.Values)
            {
                if (state.Children.Count > 0)
                {
                    var initial = state.Children.FirstOrDefault(c => c.IsInitial);
                    
                    if (initial == null)
                    {
                        // No explicit initial → use first child
                        state.Children[0].IsInitial = true;
                    }
                }
            }
        }
        
        private static void AssignHistorySlots(StateMachineGraph graph)
        {
            // ARCHITECT CRITICAL: Sort by StableId (Guid) for hot reload stability
            // NOT by name or declaration order
            
            var historyStates = graph.States.Values
                .Where(s => s.IsHistory)
                .OrderBy(s => s.StableId)  // CRITICAL: Stable sort
                .ToList();
            
            ushort slotIndex = 0;
            foreach (var state in historyStates)
            {
                state.HistorySlotIndex = slotIndex++;
            }
        }
        
        private static void ComputeTransitionRanges(StateMachineGraph graph)
        {
            // Will be used during flattening
            // Each state needs to know: FirstTransitionIndex, TransitionCount
            // This is computed when we flatten transitions into array
            // For now, just validate transitions exist
        }
    }
}