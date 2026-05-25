using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>
/// Custom canvas renderer that overlays a brief visual pulse on a WhenNode when
/// it fires at runtime in Debug mode.
///
/// In non-Debug mode (isDebugMode = false) the renderer is inactive; no allocation
/// or rendering occurs.
/// </summary>
public sealed class WhenFiringPulseRenderer : ICustomCanvasRenderer
{
    private const float PulseDuration = 0.4f;
    private static readonly Vector2 DefaultNodeSize = new(160f, 60f);

    private readonly bool _isDebugMode;
    private readonly Dictionary<NodeId, float> _pulses = new();

    public string Id => "bp.when_firing_pulse";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public bool IsActive => _isDebugMode;

    /// <param name="isDebugMode">
    /// When false the renderer is permanently inactive (no-op).
    /// Defaults to true in DEBUG builds, false in RELEASE builds.
    /// Inject false in tests to verify Release-mode behaviour.
    /// </param>
    public WhenFiringPulseRenderer(
#if DEBUG
        bool isDebugMode = true
#else
        bool isDebugMode = false
#endif
    )
    {
        _isDebugMode = isDebugMode;
    }

    /// <summary>
    /// Records a WhenNode firing event. Call from the host's debug event handler.
    /// No-op when isDebugMode is false.
    /// </summary>
    public void OnNodeFired(NodeId nodeId)
    {
        if (!_isDebugMode) return;
        _pulses[nodeId] = PulseDuration;
    }

    /// <summary>Returns true when the given node has a pending pulse.</summary>
    public bool HasPulse(NodeId nodeId) => _pulses.ContainsKey(nodeId);

    /// <summary>Number of nodes currently pulsing.</summary>
    public int ActivePulseCount => _pulses.Count;

    public void Render(ICanvasRenderContext ctx)
    {
        if (_pulses.Count == 0) return;

        var deltaTime = ImGui.GetIO().DeltaTime;
        var updates   = new List<(NodeId, float)>();

        foreach (var (nodeId, remaining) in _pulses)
        {
            float t     = remaining / PulseDuration;
            float alpha = t;

            var nodeModel = ctx.Graph.FindNode(nodeId);
            if (nodeModel is not null)
            {
                var pos  = nodeModel.Position;
                var size = nodeModel.SizeOverride ?? DefaultNodeSize;
                var expand = (1.0f - t) * 8f;
                var minG = pos - new Vector2(expand, expand);
                var maxG = pos + size + new Vector2(expand, expand);
                var minS = ctx.Viewport.GraphToScreen(minG);
                var maxS = ctx.Viewport.GraphToScreen(maxG);

                ctx.DrawList.AddRect(minS, maxS,
                    ImGui.GetColorU32(BlueprintEditorTheme.WhenFiringPulse with { W = alpha }),
                    rounding: 4f * ctx.Zoom,
                    flags: ImDrawFlags.None,
                    thickness: 3f * ctx.Zoom);
            }

            var newRemaining = remaining - deltaTime;
            updates.Add((nodeId, newRemaining));
        }

        // Apply updates after iteration to avoid mutating collection during foreach
        foreach (var (nodeId, newRemaining) in updates)
        {
            if (newRemaining <= 0f)
                _pulses.Remove(nodeId);
            else
                _pulses[nodeId] = newRemaining;
        }
    }
}
