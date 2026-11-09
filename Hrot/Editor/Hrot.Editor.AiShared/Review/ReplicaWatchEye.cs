using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Forms;

namespace Hrot.Editor.AiShared.Review;

/// <summary>
/// ⭐ Review §6.13 (RW-1; mockup <c>docs/UX/replica-watch-visual.svg</c> ①) — the watch EYE: the <see cref="IFormRowTrailer"/> a host installs on the Details forms. One eye at the end of every ATTRIBUTE row (a field with an
/// <see cref="FormField.Attribute"/>), only in the MERGED view for the one selected networked unit; teal = watched, grey = not; a click toggles the attribute in the session's <see cref="Fdp.Toolkit.ReplayBrowser.Federation.ReplicaWatchList"/>.
/// ONE class for both hosts. ⭐ 0 B per frame: it reads two ints and a set lookup.
/// </summary>
public sealed class ReplicaWatchEye : IFormRowTrailer
{
    private readonly ReplicaWatchLane _lane;
    public ReplicaWatchEye(ReplicaWatchLane lane) => _lane = lane;

    public string Glyph => IconsFontAwesome6.Eye;

    public bool Available(IReadOnlyList<Entity> selection) => selection.Count == 1 && _lane.Merged && _lane.Unit > 0;

    public int StateOf(FormField field, IReadOnlyList<Entity> selection)
        => field.Attribute == null ? 0 : _lane.Watch.IsWatched(_lane.Unit, field.Attribute, field.SubIndex) ? 2 : 1;

    public string TipOf(int state) => state == 2 ? "Watched on the timeline — click to stop" : "Watch this attribute on the timeline (replicas that disagree with the owner for long)";

    public void Click(FormField field, IReadOnlyList<Entity> selection)
    {
        if (field.Attribute != null) _lane.Watch.Toggle(_lane.Unit, field.Attribute, field.SubIndex);
    }
}
