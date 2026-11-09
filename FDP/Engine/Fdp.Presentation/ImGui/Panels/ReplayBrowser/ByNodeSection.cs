using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Replication.Components;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.Panels.ReplayBrowser;

/// <summary>
/// ⭐ G-7 / CE-4354 S5 (docs/UX/DESIGN_Review_Perspective.md §6.4 I5, M-5; mockup <c>docs/UX/better-merge-visual.svg</c> ①) — <b>Details ▸ a component ▸ <i>By node</i></b>: the OWNER's value (the crown, from
/// the recorded ownership, I8) and each replica's below the component's own tree, <b>amber</b> where a replica differs from the owner's beyond the existing diff ε (<see cref="ReplicaViews.Epsilon"/> — the ONE differ).
/// It is the <see cref="Fdp.Presentation.Utils.ComponentReflector.ComponentFooter"/> hook of the Details components view, drawn only in the MERGED view (<see cref="Manager"/> non-null) for a component at least
/// two nodes hold. ⭐ The lens is computed when the merged frame, the selection or the open component changes — never per frame (a drawn steady frame allocates nothing).
/// </summary>
public sealed class ByNodeSection
{
    public const string HeaderLabel = "By node";

    /// <summary>The host's merged view's manager, or null outside the merged view (nothing is drawn).</summary>
    public Func<FederatedReplayManager?> Manager { get; }
    /// <summary>The network id of a merged-world entity (0: none — a local entity has no views).</summary>
    public Func<Entity, long> NetworkIdOf { get; }
    /// <summary>A node's name for the rows (a role when the host knows it).</summary>
    public Func<int, string> NameOf { get; }

    private readonly ReplicaViews _views;
    private (FederatedReplayManager? m, long net, Type? type, long key) _cacheKey;
    private ReplicaView? _view;
    private string[] _names = Array.Empty<string>(), _notes = Array.Empty<string>();

    /// <summary>⭐ The rows as last drawn (a rail's measurement): the screen x of each column, and whether the value was amber. Filled only while <see cref="Capture"/> is true.</summary>
    public readonly List<(int Node, bool Owner, bool Amber, float NameX, float ValueX, float NoteX, float Y)> Rows = new();
    public bool Capture { get; set; }

    public ByNodeSection(ReplicaViews views, Func<FederatedReplayManager?> manager, Func<Entity, long> networkIdOf, Func<int, string> nameOf)
    {
        _views = views ?? throw new ArgumentNullException(nameof(views));
        Manager = manager ?? throw new ArgumentNullException(nameof(manager));
        NetworkIdOf = networkIdOf ?? throw new ArgumentNullException(nameof(networkIdOf));
        NameOf = nameOf ?? throw new ArgumentNullException(nameof(nameOf));
    }

    /// <summary>The views drawn for <paramref name="entity"/> / <paramref name="type"/> now, or null when there is nothing to show (not merged, no id, fewer than two copies). Cached while the merged frame is unchanged.</summary>
    public ReplicaView? ViewOf(Entity entity, Type type)
    {
        var m = Manager();
        if (m == null) return null;
        long net = NetworkIdOf(entity);
        if (net <= 0) return null;
        long key = ReplicaViews.StateKey(m);
        if (_cacheKey.m != m || _cacheKey.net != net || _cacheKey.type != type || _cacheKey.key != key)
        {
            _cacheKey = (m, net, type, key);
            _view = _views.Compute(m, net, type);
            int n = _view?.Rows.Count ?? 0;
            _names = new string[n]; _notes = new string[n];
            for (int i = 0; i < n; i++)
            {
                var r = _view!.Rows[i];
                _names[i] = NameOf(r.NodeId);
                _notes[i] = r.IsOwner ? "owner" : r.Differs ? "Δ " + r.Delta.ToString("0.###", CultureInfo.InvariantCulture) : "—";
            }
        }
        return _view is { Rows.Count: >= 2 } ? _view : null;
    }

    /// <summary>The <see cref="Fdp.Presentation.Utils.ComponentReflector.ComponentFooter"/> hook: draws the section under component <paramref name="type"/> of <paramref name="entity"/> (the Details components view).</summary>
    public void Draw(Fdp.Presentation.Abstractions.IInspectableSession session, Entity entity, Type type)
    {
        Rows.Clear();
        var view = ViewOf(entity, type);
        if (view == null) return;
        ImGui.PushID("bynode");
        bool open = SectionHeader.Draw("hdr", HeaderLabel, RowIcon.OfGlyph(IconsFontAwesome6.Sitemap, IconPalette.Teal), ImGuiTreeNodeFlags.DefaultOpen);
        if (open && ImGui.BeginTable("##rows", 4, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("c", ImGuiTableColumnFlags.WidthFixed, 20f);
            ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, 200f);
            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthFixed, 190f);
            ImGui.TableSetupColumn("d", ImGuiTableColumnFlags.WidthStretch);
            for (int i = 0; i < view.Rows.Count; i++)
            {
                var r = view.Rows[i];
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                float y = ImGui.GetCursorScreenPos().Y;
                if (r.IsOwner) ImGui.TextColored(IconPalette.Amber, IconsFontAwesome6.Crown);
                ImGui.TableSetColumnIndex(1);
                float nameX = ImGui.GetCursorScreenPos().X;
                ImGui.TextUnformatted(_names[i]);
                ImGui.TableSetColumnIndex(2);
                float valueX = ImGui.GetCursorScreenPos().X;
                if (r.Differs) ImGui.TextColored(IconPalette.Amber, r.Value); else ImGui.TextDisabled(r.Value);
                ImGui.TableSetColumnIndex(3);
                float noteX = ImGui.GetCursorScreenPos().X;
                if (r.Differs) ImGui.TextColored(IconPalette.Amber, _notes[i]); else ImGui.TextDisabled(_notes[i]);
                if (Capture) Rows.Add((r.NodeId, r.IsOwner, r.Differs, nameX, valueX, noteX, y));
            }
            ImGui.EndTable();
        }
        ImGui.PopID();
    }
}
