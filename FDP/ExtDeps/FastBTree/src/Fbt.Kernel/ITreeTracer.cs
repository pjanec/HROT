namespace Fbt
{
    /// <summary>
    /// Contract through which the FastBTree kernel emits structural trace
    /// events without depending on the concrete buffer/component implementation.
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
