using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler.Graph;

namespace Fhsm.Compiler
{
    public class HsmGraphValidator
    {
        public enum ErrorSeverity
        {
            Error,
            Warning
        }

        public class ValidationError
        {
            public string Message { get; set; }
            public string? StateName { get; set; }
            public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;
            
            public ValidationError(string message, string? stateName = null)
            {
                Message = message;
                StateName = stateName;
            }
            
            public override string ToString() => 
                StateName != null ? $"[{StateName}] {Severity}: {Message}" : $"{Severity}: {Message}";
        }
        
        /// <summary>
        /// Validate graph. Returns list of errors (empty if valid).
        /// </summary>
        public static List<ValidationError> Validate(StateMachineGraph graph)
        {
            var errors = new List<ValidationError>();
            
            if (graph == null)
            {
                errors.Add(new ValidationError("Graph is null"));
                return errors;
            }
            
            // Run all validation rules
            ValidateStructure(graph, errors);
            ValidateTransitions(graph, errors);
            ValidateInitialStates(graph, errors);
            ValidateHistoryStates(graph, errors);
            ValidateFunctions(graph, errors);
            ValidateLimits(graph, errors);
            ValidateIndirectEvents(graph, errors);
            ValidateSlotConflicts(graph, errors);
            
            return errors;
        }

        /// <summary>
        /// Validate graph and additionally enforce channel-safety rules.
        /// Channel-safety: any state whose OnEntryAction or ActivityAction writes to a
        /// channel must have the corresponding exit-cleanup as its OnExitAction.
        /// </summary>
        /// <param name="graph">The state-machine graph to validate.</param>
        /// <param name="requiredExitCleanups">
        /// Maps channel-writing action names to their required exit-cleanup action name.
        /// Typically sourced from the generated <c>HsmActionRegistrar.RequiredExitCleanups</c>.
        /// </param>
        public static List<ValidationError> Validate(
            StateMachineGraph graph,
            IReadOnlyDictionary<string, string>? requiredExitCleanups)
        {
            var errors = Validate(graph);
            if (requiredExitCleanups != null && errors.All(e => e.Severity != ErrorSeverity.Error || e.Message != "Graph is null"))
                ValidateChannelSafety(graph, requiredExitCleanups, errors);
            return errors;
        }

        /// <summary>
        /// Checks that every state whose OnEntryAction or ActivityAction writes to a
        /// channel has the corresponding exit-cleanup action as its OnExitAction.
        /// Call this after <see cref="Validate(StateMachineGraph)"/> with the
        /// <c>RequiredExitCleanups</c> dictionary from the generated registrar.
        /// </summary>
        public static void ValidateChannelSafety(
            StateMachineGraph graph,
            IReadOnlyDictionary<string, string> requiredExitCleanups,
            List<ValidationError> errors)
        {
            if (graph == null || requiredExitCleanups == null || requiredExitCleanups.Count == 0) return;

            foreach (var state in graph.States.Values)
            {
                // Collect which channel-writing actions are active in this state.
                var channelActions = new List<string>();
                if (state.OnEntryAction != null && requiredExitCleanups.ContainsKey(state.OnEntryAction))
                    channelActions.Add(state.OnEntryAction);
                if (state.ActivityAction != null && requiredExitCleanups.ContainsKey(state.ActivityAction))
                    channelActions.Add(state.ActivityAction);

                foreach (var actionName in channelActions)
                {
                    string required = requiredExitCleanups[actionName];
                    if (state.OnExitAction != required)
                    {
                        string actual = state.OnExitAction ?? "(none)";
                        errors.Add(new ValidationError(
                            $"Action '{actionName}' writes to a channel but OnExitAction is '{actual}' " +
                            $"(expected '{required}'). Add OnExitAction = \"{required}\" to this state.",
                            state.Name));
                    }
                }
            }
        }

        private static void ValidateSlotConflicts(StateMachineGraph graph, List<ValidationError> errors)
        {
            // For each state with orthogonal regions
            foreach (var state in graph.States.Values)
            {
                if (state.Children.Count < 2) continue; // No orthogonal regions
                if (!state.IsParallel) continue;
                
                // Build exclusion graph: which slots are used in which region
                var timerSlots = new Dictionary<int, List<string>>();
                var historySlots = new Dictionary<int, List<string>>();
                
                foreach (var region in state.Children)
                {
                    CollectSlotUsage(region, timerSlots, historySlots);
                }
                
                // Check for conflicts (same slot in multiple regions)
                foreach (var kvp in timerSlots)
                {
                    if (kvp.Value.Count > 1)
                    {
                        errors.Add(new ValidationError(
                            $"Timer slot {kvp.Key} used in multiple regions: {string.Join(", ", kvp.Value)}", state.Name)
                        { Severity = ErrorSeverity.Error });
                    }
                }
                
                foreach (var kvp in historySlots)
                {
                    if (kvp.Value.Count > 1)
                    {
                        errors.Add(new ValidationError(
                            $"History slot {kvp.Key} used in multiple regions: {string.Join(", ", kvp.Value)}", state.Name)
                        { Severity = ErrorSeverity.Error });
                    }
                }
            }
        }

        private static void CollectSlotUsage(
            StateNode region, 
            Dictionary<int, List<string>> timerSlots,
            Dictionary<int, List<string>> historySlots)
        {
            // Recursively collect timer/history slot usage
            if (region.TimerSlotIndex >= 0)
            {
                if (!timerSlots.ContainsKey(region.TimerSlotIndex))
                    timerSlots[region.TimerSlotIndex] = new List<string>();
                timerSlots[region.TimerSlotIndex].Add(region.Name);
            }
            
            if (region.HistorySlotIndex != 0xFFFF)
            {
                int slot = region.HistorySlotIndex;
                if (!historySlots.ContainsKey(slot))
                    historySlots[slot] = new List<string>();
                historySlots[slot].Add(region.Name);
            }
            
            foreach (var child in region.Children)
            {
                CollectSlotUsage(child, timerSlots, historySlots);
            }
        }

        private static void ValidateIndirectEvents(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var evt in graph.Events)
            {
                if (evt.PayloadSize > 16)
                {
                    if (!evt.IsIndirect)
                    {
                        errors.Add(new ValidationError(
                            $"Event '{evt.Name}' has payload {evt.PayloadSize}B (>16B) but not marked IsIndirect. " +
                            "Large events must be ID-only (use IsIndirect=true).")
                        { Severity = ErrorSeverity.Error });
                    }
                }
                
                // Warn if deferred + indirect (can't defer ID-only events)
                if (evt.IsIndirect && evt.IsDeferred)
                {
                    errors.Add(new ValidationError(
                        $"Event '{evt.Name}' is both IsIndirect and IsDeferred. " +
                        "ID-only events cannot be deferred (data not in queue).")
                    { Severity = ErrorSeverity.Warning });
                }
            }
        }
        
        private static void ValidateStructure(StateMachineGraph graph, List<ValidationError> errors)
        {
            // 1. Root exists
            if (graph.RootState == null)
            {
                errors.Add(new ValidationError("Root state is null"));
                return;
            }
            
            // 2. No orphans (all states reachable from root)
            var reachable = new HashSet<StateNode>();
            CollectReachable(graph.RootState, reachable);
            
            foreach (var state in graph.States.Values)
            {
                if (!reachable.Contains(state))
                {
                    errors.Add(new ValidationError("Orphan state (not reachable from root)", state.Name));
                }
            }
            
            // 3. No circular parent chains
            foreach (var state in graph.States.Values)
            {
                if (HasCircularParent(state))
                {
                    errors.Add(new ValidationError("Circular parent chain detected", state.Name));
                }
            }
            
            // 4. State names unique (already enforced by AddState, but check)
            var names = new HashSet<string>();
            foreach (var state in graph.States.Values)
            {
                if (!names.Add(state.Name))
                {
                    errors.Add(new ValidationError($"Duplicate state name: {state.Name}"));
                }
            }
        }
        
        private static void ValidateTransitions(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var state in graph.States.Values)
            {
                foreach (var trans in state.Transitions)
                {
                    // Source valid
                    if (trans.Source == null)
                    {
                        errors.Add(new ValidationError("Transition has null source", state.Name));
                        continue;
                    }
                    
                    // Target valid
                    if (trans.Target == null)
                    {
                        errors.Add(new ValidationError("Transition has null target", state.Name));
                        continue;
                    }
                    
                    // Target exists in graph
                    if (!graph.States.ContainsValue(trans.Target))
                    {
                        errors.Add(new ValidationError($"Transition target '{trans.Target.Name}' not in graph", state.Name));
                    }
                    
                    // EventId registered
                    if (!graph.EventNameToId.ContainsValue(trans.EventId))
                    {
                        errors.Add(new ValidationError($"Transition uses unregistered EventId {trans.EventId}", state.Name));
                    }
                }
            }
        }
        
        private static void ValidateInitialStates(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var state in graph.States.Values)
            {
                if (state.Children.Count > 0)
                {
                    // Parallel states implicitly enter all children, so no single initial child required
                    if (state.IsParallel) continue;

                    // Composite: must have exactly one initial
                    var initialCount = state.Children.Count(c => c.IsInitial);
                    
                    if (initialCount == 0)
                    {
                        errors.Add(new ValidationError("Composite state has no initial child", state.Name));
                    }
                    else if (initialCount > 1)
                    {
                        errors.Add(new ValidationError("Composite state has multiple initial children", state.Name));
                    }
                }
                else
                {
                    // Leaf: should not be marked initial (meaningless)
                    // Actually, initial is relative to parent, so this is OK
                }
            }
        }
        
        private static void ValidateHistoryStates(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var state in graph.States.Values)
            {
                if (state.IsHistory)
                {
                    // Must have parent
                    if (state.Parent == null)
                    {
                        errors.Add(new ValidationError("History state has no parent", state.Name));
                        continue;
                    }
                    
                    // Parent must be composite
                    if (state.Parent.Children.Count == 0)
                    {
                        errors.Add(new ValidationError("History state parent is not composite", state.Name));
                    }
                }
            }
        }
        
        private static void ValidateFunctions(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var state in graph.States.Values)
            {
                // Check actions
                if (state.OnEntryAction != null && !graph.RegisteredActions.Contains(state.OnEntryAction))
                {
                    errors.Add(new ValidationError($"OnEntry action '{state.OnEntryAction}' not registered", state.Name));
                }
                
                if (state.OnExitAction != null && !graph.RegisteredActions.Contains(state.OnExitAction))
                {
                    errors.Add(new ValidationError($"OnExit action '{state.OnExitAction}' not registered", state.Name));
                }
                
                if (state.ActivityAction != null && !graph.RegisteredActions.Contains(state.ActivityAction))
                {
                    errors.Add(new ValidationError($"Activity action '{state.ActivityAction}' not registered", state.Name));
                }
                
                // Check transition guards/actions
                foreach (var trans in state.Transitions)
                {
                    if (trans.GuardFunction != null && !graph.RegisteredGuards.Contains(trans.GuardFunction))
                    {
                        errors.Add(new ValidationError($"Guard '{trans.GuardFunction}' not registered", state.Name));
                    }
                    
                    if (trans.ActionFunction != null && !graph.RegisteredActions.Contains(trans.ActionFunction))
                    {
                        errors.Add(new ValidationError($"Transition action '{trans.ActionFunction}' not registered", state.Name));
                    }
                }
            }
        }
        
        private static void ValidateLimits(StateMachineGraph graph, List<ValidationError> errors)
        {
            // State count
            if (graph.States.Count > 65535)
            {
                errors.Add(new ValidationError($"State count {graph.States.Count} exceeds limit 65535"));
            }
            
            // Depth
            foreach (var state in graph.States.Values)
            {
                if (state.Depth > 15)
                {
                    errors.Add(new ValidationError($"Depth {state.Depth} exceeds limit 15", state.Name));
                }
            }
            
            // Transition count
            int totalTransitions = graph.States.Values.Sum(s => s.Transitions.Count);
            if (totalTransitions > 65535)
            {
                errors.Add(new ValidationError($"Transition count {totalTransitions} exceeds limit 65535"));
            }
        }
        
        // Helpers
        
        private static void CollectReachable(StateNode node, HashSet<StateNode> reachable)
        {
            if (!reachable.Add(node)) return;  // Already visited
            
            foreach (var child in node.Children)
            {
                CollectReachable(child, reachable);
            }
        }
        
        private static bool HasCircularParent(StateNode state)
            {
            var visited = new HashSet<StateNode>();
            var current = state;
            
            while (current != null)
            {
                if (!visited.Add(current)) return true;  // Cycle detected
                current = current.Parent;
            }
            
            return false;
        }
    }
}