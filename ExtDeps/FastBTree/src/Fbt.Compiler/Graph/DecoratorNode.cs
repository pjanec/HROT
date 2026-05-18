namespace Fbt.Compiler.Graph
{
    /// <summary>
    /// Decorator node wrapping a single child (Inverter, Repeater, Cooldown, Wait).
    /// </summary>
    public class DecoratorNode : BehaviorTreeNode
    {
        public BehaviorTreeNode? Child;
        public float Duration;
        public int RepeatCount;
    }
}
