using System.Text.RegularExpressions;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// <b>U-11 — the assertion <c>U-12</c> is about to bet on.</b>
///
/// <para>
/// ⭐⭐ <b><c>U-12</c> deletes <c>BlueprintAsset.Parameters</c> / <c>.WorkingState</c> /
/// <c>.Variables</c> on the strength of *"nothing reads them any more."* ⛔ If that is a belief rather
/// than a checked fact, <c>U-12</c> is the batch that finds out.</b> A grep is the only thing that can
/// say so, so the grep is a test.
/// </para>
///
/// <para>
/// ⚠ <b>Deliberately scoped to the three DECLARATION lists.</b> The three <c>*Order</c> lists are a
/// separate question — they are display metadata, they survive the store flip, and unifying them is
/// <c>U-12</c>'s call (see <c>BlueprintSchemaV2</c>'s note on why v2 keeps them per-kind).
/// </para>
///
/// <para>
/// ⚠⚠ <b>And deliberately scoped to the ASSET model.</b> <c>IrAsset</c> carries three same-named lists
/// of <c>IrField</c> — the <b>emitted</b> fields, which set the struct offsets and feed
/// <c>StructureHash</c>. ⛔ Sweeping those would move the hash and re-initialise every deployed
/// entity's blackboard. Batch 50 measured ~31 such references; they are not <c>U-11</c> sites and this
/// test must not drag them in.
/// </para>
/// </summary>
public sealed class ViewsAreUnreadTests
{
    private static readonly Regex ListRead = new(
        @"(?<recv>[A-Za-z_][A-Za-z0-9_]*)\.(?<member>Parameters|WorkingState|Variables)\b",
        RegexOptions.Compiled);

    /// <summary>Receivers that denote a <c>BlueprintAsset</c> in this tree.</summary>
    private static readonly HashSet<string> AssetReceivers = new(StringComparer.Ordinal)
    {
        "asset", "_asset", "Asset", "_parent", "targetAsset", "hostAsset",
    };

    private static string ResolveDir(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(string.Join("/", parts));
    }

    private static IEnumerable<(string File, int Line, string Text)> Offenders(string root)
    {
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                                      .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                               && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                                      .OrderBy(f => f, StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;   // prose, not a read

                foreach (Match m in ListRead.Matches(lines[i]))
                    if (AssetReceivers.Contains(m.Groups["recv"].Value))
                        yield return (Path.GetFileName(file), i + 1, trimmed);
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <b>Nothing under <c>Hrot.Blueprints.Editor</c> reads the three declaration lists.</b>
    /// </summary>
    [Fact]
    public void TheEditorReadsNoDeclarationListDirectly()
    {
        var offenders = Offenders(ResolveDir("Hrot", "Subsystems", "Blueprints", "Hrot.Blueprints.Editor")).ToList();

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} editor site(s) still read a declaration list directly — U-12 cannot "
            + "delete the views while these exist:\n  "
            + string.Join("\n  ", offenders.Select(o => $"{o.File}:{o.Line}  {o.Text}")));
    }

    /// <summary>
    /// ⭐ The compiler's <b>stages</b> too (Batch 50). ⚠ <c>Emit/</c> and <c>Lowering/</c> are excluded
    /// by construction: those receivers are <c>IrAsset</c>, and this test would be asserting the wrong
    /// thing about them.
    /// </summary>
    [Fact]
    public void TheCompilerStagesReadNoDeclarationListDirectly()
    {
        var root      = ResolveDir("Hrot", "Subsystems", "Blueprints", "Hrot.Blueprints.Compiler", "Compiler", "Stages");
        var offenders = Offenders(root).ToList();

        Assert.True(offenders.Count == 0,
            "compiler stages still reading a declaration list directly:\n  "
            + string.Join("\n  ", offenders.Select(o => $"{o.File}:{o.Line}  {o.Text}")));
    }

    /// <summary>
    /// ⛔⛔ <b>The test must be able to FAIL.</b> A grep assertion that matches nothing looks identical
    /// to a grep assertion that is green, so the pattern is pointed somewhere a direct read genuinely
    /// still exists.
    ///
    /// <para>
    /// ⚠⚠ <b><c>U-12</c> moved the canary, and the move is itself the finding.</b> It used to point at
    /// <c>DeclarationList</c>, which read <c>_asset.Parameters</c> because the three lists were the
    /// storage. ⭐ <b>After the store flip <c>DeclarationList</c> reads the store instead, so the old
    /// canary went silent</b> — and this test is what noticed, which is exactly what it is for.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>It now points at the TEST tree, and that is the honest place for it.</b> The three
    /// properties survive the flip as public members because ~431 measured call sites — almost all of
    /// them assertions in this suite — read and write them. ⇒ the sentence this whole class states is
    /// *"the PRODUCT tree is clean, and the pattern still finds nearly two hundred reads next door."*
    /// ⛔ Not <c>Emit/</c>, which would match for the wrong reason: those receivers are <c>IrAsset</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePatternActuallyMatchesAKnownRead()
    {
        var root  = ResolveDir("Hrot", "Subsystems", "Blueprints", "Hrot.Blueprints.Tests");
        var found = Offenders(root).ToList();

        Assert.NotEmpty(found);
    }
}
