using System;

namespace Fbt.Compiler.Graph
{
    /// <summary>
    /// Root container for a behavior tree graph (mutable DOM for the authoring tool).
    /// </summary>
    public class BehaviorTreeGraph
    {
        public string TreeName = string.Empty;
        public Guid TreeId = Guid.NewGuid();
        public BehaviorTreeNode? RootNode;
    }
}
