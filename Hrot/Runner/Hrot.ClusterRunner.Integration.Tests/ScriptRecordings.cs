using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ <c>CE-3299</c> B4 / <c>D18</c> ③ (R-3047) — the exercise recorder never opens over an existing recording (a loud failure, where it
/// used to truncate). A test SCRIPT that names a fixed <c>ExerciseId</c> (it has to: it goes live with an id and later replays it)
/// would then fail on its second run in the same staging root. ⇒ before a script runs, the recordings of the exercises it names are
/// removed — they are this script's own scratch output, never anyone's data.
/// </summary>
internal static class ScriptRecordings
{
    /// <summary>Removes the staging root's recordings of every exercise <paramref name="scriptPath"/> names; returns the path.</summary>
    public static string Fresh(string scriptPath)
    {
        var root = OrchestrationConstants.ResolveStagingRoot();
        if (!Directory.Exists(root)) return scriptPath;
        foreach (var id in ExercisesNamedIn(File.ReadAllText(scriptPath)))
            // every node records under its OWN staging root (nodes/node-N/exercises/<id>/) as well as the shared one
            foreach (var dir in Directory.EnumerateDirectories(root, id.ToString(), SearchOption.AllDirectories).ToList())
                if (Path.GetFileName(Path.GetDirectoryName(dir)) == OrchestrationConstants.ExercisesDirectoryName)
                    Directory.Delete(dir, recursive: true);
        return scriptPath;
    }

    private static IEnumerable<Guid> ExercisesNamedIn(string json)
    {
        var found = new List<Guid>();
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        if (p.NameEquals("ExerciseId") && p.Value.ValueKind == JsonValueKind.String && Guid.TryParse(p.Value.GetString(), out var g))
                            found.Add(g);
                        else Walk(p.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) Walk(x);
                    break;
            }
        }
        using var doc = JsonDocument.Parse(json);
        Walk(doc.RootElement);
        return found;
    }
}
