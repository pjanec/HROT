using System.Linq;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Schema;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Tests verifying that the <see cref="TimePulseIngressTranslator"/> correctly bridges
/// <see cref="TimePulseDescriptor"/> events to the <see cref="FdpEventBus"/> so
/// <c>SlaveTimeController</c> can consume them.
///
/// The translator's DDS-reading path requires a live CycloneDDS participant and is
/// exercised in the integration suite. These tests validate the bus-bridging
/// contract and the null-participant safety guard in isolation.
/// </summary>
public class TimePulseTranslatorTests
{
    [Fact]
    public void TimePulseDescriptor_HasDdsTopicAttribute()
    {
        var attr = typeof(TimePulseDescriptor).GetCustomAttributes(typeof(DdsTopicAttribute), false)
            .FirstOrDefault() as DdsTopicAttribute;

        Assert.NotNull(attr);
        Assert.Equal("TimePulse", attr!.TopicName);
    }

    // ── Registration / warm-up ────────────────────────────────────────────────

    /// <summary>
    /// The constructor must pre-register the <see cref="TimePulseDescriptor"/> event
    /// type so that <see cref="FdpEventBus.Publish{T}"/> succeeds immediately
    /// after construction (no warm-up publish required).
    /// </summary>
    [Fact]
    public void Constructor_WithNullParticipant_PreRegistersTimePulseTypeOnBus()
    {
        var eventBus = new FdpEventBus();

        _ = new TimePulseIngressTranslator(null, eventBus);

        // Publish must not throw InvalidOperationException ("type not registered")
        var pulse = new TimePulseDescriptor { MasterWallTicks = 1L, SequenceId = 1L };
        var ex = Record.Exception(() => eventBus.Publish(pulse));
        Assert.Null(ex);
    }

    // ── Bridge mapping — no skipping ─────────────────────────────────────────

    /// <summary>
    /// Verifies the exact bus-bridge path used by
    /// <see cref="TimePulseIngressTranslator.PollIngress"/>:
    ///   eventBus.Publish(sample.Data) → SwapBuffers → HasEvent&lt;TimePulseDescriptor&gt;
    ///
    /// If this assertion fails, <c>SlaveTimeController</c> would never receive
    /// pulses and IG simulation time would remain frozen.
    /// </summary>
    [Fact]
    public void EventBus_AfterPublishAndSwap_HasTimePulseEvent()
    {
        var eventBus = new FdpEventBus();
        _ = new TimePulseIngressTranslator(null, eventBus); // registers event type

        var pulse = new TimePulseDescriptor
        {
            MasterWallTicks = 1_234_567_890L,
            SimTimeSnapshot = 42.5,
            TimeScale       = 1.0f,
            SequenceId      = 7L,
        };

        eventBus.Publish(pulse);
        eventBus.SwapBuffers();

        Assert.True(eventBus.HasEvent<TimePulseDescriptor>(),
            "TimePulse event must be visible in the read buffer after SwapBuffers; " +
            "SlaveTimeController relies on HasEvent before consuming.");
    }

    /// <summary>
    /// After Publish → SwapBuffers, the event is present and the payload fields are
    /// unchanged. Validates the full read-back cycle that <c>SlaveTimeController</c> relies on.
    /// </summary>
    [Fact]
    public void EventBus_ConsumedPulse_FieldsMatchPublished()
    {
        var eventBus = new FdpEventBus();
        _ = new TimePulseIngressTranslator(null, eventBus);

        var expected = new TimePulseDescriptor
        {
            MasterWallTicks = 9_876_543_210L,
            SimTimeSnapshot = 100.25,
            TimeScale       = 2.0f,
            SequenceId      = 42L,
        };

        eventBus.Publish(expected);
        eventBus.SwapBuffers();

        // The event must be visible to SlaveTimeController (HasEvent is the entry guard)
        Assert.True(eventBus.HasEvent<TimePulseDescriptor>(),
            "TimePulse event must survive Publish → SwapBuffers and be visible via HasEvent");

        // Read back via FdpEventBus.Consume<T> (the bus's own read path)
        var events = eventBus.Consume<TimePulseDescriptor>();
        Assert.Equal(1, events.Length);
        Assert.Equal(expected.MasterWallTicks, events[0].MasterWallTicks);
        Assert.Equal(expected.TimeScale,       events[0].TimeScale);
        Assert.Equal(expected.SequenceId,      events[0].SequenceId);
    }

    // ── Test-mode safety ─────────────────────────────────────────────────────

    /// <summary>
    /// PollIngress with a null participant must be a no-op: no events emitted,
    /// no exceptions thrown.
    /// </summary>
    [Fact]
    public void PollIngress_WithNullParticipant_IsNoOpAndDoesNotEmitEvents()
    {
        var eventBus   = new FdpEventBus();
        var translator = new TimePulseIngressTranslator(null, eventBus);

        var repo = new EntityRepository();
        ISimulationView      view = repo;
        IEntityCommandBuffer cmd  = view.GetCommandBuffer();

        translator.PollIngress(cmd, view);

        eventBus.SwapBuffers();
        Assert.False(eventBus.HasEvent<TimePulseDescriptor>(),
            "No pulse should be emitted when PollIngress runs in test-mode (null DDS reader).");
    }
}
