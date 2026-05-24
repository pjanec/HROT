namespace Fbt
{
    /// <summary>
    /// Optional context callback interface for BTree execution tracing.
    /// Implement on the TContext struct to receive trace events from the Interpreter.
    /// The JIT devirtualizes all calls because TContext is a struct constraint.
    /// </summary>
    public interface ITreeTracer
    {
        void TraceNodeEvaluated(int nodeIndex, NodeStatus status);
        void TraceScopePushed(ushort newStackDepth);
        void TraceScopePopped(ushort newStackDepth);
        void TraceWaitStarted(int nodeIndex, float duration);
        void TraceWaitCompleted(int nodeIndex, float duration);
    }
}
