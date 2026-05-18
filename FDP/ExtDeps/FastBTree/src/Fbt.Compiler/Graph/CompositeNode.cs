using System.Collections.Generic;

namespace Fbt.Compiler.Graph
{
    /// <summary>
    /// Composite node with multiple children (Sequence, Selector, Parallel).
    /// </summary>
    public class CompositeNode : BehaviorTreeNode
    {
        public List<BehaviorTreeNode> Children = new List<BehaviorTreeNode>();
        public int ParallelPolicy;
    }
}
