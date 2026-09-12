using System;
using System.IO;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐⭐⭐ <b>Batch 103 (<c>103a</c>) — WHERE "THE LAYOUT" LIVES. One helper, two files, two places.</b>
///
/// <para>📄 <c>docs/UX/UX_Feature_Layout_Defaults.md</c> — 🔒 <b>the user's ruling, <c>2026-08-10</c>:</b>
/// <i>"The json must live next to the <c>imgui.ini</c> — <b>both places</b> (user and default)."</i></para>
///
/// <para>🔴 <b>The finding that made this one type instead of two paths.</b> 📐 Layout state lives in
/// <b>TWO files under TWO different roots</b>: <c>imgui.ini</c> *(docking geometry)* under
/// <c>LocalApplicationData/HROT</c>, and <c>fdp_windows.json</c> *(open/closed, active perspective, UI
/// scale)* <b>next to the exe</b>. ⇒ ⛔ <b>resetting one without the other gives a HALF-reset</b> —
/// default geometry with your old windows open, or the reverse.</para>
///
/// <para>⭐⭐ <b>So "the layout" is a DIRECTORY, not two special-cased files</b>, and a reset is a
/// directory copy. ⭐ Adding a third layout file later needs no design change.</para>
///
/// <para>⚠ <b>The app name is a PARAMETER.</b> 📌 The design's constraint 1: <i>"<c>Fdp.Presentation</c>
/// never learns what 'HROT' is."</i> ⛔ Two call sites hard-coded the string in their own bodies before
/// this; ⭐ it now travels as an argument, so the shared assembly carries the CONVENTION and the host
/// carries the NAME.</para>
///
/// <para>⚠⚠ <b>AND IT MUST RESOLVE ON LINUX.</b> 📌 <c>R-124</c> — the frame rails run under Xvfb on
/// Linux, so a Windows-only path would make every layout rail unrunnable there.
/// <see cref="UserDirectory"/> documents what .NET actually returns per platform, and
/// <c>LayoutPathsTests</c> asserts it on the platform the tests are running on rather than trusting this
/// comment.</para>
/// </summary>
public static class LayoutPaths
{
    /// <summary>⭐ The ImGui docking-geometry file. ⛔ ImGui reads this itself, by path, at setup.</summary>
    public const string IniFileName = "imgui.ini";

    /// <summary>⭐ Open/closed, active perspective, UI scale — <c>WindowManager</c>'s own file.</summary>
    public const string WindowSettingsFileName = "fdp_windows.json";

    /// <summary>⭐ The shipped pair's directory, relative to the output directory.</summary>
    public const string DefaultDirectoryName = "layout";

    /// <summary>
    /// ⭐⭐ <b>The USER layout directory — both files live here.</b>
    ///
    /// <para>📐 <b>What <c>SpecialFolder.LocalApplicationData</c> actually resolves to</b> *(measured,
    /// not assumed — <c>LayoutPathsTests</c> re-measures it on the running platform)*:
    /// <list type="bullet">
    ///   <item><b>Windows</b> — <c>%LocalAppData%</c>, i.e. <c>C:\Users\…\AppData\Local</c></item>
    ///   <item><b>Linux</b> — <c>$XDG_DATA_HOME</c>, or <c>~/.local/share</c> when it is unset</item>
    /// </list>
    /// ⇒ ⭐ the convention is cross-platform <b>as written</b>; ⛔ it is the NAME that reads
    /// Windows-only, not the behaviour.</para>
    ///
    /// <para>⚠ <b>It is CREATED, not merely computed.</b> Both former call sites called
    /// <c>Directory.CreateDirectory</c> before handing the path to ImGui, and ImGui will not create it
    /// itself — ⛔ dropping that would make the ini silently unwritable.</para>
    /// </summary>
    /// <param name="appName">The host's folder name — <c>"HROT"</c> for this repo's apps.</param>
    public static string UserDirectory(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            throw new ArgumentException("An app name is required — it names the user's layout folder.",
                                        nameof(appName));

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>⭐ The user's <c>imgui.ini</c> — what ImGui is pointed at, and what it rewrites on exit.</summary>
    public static string UserIniPath(string appName)
        => Path.Combine(UserDirectory(appName), IniFileName);

    /// <summary>
    /// ⭐⭐ The user's <c>fdp_windows.json</c>.
    ///
    /// <para>⚠ <b>This MOVES the file</b> — it used to sit next to the exe
    /// *(<c>WindowManager.DefaultSettingsPath</c>)*, which is why a clean rebuild wiped a user's window
    /// arrangement. ⭐ <see cref="LegacyWindowSettingsPath"/> is where it was, for the one-time
    /// migration.</para>
    /// </summary>
    public static string UserWindowSettingsPath(string appName)
        => Path.Combine(UserDirectory(appName), WindowSettingsFileName);

    /// <summary>
    /// ⚠ <b>Where <c>fdp_windows.json</c> USED to live</b> — beside the executable. ⭐ Read once, by the
    /// migration, so an existing user does not silently lose their arrangement. ⛔ Never written.
    /// </summary>
    public static string LegacyWindowSettingsPath()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WindowSettingsFileName);

    /// <summary>
    /// ⭐ The SHIPPED default pair, in the output directory *(<c>layout/</c>, copied by the build)*.
    /// ⚠ It may not exist — a host that ships no default is a legitimate configuration, and
    /// <see cref="TryResetUserLayout"/> answers <c>false</c> rather than throwing.
    /// </summary>
    public static string ShippedDefaultDirectory()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultDirectoryName);

    /// <summary>
    /// ⭐⭐⭐ <b>THE RESET — a file copy BEFORE the load, and nothing else.</b>
    ///
    /// <para>🔒 <b>The user's own words:</b> <i>"during the development stage — practically always now —
    /// auto-revert the user setting to the repo committed default on each new run, i.e. copy the default
    /// to the user folder force-overwriting whatever was there, so ImGui loads the user copy as usual
    /// but reset to default, and on exit save to the user folder as now."</i></para>
    ///
    /// <para>⭐⭐ <b>Why a COPY and not a load-from-default.</b> ⛔ Nothing in the load path changes:
    /// ImGui still reads the user's ini by the path it was given, and <c>WindowManager</c> still reads
    /// the user's json. ⇒ ⭐ <c>SaveSettings</c>/<c>LoadSettings</c> and ImGui's own persistence stay
    /// untouched, and the exit-save keeps overwriting the user copy exactly as before.</para>
    ///
    /// <para>⛔⛔ <b>It MUST run before ImGui is set up.</b> ImGui reads the ini when the path is
    /// installed; a copy afterwards would land on disk and be ignored until the next run.</para>
    /// </summary>
    /// <returns>⭐ The files actually copied. ⚠ Empty when no shipped default exists — ⛔ not an error.</returns>
    public static IReadOnlyList<string> TryResetUserLayout(string appName)
    {
        var from = ShippedDefaultDirectory();
        if (!Directory.Exists(from)) return Array.Empty<string>();

        var to = UserDirectory(appName);
        var copied = new List<string>();

        foreach (var name in new[] { IniFileName, WindowSettingsFileName })
        {
            var src = Path.Combine(from, name);
            if (!File.Exists(src)) continue;      // ⭐ a partial default is still a default
            File.Copy(src, Path.Combine(to, name), overwrite: true);
            copied.Add(name);
        }

        return copied;
    }

    /// <summary>
    /// ⭐⭐ <b>The SOURCE-TREE layout directory</b> — <c>&lt;repo&gt;/layout/default</c> — or <c>null</c>
    /// when the app is not running from a checkout.
    ///
    /// <para>⭐ Walks up from the output directory, ⛔ exactly the precedent
    /// <c>EditorSubsystem.ResolveAiBehaviorsDir</c> already sets for locating the source tree from a
    /// running exe. ⚠ A shipped build legitimately has no source tree — ⭐ <c>null</c> is an ANSWER,
    /// and the caller's job is to say so rather than hide the command.</para>
    ///
    /// <para>⛔ It looks for the directory that already CONTAINS a default, ⚠ not for a marker like
    /// <c>.git</c>: a checkout whose <c>layout/default</c> was deleted cannot be a save target, and a
    /// tarball that happens to carry one can.</para>
    /// </summary>
    public static string? TryFindSourceLayoutDirectory()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, DefaultDirectoryName, "default");
            if (Directory.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>File ▸ Layout ▸ Save current as default</c></b> — copies the USER pair into the
    /// SOURCE tree, so the arrangement on screen becomes the one the repo ships.
    ///
    /// <para>⚠ <b>This writes into the source tree from a running app</b> — 📌 the design lists that as
    /// a risk and guards it with the walk-up probe. ⛔ Outside a checkout it does nothing and answers
    /// <c>false</c>; the caller disables the command <b>with a reason</b> rather than hiding it, so the
    /// absence is explainable.</para>
    ///
    /// <para>⚠ <b>It saves what is ON DISK, not what is in memory.</b> ⇒ the caller must flush the
    /// <c>WindowManager</c>'s settings first, and ImGui only rewrites its ini at shutdown — ⛔ so the
    /// geometry copied here is the geometry from the last save, which is what
    /// <c>SaveIniSettingsToDisk</c> exists for.</para>
    /// </summary>
    /// <returns><c>true</c> when at least one file was written into the source tree.</returns>
    public static bool TrySaveUserLayoutAsDefault(string appName)
    {
        var target = TryFindSourceLayoutDirectory();
        if (target is null) return false;

        var from = UserDirectory(appName);
        bool wrote = false;

        foreach (var name in new[] { IniFileName, WindowSettingsFileName })
        {
            var src = Path.Combine(from, name);
            if (!File.Exists(src)) continue;
            File.Copy(src, Path.Combine(target, name), overwrite: true);
            wrote = true;
        }

        return wrote;
    }

    /// <summary>
    /// ⭐⭐ <b>The ONE-TIME migration</b> — if the user directory has no <c>fdp_windows.json</c> and the
    /// old exe-adjacent one exists, bring it across.
    ///
    /// <para>⭐ Cheap, and it stops an existing user silently losing their arrangement when the file
    /// moved. ⚠ <b>Once</b>: the presence of the new file is the "already migrated" marker, so there is
    /// no separate flag to keep in step.</para>
    ///
    /// <para>⛔ It does NOT delete the old file. ⚠ A user who downgrades afterwards gets the old one back
    /// — 📌 the design calls that acceptable and worth one changelog line.</para>
    /// </summary>
    /// <returns><c>true</c> when a file was migrated.</returns>
    public static bool TryMigrateLegacyWindowSettings(string appName)
    {
        var target = UserWindowSettingsPath(appName);
        if (File.Exists(target)) return false;

        var legacy = LegacyWindowSettingsPath();
        if (!File.Exists(legacy)) return false;

        File.Copy(legacy, target, overwrite: false);
        return true;
    }
}
