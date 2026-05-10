using CycloneDDS.Runtime;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoMap.Network;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests proving that headless subsystems correctly send gizmo primitives
/// over DDS and receive gizmo interaction events from the network.
/// </summary>
public class HeadlessGizmoStreamingTests
{
    private const int InteractionTimeoutFrames = 60;

    [Fact]
    public void HeadlessSimHost_GizmoBufferIsInitialized()
    {
        // Arrange + Act: standard harness startup is sufficient.
        using var harness = new HrotRunnerHarness();

        // Assert: the gizmo primitive buffer must be non-null, proving
        // that SimHostApp.InitializeGizmos() ran and _gizmoBuffer was created.
        Assert.NotNull(harness.SimHost.TestHook_GizmoBuffer);
    }

    [Fact]
    public void HeadlessSimHost_ReceivesGizmoInteraction_ViaDds()
    {
        // Arrange: start headless cluster and create a test-owned DDS participant
        // on the same domain so its writer matches the production reader.
        using var harness = new HrotRunnerHarness();

        // The ingress translator must exist when DDS is active (harness uses NedNetworkFactory with participant).
        Assert.NotNull(harness.SimHost.TestHook_GizmoIngressTranslator);

        using var testParticipant = new DdsParticipant((uint)harness.DomainId);
        using var interactionWriter = new DdsWriterGizmoAdapter<GizmoInteractionBatch>(testParticipant);

        // Act: publish one interaction event from the test participant.
        interactionWriter.Write(new GizmoInteractionBatch
        {
            Kind         = GizmoInteractionEventKind.MenuAction,
            SourceNodeId = 99,
            SequenceNumber = 1,
            ActionId     = 0,
        });

        // Pump frames until SimHost's ingress translator processes the DDS sample.
        bool received = harness.PumpUntil(
            () => harness.SimHost.TestHook_GizmoIngressTranslator!.ReceivedSampleCount > 0,
            InteractionTimeoutFrames);

        // Assert: the ingress translator must have seen at least one sample.
        Assert.True(received, "SimHost headless mode did not receive any GizmoInteractionBatch samples over DDS.");
    }
}
