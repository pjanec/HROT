using System;
using System.IO;
using System.Linq;
using Hrot.ClusterRunner;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ CE-4360 (the VERIFY half of "done when"; docs/DESIGN_Content_Layout.md §6) — <c>--verify all</c> lists every scenario that carries a <c>checks.json</c> at ANY depth under
/// <c>scenarios/</c>, each by its unchanged LEAF name, so filing a scenario into a sub-folder (CE-4361) does not drop it from the regression. 🔴 Mutation: <c>WithChecks</c> enumerating only the
/// top-level folders ⇒ the filed scenarios vanish from the list.
/// </summary>
public sealed class VerifyCommandScenariosTests
{
    [Fact]
    public void CE4360_VerifyAll_ListsTheScenariosWithChecks_AtAnyDepth_ByLeafName()
    {
        string root = Path.Combine(Path.GetTempPath(), "ce4360-verify-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var rel in new[] { "top-with", "top-without", "demos/air/filed-with", "demos/air/filed-without", "tests/perf/t-with" })
            {
                var dir = Path.Combine(root, rel);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "scenario.json"), "{}");
                if (rel.EndsWith("-with", StringComparison.Ordinal)) File.WriteAllText(Path.Combine(dir, "checks.json"), "{}");
            }
            Assert.Equal(new[] { "filed-with", "t-with", "top-with" }, VerifyCommand.WithChecks(root).ToArray());
        }
        finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }
    }
}
