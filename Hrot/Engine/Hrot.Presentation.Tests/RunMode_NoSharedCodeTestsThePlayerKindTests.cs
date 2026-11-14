using Fdp.Testing;
using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ CE-4149 (DESIGN_Player_Join §7a B1/B2) — the Player's gate policy is composed by the Player host, so NO shared code (<c>Hrot/Engine/**</c>, production) may
/// test <c>RunHostKind.Ig</c> (a comparison, a switch arm, a pattern). The enum value itself stays (the host names its kind); the code that behaves differently for
/// a Player reads the composed <c>HostGatePolicy</c>. Red-proof: put <c>hostKind == RunHostKind.Ig ? … </c> back into <c>HostGatePolicies.For</c> ⇒ this fails.
/// </summary>
public sealed class RunMode_NoSharedCodeTestsThePlayerKindTests
{
    [Fact]
    public void NoProductionCodeUnderHrotEngine_TestsRunHostKindPlayer()
    {
        var root = Path.Combine(TestRepo.Root, "Hrot", "Engine");
        var offenders = new List<string>();
        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(TestRepo.Root, f).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains(".Tests")) continue;
            foreach (var (line, i) in File.ReadLines(f).Select((l, i) => (l, i + 1)))
            {
                var t = line.TrimStart();
                if (t.StartsWith("//")) continue;
                var code = Regex.Replace(line, @"//.*$", "");
                if (Regex.IsMatch(code, @"RunHostKind\.Player\b") && !Regex.IsMatch(code, @"^\s*///")) offenders.Add($"{rel}:{i}");
            }
        }
        Assert.True(offenders.Count == 0, "shared code tests the Player kind: " + string.Join(", ", offenders));
    }
}
