using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Editor.AiShared.Comparison.Rendering;

/// <summary>
/// Where an annotation badge is placed relative to the graph element.
/// </summary>
public enum AnnotationPlacement
{
    /// <summary>Badge drawn on the node body.</summary>
    NodeBadge,
    /// <summary>Badge drawn at the midpoint of a connection edge (both endpoints found).</summary>
    EdgeMidpoint,
    /// <summary>Badge drawn at the surviving endpoint of a connection (one endpoint missing).</summary>
    SurvivingEndpoint,
}

/// <summary>
/// A single annotation to be drawn for one change on one graph element.
/// Built during <see cref="ComparisonAnnotationRenderer.Render"/> before any ImGui draw calls.
/// </summary>
public sealed record AnnotationRecord(
    string ElementId,
    string Severity,
    string Kind,
    string Glyph,
    Vector4 Color,
    AnnotationPlacement Placement);

/// <summary>
/// Custom canvas renderer that overlays comparison annotations on the graph canvas.
/// Runs at the <see cref="CanvasRenderPass.AfterNodes"/> pass.
/// See design section 6.4.
/// </summary>
public sealed class ComparisonAnnotationRenderer : ICustomCanvasRenderer
{
    private readonly ComparisonSessionRegistry _sessionRegistry;
    private Guid _assetId;

    private readonly List<AnnotationRecord> _lastFrameAnnotations = new();

    public string Id => "comparison.annotations";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public bool IsActive => _sessionRegistry.GetSession(_assetId) != null;

    /// <summary>Records built during the last Render() call; inspectable by tests.</summary>
    internal IReadOnlyList<AnnotationRecord> LastFrameAnnotations => _lastFrameAnnotations;

    public ComparisonAnnotationRenderer(ComparisonSessionRegistry sessionRegistry)
    {
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
    }

    /// <summary>Sets the asset that this renderer should annotate.</summary>
    public void SetActiveAsset(Guid assetId) => _assetId = assetId;

    public void Render(ICanvasRenderContext ctx)
    {
        var session = _sessionRegistry.GetSession(_assetId);
        if (session == null)
        {
            _lastFrameAnnotations.Clear();
            return;
        }

        _lastFrameAnnotations.Clear();

        foreach (var change in session.Response.Changes)
        {
            // Skip changes whose severity is not in the enabled set.
            if (!session.EnabledSeverities.Contains(change.Severity))
                continue;

            BuildAnnotations(ctx, change);
        }

        // Guard: skip draw calls when not inside a live ImGui frame (e.g. unit tests).
        var dl = ctx.DrawList;
        if (Unsafe.As<ImDrawListPtr, nint>(ref dl) == 0) return;

        foreach (var record in _lastFrameAnnotations)
        {
            DrawAnnotation(dl, ctx, record);
        }
    }

    // ---- Annotation-building (record-then-draw pattern) ---------------------

    private void BuildAnnotations(ICanvasRenderContext ctx, ComparisonChange change)
    {
        // variable_renamed: badge every node that references the old variable name.
        if (string.Equals(change.Kind, "variable_renamed", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(change.OldValue)) return;

            foreach (var node in ctx.Graph.Nodes)
            {
                if (!NodeReferencesVariable(node, change.OldValue)) continue;

                _lastFrameAnnotations.Add(new AnnotationRecord(
                    node.Id.Value.ToString(),
                    change.Severity,
                    change.Kind,
                    ComparisonStyleMap.GlyphForKind(change.Kind),
                    ComparisonStyleMap.ColorForSeverity(change.Severity),
                    AnnotationPlacement.NodeBadge));
            }
            return;
        }

        // connection_changed: split ElementId on "->".
        if (string.Equals(change.Kind, "connection_changed", StringComparison.OrdinalIgnoreCase))
        {
            if (change.ElementId == null) return;

            var sep = change.ElementId.IndexOf("->", StringComparison.Ordinal);
            if (sep >= 0)
            {
                var idA = change.ElementId[..sep].Trim();
                var idB = change.ElementId[(sep + 2)..].Trim();

                var nodeA = TryFindNode(ctx, idA);
                var nodeB = TryFindNode(ctx, idB);

                if (nodeA != null && nodeB != null)
                {
                    _lastFrameAnnotations.Add(new AnnotationRecord(
                        change.ElementId,
                        change.Severity,
                        change.Kind,
                        ComparisonStyleMap.GlyphForKind(change.Kind),
                        ComparisonStyleMap.ColorForSeverity(change.Severity),
                        AnnotationPlacement.EdgeMidpoint));
                }
                else if (nodeA != null || nodeB != null)
                {
                    _lastFrameAnnotations.Add(new AnnotationRecord(
                        change.ElementId,
                        change.Severity,
                        change.Kind,
                        ComparisonStyleMap.GlyphForKind(change.Kind),
                        ComparisonStyleMap.ColorForSeverity(change.Severity),
                        AnnotationPlacement.SurvivingEndpoint));
                }
                // else: neither endpoint found -- skip.
            }
            else
            {
                // Single ID for connection_changed -- treat as surviving endpoint.
                var node = TryFindNode(ctx, change.ElementId);
                if (node == null) return;

                _lastFrameAnnotations.Add(new AnnotationRecord(
                    change.ElementId,
                    change.Severity,
                    change.Kind,
                    ComparisonStyleMap.GlyphForKind(change.Kind),
                    ComparisonStyleMap.ColorForSeverity(change.Severity),
                    AnnotationPlacement.SurvivingEndpoint));
            }
            return;
        }

        // All other changes: look up the affected node by ElementId.
        if (change.ElementId == null) return;

        var graphNode = TryFindNode(ctx, change.ElementId);
        if (graphNode == null) return;

        _lastFrameAnnotations.Add(new AnnotationRecord(
            change.ElementId,
            change.Severity,
            change.Kind,
            ComparisonStyleMap.GlyphForKind(change.Kind),
            ComparisonStyleMap.ColorForSeverity(change.Severity),
            AnnotationPlacement.NodeBadge));
    }

    // ---- Draw helpers -------------------------------------------------------

    private static void DrawAnnotation(ImDrawListPtr dl, ICanvasRenderContext ctx, AnnotationRecord record)
    {
        INodeModel? node = ResolveDrawNode(ctx, record);
        if (node == null) return;

        var screenPos = ctx.Viewport.GraphToScreen(node.Position);
        var size = node.SizeOverride ?? new Vector2(120f, 40f);

        // Dashed 2px outline, 3px outside the node bounding box.
        var outlineMin = screenPos - new Vector2(3f, 3f);
        var outlineMax = screenPos + size + new Vector2(3f, 3f);
        dl.AddRect(outlineMin, outlineMax, ImGui.GetColorU32(record.Color), 0f, ImDrawFlags.None, 2f);

        // Glyph badge at upper-right corner.
        var badgePos = new Vector2(outlineMax.X - 16f, outlineMin.Y + 2f);
        dl.AddText(badgePos, ImGui.GetColorU32(record.Color), record.Glyph);
    }

    private static INodeModel? ResolveDrawNode(ICanvasRenderContext ctx, AnnotationRecord record)
    {
        if (record.Placement == AnnotationPlacement.EdgeMidpoint)
        {
            // Draw at the first endpoint for midpoint annotations.
            var sep = record.ElementId.IndexOf("->", StringComparison.Ordinal);
            if (sep < 0) return null;
            return TryFindNode(ctx, record.ElementId[..sep].Trim());
        }

        if (record.Placement == AnnotationPlacement.SurvivingEndpoint &&
            record.ElementId.Contains("->"))
        {
            var sep = record.ElementId.IndexOf("->", StringComparison.Ordinal);
            var idA = record.ElementId[..sep].Trim();
            var idB = record.ElementId[(sep + 2)..].Trim();
            return TryFindNode(ctx, idA) ?? TryFindNode(ctx, idB);
        }

        return TryFindNode(ctx, record.ElementId);
    }

    /// <summary>
    /// Parses <paramref name="elementId"/> as a GUID and looks up the node.
    /// Returns null if the string is not a valid GUID or the node does not exist.
    /// </summary>
    private static INodeModel? TryFindNode(ICanvasRenderContext ctx, string elementId)
    {
        if (!Guid.TryParse(elementId, out var guid)) return null;
        return ctx.Graph.FindNode(new NodeId(guid));
    }

    /// <summary>Returns true when the node's Title or Subtitle contains the variable name.</summary>
    private static bool NodeReferencesVariable(INodeModel node, string variableName)
    {
        if (node.Title.Contains(variableName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (node.Subtitle != null &&
            node.Subtitle.Contains(variableName, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}

