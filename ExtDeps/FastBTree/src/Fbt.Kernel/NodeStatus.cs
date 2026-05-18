namespace Fbt
{
    /// <summary>
    /// Result of a node's execution.
    /// </summary>
    public enum NodeStatus : byte
    {
        /// <summary>Node failed to complete its task.</summary>
        Failure = 0,
        
        /// <summary>Node successfully completed its task.</summary>
        Success = 1,
        
        /// <summary>Node is still executing (multi-frame).</summary>
        Running = 2
    }
}
