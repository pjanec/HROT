using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Data;

namespace Fhsm.Compiler
{
    public class HsmFlattener
    {
        public class FlattenedData
        {
            public StateDef[] States { get; set; } = Array.Empty<StateDef>();
            public TransitionDef[] Transitions { get; set; } = Array.Empty<TransitionDef>();
            public RegionDef[] Regions { get; set; } = Array.Empty<RegionDef>();
            public GlobalTransitionDef[] GlobalTransitions { get; set; } = Array.Empty<GlobalTransitionDef>();
            public ushort[] ActionIds { get; set; } = Array.Empty<ushort>();
            public ushort[] GuardIds { get; set; } = Array.Empty<ushort>();
        }
        
        /// <summary>
        /// Flatten normalized graph to ROM arrays.
        /// </summary>
        public static FlattenedData Flatten(StateMachineGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            
            var result = new FlattenedData();
            
            // 1. Build function dispatch tables first (needed for IDs)
            var actionTable = BuildActionTable(graph);
            var guardTable = BuildGuardTable(graph);
            
            result.ActionIds = actionTable.Values.OrderBy(v => v).ToArray();
            result.GuardIds = guardTable.Values.OrderBy(v => v).ToArray();
            
            // 2. Flatten states (use FlatIndex order)
            // Note: FlattenStates sets basic properties but not transition ranges yet
            result.States = FlattenStates(graph, actionTable);
            
            // 3. Flatten transitions (compute costs)
            result.Transitions = FlattenTransitions(graph, actionTable, guardTable);
            
            // 3b. Update StateDef transition ranges (FirstTransitionIndex, TransitionCount)
            UpdateStateTransitionRanges(result.States, graph, result.Transitions.Length);

            // 4. Flatten regions
            result.Regions = FlattenRegions(graph);
            
            // 5. Separate global transitions
            result.GlobalTransitions = FlattenGlobalTransitions(graph, actionTable, guardTable);
            
            return result;
        }

        private static void UpdateStateTransitionRanges(StateDef[] stateDefs, StateMachineGraph graph, int totalTransitions)
        {
            // We need to iterate states in FlatIndex order to match stateDefs array
            var sortedStates = graph.States.Values.OrderBy(s => s.FlatIndex).ToList();
            
            ushort currentTransIndex = 0;
            
            for(int i = 0; i < sortedStates.Count; i++)
            {
                var node = sortedStates[i];
                // We must use ref to update the struct in the array
                ref var def = ref stateDefs[i];
                
                if (node.Transitions.Count > 0)
                {
                    def.FirstTransitionIndex = currentTransIndex;
                    def.TransitionCount = (ushort)node.Transitions.Count;
                    currentTransIndex += (ushort)node.Transitions.Count;
                }
                else
                {
                    def.FirstTransitionIndex = 0xFFFF;
                    def.TransitionCount = 0;
                }
            }
        }
        
        internal static Dictionary<string, ushort> BuildActionTable(StateMachineGraph graph)
        {
            // Collect all unique action names, assign IDs
            var actions = new HashSet<string>();
            
            foreach (var state in graph.States.Values)
            {
                if (state.OnEntryAction != null) actions.Add(state.OnEntryAction);
                if (state.OnExitAction != null) actions.Add(state.OnExitAction);
                if (state.ActivityAction != null) actions.Add(state.ActivityAction);
                if (state.TimerAction != null) actions.Add(state.TimerAction);
                
                foreach (var trans in state.Transitions)
                {
                    if (trans.ActionFunction != null) actions.Add(trans.ActionFunction);
                }
                
                // Also check global transitions action
                foreach(var gt in graph.GlobalTransitions)
                {
                    if (gt.ActionFunction != null) actions.Add(gt.ActionFunction);
                }
            }
            
            // Assign Hash IDs
            var table = new Dictionary<string, ushort>();
            foreach(var action in actions)
            {
                ushort h = ComputeHash(action);
                // System.Console.WriteLine($"[Flattener] Action '{action}' -> {h}");
                table[action] = h;
            }
            
            return table;
        }

        internal static Dictionary<string, ushort> BuildGuardTable(StateMachineGraph graph)
        {
            // Similar to BuildActionTable but for guards
            var guards = new HashSet<string>();
            
            foreach (var state in graph.States.Values)
            {
                foreach (var trans in state.Transitions)
                {
                    if (trans.GuardFunction != null) guards.Add(trans.GuardFunction);
                }
                
                // Global transitions
                foreach(var gt in graph.GlobalTransitions)
                {
                    if (gt.GuardFunction != null) guards.Add(gt.GuardFunction);
                }
            }
            
            // Assign Hash IDs
            var table = new Dictionary<string, ushort>();
            foreach(var guard in guards)
            {
                table[guard] = ComputeHash(guard);
            }
            
            return table;
        }
        
        private static StateDef[] FlattenStates(StateMachineGraph graph, Dictionary<string, ushort> actionTable)
        {
            // Sort by FlatIndex (should already be assigned by Normalizer)
            var states = graph.States.Values.OrderBy(s => s.FlatIndex).ToArray();
            var result = new StateDef[states.Length];
            
            for (int i = 0; i < states.Length; i++)
            {
                var node = states[i];
                var def = new StateDef();
                
                // Hierarchy
                def.ParentIndex = node.Parent != null ? node.Parent.FlatIndex : (ushort)0xFFFF;
                def.FirstChildIndex = node.Children.Count > 0 ? node.Children[0].FlatIndex : (ushort)0xFFFF;
                def.ChildCount = (ushort)node.Children.Count;
                def.OutputLaneMask = node.OutputLaneMask;
                
                // Transitions (set later by UpdateStateTransitionRanges)
                
                // Metadata
                def.Depth = node.Depth;
                def.Flags = BuildStateFlags(node);
                
                // Actions - Use 0xFFFF (None) if not present
                def.OnEntryActionId = node.EntryActionId != 0 ? node.EntryActionId : (node.OnEntryAction != null ? actionTable[node.OnEntryAction] : (ushort)0xFFFF);
                def.OnExitActionId = node.ExitActionId != 0 ? node.ExitActionId : (node.OnExitAction != null ? actionTable[node.OnExitAction] : (ushort)0xFFFF);
                // ⭐ CE-383 — the explicit-id override, extended to ACTIVITY. Entry/exit have honoured
                //   one since the JSON parser needed it; activity did not, and a blueprint-hosted
                //   activity has no authorable NAME whose hash equals its BlueprintId. 📄 §3.2.
                def.ActivityActionId = node.ActivityActionId != 0
                    ? node.ActivityActionId
                    : (node.ActivityAction != null ? actionTable[node.ActivityAction] : (ushort)0xFFFF);
                def.TimerActionId = node.TimerAction != null ? actionTable[node.TimerAction] : (ushort)0xFFFF;
                
                // History
                def.HistorySlotIndex = node.HistorySlotIndex;
                def.TimerSlotIndex = (ushort)(node.TimerSlotIndex >= 0 ? node.TimerSlotIndex : 0xFFFF);

                result[i] = def;
            }
            
            return result;
        }
        
        private static StateFlags BuildStateFlags(StateNode node)
        {
            StateFlags flags = StateFlags.None;
            
            if (node.IsInitial) flags |= StateFlags.IsInitial;
            if (node.IsHistory) flags |= StateFlags.IsHistory;
            if (node.IsDeepHistory) flags |= StateFlags.IsDeepHistory;
            if (node.IsParallel) flags |= StateFlags.IsParallel;
            if (node.IsFinal) flags |= StateFlags.IsFinal;
            if (node.Children.Count > 0) flags |= StateFlags.IsComposite;

            // ⭐⭐⭐ CE-381 — DERIVED, never authored. The kernel's Idle arm already walks leaf→root
            //   per region for activities; this bit makes the polled check ONE BIT TEST on that walk,
            //   so a state with no polled transition costs nothing.
            // ⚠ It is also the ONLY StructureHash coverage polling gets: ComputeStructureHash hashes
            //   state.Flags and NOT trans.Flags. The residual gap is ACCEPTED (design §8b).
            foreach (var t in node.Transitions)
            {
                if (!t.IsPolled) continue;
                flags |= StateFlags.HasPolledTransition;
                break;
            }
            
            return flags;
        }
        
        private static TransitionDef[] FlattenTransitions(
            StateMachineGraph graph,
            Dictionary<string, ushort> actionTable,
            Dictionary<string, ushort> guardTable)
        {
            // Collect all transitions in state FlatIndex order
            var allTransitions = new List<TransitionNode>();
            var states = graph.States.Values.OrderBy(s => s.FlatIndex).ToArray();
            
            foreach (var state in states)
            {
                if (state.Transitions.Count > 0)
                {
                    foreach (var trans in state.Transitions)
                    {
                        allTransitions.Add(trans);
                    }
                }
            }
            
            // Flatten transitions
            var result = new TransitionDef[allTransitions.Count];
            
            for (int i = 0; i < allTransitions.Count; i++)
            {
                var node = allTransitions[i];
                var def = new TransitionDef();
                
                def.SourceStateIndex = node.Source.FlatIndex;
                def.TargetStateIndex = node.Target.FlatIndex;

                // ⭐⭐⭐ CE-381 — NORMALISE a polled transition onto the reserved id, whatever it was
                //   authored with. This is what keeps POLLED and COMPLETION disjoint BY CONSTRUCTION:
                //   authored as `.On(0)` a polled transition would carry EventId 0, and the RTC
                //   loop's completion pass (ProcessRTCPhase sets currentEventId = 0 after every
                //   executed transition) would select it TOO. 📄 design §2.3 / §10 ③.
                // ⚠ Done HERE rather than in the builder so EVERY authoring route — fluent, JSON
                //   parser, and the DTO emitter that CE-385 adds — lands on the same id.
                def.EventId = node.IsPolled ? ReservedEventIds.Polled : node.EventId;

                // ⭐ CE-383 — the explicit-id override, extended to GUARD. A blueprint-hosted guard
                //   registers under its BlueprintId, which no authorable name hashes to. 📄 §3.2.
                def.GuardId = node.GuardId != 0
                    ? node.GuardId
                    : (node.GuardFunction != null ? guardTable[node.GuardFunction] : (ushort)0xFFFF);
                // ⚠ CE-383 — ALSO honoured here, and this one is a FIX rather than an extension:
                //   TransitionNode.ActionId already existed and JsonStateMachineParser:92 already SET
                //   it, but this line ignored it ⇒ a JSON-authored transition action was parsed and
                //   silently DROPPED. 📐 Zero production blast radius (that parser has test-only
                //   callers), and leaving one slot inconsistent in the very method being made
                //   consistent is the worse outcome. Argued in the CE-383 report, not silent.
                def.ActionId = node.ActionId != 0
                    ? node.ActionId
                    : (node.ActionFunction != null ? actionTable[node.ActionFunction] : (ushort)0xFFFF);
                
                // Flags (include priority)
                def.Flags = BuildTransitionFlags(node);
                
                // Cost: ARCHITECT Q6 - structural only (LCA distance)
                def.Cost = ComputeTransitionCost(node.Source, node.Target);
                
                result[i] = def;
            }
            
            return result;
        }
        
        private static TransitionFlags BuildTransitionFlags(TransitionNode node)
        {
            TransitionFlags flags = TransitionFlags.None;
            
            if (node.IsInternal) flags |= TransitionFlags.IsInternal;

            // ⭐⭐ CE-381 — the POLLED bit. The state-level HasPolledTransition is derived from this
            //   in BuildStateFlags; the two must stay in step, which is why both are computed here
            //   from the same source property rather than authored separately.
            if (node.IsPolled) flags |= TransitionFlags.IsPolled;

            // 🔴 CE-395 — THE PRIORITY ENCODING BELOW IS WRONG AND IS FILED, NOT FIXED HERE.
            //   It writes bits 8-11 (<< 8) while Enums.cs declares Priority_Mask = 0xF000 and
            //   HsmKernelCore.cs:631 reads (ushort)Flags >> 12 ⇒ every priority reads ZERO and
            //   SelectTransition degenerates to first-match-wins. A second bug compounds it:
            //   node.Priority defaults to 128 and `& 0x0F` truncates that to 0.
            // ⛔ Not repaired inside CE-381: fixing it CHANGES WHICH TRANSITION WINS for any asset
            //   that authors a priority, which is a behaviour change and deserves its own rail.
            //   (Blast radius on shipped content is zero — all four assets author Priority 0.)

            // Encode priority (4 bits)
            // Priority is a byte (0-255). 
            // Assuming bits 8-11 usage: (priority & 0x0F) << 8.
            // Wait, TransitionFlags is ushort usually? Let's check kernel def.
            // Assuming it is.
            flags |= (TransitionFlags)((node.Priority & 0x0F) << 8);  
            
            return flags;
        }
        
        private static byte ComputeTransitionCost(StateNode source, StateNode target)
        {
            // ARCHITECT Q6: Structural cost only (LCA distance)
            // Cost = steps to exit + steps to enter
            
            // Special case: self transition? 
            if (source == target) return 1; // Exit and Enter self? Or just 0?
            // If LCA logic handles self: LCA(A, A) = A. Exit=0, Enter=0. Cost 0?
            // Self transition usually exits and enters.
            // If internal, it doesn't.
            // But internal flag is separate.
            // Let's assume generic LCA logic.
            
            var lca = FindLCA(source, target);
            
            byte exitSteps = 0;
            var current = source;
            while (current != lca && current != null)
            {
                exitSteps++;
                current = current.Parent;
            }
            
            byte enterSteps = 0;
            current = target;
            while (current != lca && current != null)
            {
                enterSteps++;
                current = current.Parent;
            }
            
            return (byte)(exitSteps + enterSteps);
        }
        
        private static StateNode? FindLCA(StateNode source, StateNode target)
        {
            // Mark ancestors of source
            var ancestors = new HashSet<StateNode>();
            var current = source;
            while (current != null)
            {
                ancestors.Add(current);
                current = current.Parent;
            }
            
            // Walk target up until hit marked ancestor
            current = target;
            while (current != null)
            {
                if (ancestors.Contains(current)) return current;
                current = current.Parent;
            }
            
            return null;  // Should not happen if graph is valid
        }
        
        private static RegionDef[] FlattenRegions(StateMachineGraph graph)
        {
            var regionDefs = new List<RegionDef>();
            
            // Region 0: Global/Root
            // Note: Use RootState's Initial child if available, or RootState itself?
            // Usually RegionDef points to the context root.
            // For Main region, Parent is None, Initial is Root.
            if (graph.RootState != null)
            {
                regionDefs.Add(new RegionDef
                {
                    ParentStateIndex = 0xFFFF,
                    InitialStateIndex = graph.RootState.FlatIndex,
                    Priority = 0
                });
            }
            
            // Find parallel states to define orthogonal regions
            // We iterate in FlatIndex order for determinism
            var parallelStates = graph.States.Values
                .Where(s => s.IsParallel)
                .OrderBy(s => s.FlatIndex);
                
            foreach (var pState in parallelStates)
            {
                // Each child of a parallel state defines an orthogonal region
                foreach (var child in pState.Children)
                {
                    regionDefs.Add(new RegionDef
                    {
                        ParentStateIndex = pState.FlatIndex,
                        InitialStateIndex = child.FlatIndex,
                        Priority = 0
                    });
                }
            }
            
            return regionDefs.ToArray();
        }
        
        private static GlobalTransitionDef[] FlattenGlobalTransitions(
            StateMachineGraph graph,
            Dictionary<string, ushort> actionTable,
            Dictionary<string, ushort> guardTable)
        {
            // ARCHITECT Q7: Separate table for global transitions
            var result = new GlobalTransitionDef[graph.GlobalTransitions.Count];
            
            for (int i = 0; i < graph.GlobalTransitions.Count; i++)
            {
                var node = graph.GlobalTransitions[i];
                var def = new GlobalTransitionDef();
                
                def.TargetStateIndex = node.Target.FlatIndex;
                def.EventId = node.EventId;
                // ⭐ CE-383 — the explicit-id override, extended to GUARD. A blueprint-hosted guard
                //   registers under its BlueprintId, which no authorable name hashes to. 📄 §3.2.
                def.GuardId = node.GuardId != 0
                    ? node.GuardId
                    : (node.GuardFunction != null ? guardTable[node.GuardFunction] : (ushort)0xFFFF);
                // ⚠ CE-383 — ALSO honoured here, and this one is a FIX rather than an extension:
                //   TransitionNode.ActionId already existed and JsonStateMachineParser:92 already SET
                //   it, but this line ignored it ⇒ a JSON-authored transition action was parsed and
                //   silently DROPPED. 📐 Zero production blast radius (that parser has test-only
                //   callers), and leaving one slot inconsistent in the very method being made
                //   consistent is the worse outcome. Argued in the CE-383 report, not silent.
                def.ActionId = node.ActionId != 0
                    ? node.ActionId
                    : (node.ActionFunction != null ? actionTable[node.ActionFunction] : (ushort)0xFFFF);
                def.Flags = BuildTransitionFlags(node);
                
                result[i] = def;
            }
            
            return result;
        }

        private static ushort ComputeHash(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (ushort)(hash & 0xFFFF);
        }
    }
}
