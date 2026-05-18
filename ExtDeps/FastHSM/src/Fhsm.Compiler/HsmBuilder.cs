using System;
using Fhsm.Compiler.Graph;

namespace Fhsm.Compiler
{
    /// <summary>
    /// Fluent API for building state machines.
    /// </summary>
    public class HsmBuilder
    {
        private readonly StateMachineGraph _graph;
        
        public HsmBuilder(string machineName)
        {
            _graph = new StateMachineGraph(machineName);
        }
        
        public StateBuilder State(string name)
        {
            var state = new StateNode(name);
            _graph.AddState(state);
            _graph.RootState.AddChild(state);  // Top-level states are children of root
            
            return new StateBuilder(state, _graph);
        }
        
        public HsmBuilder Event(string eventName, ushort eventId, int payloadSize = 0, bool isIndirect = false, bool isDeferred = false)
        {
            _graph.EventNameToId[eventName] = eventId;
            _graph.Events.Add(new EventDefinition(eventName, eventId)
            {
                PayloadSize = payloadSize,
                IsIndirect = isIndirect,
                IsDeferred = isDeferred
            });
            return this;
        }
        
        public HsmBuilder RegisterAction(string functionName)
        {
            _graph.RegisteredActions.Add(functionName);
            return this;
        }
        
        public HsmBuilder RegisterGuard(string functionName)
        {
            _graph.RegisteredGuards.Add(functionName);
            return this;
        }

        public StateMachineGraph Build() => _graph;
        
        // Internal: Get graph for compiler
        internal StateMachineGraph GetGraph() => _graph;
    }
    
    /// <summary>
    /// Builder for configuring a single state.
    /// </summary>
    public class StateBuilder
    {
        private readonly StateNode _state;
        private readonly StateMachineGraph _graph;
        
        public StateNode State => _state;

        internal StateBuilder(StateNode state, StateMachineGraph graph)
        {
            _state = state;
            _graph = graph;
        }
        
        public StateBuilder OnEntry(string actionName)
        {
            _state.OnEntryAction = actionName;
            return this;
        }
        
        public StateBuilder OnExit(string actionName)
        {
            _state.OnExitAction = actionName;
            return this;
        }
        
        public StateBuilder Activity(string actionName)
        {
            _state.ActivityAction = actionName;
            return this;
        }
        
        public StateBuilder Initial()
        {
            _state.IsInitial = true;
            return this;
        }
        
        public StateBuilder History()
        {
            _state.IsHistory = true;
            return this;
        }

        public StateBuilder Final()
        {
            _state.IsFinal = true;
            return this;
        }

        public StateBuilder Child(string childName, Action<StateBuilder> configure)
        {
            var child = new StateNode(childName);
            _state.AddChild(child);
            _graph.AddState(child);
            
            var childBuilder = new StateBuilder(child, _graph);
            configure?.Invoke(childBuilder);
            
            return this;
        }
        
        public TransitionBuilder On(string eventName)
        {
            if (!_graph.EventNameToId.TryGetValue(eventName, out ushort eventId))
                throw new InvalidOperationException($"Event '{eventName}' not registered");
            
            return new TransitionBuilder(_state, eventId, _graph);
        }

        public TransitionBuilder On(ushort eventId)
        {
            return new TransitionBuilder(_state, eventId, _graph);
        }
    }
    
    /// <summary>
    /// Builder for configuring a transition.
    /// </summary>
    public class TransitionBuilder
    {
        private readonly StateNode _source;
        private readonly ushort _eventId;
        private readonly StateMachineGraph _graph;
        private readonly TransitionNode _transition;
        
        internal TransitionBuilder(StateNode source, ushort eventId, StateMachineGraph graph)
        {
            _source = source;
            _eventId = eventId;
            _graph = graph;
            // Target is set later, passed as null initially
            _transition = new TransitionNode(source, null!, eventId);
        }
        
        public TransitionBuilder GoTo(string targetStateName)
        {
            var target = _graph.FindState(targetStateName);
            if (target == null)
                throw new InvalidOperationException($"Target state '{targetStateName}' not found");
            
            _transition.Target = target;
            _source.AddTransition(_transition);
            return this;
        }

        public TransitionBuilder GoTo(StateBuilder target)
        {
            _transition.Target = target.State;
            _source.AddTransition(_transition);
            return this;
        }
        
        public TransitionBuilder Guard(string guardName)
        {
            _transition.GuardFunction = guardName;
            return this;
        }
        
        public TransitionBuilder Action(string actionName)
        {
            _transition.ActionFunction = actionName;
            return this;
        }
        
        public TransitionBuilder Priority(byte priority)
        {
            _transition.Priority = priority;
            return this;
        }
    }
}