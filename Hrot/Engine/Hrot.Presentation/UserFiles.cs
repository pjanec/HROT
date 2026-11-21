using System;
using System.IO;
using Fdp.Presentation.WindowManager;

namespace Hrot.UI.Common
{
    /// <summary>
    /// ⭐ CE-1441 (audit #16) — WHERE this repo's per-user preference files live: the one resolution of
    /// <c>&lt;user folder&gt;/&lt;file&gt;</c> (<see cref="LayoutPaths.UserDirectory"/>, <c>HROT</c>) that <c>AudioSettings</c>,
    /// <c>MapLayerPreferences</c>, <c>UnitDebugSettingsMemory</c> and <c>UserRecentPicks</c> each used to spell out. The READ / WRITE
    /// is <see cref="NodeEditor.Core.IO.SafeTextFile"/>.
    /// </summary>
    public static class UserFiles
    {
        /// <summary>The per-user folder's name for this repo's apps (as <c>RaylibPresentationShell.AppFolderName</c>).</summary>
        public const string AppFolderName = "HROT";

        /// <summary>The user folder's path of <paramref name="name"/> (a file or sub-folder); the user folder is created. Throws when there is none.</summary>
        public static string PathOf(string name) => Path.Combine(LayoutPaths.UserDirectory(AppFolderName), name);

        /// <summary>As <see cref="PathOf"/>, but <see langword="null"/> when there is no user folder.</summary>
        public static string? TryPathOf(string name)
        {
            try { return PathOf(name); }
            catch (Exception) { return null; }
        }
    }
}
