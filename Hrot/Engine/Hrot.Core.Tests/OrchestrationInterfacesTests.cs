using Hrot.Core.Network;

namespace Hrot.Core.Tests;

/// <summary>
/// Verifies that <see cref="IOrchestrationTranslator"/> and the null implementations
/// compile and function without any CycloneDDS assembly references (HEXAG2-S003).
/// </summary>
public sealed class OrchestrationInterfacesTests
{
    [Fact]
    public void NullOrchestrationTranslator_ImplementsInterface_WithoutDdsReferences()
    {
        // Arrange + Act
        using IOrchestrationTranslator translator = new NullOrchestrationTranslator();

        // Assert -- no exceptions; interface contract satisfied
        translator.Tick();
    }

    [Fact]
    public void NullMasterTimeTranslators_ImplementsInterface_WithoutDdsReferences()
    {
        using IMasterTimeTranslators t = new NullMasterTimeTranslators();
        t.ScanAndPublish();
        t.PollIngress();
        t.PollNtpIngress();
    }

    [Fact]
    public void NullSlaveOrchestrationTranslator_ImplementsInterface_WithoutDdsReferences()
    {
        using ISlaveOrchestrationTranslator translator = new NullSlaveOrchestrationTranslator();
        translator.Tick();
    }

    [Fact]
    public void NullOrchestrationObserver_ImplementsInterface_WithoutDdsReferences()
    {
        using IOrchestrationObserver observer = new NullOrchestrationObserver();
        observer.Tick();
    }
}
