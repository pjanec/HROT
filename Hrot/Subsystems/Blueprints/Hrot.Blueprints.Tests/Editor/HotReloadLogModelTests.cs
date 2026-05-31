using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Debug;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class HotReloadLogModelTests
{
    // ---- Minimal in-process coordinator test double ----

    private sealed class FakeCoordinator : IBlueprintEditorCoordinator
    {
        public event Action<ReloadCompletedInfo>? OnReloadCompleted;
        public event Action<string, ReloadSource>? OnReloadFailed;

        public void FireCompleted(ReloadCompletedInfo info) => OnReloadCompleted?.Invoke(info);
        public void FireFailed(string msg, ReloadSource src) => OnReloadFailed?.Invoke(msg, src);
    }

    private static ReloadLogEntry MakeEntry(bool succeeded = true)
        => new(DateTime.UtcNow, Hrot.Blueprints.Editor.ReloadSource.QuickReloadViaApi, succeeded, null, 0);

    // ---- HotReloadLogModel tests ----------------------------------------

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

    // ---- HotReloadLogWindow coordinator-path tests ----------------------

    [Fact]
    public void HotReloadLogWindow_CoordinatorEvent_OnReloadCompleted_AddsEntry()
    {
        var coord = new FakeCoordinator();
        using var window = new HotReloadLogWindow(coord);

        var info = new ReloadCompletedInfo(
            ReloadSource.QuickReloadViaApi,
            new[] { Guid.NewGuid() },
            null,
            42);
        coord.FireCompleted(info);

        Assert.Equal(1, window.Model.Count);
        Assert.True(window.Model.Entries.First().Succeeded);
    }

    [Fact]
    public void HotReloadLogWindow_CoordinatorEvent_OnReloadFailed_AddsFailedEntry()
    {
        var coord = new FakeCoordinator();
        using var window = new HotReloadLogWindow(coord);

        coord.FireFailed("build error", ReloadSource.QuickReloadViaApi);

        Assert.Equal(1, window.Model.Count);
        Assert.False(window.Model.Entries.First().Succeeded);
    }

    [Fact]
    public void HotReloadLogWindow_Dispose_Unsubscribes_From_Coordinator()
    {
        var coord = new FakeCoordinator();
        var window = new HotReloadLogWindow(coord);
        window.Dispose();

        var info = new ReloadCompletedInfo(
            ReloadSource.QuickReloadViaApi,
            new[] { Guid.NewGuid() },
            null,
            10);
        coord.FireCompleted(info);

        Assert.Equal(0, window.Model.Count);
    }
}

