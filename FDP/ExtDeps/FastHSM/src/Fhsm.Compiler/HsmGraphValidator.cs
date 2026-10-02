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
        /// channel releases that channel when the state exits.
        /// <para>
        /// CE-406 RE-AIMED THIS CHECK. It was written (BHU-014) when binding the cleanup
        /// BY NAME as OnExitAction was the only way to release a channel, and it was keyed
        /// and worded for that world. Two things then changed underneath it:
        /// </para>
        /// <para>
        /// (1) Q74 D-B1/D-D1 made the binding AUTOMATIC. The emitter bakes .OnExitId(hash)
        /// into ExitActionId and leaves OnExitAction null, so demanding
        /// OnExitAction == required would flag every correctly auto-bound state — including
        /// HsmTwoChannelRegionsDemo, the asset that auto-bind fixed. A state carrying ANY
        /// ExitActionId already releases, whoever bound it, so that is the first check here.
        /// </para>
        /// <para>
        /// (2) The dictionary is keyed on the SHORT method name (HsmActionGenerator emits
        /// m.Name) while an editor-authored asset holds the fully-qualified name, so a
        /// direct ContainsKey missed every real state. Matching on the short name is not a
        /// loosening: HsmActionDispatcher itself keys registrations on the short name, so
        /// this makes the validator agree with what actually happens at runtime.
        /// </para>
        /// <para>
        /// What remains, and the only thing this can now catch, is the one hole auto-bind
        /// cannot fill: a state that binds its OWN OnExitAction. The emitter fills an empty
        /// slot and never overwrites an authored one, so such a state silently loses its
        /// cleanup. Call this with the generated <c>HsmActionRegistrar.RequiredExitCleanups</c>;
        /// it is a test-suite helper, per Fdp.Toolkits.Analyzers.md section 7 item 7.
        /// </para>
        /// </summary>
        public static void ValidateChannelSafety(
            StateMachineGraph graph,
            IReadOnlyDictionary<string, string> requiredExitCleanups,
            List<ValidationError> errors)
        {
            if (graph == null || requiredExitCleanups == null || requiredExitCleanups.Count == 0) return;

            foreach (var state in graph.States.Values)
            {
                // A baked exit-action id means SOMETHING releases on exit -- the auto-bind, or
                // an explicit OnExitId. Either way the channel is not left standing.
                if (state.ExitActionId != 0) continue;

                // Collect which channel-writing actions are active in this state.
                var channelActions = new List<string>();
                AddIfChannelWriter(state.OnEntryAction, requiredExitCleanups, channelActions);
                AddIfChannelWriter(state.ActivityAction, requiredExitCleanups, channelActions);

                foreach (var actionName in channelActions)
                {
                    string required = requiredExitCleanups[ShortMemberName(actionName)];
                    // Short-name on BOTH sides, for the same reason as the lookup above: an
                    // author may have bound the cleanup by its fully-qualified name.
                    if (state.OnExitAction != null && ShortMemberName(state.OnExitAction) == required)
                        continue;

                    errors.Add(new ValidationError(
                        state.OnExitAction == null
                            ? $"Action '{actionName}' writes to a channel and this state binds no exit " +
                              $"cleanup, so the channel stays active after the state exits. Expected " +
                              $"'{required}'. (An asset emitted through HsmEmitCore gets this bound " +
                              $"automatically; a graph built by hand must bind it.)"
                            : $"Action '{actionName}' writes to a channel but this state binds its own " +
                              $"OnExitAction '{state.OnExitAction}', which displaces the required cleanup " +
                              $"'{required}' -- the auto-bind fills an empty slot and never overwrites an " +
                              $"authored one, so the channel stays active after the state exits.",
                        state.Name));
                }
            }
        }

        private static void AddIfChannelWriter(
            string? action,
            IReadOnlyDictionary<string, string> requiredExitCleanups,
            List<string> into)
        {
            if (action != null && requiredExitCleanups.ContainsKey(ShortMemberName(action)))
                into.Add(action);
        }

        /// <summary>
        /// The trailing member name of a possibly fully-qualified action name. Assets hold the
        /// FQN; the generated cleanup map and HsmActionDispatcher both key on the short name.
        /// </summary>
        private static string ShortMemberName(string action)
        {
            int dot = action.LastIndexOf('.');
            return dot >= 0 ? action.Substring(dot + 1) : action;
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