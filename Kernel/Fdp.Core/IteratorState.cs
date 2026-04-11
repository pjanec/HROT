namespace Fdp.Kernel
{
    /// <summary>
    /// Holds the state of a time-sliced iteration.
    /// Allows pausing and resuming execution across multiple frames.
    /// </summary>
    public class IteratorState
    {
        /// <summary>
        /// The index of the next entity to process.
        /// </summary>
        public int NextEntityId { get; set; }
        
        /// <summary>
        /// Indicates if the iteration has completed all entities.
        /// </summary>
        public bool IsComplete { get; set; }
        
        /// <summary>
        /// Resets the iterator to the beginning.
        /// </summary>
        public void Reset()
        {
            NextEntityId = 0;
            IsComplete = false;
        }
    }
}
