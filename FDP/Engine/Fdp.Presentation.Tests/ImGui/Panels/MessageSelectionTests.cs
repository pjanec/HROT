using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core.Logging;
using Fdp.Presentation.Panels;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐ W-H3 (CE-4236) — the Message Log's selection names a MESSAGE, not an index: after the source drops its oldest lines (<see cref="MessageLogLimits"/>) or a filter changes the list, the selected line is still the selected line.
/// Red-proof: key <see cref="MessageSelection"/> by the index again (store <c>view[clicked]</c>) ⇒ the trim rail selects a neighbour.
/// </summary>
public sealed class MessageSelectionTests
{
    private static List<MessageLogEntry> Log(int n) => Enumerable.Range(0, n).Select(i => new MessageLogEntry(DateTime.UtcNow, LogSeverity.Info, "x", "m" + i, Array.Empty<LogChunk>())).ToList();
    private static List<int> All(IReadOnlyList<MessageLogEntry> m) => Enumerable.Range(0, m.Count).ToList();

    [Fact]
    public void ASelectedLine_StaysTheSameLine_WhenTheSourceTrimsItsOldestBlock()
    {
        var log = Log(MessageLogLimits.MaxRetained);
        var sel = new MessageSelection();
        sel.Click(log, All(log), 5000, ctrl: false, shift: false);   // "m5000"
        var picked = log[5000];
        for (int i = 0; i < 500; i++) MessageLogLimits.Add(log, new MessageLogEntry(DateTime.UtcNow, LogSeverity.Info, "x", "late" + i, Array.Empty<LogChunk>()));   // one trim of 1000: the log is 9 500 long, m5000 now sits at index 4000
        Assert.True(sel.Contains(picked));                                                  // the same message
        Assert.Equal(new[] { "m5000" }, sel.InOrder(log).Select(m => m.Message));          // ...and Copy would write it
        Assert.Equal(4000, log.IndexOf(picked));                                            // its index moved by the trim...
        Assert.False(sel.Contains(log[5000]));                                              // ...and the line now at the OLD index (it was m6000) is not selected
    }

    [Fact]
    public void AShiftRange_AnchorsOnTheLastClickedMessage_NotItsOldPosition()
    {
        var log = Log(100);
        var sel = new MessageSelection();
        sel.Click(log, All(log), 10, false, false);                                         // anchor on m10
        var view = Enumerable.Range(0, 100).Where(i => i % 2 == 0).ToList();                // a filter now lists only even messages: m10 is at position 5
        sel.Click(log, view, 8, false, shift: true);                                        // shift-click m16
        Assert.Equal(new[] { "m10", "m12", "m14", "m16" }, sel.InOrder(log).Select(m => m.Message).Where(m => view.Contains(int.Parse(m[1..]))));
    }

    [Fact]
    public void CtrlToggles_APlainClickReplaces_AndAnAnchorThatLeftTheLogStartsANewOne()
    {
        var log = Log(10);
        var sel = new MessageSelection();
        sel.Click(log, All(log), 2, false, false); sel.Click(log, All(log), 4, ctrl: true, false);
        Assert.Equal(new[] { "m2", "m4" }, sel.InOrder(log).Select(m => m.Message));
        sel.Click(log, All(log), 4, ctrl: true, false);                                      // toggled off
        Assert.Equal(new[] { "m2" }, sel.InOrder(log).Select(m => m.Message));
        sel.Click(log, All(log), 7, false, false);
        Assert.Equal(new[] { "m7" }, sel.InOrder(log).Select(m => m.Message));
        var gone = log[7]; log.RemoveAt(7);
        sel.Click(log, All(log), 3, false, shift: true);                                     // the anchor m7 is gone: no range, a plain click's behaviour
        Assert.Equal(new[] { "m3" }, sel.InOrder(log).Select(m => m.Message));
        Assert.False(sel.Contains(gone) && log.Contains(gone));
        sel.Clear(); Assert.Equal(0, sel.Count);
    }
}
