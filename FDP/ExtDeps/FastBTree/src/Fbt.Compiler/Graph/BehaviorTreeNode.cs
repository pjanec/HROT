using System;
using Fbt;

namespace Fbt.Compiler.Graph
{
    /// <summary>
    /// Abstract base for all behavior tree graph nodes.
    /// Carries no runtime execution logic — pure data for the authoring tool.
    /// </summary>
    public abstract class BehaviorTreeNode
    {
        public Guid VisualId = Guid.NewGuid();
        public NodeType Type;
        public BehaviorTreeNode? Parent;
        public float UiPosX;
        public float UiPosY;
        public string CustomComment = string.Empty;
    }
}
