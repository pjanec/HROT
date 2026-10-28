using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Picker;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Tests.Browser;

/// <summary>
/// ⭐ <c>CE-4098</c> (AQ108 D19) — WHERE the Recent lists live and that every host's pickers carry them by construction: ONE file in the user folder
/// beside the layout's own user files, and the ONE way a host makes its picker registry (<c>EntityAuthoring.CreatePickers</c>) hands it the store.
/// 🔴 Red-proof: drop the <c>Recent =</c> line from <c>EntityAuthoring.CreatePickers</c> ⇒ the registry assertion fails (a host would show no Recent).
/// </summary>
public sealed class UserRecentPicksTests
{
    [Fact]
    public void TheListsLiveInTheUserFolder_BesideTheLayoutsUserFiles()
    {
        var path = UserRecentPicks.DefaultPath();

        Assert.NotNull(path);
        Assert.Equal(UserRecentPicks.FileName, Path.GetFileName(path));
        Assert.Equal(
            Path.GetDirectoryName(LayoutPaths.UserWindowSettingsPath(Hrot.UI.Common.Audio.AudioSettings.AppFolderName)),
            Path.GetDirectoryName(path));
    }

    [Fact]
    public void WithNoPath_TheStoreIsInMemory_AndAPathBindsIt()
    {
        Assert.Null(UserRecentPicks.Open(null).PersistPath);
        Assert.Null(UserRecentPicks.Open("").PersistPath);

        var file = Path.Combine(Path.GetTempPath(), "urp-" + Guid.NewGuid().ToString("N"), "recent-picks.json");
        Assert.Equal(file, UserRecentPicks.Open(file).PersistPath);
    }

    [Fact]
    public void EveryHostsPickerRegistry_CarriesTheSameStore_ByConstruction()
    {
        var mine = new RecentStore();
        Assert.Same(mine, EntityAuthoring.CreatePickers(recent: mine).Recent);

        // no store passed ⇒ the user's ONE shared list (the editor and CGF both call it this way)
        Assert.Same(UserRecentPicks.Shared, EntityAuthoring.CreatePickers().Recent);
        Assert.Same(EntityAuthoring.CreatePickers().Recent, EntityAuthoring.CreatePickers().Recent);
    }
}
