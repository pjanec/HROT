namespace Fbt
{
    /// <summary>
    /// Type of behavior tree node.
    /// </summary>
    public enum NodeType : byte
    {
        // Core Composites
        /// <summary>Entry point of the tree.</summary>
        Root = 0,
        /// <summary>Executes children until one succeeds.</summary>
        Selector = 1,
        /// <summary>Executes children until one fails.</summary>
        Sequence = 2,
        /// <summary>Executes children in parallel.</summary>
        Parallel = 3,
        
        // Leaves
        /// <summary>Leaf node that performs an action.</summary>
        Action = 10,
        /// <summary>Leaf node that checks a condition.</summary>
        Condition = 11,
        /// <summary>Leaf node that waits for a specific time.</summary>
        Wait = 12,
        
        // Decorators
        /// <summary>Inverts the result of its child.</summary>
        Inverter = 20,
        /// <summary>Repeats its child a number of times or forever.</summary>
        Repeater = 21,
        /// <summary>Limits the execution frequency of its child.</summary>
        Cooldown = 22,
        /// <summary>Always returns success.</summary>
        ForceSuccess = 23,
        /// <summary>Always returns failure.</summary>
        ForceFailure = 24,
        /// <summary>Repeats child until it succeeds.</summary>
        UntilSuccess = 25,
        /// <summary>Repeats child until it fails.</summary>
        UntilFailure = 26,
        
        // Advanced
        /// <summary>Runs a service periodically while child runs.</summary>
        Service = 30,
        /// <summary>Aborts execution if condition changes.</summary>
        Observer = 31,
        /// <summary>Executes another behavior tree.</summary>
        Subtree = 40
    }
}
