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
        
        public StateBuilder State(string name, Guid stableId = default)
        {
            var state = new StateNode(name, stableId == default ? null : (Guid?)stableId);
            _graph.AddState(state);
            _graph.RootState.AddChild(state);  // Top-level states are children of root
            
            return new StateBuilder(state, _graph);
        }

        /// <summary>
        /// Adds a global transition that fires for any active state when <paramref name="eventName"/> is raised.
        /// ⭐ <c>CE-506</c>: an optional <paramref name="guard"/>, effect <paramref name="action"/> and <paramref name="priority"/>
        /// — the kernel's <c>GlobalTransitionDef</c> always had the slots; this overload never filled them, so an authored
        /// global guard/action was silently dropped. The guard is evaluated against the ACTIVE leaf (region 0), as before.
        /// Among global transitions on one event the highest priority is tried first (ties keep declaration order).
        /// </summary>
        public HsmBuilder GlobalTransition(string eventName, string targetStateName, Guid visualId = default,
            string? guard = null, string? action = null, byte? priority = null)
        {
            if (!_graph.EventNameToId.TryGetValue(eventName, out ushort eventId))
                throw new InvalidOperationException($"Event '{eventName}' not registered");

            var target = _graph.FindState(targetStateName)
                ?? throw new InvalidOperationException($"Target state '{targetStateName}' not found");

            var t = new TransitionNode
            {
                Source = null,  // global transitions have no source state
                Target = target,
                EventId = eventId,
                VisualId = visualId == default ? Guid.NewGuid() : visualId,
                GuardFunction = guard,
                ActionFunction = action,
            };
            if (priority.HasValue) t.Priority = priority.Value;
            _graph.GlobalTransitions.Add(t);
            return this;
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

        public StateBuilder Parallel()
        {
            _state.IsParallel = true;
            return this;
        }

        /// <summary>
        /// ⭐ CE-1003 (Q84 A0) — declare which orthogonal region of its PARALLEL parent this child belongs to.
        /// Children sharing an index form one region (a sub-state machine); <see cref="Initial"/> marks that region's
        /// initial state. A child that never calls this is a region of its own.
        /// </summary>
        public StateBuilder InRegion(int regionIndex)
        {
            if (regionIndex < 0) throw new ArgumentOutOfRangeException(nameof(regionIndex));
            _state.RegionIndex = regionIndex;
            return this;
        }

        public StateBuilder DeepHistory()
        {
            _state.IsDeepHistory = true;
            return this;
        }

        /// <summary>
        /// ⭐⭐ <b><c>CE-383</c> — bake an EXPLICIT activity action id, bypassing the name hash.</b>
        /// ⛔ Not for hand-written actions: use <see cref="Activity(string)"/>, whose FQN hash is the
        /// identity (<c>E6</c>(A)). ⭐ This exists for a BLUEPRINT-hosted activity, which registers
        /// under its <c>BlueprintId</c> — a value no authorable name hashes to. 📄
        /// <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.2.
        /// </summary>
        public StateBuilder ActivityId(ushort actionId)
        {
            _state.ActivityActionId = actionId;
            return this;
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-388</c> / <c>Q74 D-B1</c> — bake an EXPLICIT exit action id.</b>
        /// The mirror of <see cref="ActivityId(ushort)"/>, and it exists for the same reason: a
        /// BLUEPRINT-hosted activity's exit-cleanup registers under an id no authorable name hashes
        /// to. ⛔ Not for hand-written cleanups — use <see cref="OnExit(string)"/>, whose FQN hash
        /// is the identity.
        ///
        /// <para>⚠⚠ <b>THE FILL-AN-EMPTY-SLOT RULE IS ENFORCED BY THE EMITTER, NOT HERE.</b>
        /// 📐 <c>HsmFlattener:173</c> reads <c>ExitActionId != 0 ? ExitActionId : hash(OnExitAction)</c>
        /// — the explicit id WINS over the name — so this builder cannot express "only if unset".
        /// ⭐ <c>HsmEmitCore</c> therefore emits <c>.OnExitId(n)</c> only for a state that has no
        /// <c>OnExitAction</c>, which is what keeps an authored cleanup authoritative. ⛔ A caller
        /// that sets both gets the id, silently — do not do that.</para>
        /// </summary>
        public StateBuilder OnExitId(ushort actionId)
        {
            // ⭐ The SAME field the JSON parser and CE-383's ActivityId shape already use; 0 = unset.
            _state.ExitActionId = actionId;
            return this;
        }

        public StateBuilder TimerAction(string actionName)
        {
            _state.TimerAction = actionName;
            return this;
        }

        public StateBuilder Final()
        {
            _state.IsFinal = true;
            return this;
        }

        public StateBuilder DeferEvent(ushort eventId)
        {
            if (!_state.DeferredEventIds.Contains(eventId))
                _state.DeferredEventIds.Add(eventId);
            return this;
        }

        public StateBuilder Child(string childName, Action<StateBuilder> configure, Guid stableId = default)
        {
            var child = new StateNode(childName, stableId == default ? null : (Guid?)stableId);
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
        
        public TransitionBuilder GoTo(string targetStateName, Guid visualId = default)
        {
            var target = _graph.FindState(targetStateName);
            if (target == null)
                throw new InvalidOperationException($"Target state '{targetStateName}' not found");
            
            _transition.VisualId = visualId == default ? Guid.NewGuid() : visualId;
            _transition.Target = target;
            _source.AddTransition(_transition);
            return this;
        }

        public TransitionBuilder GoTo(StateBuilder target, Guid visualId = default)
        {
            _transition.VisualId = visualId == default ? Guid.NewGuid() : visualId;
            _transition.Target = target.State;
            _source.AddTransition(_transition);
            return this;
        }
        
        public TransitionBuilder Guard(string guardName)
        {
            _transition.GuardFunction = guardName;
            return this;
        }
        
        /// <summary>
        /// ⭐⭐ <b><c>CE-383</c> — bake an EXPLICIT guard id, bypassing the name hash.</b>
        /// ⛔ Not for hand-written guards: use <see cref="Guard(string)"/>. ⭐ This exists for a
        /// BLUEPRINT-hosted guard, which registers under its <c>BlueprintId</c>. 📄 §3.2.
        /// </summary>
        public TransitionBuilder GuardId(ushort guardId)
        {
            _transition.GuardId = guardId;
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

        /// <summary>
        /// ⭐⭐⭐ <b>CE-381 — mark this transition POLLED: its guard is evaluated every quiescent
        /// tick, with no event.</b>
        ///
        /// <para>⛔⛔ <b>This is NOT "a transition with no event".</b> An eventless transition
        /// (<c>EventId 0</c>) is already selected by the RTC loop's completion pass and fires ONCE,
        /// as a consequence of another transition firing. A polled transition fires whenever its
        /// guard passes while the machine is otherwise idle. 🔒 The two were deliberately NOT
        /// collapsed onto one encoding — 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §2.3,
        /// §3.1, §10 ③.</para>
        ///
        /// <para>⚠ A polled transition normally carries a <see cref="Guard"/>. One without a guard
        /// fires on the first quiescent tick, which is legal and occasionally what you want.</para>
        /// </summary>
        public TransitionBuilder Polled(bool polled = true)
        {
            _transition.IsPolled = polled;
            return this;
        }
    }
}