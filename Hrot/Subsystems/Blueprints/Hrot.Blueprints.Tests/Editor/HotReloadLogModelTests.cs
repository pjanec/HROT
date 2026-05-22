using Hrot.Blueprints.Editor.Debug;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class HotReloadLogModelTests
{
    private static ReloadLogEntry MakeEntry(bool succeeded = true)
        => new(DateTime.UtcNow, Hrot.Blueprints.Editor.ReloadSource.QuickReloadViaApi, succeeded, null, 0);

    [Fact]
    public void HotReloadLogModel_AddEntry_IncreasesCount()
    {
        var model = new HotReloadLogModel();
        model.AddEntry(MakeEntry());
        Assert.Equal(1, model.Count);
    }

    [Fact]
    public void HotReloadLogModel_Add_BeyondMax_Evicts_Oldest()
    {
        var model = new HotReloadLogModel();
        var oldest = MakeEntry();
        model.AddEntry(oldest);
        for (int i = 1; i <= HotReloadLogModel.MaxEntries; i++)
            model.AddEntry(MakeEntry());

        Assert.Equal(HotReloadLogModel.MaxEntries, model.Count);
        Assert.DoesNotContain(oldest, model.Entries);
    }

    [Fact]
    public void HotReloadLogModel_Clear_ResetsCount()
    {
        var model = new HotReloadLogModel();
        for (int i = 0; i < 5; i++)
            model.AddEntry(MakeEntry());
        model.Clear();
        Assert.Equal(0, model.Count);
    }

    [Fact]
    public void HotReloadLogWindow_OnReloadCompleted_AddsEntry()
    {
        var window = new HotReloadLogWindow();
        var info = new Hrot.Blueprints.Editor.ReloadCompletedInfo(
            Hrot.Blueprints.Editor.ReloadSource.QuickReloadViaApi,
            new[] { Guid.NewGuid() },
            null,
            42);
        window.OnReloadCompleted(info);
        Assert.Equal(1, window.Model.Count);
        Assert.True(window.Model.Entries.First().Succeeded);
    }

    [Fact]
    public void HotReloadLogWindow_OnReloadFailed_AddsFailedEntry()
    {
        var window = new HotReloadLogWindow();
        window.OnReloadFailed("build error", Hrot.Blueprints.Editor.ReloadSource.QuickReloadViaApi);
        Assert.Equal(1, window.Model.Count);
        Assert.False(window.Model.Entries.First().Succeeded);
    }
}
