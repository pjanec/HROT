namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Discriminator for cognitive and hardware-level interrupt signals.
    /// </summary>
    public enum CognitiveInterruptType : byte
    {
        MobilityLost = 0,
        Reserved = 1
    }
}
