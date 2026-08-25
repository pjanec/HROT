using System;
using System.IO;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-505</c> — WHERE THE DEBUG SESSION FILE LIVES, and the git-curated reset.</b>
///
/// <para>🔒 <b>The user's ruling, <c>2026-08-24</c>, verbatim:</b> <i>"ad file path - user local folder;
/// BUT during development we need clean env controlled from git only. let's apply same rule as for
/// curated scenarios and imgui.ini - always overwrite the user's copy with git maintained curated copy
/// on start."</i></para>
///
/// <para>⭐⭐ <b>This is the <c>imgui.ini</c> pattern, not the curated-scenarios one</b>, and the choice is
/// deliberate: <c>Fdp.Presentation.WindowManager.LayoutPaths.TryResetUserLayout</c> copies
/// from the <b>output directory</b> *(<c>layout/</c>, put there by the build)*, so the reset also holds in
/// a deployed build and in CI. ⛔ <c>CuratedScenarios</c> walks up to the SOURCE TREE and is therefore
/// dev-only; the debug session wants the deterministic-clean-environment property everywhere, which is
/// the whole point of the ruling.</para>
///
/// <para>⭐ <b>The move it makes.</b> 📐 Before this, the file was
/// <c>&lt;repo-root&gt;/.debug/bpsession.json</c> *(the <c>CF-8</c> default —
/// <c>.dev/blueprint-dbg-1/TASK-DETAIL.md:699</c>, which offered <i>"a gitignored path … or the editor's
/// per-user data dir"</i> and picked the former)*. ⛔ <c>.gitignore:65</c> ignores <c>.debug/</c>, so that
/// location <b>cannot host a git-maintained curated copy</b> — the ruling's two halves force the file to
/// the per-user data dir, which was the alternative the same design already named.</para>
///
/// <para>⚠⚠ <b>It also un-bricks the editor.</b> 📄
/// <c>docs/blueprints/FINDINGS_Empty_Breakpoint_Bricks_The_Editor.md</c>: a poisoned session file killed
/// the process on every launch and <i>"the only recovery was deleting a gitignored file by hand"</i>.
/// ⭐ With the reset in place a development start always begins from the committed clean file, so the
/// poison survives at most one run.</para>
///
/// <para>⛔ <b>The user directory is a PARAMETER.</b> 📌 The same constraint <c>LayoutPaths</c> documents
/// for the app name — <c>Hrot.Diagnostics.Breakpoints</c> must not learn what <c>"HROT"</c> is, and it
/// does not reference <c>Fdp.Presentation</c>. ⇒ the caller passes
/// <c>LayoutPaths.UserDirectory(appName)</c> and this type carries only the convention.</para>
/// </summary>
public static class DebugSessionPaths
{
    /// <summary>⭐ The session file's name, in both places *(user copy and shipped curated copy)*.</summary>
    public const string FileName = "bpsession.json";

    /// <summary>⭐ The shipped curated copy's directory, relative to the output directory.</summary>
    public const string CuratedDirectoryName = "debug";

    /// <summary>
    /// ⭐ The SHIPPED curated session directory — <c>&lt;output&gt;/debug/</c>, put there by the build
    /// *(<c>Hrot.ClusterRunner.csproj</c> copies <c>debug/default/bpsession.json</c> there)*.
    /// ⚠ It may not exist — a host that ships no curated session is legitimate, and
    /// <see cref="TryResetUserSession"/> answers <c>false</c> rather than throwing.
    /// </summary>
    public static string ShippedCuratedDirectory()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CuratedDirectoryName);

    /// <summary>⭐ The user's session file, inside the per-user data directory the caller resolves.</summary>
    public static string UserPath(string userDirectory)
    {
        if (string.IsNullOrWhiteSpace(userDirectory))
            throw new ArgumentException("A user directory is required — it is where the session file lives.",
                                        nameof(userDirectory));
        return Path.Combine(userDirectory, FileName);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE RESET — force-copy the git-maintained curated session over the user's copy.</b>
    ///
    /// <para>⛔⛔ <b>It MUST run before the session is loaded</b>, for the same reason
    /// <c>TryResetUserLayout</c> must run before ImGui is set up: a copy afterwards lands on disk and is
    /// ignored until the next run.</para>
    ///
    /// <para>⭐ Nothing in the load or save path changes. The editor still reads and writes the USER copy
    /// by the path it was given — the reset only decides what that copy contains at start.</para>
    /// </summary>
    /// <returns><c>true</c> when the curated copy was written over the user's.</returns>
    public static bool TryResetUserSession(string userDirectory)
        => TryResetUserSessionFrom(ShippedCuratedDirectory(), userDirectory);

    /// <summary>
    /// The pure copy behind <see cref="TryResetUserSession"/> — exposed so the behaviour is testable
    /// without depending on what the build happened to place in the output directory
    /// *(the precedent: <c>CuratedScenarios.SeedFrom</c>)*.
    /// </summary>
    public static bool TryResetUserSessionFrom(string curatedDirectory, string userDirectory)
    {
        if (string.IsNullOrWhiteSpace(curatedDirectory)) return false;

        var src = Path.Combine(curatedDirectory, FileName);
        if (!File.Exists(src)) return false;       // ⭐ no curated copy shipped — not an error

        var dst = UserPath(userDirectory);
        Directory.CreateDirectory(userDirectory);
        File.Copy(src, dst, overwrite: true);
        return true;
    }
}
