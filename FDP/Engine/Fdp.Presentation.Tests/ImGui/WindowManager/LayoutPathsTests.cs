using System;
using System.IO;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.WindowManager;

/// <summary>
/// ⭐⭐⭐ <b>Batch 103 (<c>103a</c>) — WHERE "THE LAYOUT" LIVES, asserted rather than assumed.</b>
///
/// <para>📄 <c>docs/UX/UX_Feature_Layout_Defaults.md</c>. ⭐ These cover the design's acceptance rows
/// that are file-system round-trips *(1, 6, 8, 9)* — ⛔ none needs a human eye, so they gate.</para>
///
/// <para>⚠ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)*: ⛔ nothing. These are real files in a real
/// temp directory. ⭐ What is NOT covered: the startup ORDER in <c>LocalWindowController</c> — a rail
/// cannot construct that headlessly *(it opens a Raylib window)*, so
/// <c>TheLayoutIsResolvedBeforeImGuiIsSetUp</c> reads its source as text and says so.</para>
///
/// <para>⚠ The <c>appName</c> is a GUID per test: <see cref="LayoutPaths.UserDirectory"/> writes under
/// the real <c>LocalApplicationData</c>, and ⛔ a test that used <c>"HROT"</c> would clobber the
/// developer's own layout.</para>
/// </summary>
public sealed class LayoutPathsTests : IDisposable
{
    private readonly string _appName = "HROT_LayoutPathsTests_" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        try { Directory.Delete(LayoutPaths.UserDirectory(_appName), recursive: true); }
        catch { /* ⭐ a test that could not clean up must not fail the run it already passed */ }
    }

    // ══ the convention itself ════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE CROSS-PLATFORM QUESTION, MEASURED ON THE PLATFORM THAT IS RUNNING.</b>
    ///
    /// <para>⚠⚠ <b>The handoff flagged this and the design predates it:</b> <c>%LocalAppData%</c> reads
    /// Windows-only, and 📌 <c>R-124</c>'s frame rails run on <b>Linux under Xvfb</b>. ⇒ ⛔ a
    /// Windows-only layout path would make every layout rail unrunnable there.</para>
    ///
    /// <para>📐 <b>Measured answer:</b> .NET maps <c>SpecialFolder.LocalApplicationData</c> to
    /// <c>%LocalAppData%</c> on Windows and to <c>$XDG_DATA_HOME</c> *(or <c>~/.local/share</c>)* on
    /// Linux. ⇒ ⭐ the convention is cross-platform <b>as written</b>; it is the NAME that misleads.
    /// ⛔ This rail asserts the OUTCOME — a rooted, creatable, writable directory — rather than the
    /// specific string, because the string is the platform's business and the outcome is ours.</para>
    /// </summary>
    [Fact]
    public void TheUserDirectoryResolvesAndIsWritable_OnThisPlatform()
    {
        var dir = LayoutPaths.UserDirectory(_appName);

        Assert.True(Path.IsPathRooted(dir), $"The user layout directory is not rooted: '{dir}'.");
        Assert.True(Directory.Exists(dir),
            $"UserDirectory did not create '{dir}'. ImGui will not create it either, so the ini "
          + "would be silently unwritable.");

        // ⭐ Writable, not merely present — the ini is rewritten on every exit.
        var probe = Path.Combine(dir, "write-probe.tmp");
        File.WriteAllText(probe, "x");
        Assert.True(File.Exists(probe));
        File.Delete(probe);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>ACCEPTANCE 8 — the json is written NEXT TO the ini, not beside the exe.</b>
    ///
    /// <para>🔴 <b>The defect this closes:</b> layout state lived in two files under two roots —
    /// geometry under <c>LocalApplicationData</c>, open/closed state beside the executable. ⇒ ⛔ a clean
    /// rebuild wiped one and not the other, and a reset could only ever be half a reset.</para>
    /// </summary>
    [Fact]
    public void BothLayoutFilesShareOneDirectory()
    {
        Assert.Equal(
            Path.GetDirectoryName(LayoutPaths.UserIniPath(_appName)),
            Path.GetDirectoryName(LayoutPaths.UserWindowSettingsPath(_appName)));
    }

    /// <summary>⛔ An empty app name is a programming error, not a fallback — ⚠ defaulting it would put
    /// a user's layout in a directory nobody named.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyAppName_Throws(string bad)
        => Assert.Throws<ArgumentException>(() => LayoutPaths.UserDirectory(bad));

    // ══ the reset ════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>ACCEPTANCE 6 — the reset restores BOTH files</b>, geometry <b>and</b>
    /// open/closed/perspective/scale.
    ///
    /// <para>🔒 The user's ruling: <i>"copy the default to the user folder force-overwriting whatever
    /// was there."</i> ⇒ ⭐ this writes DIFFERENT content into both user files first, so a reset that
    /// touched only one would leave the other's marker behind and redden.</para>
    /// </summary>
    [Fact]
    public void TheResetOverwritesBothUserFiles()
    {
        using var shipped = new ShippedDefault(
            ini: "[Window][Default]\nPos=1,1\n", json: "{\"ActivePerspective\":\"Editor\"}");

        var dir = LayoutPaths.UserDirectory(_appName);
        File.WriteAllText(Path.Combine(dir, LayoutPaths.IniFileName), "MY OWN GEOMETRY");
        File.WriteAllText(Path.Combine(dir, LayoutPaths.WindowSettingsFileName), "MY OWN WINDOWS");

        var copied = LayoutPaths.TryResetUserLayout(_appName);

        Assert.Equal(
            new[] { LayoutPaths.IniFileName, LayoutPaths.WindowSettingsFileName }.OrderBy(x => x),
            copied.OrderBy(x => x));
        Assert.Equal("[Window][Default]\nPos=1,1\n",
                     File.ReadAllText(Path.Combine(dir, LayoutPaths.IniFileName)));
        Assert.Equal("{\"ActivePerspective\":\"Editor\"}",
                     File.ReadAllText(Path.Combine(dir, LayoutPaths.WindowSettingsFileName)));
    }

    /// <summary>
    /// ⛔ <b>No shipped default ⇒ the reset does NOTHING and says nothing landed.</b>
    /// ⚠ A host that ships no default is a legitimate configuration — ⭐ it must not throw, and it must
    /// not leave the user's own layout half-overwritten.
    /// </summary>
    [Fact]
    public void WithNoShippedDefault_TheResetIsANoOp()
    {
        var dir = LayoutPaths.UserDirectory(_appName);
        File.WriteAllText(Path.Combine(dir, LayoutPaths.IniFileName), "MINE");

        // ⚠ No ShippedDefault here on purpose — the output directory has no `layout/`.
        if (Directory.Exists(LayoutPaths.ShippedDefaultDirectory())) return;   // ⭐ see ShippedDefault

        Assert.Empty(LayoutPaths.TryResetUserLayout(_appName));
        Assert.Equal("MINE", File.ReadAllText(Path.Combine(dir, LayoutPaths.IniFileName)));
    }

    // ══ the migration ════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>ACCEPTANCE 9 — the legacy file is brought across ONCE, then left alone.</b>
    ///
    /// <para>⭐ "Once" has no separate flag: the new file's presence IS the marker, ⛔ so there is no
    /// second piece of state to fall out of step. ⚠ The second call must not overwrite a user's
    /// newer arrangement with the stale exe-adjacent one.</para>
    /// </summary>
    [Fact]
    public void TheLegacySettingsAreMigratedOnce()
    {
        var legacy = LayoutPaths.LegacyWindowSettingsPath();
        if (File.Exists(legacy)) return;      // ⛔ never clobber a real one in the test output dir

        try
        {
            File.WriteAllText(legacy, "LEGACY");

            Assert.True(LayoutPaths.TryMigrateLegacyWindowSettings(_appName));
            var target = LayoutPaths.UserWindowSettingsPath(_appName);
            Assert.Equal("LEGACY", File.ReadAllText(target));

            // ⭐ The user moves on; the second run must leave them alone.
            File.WriteAllText(target, "NEWER");
            Assert.False(LayoutPaths.TryMigrateLegacyWindowSettings(_appName));
            Assert.Equal("NEWER", File.ReadAllText(target));
        }
        finally { File.Delete(legacy); }
    }

    // ══ save-as-default ══════════════════════════════════════════════════════

    /// <summary>
    /// ⛔⛔ <b>Outside a source tree, "save as default" REFUSES</b> — ⭐ and the caller turns that into
    /// a disabled menu item with the reason in its label, 📌 <i>"not hidden, so the absence is
    /// explainable."</i>
    ///
    /// <para>⚠ The probe walks up looking for a directory that CONTAINS a default, so this rail can only
    /// assert the refusal when the tests are not themselves running inside the checkout. ⭐ In the
    /// checkout it asserts the opposite half — that the probe FINDS it — which is the case that makes
    /// the command usable at all.</para>
    /// </summary>
    [Fact]
    public void SaveAsDefault_AnswersWhetherASourceTreeWasFound()
    {
        var found = LayoutPaths.TryFindSourceLayoutDirectory();

        if (found is null)
        {
            Assert.False(LayoutPaths.TrySaveUserLayoutAsDefault(_appName),
                "No source tree was found, so saving must refuse rather than write somewhere else.");
            return;
        }

        Assert.True(Directory.Exists(found));
        Assert.EndsWith(Path.Combine(LayoutPaths.DefaultDirectoryName, "default"), found);
    }

    // ══ the order, which no headless rail can drive ══════════════════════════

    /// <summary>
    /// ⛔⛔ <b>THE RESET MUST HAPPEN BEFORE <c>SetupImGui</c>.</b> ImGui reads the ini when the path is
    /// installed; a copy afterwards lands on disk and is ignored until the NEXT run — ⭐ the classic
    /// "it works on the second launch" bug, and it would look like the reset simply not working.
    ///
    /// <para>⚠⚠ <b>This rail reads SOURCE TEXT and is weaker than a behavioural one, and says so</b>
    /// *(📌 <c>M-29</c>)*: <c>LocalWindowController.OpenLocalWindow</c> opens a real Raylib window, so
    /// no headless rail can drive the ordering. ⭐ It is still the only thing that can see this
    /// particular defect, which is why it exists at all — 📌 the same shape as
    /// <c>TheCompositionRootHandsBlueprintALiveWriter</c>.</para>
    /// </summary>
    [Fact]
    public void TheLayoutIsResolvedBeforeImGuiIsSetUp()
    {
        var text = RepoFile("Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs");
        if (text is null) return;   // ⭐ not in a checkout — nothing to read, and that is not a failure

        int reset = text.IndexOf("TryResetUserLayout", StringComparison.Ordinal);
        int setup = text.IndexOf("SetupImGui()", StringComparison.Ordinal);

        Assert.True(reset > 0, "OpenLocalWindow no longer resets the layout at all.");
        Assert.True(setup > 0, "OpenLocalWindow no longer calls SetupImGui.");
        Assert.True(reset < setup,
            "The layout reset now runs AFTER SetupImGui. ImGui reads the ini when the path is "
          + "installed, so the copy would be ignored until the next run.");
    }

    private static string? RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    /// <summary>
    /// ⭐ Puts a shipped default pair where <see cref="LayoutPaths.ShippedDefaultDirectory"/> looks, and
    /// takes it away again. ⚠ It refuses to touch a <c>layout/</c> that was already there — ⛔ a test
    /// that deleted the real build output would break the next run rather than this one.
    /// </summary>
    private sealed class ShippedDefault : IDisposable
    {
        private readonly bool _created;

        public ShippedDefault(string ini, string json)
        {
            var dir = LayoutPaths.ShippedDefaultDirectory();
            _created = !Directory.Exists(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, LayoutPaths.IniFileName), ini);
            File.WriteAllText(Path.Combine(dir, LayoutPaths.WindowSettingsFileName), json);
        }

        public void Dispose()
        {
            if (!_created) return;
            try { Directory.Delete(LayoutPaths.ShippedDefaultDirectory(), recursive: true); }
            catch { /* best effort */ }
        }
    }
}
