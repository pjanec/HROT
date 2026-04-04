using System;
using System.Collections.Generic;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Services;
using Hrot.NED.Messages;

namespace Hrot.Map.Common.Tests.Replication.Egress;

/// <summary>
/// Standalone unit tests for <see cref="SpawnEntityCommandEgressTranslator"/> (DEBT-05).
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
    /// Side-channel path: when the <c>tryGetPrebuilt</c> delegate returns a pre-built
    /// <see cref="CreateEntityRequest"/>, that request is written verbatim (not a newly
    /// constructed one). The prebuilt's <c>Flags</c> (set to a sentinel value) must be
    /// preserved rather than replaced by the standard-path default of 0.
    /// </summary>
    [Fact]
    public void SpawnEntityCommand_WithPrebuilt_WritesPrebuiltRequest_NotNewlyBuiltOne()
    {
        var bus    = new FdpEventBus();
        var writer = new CapturingWriter<CreateEntityRequest>();

        // The standard path always builds with Flags = 0; set a sentinel value here
        // so we can confirm the prebuilt is forwarded verbatim.
        var prebuiltRequestId = Guid.NewGuid();
        var prebuilt = new CreateEntityRequest { Flags = 77L, RequestId = prebuiltRequestId };

        var translator = new SpawnEntityCommandEgressTranslator(
            writer, bus, geoTransform: null,
            tryGetPrebuilt: _ => prebuilt);

        // Command has a DIFFERENT RequestId — if the standard path ran, RequestId would differ.
        bus.PublishManaged(new SpawnEntityCommand { TkbType = 1L, RequestId = Guid.NewGuid() });
        bus.SwapBuffers();

        translator.PollIngress(null!, null!);

        Assert.Equal(1, writer.Publishes.Count);
        // Confirm prebuilt was forwarded: Flags = 77 (not the standard-path default of 0)
        Assert.Equal(77L, writer.Publishes[0].Flags);
        Assert.Equal(prebuiltRequestId, writer.Publishes[0].RequestId);
    }
}
