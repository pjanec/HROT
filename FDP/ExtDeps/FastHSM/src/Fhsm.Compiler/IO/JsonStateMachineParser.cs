using System;
using System.Text.Json;
using Fhsm.Compiler.Graph;

namespace Fhsm.Compiler.IO
{
    /// <summary>
    /// Parses JSON state machine definitions into StateMachineGraph.
    /// </summary>
    public class JsonStateMachineParser
    {
        public StateMachineGraph Parse(string json)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var graph = new StateMachineGraph(root.GetProperty("name").GetString() ?? "Unnamed");
            
            // Parse states
            if (root.TryGetProperty("states", out var states))
            {
                foreach (var state in states.EnumerateArray())
                {
                    ParseState(state, graph, null);
                }
            }
            
            // Parse transitions
            if (root.TryGetProperty("transitions", out var transitions))
            {
                foreach (var transition in transitions.EnumerateArray())
                {
                    ParseTransition(transition, graph);
                }
            }
            
            return graph;
        }
        
        private void ParseState(JsonElement stateJson, StateMachineGraph graph, StateNode? parent)
        {
            var name = stateJson.GetProperty("name").GetString() ?? "Unnamed";
            var state = graph.AddState(name, parent);
            
            // Optional: entry/exit actions
            if (stateJson.TryGetProperty("onEntry", out var entry))
            {
                state.EntryActionId = (ushort)entry.GetInt32();
            }
            
            if (stateJson.TryGetProperty("onExit", out var exit))
            {
                state.ExitActionId = (ushort)exit.GetInt32();
            }
            
            // Recursive: nested states
            if (stateJson.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    ParseState(child, graph, state);
                }
            }
        }
        
        private void ParseTransition(JsonElement transJson, StateMachineGraph graph)
        {
            var source = transJson.GetProperty("source").GetString();
            var target = transJson.GetProperty("target").GetString();
            var eventId = (ushort)transJson.GetProperty("event").GetInt32();
            
            var sourceState = graph.FindStateByName(source);
            var targetState = graph.FindStateByName(target);
            
            if (sourceState == null || targetState == null)
                throw new InvalidOperationException($"Invalid transition: {source} -> {target}");
            
            var transition = new TransitionNode
            {
                Source = sourceState,
                Target = targetState,
                EventId = eventId
            };
            
            if (transJson.TryGetProperty("guard", out var guard))
            {
                transition.GuardId = (ushort)guard.GetInt32();
            }
            
            if (transJson.TryGetProperty("action", out var action))
            {
                transition.ActionId = (ushort)action.GetInt32();
            }
            
            sourceState.Transitions.Add(transition);
        }
    }
}
