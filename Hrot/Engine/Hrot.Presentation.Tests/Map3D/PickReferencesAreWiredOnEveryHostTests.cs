using System.Text.RegularExpressions;
using Fdp.Testing;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-4206 R1 (the silent-default rule) — the live reference scope reaches the link leaves through the adapter's repository, so a production
/// <c>new CanvasMapPickAdapter(canvas, …)</c> that HOLDS a repository must pass it (its second argument, positional): a host that forgets draws the
/// plain link forms with no names, GUIDs or keys. The construction itself is rail-checked in <c>CE4206_TheBrokerCarriesTheLiveReferenceScope…</c>.
/// </summary>
public sealed class PickReferencesAreWiredOnEveryHostTests
{
    [Fact]
    public void EveryProductionCanvasMapPickAdapter_IsBuiltOverTheHostsRepository()
    {
        var found = new List<string>();
        foreach (var tree in new[] { "Hrot", "Stride" })
        {
            var dir = Path.Combine(TestRepo.Root, tree);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(TestRepo.Root, f).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains(".Tests")) continue;
                var code = string.Join('\n', File.ReadLines(f).Where(l => !l.TrimStart().StartsWith("//")));
                foreach (Match m in Regex.Matches(code, @"new\s+(?:[\w.]+\.)?CanvasMapPickAdapter\(\s*([^,()]+),\s*([^,()]*)"))
                    if (Regex.IsMatch(m.Groups[2].Value.Trim(), @"^\w+\s*:") || m.Groups[2].Value.Trim().Length == 0)
                        found.Add($"{rel}: second argument is '{m.Groups[2].Value.Trim()}' (no repository)");
                if (code.Contains("new CanvasMapPickAdapter(") || code.Contains(".CanvasMapPickAdapter(")) found.Add(rel + " (counted)");
            }
        }
        Assert.True(found.Count(x => x.EndsWith("(counted)")) >= 4, "IG, CGF, SimHost and the editor each build one: " + string.Join("; ", found));
        Assert.Empty(found.Where(x => !x.EndsWith("(counted)")));
    }
}
