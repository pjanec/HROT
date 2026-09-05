using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Diagnostics.Contracts.Panels;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs-3</c> — THE MAP/GIZMO FEED AS A PEER OF THE PANELS.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Adoption <c>U-obs-3</c> · §UML, where
/// <c>DebugPrimitiveBuffer ..&gt; PanelSnapshotService : peer feed for the map</c> has been drawn since
/// the design was written and was the last unbuilt edge on that diagram.
///
/// <para>⭐⭐ <b>Why a peer and not a special case.</b> The map is the one surface that is not an ImGui
/// panel, and before this it was reachable only through its own endpoint. ⇒ an agent asking <i>"what is
/// on screen?"</i> had to know to ask twice. ⭐ Registered here, ONE <c>PanelSnapshot.DumpAll()</c>
/// carries the panels and the map together.</para>
///
/// <para>⚠⚠ <b>Both types already live in <c>Fdp.Diagnostics.Contracts</c></b> — <c>DebugPrimitiveBuffer</c>
/// and <c>PanelSnapshot</c> are assembly neighbours, so this adds no dependency in either direction.
/// 📌 That is why the design put the feed here rather than in a host.</para>
/// </summary>
public sealed class GizmoFrameViewModel : IPanelViewModel
{
    /// <summary>⭐⭐ THE KIND — every host's map feed reports the same one, so a conformance suite can
    /// diff a CGF map against an Editor map. ⚠ Underscore-prefixed to match the endpoint's
    /// <c>/panels/_gizmo</c> path: it is a feed, not a window a user can open.</summary>
    public const string Kind = "_gizmo";

    /// <inheritdoc/>
    public string PanelId   { get; }
    /// <inheritdoc/>
    public string PanelKind => Kind;

    /// <summary>⭐ Primitives written this frame, BEFORE the truncation cap.</summary>
    public int Count { get; }

    /// <summary>⚠ Primitives the buffer dropped on capacity overflow — a silent loss otherwise.</summary>
    public int Dropped { get; }

    /// <summary>⭐ How many this model actually carries.</summary>
    public int Emitted { get; }

    /// <summary>⛔ <c>true</c> when <see cref="Emitted"/> &lt; <see cref="Count"/>. ⚠ Reported, never
    /// silent: a reader that cannot tell a full frame from a clipped one would take "no more
    /// primitives" from an arbitrary cap.</summary>
    public bool Truncated { get; }

    private readonly JsonArray _primitives;

    internal GizmoFrameViewModel(string panelId, int count, int dropped, int emitted, JsonArray primitives)
    {
        PanelId     = panelId;
        Count       = count;
        Dropped     = dropped;
        Emitted     = emitted;
        Truncated   = emitted < count;
        _primitives = primitives;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⛔ Hand-written rather than <c>PanelDump.Of</c>: the payload is a pre-built
    /// <see cref="JsonArray"/> of shape-projected primitives, and reflection over this type would emit
    /// it as an opaque node.
    /// </remarks>
    public JsonNode Dump() => new JsonObject
    {
        ["panelId"]    = PanelId,
        ["panelKind"]  = PanelKind,
        ["count"]      = Count,
        ["dropped"]    = Dropped,
        ["emitted"]    = Emitted,
        ["truncated"]  = Truncated,
        ["primitives"] = _primitives.DeepClone().AsArray(),
    };
}

/// <summary>
/// ⭐⭐ <c>U-obs-3</c> — builds and publishes <see cref="GizmoFrameViewModel"/>. ⛔ Static and
/// caller-driven: this assembly has no frame loop, so whoever ends the gizmo frame calls
/// <see cref="Publish"/> just before resetting the buffer.
/// </summary>
public static class GizmoFramePanel
{
    /// <summary>⭐ Default cap, matching the endpoint's own default so the two agree by construction.</summary>
    public const int DefaultMax = 500;

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-485</c> — THE ADDRESS OF A HOST'S MAP FEED.</b> <c>"{host}/_gizmo"</c>.
    ///
    /// <para>⛔⛔ <b>The first cut DEFAULTED the address to the KIND</b> — <c>Publish(buffer)</c> wrote
    /// under the literal <c>"_gizmo"</c>. ⚠ Harmless with ONE publisher, and 📌 <b>invisible for exactly
    /// the reason <c>U1d</c>'s original defect was invisible: for a singleton the address and the kind
    /// are the same string.</b> ⇒ the moment a second host published — which is what the cross-host
    /// conformance work asks for — both would key the same entry and one would silently overwrite the
    /// other. ⛔ That is the precise failure <c>PanelIds</c>'s address/kind split exists to prevent, and
    /// the default reintroduced it. ⇒ ⭐ <b>there is no default any more: a caller NAMES its host.</b></para>
    /// </summary>
    public static string AddressFor(string host)
        => string.IsNullOrWhiteSpace(host)
            ? throw new ArgumentException("A gizmo feed's address needs its host's name.", nameof(host))
            : $"{host}/{GizmoFrameViewModel.Kind}";

    /// <summary>
    /// ⭐⭐⭐ <b>BUILD — a pure projection of the frame. No ImGui, no side effects.</b>
    ///
    /// <para>⛔⛔ <b>Projected PER SHAPE, never serialized wholesale.</b> 📐 <c>DebugPrimitive</c> is a
    /// 64-byte explicit-layout union whose fields OVERLAP by shape ⇒ a blanket dump emits whichever
    /// field happens to alias the bytes, and it reads as data. ⚠ A shape with no projection yet is
    /// reported as ITSELF with a note — ⛔ inventing fields for it would not be true.</para>
    /// </summary>
    public static GizmoFrameViewModel BuildViewModel(
        DebugPrimitiveBuffer buffer, string panelId, int max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var frame   = buffer.GetFrame();
        var items   = new JsonArray();
        int emitted = Math.Min(frame.Length, Math.Max(0, max));

        for (int i = 0; i < emitted; i++)
            items.Add(Describe(frame[i]));

        return new GizmoFrameViewModel(panelId, frame.Length, buffer.DroppedCount, emitted, items);
    }

    /// <summary>
    /// ⭐⭐ Declares the feed instrumented and — when capture is on — publishes this frame's model.
    /// ⚠ <b>Call it BEFORE the buffer's <c>EndFrame</c>/<c>Clear</c></b>: those reset the transient
    /// write cursor, so a call afterwards would publish an empty frame every time.
    /// </summary>
    public static GizmoFrameViewModel Publish(
        DebugPrimitiveBuffer buffer, string panelId, int max = DefaultMax)
    {
        if (string.IsNullOrWhiteSpace(panelId))
            throw new ArgumentException(
                "A gizmo feed needs its HOST's address — see AddressFor. A shared literal would make "
              + "two hosts overwrite each other in the snapshot.", nameof(panelId));

        // ⭐⭐⭐ DECLARED ALWAYS, ungated on CaptureEnabled — same rule as every converted panel:
        //   a feed nobody captured must stay distinguishable from a feed nobody instrumented.
        PanelSnapshot.DeclareInstrumented(panelId);

        var vm = BuildViewModel(buffer, panelId, max);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ One primitive, projected by its shape.</summary>
    private static JsonObject Describe(in DebugPrimitive p)
    {
        var node = new JsonObject
        {
            ["shape"] = p.Shape.ToString(),
            ["space"] = p.Space.ToString(),
            ["layer"] = p.DebugLayer,
            ["color"] = $"#{p.Color.R:X2}{p.Color.G:X2}{p.Color.B:X2}{p.Color.A:X2}",
        };

        switch (p.Shape)
        {
            case DebugPrimitiveShape.Line:
                node["from"] = Vec3(p.LineStart);
                node["to"]   = Vec3(p.LineEnd);
                break;

            case DebugPrimitiveShape.Arrow:
                node["from"] = Vec3(p.ArrowFrom);
                node["to"]   = Vec3(p.ArrowTo);
                break;

            case DebugPrimitiveShape.Sphere:
                node["center"] = Vec3(p.SphereCenter);
                node["radius"] = p.SphereRadius;
                break;

            case DebugPrimitiveShape.Box2D:
                node["center"]   = new JsonObject { ["x"] = p.BoxCenterX, ["y"] = p.BoxCenterY };
                node["extent"]   = new JsonObject { ["x"] = p.BoxExtentX, ["y"] = p.BoxExtentY };
                node["angleDeg"] = p.BoxAngleDeg;
                break;

            case DebugPrimitiveShape.Text:
                node["at"]   = new JsonObject { ["x"] = p.TextX, ["y"] = p.TextY };
                node["text"] = p.TextContent.ToString();
                break;

            case DebugPrimitiveShape.SpatialAnchor:
                node["networkId"] = p.StructNetworkId;
                break;

            default:
                node["note"] = "no field projection for this shape yet";
                break;
        }

        return node;
    }

    private static JsonObject Vec3(System.Numerics.Vector3 v)
        => new() { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z };
}
