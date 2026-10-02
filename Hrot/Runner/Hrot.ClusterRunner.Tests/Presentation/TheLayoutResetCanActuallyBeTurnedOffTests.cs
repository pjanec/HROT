using System;
using System.IO;
using System.Linq;
using CommandLine;
using Hrot.ClusterRunner.Configuration;
using Xunit;

namespace Hrot.ClusterRunner.Tests.Presentation;

/// <summary>
/// ⭐⭐⭐ <b>A DESTRUCTIVE DEFAULT MUST HAVE A WORKING WAY OFF.</b>
///
/// <para>📌 <c>103a</c> ships <c>--reset-layout</c> defaulting to <b>ON</b>, which force-overwrites the
/// user's own window arrangement on every run. 🔒 That default is the user's own ruling and is not in
/// question — ⛔ <b>but an opt-out that does not work turns a deliberate default into an unconditional
/// one</b>, and the user's stated reason for shipping their tuned layout was <i>"i would not like to
/// lose it."</i></para>
///
/// <para>🔴🔴 <b>Both documented escape hatches were broken, in DIFFERENT directions</b> — measured
/// <c>2026-08-21</c> against <c>CommandLineParser 2.9.1</c> with the shipped <c>bool</c> option:
/// <list type="bullet">
///   <item><c>--no-reset-layout</c> — the spelling the STARTUP LOG told the user to use — answers
///   <b><c>UnknownOptionError</c></b>. ⛔ The runner does not start. <c>CommandLineParser</c> has no
///   negation form; the design document names one that never existed.</item>
///   <item><c>--reset-layout=false</c> — the spelling the <c>HelpText</c> documents — parses cleanly
///   and leaves the value <b><c>true</c></b>. ⛔⛔ <b>Silent</b>: a plain <c>bool</c> is a SWITCH, so
///   its <c>=false</c> is discarded.</item>
/// </list>
/// ⇒ ⚠⚠ <b>a user following either instruction still loses their layout</b>, one loudly and one without
/// a word.</para>
///
/// <para>⭐⭐ <b>The fix is the TYPE:</b> <c>bool?</c> makes the option take a value instead of being a
/// switch. ⛔ These rails exist because that is a claim about a THIRD-PARTY PARSER — 📌 exactly the kind
/// of claim a comment cannot hold, and one that was already gotten wrong by reading instead of
/// running.</para>
/// </summary>
public sealed class TheLayoutResetCanActuallyBeTurnedOffTests
{
    /// <summary>⭐ The real production option class, through a real parser. ⛔ Nothing here is a stand-in.
    /// ⚠ <c>HelpWriter = null</c> only stops the parser printing usage into the test output.
    ///
    /// <para>⚠⚠ <c>-m editor</c> is prepended because <c>--mode</c> is <c>Required</c> — ⛔ without it
    /// EVERY parse answers <c>MissingRequiredOptionError</c>, and a rail written with <c>?? true</c>
    /// defaults would then have passed while never reaching the option under test. 📌 Found by this
    /// rail's own first run, which is the only reason it is stated here.</para></summary>
    private static HrotRunnerConfiguration? Parse(params string[] args)
    {
        HrotRunnerConfiguration? parsed = null;
        new Parser(with => with.HelpWriter = null)
            .ParseArguments<HrotRunnerConfiguration>(new[] { "-m", "editor" }.Concat(args).ToArray())
            .WithParsed(c => parsed = c);
        return parsed;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ONE THAT MATTERS.</b> ⛔ If this goes red, the destructive default is unconditional
    /// again and the user silently loses their layout on every run.
    /// </summary>
    [Theory]
    [InlineData("--reset-layout=false")]
    [InlineData("--reset-layout", "false")]
    public void TheDocumentedOptOutActuallyTurnsTheResetOff(params string[] args)
    {
        var config = Parse(args);

        Assert.True(config != null,
            $"'{string.Join(' ', args)}' did not parse at all — the runner would refuse to start.");
        Assert.False(config!.ResetLayout ?? true,
            $"'{string.Join(' ', args)}' parsed, but the reset stayed ON. ⛔ This is the SILENT half of "
          + "the defect: a plain `bool` [Option] is a switch, so `=false` is discarded. The option must "
          + "be `bool?` so it takes a VALUE.");
    }

    /// <summary>⭐ The default is unchanged by the fix — 🔒 the user's ruling is that it is ON.
    /// ⛔ Asserted on the PARSED object, not through a <c>?? true</c> fallback that a failed parse would
    /// satisfy.</summary>
    [Fact]
    public void WithNoArgumentsTheResetIsStillOn()
    {
        var config = Parse();
        Assert.NotNull(config);
        Assert.True(config!.ResetLayout ?? true, "the shipped default must stay ON.");
    }

    /// <summary>⭐ And it can still be asked for explicitly, so a script can be unambiguous.</summary>
    [Fact]
    public void ItCanBeTurnedOnExplicitly()
    {
        var config = Parse("--reset-layout=true");
        Assert.NotNull(config);
        Assert.True(config!.ResetLayout);
    }

    /// <summary>
    /// ⚠⚠ <b>Documents the spelling that DOES NOT EXIST</b>, so nobody re-adds it to a help string.
    /// ⛔ This rail asserts a LIMITATION, not a feature: <c>CommandLineParser</c> has no <c>--no-x</c>
    /// form. ⭐ If a future parser upgrade gains one, this goes red and the message below is the fix
    /// instruction, not a bug report.
    /// </summary>
    [Fact]
    public void TheNegatedSpellingIsNotAThing()
        => Assert.True(Parse("--no-reset-layout") == null,
            "`--no-reset-layout` now parses. If the parser gained a negation form, say so here and in "
          + "HrotRunnerConfiguration — until then it is an UnknownOptionError that stops the runner.");

    /// <summary>
    /// ⭐⭐ <b>The defect that actually reached the user was a STRING, not a type.</b> The startup log
    /// told them to use a flag that stops the runner. ⇒ ⛔ no production source may name it.
    ///
    /// <para>⚠ A source-text rail, and weak on purpose — 📌 it cannot prove a message is CORRECT, only
    /// that this one wrong spelling is gone. ⭐ The behavioural rails above carry the real claim.</para>
    /// </summary>
    [Fact]
    public void NoProductionSourceTellsTheUserToUseTheNegatedSpelling()
    {
        var root = RepoRoot();
        if (root is null) return;

        var offenders = new[] { "Hrot", "FDP" }
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                    StringComparison.Ordinal))
            .Where(f => !f.EndsWith(nameof(TheLayoutResetCanActuallyBeTurnedOffTests) + ".cs",
                                    StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("--no-reset-layout", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files name `--no-reset-layout`, which the parser rejects with UnknownOptionError:\n"
          + string.Join("\n", offenders.Select(o => "    " + o))
          + "\n\n  Use `--reset-layout=false`.");
    }

    private static string? RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "layout", "default"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
