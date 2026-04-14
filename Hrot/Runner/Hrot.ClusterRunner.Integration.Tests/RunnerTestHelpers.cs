using System;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Shared helpers reused across runner integration test classes.
/// Extracted from MapPlacementIntegrationTests, AreaAuthoringIntegrationTests,
/// MiniExConIntegrationTests, and SpawnMovingVehicleWithGatewayIntegrationTests.
/// </summary>
internal static class RunnerTestHelpers
{
    /// <summary>
    /// Attempts to take a <see cref="CreateUpdateDeleteEntityAck"/> from <paramref name="reader"/>
    /// that matches <paramref name="requestId"/>.
    ///
    /// <para>Returns <c>false</c> (letting <c>PumpUntil</c> retry) when:</para>
    /// <list type="bullet">
    ///   <item>No valid sample is available.</item>
    ///   <item>The available sample does not match <paramref name="requestId"/>.</item>
    ///   <item>The sample is a Phase-1 <see cref="NedStatusCode.InProgress"/> intermediate ACK —
    ///         callers must wait for the terminal Success / Error ACK.</item>
    /// </list>
    /// </summary>
    public static bool TryTakeCreateAck(
        DdsReader<CreateUpdateDeleteEntityAck> reader,
        Guid requestId,
        out CreateUpdateDeleteEntityAck ack)
    {
        using var loan = reader.Take(1);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var data = sample.Data;
            if (data.RequestId != requestId) continue;

            // Skip Phase-1 InProgress ACKs — let PumpUntil retry until the
            // terminal Success/Error ACK arrives.
            if (data.StatusCode == (int)NedStatusCode.InProgress)
            {
                ack = default;
                return false;
            }

            ack = data;
            return true;
        }

        ack = default;
        return false;
    }
}
