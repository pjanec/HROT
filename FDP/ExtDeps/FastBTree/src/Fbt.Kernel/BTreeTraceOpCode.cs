namespace Fbt
{
    /// <summary>Op-codes emitted by the Interpreter to ITreeTracer implementations.</summary>
    public enum BTreeTraceOpCode : byte
    {
        None           = 0x00,
        NodeEvaluated  = 0x01,
        ScopePushed    = 0x02,
        ScopePopped    = 0x03,
        WaitStarted    = 0x04,
        WaitCompleted  = 0x05,
        ChannelMutated = 0x06,
        Error          = 0x0E,
    }
}
