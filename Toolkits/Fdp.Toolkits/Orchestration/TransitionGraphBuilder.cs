namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Fluent builder that constructs an <see cref="ITransitionGraph"/> from
    /// explicit state and edge declarations.
    ///
    /// <para>
    /// Calling <see cref="AddTransition"/> for a state ID implicitly registers
    /// both the <c>fromStateId</c> and <c>toStateId</c> as known states, so
    /// <see cref="AddState"/> calls are optional.
    /// </para>
    /// </summary>
    public sealed class TransitionGraphBuilder
    {
        private readonly Dictionary<int, List<int>> _edges      = new();
        private readonly Dictionary<int, string>    _stateNames = new();

        /// <summary>
        /// Registers a state with an optional debug name.
        /// Idempotent — re-registering the same ID is safe.
        /// </summary>
        public TransitionGraphBuilder AddState(int stateId, string debugName = "")
        {
            _edges.TryAdd(stateId, new List<int>());
            if (!string.IsNullOrEmpty(debugName))
                _stateNames[stateId] = debugName;
            return this;
        }

        /// <summary>
        /// Declares a directed transition from <paramref name="fromStateId"/> to
        /// <paramref name="toStateId"/>.  Both states are implicitly registered.
        /// </summary>
        public TransitionGraphBuilder AddTransition(int fromStateId, int toStateId)
        {
            AddState(fromStateId);
            AddState(toStateId);
            _edges[fromStateId].Add(toStateId);
            return this;
        }

        /// <summary>Builds the immutable <see cref="ITransitionGraph"/>.</summary>
        public ITransitionGraph Build()
        {
            var edges = new Dictionary<int, IReadOnlyList<int>>();
            foreach (var kv in _edges)
                edges[kv.Key] = kv.Value.ToArray();
            return new BuiltGraph(edges);
        }

        // ── Private immutable implementation ─────────────────────────────────

        private sealed class BuiltGraph : ITransitionGraph
        {
            private readonly Dictionary<int, IReadOnlyList<int>> _edges;
            private readonly IReadOnlyList<int> _allStates;

            public BuiltGraph(Dictionary<int, IReadOnlyList<int>> edges)
            {
                _edges     = edges;
                _allStates = edges.Keys.ToArray();
            }

            public IReadOnlyList<int> GetNeighbors(int fromStateId)
                => _edges.TryGetValue(fromStateId, out var neighbors) ? neighbors : Array.Empty<int>();

            public IReadOnlyList<int> AllStates => _allStates;
        }
    }
}
