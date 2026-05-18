namespace Fbt.Compiler.Graph
{
    /// <summary>
    /// Logic leaf node (Action or Condition).
    /// For expression-bound leaves, <see cref="TargetDtoType"/> and
    /// <see cref="TargetFieldName"/> identify the projected blackboard field.
    /// </summary>
    public class LogicNode : BehaviorTreeNode
    {
        public string DelegateName = string.Empty;
        public string TargetDtoType = string.Empty;
        public string TargetFieldName = string.Empty;
    }
}
