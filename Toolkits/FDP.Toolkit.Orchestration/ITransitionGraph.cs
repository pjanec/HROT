namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Provides the adjacency information required by <c>TransitionPlanner</c> to
    /// compute BFS shortest paths between state IDs.
    ///
    /// <para>
    /// Bagira's concrete implementation, <c>BagiraStateGraph</c>, is constructed
    /// via <see cref="TransitionGraphBuilder"/> using the <c>DSMState</c> edge table
    /// previously hardcoded inside <c>TransitionPlanner</c>.
    /// </para>
    /// </summary>
    public interface ITransitionGraph
    {
        /// <summary>
        /// Returns the state IDs directly reachable from <paramref name="fromStateId"/>
        /// in a single transition step.
        /// </summary>
        IReadOnlyList<int> GetNeighbors(int fromStateId);

        /// <summary>All known state IDs registered with this graph.</summary>
        IReadOnlyList<int> AllStates { get; }
    }
}
