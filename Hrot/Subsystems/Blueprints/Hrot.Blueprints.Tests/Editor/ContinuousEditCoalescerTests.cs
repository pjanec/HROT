using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-11 / Q22-E1 — one undo entry per continuous gesture.
///
/// <para>
/// A slider drag or text entry fires a change per frame. One entry each would make Ctrl+Z walk back
/// through a drag character by character and would evict the rest of the undo history in a single
/// gesture. <see cref="ContinuousEditCoalescer{T}"/> holds the pre-gesture value and reports it once,
/// at commit.
/// </para>
///
/// <para>
/// The type takes ImGui's two signals as plain booleans precisely so this rule is testable headless —
/// the widgets it serves are not.
/// </para>
/// </summary>
public sealed class ContinuousEditCoalescerTests
{
    /// <summary>
    /// The load-bearing detail, and the one the architect's E1 answer got backwards: the baseline
    /// must be taken on <c>IsItemActivated()</c>. <c>IsItemDeactivatedAfterEdit()</c> fires only
    /// after the value has already changed, so a baseline captured there is the *new* value.
    /// </summary>
    [Fact]
    public void Baseline_IsTheValueFromBeforeTheGesture_NotTheLatestOne()
    {
        var c = new ContinuousEditCoalescer<string>();

        c.BeginIfNeeded(activated: true, current: "start");
        // ... the widget churns through intermediate values across later frames ...
        c.BeginIfNeeded(activated: false, current: "s");
        c.BeginIfNeeded(activated: false, current: "st");

        Assert.True(c.TryCommit(deactivatedAfterEdit: true, out var baseline));
        Assert.Equal("start", baseline);
    }

    /// <summary>A re-fired activation mid-gesture must not overwrite the baseline.</summary>
    [Fact]
    public void RepeatedActivation_DuringOneGesture_KeepsTheOriginalBaseline()
    {
        var c = new ContinuousEditCoalescer<int>();

        c.BeginIfNeeded(activated: true, current: 1);
        c.BeginIfNeeded(activated: true, current: 99);

        Assert.True(c.TryCommit(deactivatedAfterEdit: true, out var baseline));
        Assert.Equal(1, baseline);
    }

    [Fact]
    public void Commit_FiresExactlyOnce_PerGesture()
    {
        var c = new ContinuousEditCoalescer<int>();
        c.BeginIfNeeded(activated: true, current: 5);

        Assert.True(c.TryCommit(deactivatedAfterEdit: true, out _));
        Assert.False(c.TryCommit(deactivatedAfterEdit: true, out _),
            "a second commit without a new activation would push a duplicate undo entry");
    }

    [Fact]
    public void NoCommit_WhileTheWidgetIsStillActive()
    {
        var c = new ContinuousEditCoalescer<int>();
        c.BeginIfNeeded(activated: true, current: 5);

        Assert.False(c.TryCommit(deactivatedAfterEdit: false, out _));
        Assert.True(c.IsTracking);
    }

    /// <summary>
    /// Deactivation without a preceding activation (e.g. a widget that was never touched, or a
    /// coalescer created mid-gesture) must not manufacture an entry from a default baseline.
    /// </summary>
    [Fact]
    public void CommitWithoutActivation_IsIgnored()
    {
        var c = new ContinuousEditCoalescer<string>();

        Assert.False(c.TryCommit(deactivatedAfterEdit: true, out _));
    }

    [Fact]
    public void TwoGestures_EachProduceTheirOwnBaseline()
    {
        var c = new ContinuousEditCoalescer<int>();

        c.BeginIfNeeded(activated: true, current: 1);
        Assert.True(c.TryCommit(deactivatedAfterEdit: true, out var first));

        c.BeginIfNeeded(activated: true, current: 2);
        Assert.True(c.TryCommit(deactivatedAfterEdit: true, out var second));

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void Abandon_DropsAnInFlightGesture()
    {
        var c = new ContinuousEditCoalescer<int>();
        c.BeginIfNeeded(activated: true, current: 5);

        c.Abandon();

        Assert.False(c.IsTracking);
        Assert.False(c.TryCommit(deactivatedAfterEdit: true, out _));
    }
}
