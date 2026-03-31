using Hrot.ExCon.Panels;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="InteractionPanel"/>.
///
/// Tests drive the panel through <see cref="InteractionPanel.AddLog"/> /
/// <see cref="InteractionPanel.DrainPendingLogs"/> and assert against
/// <see cref="InteractionPanel.Entries"/> /
/// <see cref="InteractionPanel.EntryCount"/> without needing ImGui.
///
/// <para>Since <see cref="InteractionPanel.AddLog"/> now enqueues to a staging
/// <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/> (ExCon-DEBT-034),
/// every test must call <see cref="InteractionPanel.DrainPendingLogs"/> before
/// asserting on <see cref="InteractionPanel.Entries"/>.</para>
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

    // ── AddLog + DrainPendingLogs – basic insertion ───────────────────────────

    [Fact]
    public void AddLog_SingleEntry_EntryCountIsOne()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "MapClickEvent", "Pos:45.12,12.33");
        panel.DrainPendingLogs();
        Assert.Equal(1, panel.EntryCount);
    }

    [Fact]
    public void AddLog_StoresDirection()
    {
        var panel = new InteractionPanel();
        panel.AddLog("TX", "CreateEntityReq", "Type:T-72");
        panel.DrainPendingLogs();

        Assert.Equal("TX", panel.Entries[0].Direction);
    }

    [Fact]
    public void AddLog_StoresTopic()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "CreateEntityAck", "Success");
        panel.DrainPendingLogs();

        Assert.Equal("CreateEntityAck", panel.Entries[0].Topic);
    }

    [Fact]
    public void AddLog_StoresDetails()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "EntityMaster", "ID:5000005");
        panel.DrainPendingLogs();

        Assert.Equal("ID:5000005", panel.Entries[0].Details);
    }

    [Fact]
    public void AddLog_StoresTimestamp()
    {
        var before = DateTime.UtcNow;
        var panel  = new InteractionPanel();
        panel.AddLog("RX", "TopicA", "detail");
        panel.DrainPendingLogs();
        var after = DateTime.UtcNow;

        Assert.InRange(panel.Entries[0].Time, before, after);
    }

    // ── DrainPendingLogs return value ─────────────────────────────────────────

    [Fact]
    public void DrainPendingLogs_WithPendingEntries_ReturnsCorrectCount()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "A", "1");
        panel.AddLog("TX", "B", "2");
        panel.AddLog("RX", "C", "3");

        int drained = panel.DrainPendingLogs();

        Assert.Equal(3, drained);
    }

    [Fact]
    public void DrainPendingLogs_EmptyQueue_ReturnsZero()
    {
        var panel = new InteractionPanel();
        int drained = panel.DrainPendingLogs();
        Assert.Equal(0, drained);
    }

    [Fact]
    public void DrainPendingLogs_BeforeDrain_EntriesEmpty()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "A", "1");

        // Before drain, committed log is empty
        Assert.Equal(0, panel.EntryCount);
    }

    // ── Order preservation ────────────────────────────────────────────────────

    [Fact]
    public void AddLog_MultipleEntries_OrderedOldestFirst()
    {
        var panel = new InteractionPanel();
        panel.AddLog("RX", "First",  "1");
        panel.AddLog("TX", "Second", "2");
        panel.AddLog("RX", "Third",  "3");
        panel.DrainPendingLogs();

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
        panel.DrainPendingLogs();

        Assert.Equal(PanelConstants.MaxLogEntries, panel.EntryCount);
    }

    [Fact]
    public void AddLog_OneOverCap_EntryCountStaysAtMax()
    {
        var panel = new InteractionPanel();
        for (int i = 0; i <= PanelConstants.MaxLogEntries; i++)
            panel.AddLog("RX", $"Topic{i}", $"detail{i}");
        panel.DrainPendingLogs();

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
        panel.DrainPendingLogs();

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
        panel.DrainPendingLogs();

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
        panel.DrainPendingLogs();

        // IReadOnlyList cannot be directly cast to List<T> from a sealed class
        // — confirm it is not the underlying mutable list reference.
        Assert.IsNotType<List<LogEntry>>(panel.Entries);
    }

    // ── Thread safety (ExCon-DEBT-034) ──────────────────────────────────────────

    [Fact]
    public void AddLog_ConcurrentWriters_AllEntriesDrained()
    {
        // Verify that concurrent AddLog calls from multiple threads do not
        // corrupt the staging queue and all entries are eventually drained.
        const int ThreadCount   = 8;
        const int EntriesPerThread = 50;

        var panel = new InteractionPanel();

        var threads = Enumerable.Range(0, ThreadCount).Select(_ => new Thread(() =>
        {
            for (int i = 0; i < EntriesPerThread; i++)
                panel.AddLog("RX", "ConcurrentTopic", "payload");
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        // Drain all queued entries on the "main thread" (this thread)
        panel.DrainPendingLogs();

        // Total written = ThreadCount × EntriesPerThread; cap limits committed entries
        int expectedCommitted = Math.Min(
            ThreadCount * EntriesPerThread,
            PanelConstants.MaxLogEntries);

        Assert.Equal(expectedCommitted, panel.EntryCount);
    }

    [Fact]
    public void AddLog_ConcurrentWriters_NoExceptionsThrown()
    {
        // Smoke test: concurrent writers must never throw.
        var panel     = new InteractionPanel();
        var exception = (Exception?)null;

        var threads = Enumerable.Range(0, 10).Select(i => new Thread(() =>
        {
            try
            {
                for (int j = 0; j < 200; j++)
                    panel.AddLog("RX", $"T{i}", $"d{j}");
            }
            catch (Exception ex)
            {
                Volatile.Write(ref exception, ex);
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Null(exception);
    }
}
