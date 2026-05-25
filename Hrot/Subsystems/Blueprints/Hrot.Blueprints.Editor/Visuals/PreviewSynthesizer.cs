using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Synthesizes a short human-readable summary of a WhenNode's current configuration.
/// Used both by the canvas attachment pill and by the editor drawer's preview line.
/// </summary>
public static class PreviewSynthesizer
{
    /// <summary>
    /// Returns a summary string of at most <paramref name="maxLength"/> characters.
    /// Trailing characters beyond the limit are replaced with "…".
    /// </summary>
    public static string Synthesize(WhenNode node, int maxLength = 40)
    {
        var raw = node.Mode switch
        {
            WhenMode.ValueChanged  => SynthesizeValueChanged(node.ValueChanged),
            WhenMode.EventFired    => SynthesizeEventFired(node.EventFired),
            WhenMode.ConditionMet  => SynthesizeConditionMet(node.ConditionMet),
            WhenMode.EqsResult     => SynthesizeEqsResult(node.EqsResult),
            _                      => "(unknown mode)"
        };

        var edges = node.Edges == WhenEdge.None ? " (no edge)" :
                    node.Edges == WhenEdge.RisingEdge ? " ↑" :
                    node.Edges == WhenEdge.FallingEdge ? " ↓" : " ↑↓";

        var full = raw + edges;
        return full.Length <= maxLength ? full : full[..(maxLength - 1)] + "…";
    }

    private static string SynthesizeValueChanged(ValueChangedPayload? p)
    {
        if (p is null) return "Value Changed";
        if (string.IsNullOrEmpty(p.PropertyPath)) return "Value Changed";
        var propShort = p.PropertyPath.Contains('.')
            ? p.PropertyPath[(p.PropertyPath.LastIndexOf('.') + 1)..]
            : p.PropertyPath;
        return propShort;
    }

    private static string SynthesizeEventFired(EventFiredPayload? p)
    {
        if (p is null) return "Event Fired";
        if (string.IsNullOrEmpty(p.EventTypeId)) return "Event Fired";
        var eventShort = p.EventTypeId.Contains('.')
            ? p.EventTypeId[(p.EventTypeId.LastIndexOf('.') + 1)..]
            : p.EventTypeId;
        return eventShort;
    }

    private static string SynthesizeConditionMet(ConditionMetPayload? p)
    {
        return "Condition Met";
    }

    private static string SynthesizeEqsResult(EqsResultPayload? p)
    {
        if (p is null) return "EQS Result";
        var trigger = p.Trigger switch
        {
            EqsTrigger.FirstReady    => "Ready",
            EqsTrigger.TopChanged    => "TopChanged",
            EqsTrigger.ScoreCrossed  => "Score≥" + p.ScoreThreshold.ToString("F1"),
            EqsTrigger.BecomesStale  => "Stale",
            _                        => "EQS"
        };
        return string.IsNullOrEmpty(p.SensorVariableName) ? trigger : $"{p.SensorVariableName} {trigger}";
    }
}
