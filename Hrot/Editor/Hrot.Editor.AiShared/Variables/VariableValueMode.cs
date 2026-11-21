namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>What the ONE Value column currently means.</b>
///
/// <para>📌 <b><c>Q32</c> ruling 3, verbatim:</b> <i>"ONE Value column, meaning switched by run state —
/// <b>initial</b> when not running, <b>current</b> when running or paused, across live / replay /
/// preview"</i> · ⛔ <i>"the coordinator argued two columns and is <b>overruled</b>"</i>.</para>
/// </summary>
public enum VariableValueMode
{
    /// <summary>The declared starting value — what the variable will be when the sim starts.</summary>
    Initial,

    /// <summary>What the variable is right now, read from the live blackboard.</summary>
    Current,
}

/// <summary>
/// ⭐⭐⭐ <b>THE one place that answers <i>"initial or current?"</i></b>
///
/// <para>⛔ <b>Not a bool per call site.</b> The cell, the tooltip and *(from row 59)* the dialog's
/// write target all key off run state; three independent readings of one question is exactly the shape
/// ruling 9 forbids. ⭐ One function, and every caller asks it.</para>
///
/// <para>⛔ <b>And NOT a second notion of "running".</b> <see cref="VariableRunState"/> already ships
/// and is already what <c>VariableChangeMonitor</c> observes against; coining another would leave the
/// highlight and the value disagreeing about whether the sim is up.</para>
/// </summary>
public static class VariableValue
{
    /// <summary>
    /// ⭐ <b>Planning ⇒ Initial; everything else ⇒ Current.</b>
    ///
    /// <para>⚠ <b><c>Replay</c> is CURRENT, not initial</b> — ruling 3 says the current arm spans
    /// <i>"live / replay / preview"</i>. ⛔ A replayed frame has real values; showing the declared
    /// default there would silently mislabel recorded data as a plan.</para>
    /// </summary>
    public static VariableValueMode ModeFor(VariableRunState runState)
        => runState == VariableRunState.Planning ? VariableValueMode.Initial : VariableValueMode.Current;
}
