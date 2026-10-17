using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐ Every file a SHIPPED terrain's <c>terrain.json</c> names must be in the build output beside it.
///
/// <para>📌 Measured <c>2026-10-17</c> on a five-process cluster: <c>demo-city</c>'s <c>terrain.json</c> names
/// <c>demo-city.net.xml</c> (AQ93 §11.3), but the <c>Recipes/Terrain</c> content glob in
/// <c>Hrot.AI.Behaviors.csproj</c> copied only <c>*.json</c>/<c>*.geojson</c>/<c>*.asc</c> ⇒ every node's PrepareLive
/// threw <c>FileNotFoundException</c> and the load never reached <c>OperatingLive</c>. ⛔ The editor and the unit
/// suites read the source tree, so nothing caught it.</para>
///
/// <para>⭐ This test project's output carries the shipped terrains (Content flows from <c>Hrot.AI.Behaviors</c>
/// through <c>Hrot.ClusterRunner</c>), so the rail reads exactly what a cluster node reads. A file reference is
/// any string value shaped like a relative file name (<c>name.ext</c>), outside <c>$</c>-prefixed keys.</para>
/// </summary>
public sealed class ShippedTerrainFilesAreDeployedTests
{
    private static readonly Regex FileName = new(@"^[\w.\-/]+\.[A-Za-z][A-Za-z0-9]{1,7}$", RegexOptions.Compiled);

    private static string ShippedRoot => Path.Combine(AppContext.BaseDirectory, "Recipes", "Terrain");

    [Fact]
    public void EveryShippedTerrain_HasEveryFileItNames_InTheBuildOutput()
    {
        Assert.True(Directory.Exists(ShippedRoot), $"no shipped terrains under '{ShippedRoot}' — the rail would prove nothing.");

        var definitions = Directory.GetFiles(ShippedRoot, "terrain.json", SearchOption.AllDirectories);
        Assert.NotEmpty(definitions);

        var missing = new List<string>();
        foreach (var definition in definitions)
        {
            var folder = Path.GetDirectoryName(definition)!;
            using var doc = JsonDocument.Parse(File.ReadAllText(definition));
            foreach (var name in FileReferences(doc.RootElement))
                if (!File.Exists(Path.Combine(folder, name)))
                    missing.Add($"{Path.GetFileName(folder)}/{name}");
        }

        Assert.True(missing.Count == 0,
            "shipped terrain files named in terrain.json but not deployed (add the extension to the Recipes/Terrain "
          + "content glob in Hrot.AI.Behaviors.csproj): " + string.Join(", ", missing));
    }

    /// <summary>⭐ The rail can fail: the known SUMO net reference is found by the walk.</summary>
    [Fact]
    public void TheWalk_FindsTheSumoNetReference()
    {
        using var doc = JsonDocument.Parse("""{"$comment":"x.y","world":"a.world.geojson","sumoNet":{"file":"a.net.xml","anchor":[1.0,2.0]}}""");
        Assert.Equal(new[] { "a.world.geojson", "a.net.xml" }, FileReferences(doc.RootElement).ToArray());
    }

    private static IEnumerable<string> FileReferences(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    if (p.Name.StartsWith('$')) continue;
                    foreach (var f in FileReferences(p.Value)) yield return f;
                }
                break;
            case JsonValueKind.Array:
                foreach (var e in element.EnumerateArray())
                    foreach (var f in FileReferences(e)) yield return f;
                break;
            case JsonValueKind.String:
                var s = element.GetString()!;
                if (FileName.IsMatch(s)) yield return s;
                break;
        }
    }
}
