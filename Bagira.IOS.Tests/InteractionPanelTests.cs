using Bagira.IOS.Panels;

namespace Bagira.IOS.Tests;

/// <summary>
/// Unit tests for <see cref="InteractionPanel"/>.
///
/// Tests drive the panel through <see cref="InteractionPanel.AddLog"/> and
/// assert against <see cref="InteractionPanel.Entries"/> /
/// <see cref="InteractionPanel.EntryCount"/> without needing ImGui.
/// </summary>
public class InteractionPanelTests
{
    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void EntryCount_NewPanel_IsZero()
    {
        var panel = new InteractionPanel();
        Assert.Equal(0, panel.EntryCount);
    }

    [Fact]
    public void Entries_NewPanel_IsEmpty()
    {
        var panel = new InteractionPanel();
        Assert.Empty(panel.Entries);
    }

    // ── AddLog – basic insertion ──────────────────────────────────────────────

    [Fact]
    public void AddLog_SingleEntry_EntryCountIsOne()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "MapClickEvent", "Pos:45.12,12.33");
        Assert.Equal(1, panel.EntryCount);
    }

    [Fact]
    public void AddLog_StoresDirection()
    {
        var panel = new InteractionPanel();
        panel.AddLog("TX", "CreateEntityReq", "Type:T-72");

        Assert.Equal("TX", panel.Entries[0].Direction);
    }

    [Fact]
    public void AddLog_StoresTopic()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "CreateEntityAck", "Success");

        Assert.Equal("CreateEntityAck", panel.Entries[0].Topic);
    }

    [Fact]
    public void AddLog_StoresDetails()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "EntityMaster", "ID:5000005");

        Assert.Equal("ID:5000005", panel.Entries[0].Details);
    }

    [Fact]
    public void AddLog_StoresTimestamp()
    {
        var before = DateTime.UtcNow;
        var panel  = new InteractionPanel();
        panel.AddLog("RX", "TopicA", "detail");
        var after = DateTime.UtcNow;

        Assert.InRange(panel.Entries[0].Time, before, after);
    }

    // ── Order preservation ────────────────────────────────────────────────────

    [Fact]
    public void AddLog_MultipleEntries_OrderedOldestFirst()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "First",  "1");
        panel.AddLog("TX", "Second", "2");
        panel.AddLog("RX", "Third",  "3");

        Assert.Equal("First",  panel.Entries[0].Topic);
        Assert.Equal("Second", panel.Entries[1].Topic);
        Assert.Equal("Third",  panel.Entries[2].Topic);
    }

    // ── Cap enforcement ───────────────────────────────────────────────────────

    [Fact]
    public void AddLog_ExactlyAtCap_EntryCountEqualsMax()
    {
        var panel = new InteractionPanel();
        for (int i = 0; i < PanelConstants.MaxLogEntries; i++)
            panel.AddLog("RX", $"Topic{i}", $"detail{i}");

        Assert.Equal(PanelConstants.MaxLogEntries, panel.EntryCount);
    }

    [Fact]
    public void AddLog_OneOverCap_EntryCountStaysAtMax()
    {
        var panel = new InteractionPanel();
        for (int i = 0; i <= PanelConstants.MaxLogEntries; i++)
            panel.AddLog("RX", $"Topic{i}", $"detail{i}");

        Assert.Equal(PanelConstants.MaxLogEntries, panel.EntryCount);
    }

    [Fact]
    public void AddLog_AtCapacity_OldestEntryEvicted()
    {
        var panel = new InteractionPanel();
        // Fill to capacity
        for (int i = 0; i < PanelConstants.MaxLogEntries; i++)
            panel.AddLog("RX", $"OldTopic{i}", "old");

        // Add one more — oldest should be gone
        panel.AddLog("TX", "NewestTopic", "new");

        Assert.Equal("OldTopic1", panel.Entries[0].Topic);
        Assert.Equal("NewestTopic", panel.Entries[^1].Topic);
    }

    [Fact]
    public void AddLog_ManyOverCap_OldestEvictedEachTime()
    {
        var panel = new InteractionPanel();
        int overBy = 10;
        for (int i = 0; i < PanelConstants.MaxLogEntries + overBy; i++)
            panel.AddLog("RX", $"Topic{i}", "d");

        // The first 'overBy' topics should have been evicted
        Assert.Equal($"Topic{overBy}", panel.Entries[0].Topic);
        Assert.Equal(PanelConstants.MaxLogEntries, panel.EntryCount);
    }

    // ── Entries are read-only (external mutation has no effect) ───────────────

    [Fact]
    public void Entries_IsReadOnlyList_CannotCastAndMutate()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "TopicA", "d");

        // IReadOnlyList cannot be directly cast to List<T> from a sealed class
        // — confirm it is not the underlying mutable list reference.
        Assert.IsNotType<List<LogEntry>>(panel.Entries);
    }
}
