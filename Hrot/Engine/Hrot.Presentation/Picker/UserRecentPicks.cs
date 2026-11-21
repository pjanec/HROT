using System;
using System.IO;
using Fdp.Presentation.WindowManager;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.Picker;

/// <summary>
/// ⭐⭐ <b>CE-4098 (AQ108 D19) — THE user's Recent lists: one persisted <see cref="RecentStore"/> per process, in the user folder beside the
/// layout's own user files</b> (<see cref="LayoutPaths.UserDirectory"/>: <c>imgui.ini</c>, <c>fdp_windows.json</c>, <c>audio.json</c>,
/// <c>map-layers.json</c> — one file, <c>recent-picks.json</c>, per machine and user, shared by the editor, CGF, SimHost and IG).
///
/// <para>⭐ <b>One list per kind, never a second store:</b> every picker registry made by <c>EntityAuthoring.CreatePickers</c> carries
/// <see cref="Shared"/>, and the asset browser's panel options take the same instance — so File › Open Asset, File › Open
/// Scenario, File › Open Recording and the docked browser all read and push the lists named by
/// <c>AssetRecents.ContextFor(kind)</c>. The keys are the pickers' own item identities (scenario / asset ids, exercise ids), never an
/// absolute path, so the content-folder move (CE-4089) leaves them valid; an entry that no longer resolves is not shown.</para>
///
/// <para>A missing or unreadable file gives empty lists; a failed write is ignored (as <c>MapLayerPreferences</c>, <c>AudioSettings</c>): a
/// recent list must never stop a host.</para>
/// </summary>
public static class UserRecentPicks
{
    public const string FileName = "recent-picks.json";

    private static readonly Lazy<RecentStore> SharedStore = new(() => Open(DefaultPath()), isThreadSafe: true);

    /// <summary>Where this user's file is (the folder is created); null when there is no user folder.</summary>
    public static string? DefaultPath()
    {
        return Hrot.UI.Common.UserFiles.TryPathOf(FileName);
    }

    /// <summary>The store persisted at <paramref name="path"/> (in-memory when it is null or empty).</summary>
    public static RecentStore Open(string? path)
        => string.IsNullOrEmpty(path) ? new RecentStore() : RecentStore.Load(path);

    /// <summary>The process-wide persisted store — what every host's pickers and the asset browser share.</summary>
    public static RecentStore Shared => SharedStore.Value;
}
