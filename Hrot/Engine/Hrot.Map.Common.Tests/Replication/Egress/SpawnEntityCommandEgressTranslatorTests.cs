using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.IG.Components;
using Fdp.Kernel;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Map.Common.Tests.Replication.Egress;

/// <summary>
/// Standalone unit tests for <see cref="SpawnEntityCommandEgressTranslator"/> (DEBT-05 / PACK3-A005).
/// Uses the internal testable constructor (accessible via InternalsVisibleTo from
/// <c>Hrot.Map.Common.csproj</c>).
/// </summary>
public class SpawnEntityCommandEgressTranslatorTests
{
    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class CapturingWriter<T> : IDdsWriter<T>
    {
        public List<T> Publishes { get; } = new();
        public void Write(T sample) => Publishes.Add(sample);
        public void DisposeInstance(T key) { }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Standard path: publishing a <see cref="SpawnEntityCommand"/> results in exactly
    /// one <see cref="CreateEntityRequest"/> whose <c>RequestId</c> matches the command.
    /// </summary>
    [Fact]
    public void SpawnEntityCommand_IsConsumed_WritesOneCreateEntityRequest_WithMatchingRequestId()
    {
        var bus        = new FdpEventBus();
        var writer     = new CapturingWriter<CreateEntityRequest>();
        var translator = new SpawnEntityCommandEgressTranslator(writer, bus, geoTransform: null);

        var requestId = Guid.NewGuid();
        bus.PublishManaged(new SpawnEntityCommand { TkbType = 1L, RequestId = requestId });
        bus.SwapBuffers();

        translator.PollIngress(null!, null!);

        Assert.Equal(1, writer.Publishes.Count);
        Assert.Equal(requestId, writer.Publishes[0].RequestId);
    }

    /// <summary>
    /// PACK3-A005 Test 1 — Boundary unit test:
    /// Translator synthesises correct <c>dtMapVisualOverlay</c> DDS payload strictly
    /// from domain events — no delegate required.
    /// </summary>
    [Fact]
    public void EgressTranslator_SynthesizesDdsPayload_StrictlyFromDomainEvent()
    {
        var bus    = new FdpEventBus();
        var writer = new CapturingWriter<CreateEntityRequest>();

        // No delegate — translator must produce the descriptor from InitialComponents only.
        var translator = new SpawnEntityCommandEgressTranslator(writer, bus, geoTransform: null);

        bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType           = 1001L,
            RequestId         = Guid.NewGuid(),
            InitialComponents = new List<object>
            {
                new EditablePolyline { Points = new List<Vector2> { new Vector2(10f, 10f) } },
                new MapOverlayStyle  { FillR  = 255 },
            },
        });
        bus.SwapBuffers();

        translator.PollIngress(null!, null!);

        // Exactly one request must be written.
        Assert.Equal(1, writer.Publishes.Count);

        var descriptors = writer.Publishes[0].InitialDescriptors;
        Assert.NotNull(descriptors);

        // Must contain a dtMapVisualOverlay descriptor.
        var overlayDesc = descriptors!.FirstOrDefault(d => d._d == EDescriptorType.dtMapVisualOverlay);
        Assert.NotEqual(default, overlayDesc);

        // The overlay must carry exactly one GeoPoint (matching the EditablePolyline).
        Assert.NotNull(overlayDesc.MapVisualOverlay.Points);
        Assert.Equal(1, overlayDesc.MapVisualOverlay.Points!.Count);
    }
}
