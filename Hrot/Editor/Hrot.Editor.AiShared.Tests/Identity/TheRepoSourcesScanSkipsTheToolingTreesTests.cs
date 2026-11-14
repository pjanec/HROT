using System;
using System.IO;
using System.Linq;
using Fdp.Testing;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// ⭐ <c>CE-1366</c> — the ONE repo-wide source scan (<see cref="RepoSources"/>) never lists a file under <c>.claude/worktrees/…</c> (an agent worktree is a FULL copy of the tree: a scan that
/// descended into it listed every file twice), <c>obj/</c>, <c>bin/</c> or <c>.git/</c>, at any depth. 🔴 Red-proof: drop <c>".claude"</c> from <see cref="RepoSources.SkippedDirectories"/> ⇒ red.
/// </summary>
public sealed class TheRepoSourcesScanSkipsTheToolingTreesTests
{
    [Fact]
    public void CsFiles_SkipsAgentWorktrees_ObjBinAndGit_AtAnyDepth()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo-sources-" + Guid.NewGuid().ToString("N"));
        try
        {
            void Plant(string rel) { var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "class X {}"); }
            Plant("Hrot/Real/A.cs");
            Plant("Hrot/Real/B.json");
            Plant(".claude/worktrees/x/Hrot/Real/A.cs");          // the planted worktree copy
            Plant("Hrot/Real/obj/Debug/Gen.cs");
            Plant("Hrot/Real/bin/Debug/Gen.cs");
            Plant(".git/hooks/Gen.cs");
            Plant("Hrot/Other/node_modules/Gen.cs");

            var found = RepoSources.CsFiles(root).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "Hrot/Other/node_modules/Gen.cs", "Hrot/Real/A.cs" }, found);   // node_modules is NOT a default skip …

            var more = RepoSources.CsFiles(root, "node_modules").Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToArray();
            Assert.Equal(new[] { "Hrot/Real/A.cs" }, more);                                      // … a caller asks for it

            Assert.Equal(new[] { "Hrot/Real/B.json" }, RepoSources.Files(root, "*.json").Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
