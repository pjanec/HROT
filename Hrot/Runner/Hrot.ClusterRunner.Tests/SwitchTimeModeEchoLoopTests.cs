using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost_Core.Time;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Verifies that <see cref="SwitchTimeModeDescriptorTranslator"/> breaks the DDS echo loop:
/// a message received via <see cref="SwitchTimeModeDescriptorTranslator.PollIngress"/> must
/// NOT be re-published to DDS by the next <see cref="SwitchTimeModeDescriptorTranslator.ScanAndPublish"/> call.
/// </summary>
[Collection("SwitchTimeModeEchoLoopTests")]
public sealed class SwitchTimeModeEchoLoopTests : IDisposable
{
    // Domain 29 is reserved for SwitchTimeModeEchoLoop tests.
    private const int TestDomain = 29;

    private readonly DdsParticipant _participant;

    public SwitchTimeModeEchoLoopTests()
    {
        _participant = new DdsParticipant(TestDomain);
    }

    public void Dispose() => _participant.Dispose();

    /// <summary>
    /// After a <see cref="SwitchTimeModeWireDto"/> sample is received via DDS (simulating a
    /// remote node publishing), <see cref="SwitchTimeModeDescriptorTranslator.PollIngress"/>
    /// publishes the event to the local bus. The immediately subsequent
    /// <see cref="SwitchTimeModeDescriptorTranslator.ScanAndPublish"/> must NOT re-write
    /// the identical message back to DDS, breaking the echo loop.
    /// </summary>
    [Fact(Timeout = 15_000)]
    public void PollIngress_ThenScanAndPublish_DoesNotEchoBack()
    {
        var bus        = new FdpEventBus();
        var translator = new SwitchTimeModeDescriptorTranslator(_participant, bus);

        // Write a wire message to DDS to simulate a remote master publishing a pause barrier.
        using var externalWriter = new DdsWriter<SwitchTimeModeWireDto>(_participant);
        var wireMsg = new SwitchTimeModeWireDto
        {
            TargetModeInt    = (int)TimeMode.Deterministic,
            BarrierWallTicks = 9_000_000_000L,
            FixedDelta       = 1f / 60f,
        };
        externalWriter.Write(wireMsg);

        Thread.Sleep(150); // Allow DDS propagation

        // PollIngress: reads the DDS sample, caches _lastIngressed, publishes to bus.
        translator.PollIngress(null!, null!);

        // Prepare a separate DDS reader to observe whether the translator re-publishes to DDS.
        using var echoObserver = new DdsReader<SwitchTimeModeWireDto>(_participant);

        // Swap the event bus so the bus event from PollIngress is available to Consume<T>.
        bus.SwapBuffers();

        Thread.Sleep(50); // Give echoObserver time to be established before ScanAndPublish writes

        // Drain any TransientLocal historical samples delivered to echoObserver
        // (the original wireMsg stored by DDS durability) before calling ScanAndPublish.
        using (var drain = echoObserver.Take()) { /* discard historical samples */ }

        // ScanAndPublish: should suppress the echoed event.
        translator.ScanAndPublish(null!);

        Thread.Sleep(150); // Wait to ensure any re-published sample would have arrived

        // Assert: no echoed sample should appear on the DDS topic.
        bool echoDetected = false;
        using var loan = echoObserver.Take();
        foreach (var s in loan)
        {
            if (s.IsValid &&
                s.Data.BarrierWallTicks == wireMsg.BarrierWallTicks &&
                s.Data.TargetModeInt    == wireMsg.TargetModeInt &&
                s.Data.FixedDelta       == wireMsg.FixedDelta)
            {
                echoDetected = true;
            }
        }

        Assert.False(echoDetected,
            "ScanAndPublish must not re-publish an event that was just ingested via PollIngress (echo loop suppression).");
    }

    /// <summary>
    /// A new <see cref="SwitchTimeModeEvent"/> with a different <c>BarrierWallTicks</c>
    /// (i.e., a genuine new pause command from the coordinator) must still be published to DDS
    /// even when a prior event was cached in <c>_lastIngressed</c>.
    /// </summary>
    [Fact(Timeout = 15_000)]
    public void ScanAndPublish_NewUniqueEvent_IsPublishedDespiteCachedIngress()
    {
        var bus        = new FdpEventBus();
        var translator = new SwitchTimeModeDescriptorTranslator(_participant, bus);

        // Simulate an earlier ingress from the network.
        using var externalWriter = new DdsWriter<SwitchTimeModeWireDto>(_participant);
        externalWriter.Write(new SwitchTimeModeWireDto
        {
            TargetModeInt    = (int)TimeMode.Deterministic,
            BarrierWallTicks = 1_000_000L,
            FixedDelta       = 1f / 60f,
        });

        Thread.Sleep(150);
        translator.PollIngress(null!, null!);
        bus.SwapBuffers(); // consume the bus event from PollIngress
        translator.ScanAndPublish(null!); // discard the echo

        // Now publish a NEW event with a different barrier (fresh coordinator command).
        long newBarrier = 9_999_999_999L;
        bus.Publish(new SwitchTimeModeEvent
        {
            TargetMode       = TimeMode.Deterministic,
            BarrierWallTicks = newBarrier,
            FixedDelta       = 1f / 60f,
        });
        bus.SwapBuffers();

        using var observer = new DdsReader<SwitchTimeModeWireDto>(_participant);
        Thread.Sleep(50);

        translator.ScanAndPublish(null!);

        Thread.Sleep(150);

        bool published = false;
        using var loan = observer.Take();
        foreach (var s in loan)
        {
            if (s.IsValid && s.Data.BarrierWallTicks == newBarrier)
                published = true;
        }

        Assert.True(published,
            "A new SwitchTimeModeEvent with a unique BarrierWallTicks must be published to DDS.");
    }
}

[CollectionDefinition("SwitchTimeModeEchoLoopTests", DisableParallelization = true)]
public class SwitchTimeModeEchoLoopTestCollection { }
