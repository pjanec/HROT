using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class PreferencesTests
{
    // SC1
    [Fact]
    public void Preferences_Defaults_AreCorrect()
    {
        var defaults = BlueprintEditorPreferences.Defaults;
        Assert.Equal(64, defaults.NodeHistorySize);
        Assert.False(defaults.AutoReloadOnSave);
        Assert.Equal(8.0f, defaults.GraphEditorGridSnap);
    }

    // SC2
    [Fact]
    public void Preferences_SaveAndLoad_RoundTrip()
    {
        var path = Path.GetTempFileName();
        try
        {
            var prefs = new BlueprintEditorPreferences
            {
                AutoReloadOnSave      = true,
                WatchPanelVisible     = false,
                GraphEditorGridSnap   = 16.0f,
                NodeHistorySize       = 128,
                HotReloadLogMaxEntries = 500,
            };
            prefs.Save(path);

            var loaded = BlueprintEditorPreferences.Load(path);

            Assert.Equal(prefs.AutoReloadOnSave, loaded.AutoReloadOnSave);
            Assert.Equal(prefs.WatchPanelVisible, loaded.WatchPanelVisible);
            Assert.Equal(prefs.GraphEditorGridSnap, loaded.GraphEditorGridSnap);
            Assert.Equal(prefs.NodeHistorySize, loaded.NodeHistorySize);
            Assert.Equal(prefs.HotReloadLogMaxEntries, loaded.HotReloadLogMaxEntries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // SC3
    [Fact]
    public void Preferences_Load_NonExistentFile_ReturnsDefaults()
    {
        var loaded = BlueprintEditorPreferences.Load("/nonexistent/path/prefs.json");
        Assert.Equal(64, loaded.NodeHistorySize);
    }

    // SC4
    [Fact]
    public void Preferences_Load_InvalidJson_ReturnsDefaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not valid json");
            var loaded = BlueprintEditorPreferences.Load(path);
            Assert.Equal(64, loaded.NodeHistorySize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // SC5
    [Fact]
    public void PreferencesWindow_Title_IsCorrect()
    {
        var window = new PreferencesWindow(new BlueprintEditorPreferences(), "dummy.json");
        Assert.Equal("Blueprint Preferences", window.Title);
    }
}
