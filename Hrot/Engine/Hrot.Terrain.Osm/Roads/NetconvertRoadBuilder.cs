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

    /// <summary>⭐ CE-4409 — the child's <c>SUMO_HOME</c>: the located install's home (a rail pins it).</summary>
    public static string? SumoHome(string netconvertPath) => Netconvert.SumoHomeOf(netconvertPath);

    public Task BuildAsync(string osmFile, UtmFrame frame, string roadsFolder, CancellationToken cancellation)
    {
        var found = Netconvert.Locate();
        if (found.Problem != null || found.Path == null) throw new InvalidOperationException(found.Problem);
        Directory.CreateDirectory(roadsFolder);
        string net = Path.Combine(roadsFolder, "roads.net.xml");
        cancellation.ThrowIfCancellationRequested();
        var (code, outText, err) = Netconvert.Run(found.Path, Arguments(Path.GetFullPath(osmFile), frame, net), roadsFolder, SumoHome(found.Path));
        if (code != 0 || !File.Exists(net))
        {
            string said = (err.Trim().Length > 0 ? err : outText).Trim().Replace("\r", "").Replace("\n", " | ");   // ⭐ CE-4409 — what netconvert said, whichever stream it used
            throw new InvalidOperationException("netconvert failed (exit " + code.ToString(CultureInfo.InvariantCulture) + "): " + said);
        }
        Netconvert.StripGeneratedHeader(net);
        return Task.CompletedTask;
    }
}
