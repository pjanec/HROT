using System.Globalization;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Osm.Roads;

/// <summary>
/// ⭐ The OSM file → <c>roads/roads.net.xml</c> through SUMO's netconvert (the shared <see cref="Netconvert"/> locator, SUMO 1.20.0) —
/// Terrain From OSM §5.4 / F-7: an EXPLICIT projection (the frame's zone and hemisphere) and an offset of (−e0, −n0) with normalisation off,
/// so net coordinates ARE the terrain's local metres (manifest <c>sumoAnchor</c> = [0, 0]); edges kept inside the local bounds.
/// </summary>
public sealed class NetconvertRoadBuilder : IRoadBuilder
{
    private readonly double _minX, _minY, _maxX, _maxY;

    /// <param name="bounds">the terrain's local bounds (m); edges outside are dropped</param>
    public NetconvertRoadBuilder(double minX, double minY, double maxX, double maxY)
    {
        _minX = minX; _minY = minY; _maxX = maxX; _maxY = maxY;
    }

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The netconvert arguments — public so a test can pin the projection.</summary>
    public IReadOnlyList<string> Arguments(string osmFile, UtmFrame frame, string netFile) => new[]
    {
        "--osm-files", osmFile,
        "--proj", $"+proj=utm +zone={frame.Zone.ToString(CultureInfo.InvariantCulture)}{(frame.South ? " +south" : "")} +datum=WGS84 +units=m +no_defs",
        "--offset.disable-normalization", "true",
        "--offset.x", N(-frame.E0), "--offset.y", N(-frame.N0),
        "--keep-edges.in-boundary", $"{N(_minX)},{N(_minY)},{N(_maxX)},{N(_maxY)}",
        // the cleanup SUMO's own OSM import recommends (tools/osmBuild.py): simplified geometry, guessed ramps and signals, joined junctions
        "--geometry.remove", "--ramps.guess", "--junctions.join", "--tls.guess-signals", "--tls.discard-simple", "--tls.join",
        "--output.street-names", "--output.original-names",
        "--no-warnings",
        "-o", netFile,
    };

    public Task BuildAsync(string osmFile, UtmFrame frame, string roadsFolder, CancellationToken cancellation)
    {
        var found = Netconvert.Locate();
        if (found.Problem != null || found.Path == null) throw new InvalidOperationException(found.Problem);
        Directory.CreateDirectory(roadsFolder);
        string net = Path.Combine(roadsFolder, "roads.net.xml");
        cancellation.ThrowIfCancellationRequested();
        var (code, _, err) = Netconvert.Run(found.Path, Arguments(Path.GetFullPath(osmFile), frame, net), roadsFolder);
        if (code != 0 || !File.Exists(net))
            throw new InvalidOperationException("netconvert failed (exit " + code.ToString(CultureInfo.InvariantCulture) + "): " +
                                                err.Trim().Replace("\r", "").Replace("\n", " | "));
        Netconvert.StripGeneratedHeader(net);
        return Task.CompletedTask;
    }
}
