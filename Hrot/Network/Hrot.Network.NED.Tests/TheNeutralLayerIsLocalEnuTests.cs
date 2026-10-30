using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CycloneDDS.Runtime;
using Hrot.Core.Network;
using Hrot.Network.NED.ExCon;
using Hrot.Network.NED.IG;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-4073</c> (AQ107 B's tail, R-4092) rail (c) — the ExCon / IG NEUTRAL layer carries LOCAL ENU METRES, unchanged, end to end.</b>
///
/// <para>🔴 The neutral DTOs (<c>MapClickEventDto</c>, <c>CreateEntityCommand</c>, the route waypoints, the move-update JSON) used to hold a
/// geodetic latitude / longitude / altitude and converted to the wire's local ENU at a boundary
/// (<c>NeutralGeoBoundary</c>, deleted), through a transform that was <c>null</c> on a host with no terrain — where the offline
/// convention (X = longitude, Y = latitude) applied silently. ⭐ Now there is NO conversion: what the console / IG puts in
/// the DTO is the wire's <c>Vec3d</c>, a copy. ⇒ these rails use values no geodetic conversion could return unchanged
/// (thousands of metres: a latitude cannot exceed 90), so a re-introduced conversion — or a swapped axis — reddens every one.</para>
///
/// <para>⭐ The pure halves call the translation helper directly; the DDS halves run the REAL adapters over a real participant (the
/// writer side is the IG / ExCon adapter, the reader side the ingress handler or a raw reader of the topic the wire carries).</para>
///
/// <para>📌 A new class, not a fold-in: the NED neutral adapters had NO suite (measured at the base commit — the only tests naming
/// <c>NedIgNetworkAdapter</c> / <c>NedTranslationHelper</c> were the IG's draw-route test with a mock adapter).</para>
/// </summary>
public class TheNeutralLayerIsLocalEnuTests
{
    // Not a latitude, not a longitude, not an altitude of anywhere on Earth — only a local metre survives a copy unchanged.
    private const double X = 12345.678;
    private const double Y = -9876.543;
    private const double Z = 33.25;

    private const int DomainA = 228;
    private const int DomainB = 229;

    // ══ the pure halves ═══════════════════════════════════════════════════════════

    [Fact]
    public void ACreateEntityCommand_BecomesAWorldPosDescriptor_WithTheSameMetres()
    {
        var descriptors = NedTranslationHelper.BuildCreateEntityDescriptors(
            new CreateEntityCommand { TkbType = 105, X = X, Y = Y, Z = Z });

        var wp = Assert.Single(descriptors, d => d._d == EDescriptorType.dtWorldPos).WorldPos.Position;
        Assert.Equal(X, wp.X);
        Assert.Equal(Y, wp.Y);
        Assert.Equal(Z, wp.Z);
    }

    [Fact]
    public void TheGatewayCreateRequest_CarriesTheSameMetres()
    {
        var req = NedTranslationHelper.ToCreateEntityRequest(
            new CreateEntityCommand { TkbType = 105, X = X, Y = Y, Z = Z });

        var wp = Assert.Single(req.InitialDescriptors, d => d._d == EDescriptorType.dtWorldPos).WorldPos.Position;
        Assert.Equal((X, Y, Z), (wp.X, wp.Y, wp.Z));
    }

    [Fact]
    public void AMoveUpdateJson_WithXYZ_BecomesAWorldPosPayload_WithTheSameMetres()
    {
        // ⭐ The shape IgApplication.SendGeoSpatialUpdate sends: keys x / y / z (they were lat / lon / alt).
        var json = System.Text.Json.JsonSerializer.Serialize(new { type = "WorldPos", entityId = 7, x = X, y = Y, z = Z });

        var req = NedTranslationHelper.ToUpdateDescriptorRequest(
            new UpdateEntityDescriptorCommand { EntityId = 7, DescriptorJson = json });

        Assert.Equal(EDescriptorType.dtWorldPos, req.Payload._d);
        Assert.Equal((X, Y, Z), (req.Payload.WorldPos.Position.X, req.Payload.WorldPos.Position.Y, req.Payload.WorldPos.Position.Z));
    }

    // ══ the real adapters over a real participant ═════════════════════════════════

    private static async Task<T?> WaitFor<T>(Func<T?> poll, int timeoutMs = 8000) where T : class
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            var got = poll();
            if (got != null) return got;
            await Task.Delay(50);
        }
        return null;
    }



}
