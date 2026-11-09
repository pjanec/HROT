using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using System;
using Fdp.Presentation.Forms;
using Fdp.Presentation.Panels.ReplayBrowser;

namespace Hrot.Editor.AiShared.Review;

/// <summary>
/// ⭐ Review §6.13 (RW-1; mockup <c>docs/UX/replica-watch-visual.svg</c> ①) — the watch EYE: the <see cref="IFormRowTrailer"/> a host installs on the Details forms. One eye at the end of every ATTRIBUTE row (a field with an
/// <see cref="FormField.Attribute"/>), only in the MERGED view for the one selected networked unit; teal = watched, grey = not; a click toggles the attribute in the session's <see cref="Fdp.Toolkit.ReplayBrowser.Federation.ReplicaWatchList"/>.
/// ONE class for both hosts. ⭐ 0 B per frame: it reads two ints and a set lookup.
/// </summary>
public sealed class ReplicaWatchEye : IFormRowTrailer
{
    private readonly ReplicaWatchLane _lane;
    private readonly Func<ByNodeSection?> _byNode;
    /// <param name="byNode">The host's By node section (its attribute view draws the watched attributes' per-node values); may resolve to null.</param>
    public ReplicaWatchEye(ReplicaWatchLane lane, Func<ByNodeSection?>? byNode = null) { _lane = lane; _byNode = byNode ?? (() => null); }

    public string Glyph => IconsFontAwesome6.Eye;

    public bool Available(IReadOnlyList<Entity> selection) => selection.Count == 1 && _lane.Merged && _lane.Unit > 0;

    public int StateOf(FormField field, IReadOnlyList<Entity> selection)
        => field.Attribute == null ? 0 : _lane.Watch.IsWatched(_lane.Unit, field.Attribute, field.SubIndex) ? 2 : 1;

    public string TipOf(int state) => state == 2 ? "Watched on the timeline — click to stop" : "Watch this attribute on the timeline (replicas that disagree with the owner for long)";

    public void Click(FormField field, IReadOnlyList<Entity> selection)
    {
        if (field.Attribute != null) _lane.Watch.Toggle(_lane.Unit, field.Attribute, field.SubIndex, label: field.Label);
    }

    /// <summary>⭐ RW-6 — under the form: "&lt;attribute&gt; — by node" for each watched attribute THIS form declares (a watched attribute shows under its own tab).</summary>
    public void Footer(FormDecl decl, IReadOnlyList<Entity> selection)
    {
        if (_byNode() is not { } section || selection.Count != 1) return;
        var items = _lane.Watch.ItemsOf(_lane.Unit);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            bool here = false;
            for (int f = 0; f < decl.Fields.Count && !here; f++) here = decl.Fields[f].Attribute == it.Path && decl.Fields[f].SubIndex == it.SubIndex;
            if (here) section.DrawAttribute(selection[0], it);
        }
    }
}
