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
/// It is the <see cref="Fdp.Presentation.Utils.ComponentReflector.ComponentFooter"/> hook of the Details components view, drawn only in the MERGED view (<see cref="ReviewReadModel.IsMerged"/>) for a component at least
/// two nodes hold. ⭐ The lens is computed when the merged frame, the selection or the open component changes — never per frame (a drawn steady frame allocates nothing).
/// </summary>
public sealed class ByNodeSection
{
    public const string HeaderLabel = "By node";

    /// <summary>⭐ CE-4496 R-2 — the review's view: the section ASKS for its views (<see cref="ReviewRequestCollector"/>) and reads them from the published model; ⛔ no manager here.</summary>
    public IReviewView Review { get; }
    /// <summary>The network id of a merged-world entity (0: none — a local entity has no views).</summary>
    public Func<Entity, long> NetworkIdOf { get; }
    /// <summary>A node's name for the rows (a role when the host knows it).</summary>
    public Func<int, string> NameOf { get; }

    /// <summary>⭐ §6.13 RW-6 — the session's replica watch (null: no eye, no attribute sections). The header's eye toggles it; <see cref="DrawAttribute"/> shows a watched attribute's per-node values.</summary>
    public ReplicaWatchList? Watch { get; set; }

    private readonly Dictionary<(string, int), AttrCache> _attr = new();

    private sealed class AttrCache
    {
        public ReviewReadModel? M; public long Net; public int WatchVersion = -1; public ReplicaView? View; public string Title = ""; public string[] Names = Array.Empty<string>(), Notes = Array.Empty<string>();
    }
    private (ReviewReadModel? m, long net, Type? type) _cacheKey;
    private ReplicaView? _view;
    private string[] _names = Array.Empty<string>(), _notes = Array.Empty<string>();

    /// <summary>⭐ The rows as last drawn (a rail's measurement): the screen x of each column, and whether the value was amber. Filled only while <see cref="Capture"/> is true.</summary>
    public readonly List<(int Node, bool Owner, bool Amber, float NameX, float ValueX, float NoteX, float Y)> Rows = new();
    public bool Capture { get; set; }

    public ByNodeSection(IReviewView review, Func<Entity, long> networkIdOf, Func<int, string> nameOf)
    {
        Review = review ?? throw new ArgumentNullException(nameof(review));
        NetworkIdOf = networkIdOf ?? throw new ArgumentNullException(nameof(networkIdOf));
        NameOf = nameOf ?? throw new ArgumentNullException(nameof(nameOf));
    }

    /// <summary>
    /// The views drawn for <paramref name="entity"/> / <paramref name="type"/> now, or null when there is nothing to show (not merged, no id, fewer than two copies, or the view not yet in the model —
    /// it is ASKED for here and arrives with the next model). Cached per model (0 B steady).
    /// </summary>
    public ReplicaView? ViewOf(Entity entity, Type type)
    {
        var m = Review.Model;
        if (!m.IsMerged) return null;
        long net = NetworkIdOf(entity);
        if (net <= 0) return null;
        Review.Requests.AskByNode(net, type);
        if (!ReferenceEquals(_cacheKey.m, m) || _cacheKey.net != net || _cacheKey.type != type)
        {
            _cacheKey = (m, net, type);
            _view = m.TryByNode(net, type, out var v) ? v : null;
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
        DrawComponentEye(entity, type);
        if (open) DrawRows(view, _names, _notes);
        ImGui.PopID();
    }

    private void DrawRows(ReplicaView view, string[] names, string[] notes)
    {
        if (!ImGui.BeginTable("##rows", 4, ImGuiTableFlags.SizingFixedFit)) return;
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
            ImGui.TextUnformatted(names[i]);
            ImGui.TableSetColumnIndex(2);
            float valueX = ImGui.GetCursorScreenPos().X;
            if (r.Differs) ImGui.TextColored(IconPalette.Amber, r.Value); else ImGui.TextDisabled(r.Value);
            ImGui.TableSetColumnIndex(3);
            float noteX = ImGui.GetCursorScreenPos().X;
            if (r.Differs) ImGui.TextColored(IconPalette.Amber, notes[i]); else ImGui.TextDisabled(notes[i]);
            if (Capture) Rows.Add((r.NodeId, r.IsOwner, r.Differs, nameX, valueX, noteX, y));
        }
        ImGui.EndTable();
    }

    /// <summary>The eye at the right end of the header line (teal = watched): the component's non-indexed attributes the vocabulary has are watched / unwatched together.</summary>
    private void DrawComponentEye(Entity entity, Type type)
    {
        if (Watch == null) return;
        long net = NetworkIdOf(entity);
        if (net <= 0) return;
        var paths = AttributePathsOf(type);
        if (paths.Count == 0) return;
        bool all = true;
        foreach (var path in paths) all &= Watch.IsWatched(net, path, 0);
        if (EyeButton("##eye", all, all ? "Watched on the timeline — click to stop" : "Watch this component's attributes on the timeline"))
            foreach (var path in paths) { if (all) Watch.Remove(net, path, 0); else Watch.Add(net, path, 0); }
    }

    private readonly Dictionary<Type, List<string>> _pathsOf = new();
    private List<string> AttributePathsOf(Type type)
    {
        if (_pathsOf.TryGetValue(type, out var l)) return l;
        l = new List<string>();
        foreach (var d in Fdp.Toolkit.Replication.Attributes.AttributeVocabulary.All)
            if (d.Component == type && !d.JsonPath.EndsWith(".*", StringComparison.Ordinal)) l.Add(d.JsonPath);
        return _pathsOf[type] = l;
    }

    /// <summary>A small eye toggle on the same line as the header, right-aligned. True when clicked.</summary>
    internal static bool EyeButton(string id, bool on, string tip)
    {
        float w = ImGui.GetFrameHeight();
        ImGui.SameLine(Math.Max(0f, ImGui.GetWindowWidth() - w - ImGui.GetStyle().WindowPadding.X));
        var pos = ImGui.GetCursorScreenPos();
        bool pressed = ImGui.InvisibleButton(id, new Vector2(w, ImGui.GetFrameHeight()));
        float gw = ImGui.CalcTextSize(IconsFontAwesome6.Eye).X;
        ImGui.GetWindowDrawList().AddText(new Vector2(pos.X + (w - gw) * 0.5f, pos.Y + (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(on ? IconPalette.Teal : IconPalette.Muted), IconsFontAwesome6.Eye);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        return pressed;
    }

    /// <summary>
    /// ⭐ RW-6 (mockup ① "Strength — by node") — the per-node values of ONE watched attribute of <paramref name="entity"/>: the same rows as the component's By node (owner crown, amber where a replica differs beyond the
    /// item's tolerance), read through <c>AttributeReaders</c> on each node's sandbox. The header's eye (teal) stops watching it. Drawn only in the merged view for a networked unit; cached per merged frame (0 B steady).
    /// </summary>
    public void DrawAttribute(Entity entity, ReplicaWatchItem item)
    {
        Rows.Clear();
        var m = Review.Model;
        if (!m.IsMerged || Watch == null) return;
        long net = NetworkIdOf(entity);
        if (net <= 0) return;
        Review.Requests.AskWatch(net, item);
        var id = (item.Path, item.SubIndex);
        if (!_attr.TryGetValue(id, out var c)) _attr[id] = c = new AttrCache();
        if (!ReferenceEquals(c.M, m) || c.Net != net || c.WatchVersion != Watch.Version)
        {
            c.M = m; c.Net = net; c.WatchVersion = Watch.Version;
            c.View = m.TryWatch(net, item, out var v) ? v : null;
            c.Title = item.Label + " — by node";
            int n = c.View?.Rows.Count ?? 0;
            c.Names = new string[n]; c.Notes = new string[n];
            for (int i = 0; i < n; i++)
            {
                var r = c.View!.Rows[i];
                c.Names[i] = NameOf(r.NodeId);
                c.Notes[i] = r.IsOwner ? "owner" : r.Differs ? "Δ " + r.Delta.ToString("0.###", CultureInfo.InvariantCulture) : "—";
            }
        }
        if (c.View is not { Rows.Count: >= 1 } view) return;
        ImGui.PushID(item.Path); ImGui.PushID(item.SubIndex);
        bool open = SectionHeader.Draw("hdr", c.Title, RowIcon.OfGlyph(IconsFontAwesome6.Sitemap, IconPalette.Teal), ImGuiTreeNodeFlags.DefaultOpen);
        if (EyeButton("##eye", true, "Watched on the timeline — click to stop")) Watch.Remove(net, item.Path, item.SubIndex);
        if (open) DrawRows(view, c.Names, c.Notes);
        ImGui.PopID(); ImGui.PopID();
    }
}
