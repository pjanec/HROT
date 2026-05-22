using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.UI.HotReload;

/// <summary>
/// Ring buffer of recent <see cref="GraphChangeNotification"/> entries.
/// Used to render fading badges on affected nodes.
/// </summary>
public sealed class RecentChanges
{
    private readonly Queue<TimedChange> _entries = new();
    private const int MaxEntries = 200;
    private static readonly TimeSpan FadeWindow = TimeSpan.FromSeconds(2.0);

    /// <summary>Record a new change notification at the given wall-clock elapsed time.</summary>
    public void Add(GraphChangeNotification n, TimeSpan now)
    {
        _entries.Enqueue(new TimedChange(n, now));
        while (_entries.Count > MaxEntries)
            _entries.Dequeue();
    }

    /// <summary>Returns 0..1 opacity for a node's badge, or 0 if no badge.</summary>
    public float GetBadgeOpacity(NodeId node, TimeSpan now)
    {
        var t = LatestTimeForNode(node, now);
        if (t < TimeSpan.Zero) return 0f;
        var age = now - t;
        if (age >= FadeWindow) return 0f;
        return 1f - (float)(age / FadeWindow);
    }

    /// <summary>Returns the kind of badge (Added/Removed/Modified) or null.</summary>
    public ChangeBadgeKind? GetBadgeKind(NodeId node, TimeSpan now)
    {
        var age = TimeSpan.MaxValue;
        ChangeBadgeKind? kind = null;
        foreach (var tc in _entries)
        {
            if (tc.Notification.AffectedNodes is null || !tc.Notification.AffectedNodes.Contains(node)) continue;
            var thisAge = now - tc.AddedAt;
            if (thisAge >= FadeWindow) continue;
            if (thisAge < age)
            {
                age  = thisAge;
                kind = tc.Notification.Kind switch
                {
                    GraphChangeKind.NodesAdded    => ChangeBadgeKind.Added,
                    GraphChangeKind.NodesRemoved  => ChangeBadgeKind.Removed,
                    _                             => ChangeBadgeKind.Modified,
                };
            }
        }
        return kind;
    }

    private TimeSpan LatestTimeForNode(NodeId node, TimeSpan now)
    {
        var latest = TimeSpan.MinValue;
        foreach (var tc in _entries)
        {
            if (tc.Notification.AffectedNodes is null || !tc.Notification.AffectedNodes.Contains(node)) continue;
            if ((now - tc.AddedAt) >= FadeWindow) continue;
            if (tc.AddedAt > latest) latest = tc.AddedAt;
        }
        return latest;
    }
}

/// <summary>Kind of change badge to display on a node.</summary>
public enum ChangeBadgeKind { Added, Removed, Modified }

internal readonly record struct TimedChange(GraphChangeNotification Notification, TimeSpan AddedAt);
