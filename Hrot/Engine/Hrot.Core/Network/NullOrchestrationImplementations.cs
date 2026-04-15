namespace Hrot.Core.Network;

/// <summary>No-op implementation of <see cref="IOrchestrationTranslator"/> for headless/offline mode.</summary>
public sealed class NullOrchestrationTranslator : IOrchestrationTranslator
{
    public void Tick() { }
    public void Dispose() { }
}

/// <summary>No-op implementation of <see cref="IMasterTimeTranslators"/> for headless/offline mode.</summary>
public sealed class NullMasterTimeTranslators : IMasterTimeTranslators
{
    public void ScanAndPublish() { }
    public void PollIngress() { }
    public void PollNtpIngress() { }
    public void Dispose() { }
}

/// <summary>No-op implementation of <see cref="ISlaveOrchestrationTranslator"/> for headless/offline mode.</summary>
public sealed class NullSlaveOrchestrationTranslator : ISlaveOrchestrationTranslator
{
    public void Tick() { }
    public void Dispose() { }
}

/// <summary>No-op implementation of <see cref="IOrchestrationObserver"/> for headless/offline mode.</summary>
public sealed class NullOrchestrationObserver : IOrchestrationObserver
{
    public void Tick() { }
    public void Dispose() { }
}

/// <summary>No-op implementation of <see cref="IDisposable"/> for factory methods that return IDisposable handles.</summary>
public sealed class NullDisposable : IDisposable
{
    public void Dispose() { }
}
