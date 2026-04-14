using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Hrot.IG.Components;
using Fdp.Core.Logging;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Raylib_cs;

namespace Hrot.ScenarioEditor.Adapters;

/// <summary>
/// Production-quality entity visualizer that renders from <see cref="ResolvedStyle"/>
/// and gates visibility via <see cref="CullingState"/>, superseding
/// <see cref="StubVisualizerAdapter"/>.
///
/// Rendering contract:
/// <list type="bullet">
///   <item>Only entities tagged visible by <see cref="CullingState.IsVisible"/> produce draw calls.</item>
///   <item>Affiliation-driven RGBA tint colours come from <see cref="ResolvedStyle"/> fields.</item>
///   <item>Symbol textures are loaded lazily from <see cref="NedVisualizerAdapterConstants.AssetBasePath"/>;
///         missing files fall back to a tinted circle.</item>
///   <item>Labels are suppressed at <see cref="Components.CullingStateConstants.LodIconOnly"/>.</item>
///   <item>Damage bar is drawn when <see cref="ResolvedStyle.DamageLevel"/> > 0.</item>
///   <item>Selection ring is drawn when <paramref name="isSelected"/> is <c>true</c>.</item>
/// </list>
///
/// All sizes, thresholds, and asset paths are referenced from
/// <see cref="NedVisualizerAdapterConstants"/> (Â§CODE-STANDARDS Â§1).
/// No allocations on the hot rendering path (Â§CODE-STANDARDS Â§4).
/// </summary>
public class NedVisualizerAdapter : IVisualizerAdapter
{
    // Texture cache -- allocations occur only on first encounter of each texture name.
    private readonly Dictionary<string, Texture2D> _textureCache = new();
    private readonly HashSet<int> _renderTracedEntities = new();
    private readonly long _localNodeId;
    private const string RenderTraceEntityEnvVar = "HROT_TRACE_ENTITY_ID";
    internal static int? RenderTraceEntityIdOverride { get; set; }

    public NedVisualizerAdapter(long localNodeId = 0)
    {
        _localNodeId = localNodeId;
    }

    // â”€â”€ IVisualizerAdapter â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>null</c> (skipping all rendering) when:
    /// <list type="bullet">
    ///   <item>The entity has no <see cref="SimTransform"/>.</item>
    ///   <item>The entity has no <see cref="CullingState"/> (system has not run yet).</item>
    ///   <item><see cref="CullingState.IsVisible"/> is <c>false</c> (entity is off-screen).</item>
    /// </list>
    /// </remarks>
    public Vector2? GetPosition(ISimulationView view, Entity entity)
    {
        if (!view.HasComponent<SimTransform>(entity))
            return null;

        if (!view.HasComponent<CullingState>(entity))
            return null;

        ref readonly var culling = ref view.GetComponentRO<CullingState>(entity);
        if (!culling.IsVisible)
            return null;

        ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);
        return new Vector2(transform.Position.X, transform.Position.Y);
    }

    /// <inheritdoc/>
    /// <remarks>Called inside Raylib <c>BeginMode2D</c>.</remarks>
    public void Render(
        ISimulationView view,
        Entity          entity,
        Vector2         position,
        RenderContext   ctx,
        bool            isSelected,
        bool            isHovered)
    {
        var traceEntityId = ResolveRenderTraceEntityId();
        if (traceEntityId.HasValue
            && traceEntityId.Value == entity.Index
            && _renderTracedEntities.Add(entity.Index))
        {
                var positionLabel = string.Concat(position.X, ",", position.Y);
                FdpLog<NedVisualizerAdapter>.Debug(
                    "[Node-{0}] Render: Drawing Entity={1} at ({2})", _localNodeId, entity.Index, positionLabel);
        }

        // â”€â”€ Resolve style â€” fall back to unknown white when absent â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        Color  tint        = new Color(
            ResolvedStyleConstants.UnknownTintR,
            ResolvedStyleConstants.UnknownTintG,
            ResolvedStyleConstants.UnknownTintB,
            ResolvedStyleConstants.UnknownTintA);
        string textureName = string.Empty;
        string labelText   = string.Empty;
        float  damage      = 0f;

        if (view.HasComponent<ResolvedStyle>(entity))
        {
            ref readonly var style = ref view.GetComponentRO<ResolvedStyle>(entity);
            tint        = new Color(style.TintR, style.TintG, style.TintB, style.TintA);
            textureName = style.GetTextureName();
            labelText   = style.GetLabelText();
            damage      = style.DamageLevel;
        }

        // â”€â”€ Read LOD level (already guaranteed visible via GetPosition gate) â”€â”€
        byte lod = CullingStateConstants.LodFull;
        if (view.HasComponent<CullingState>(entity))
        {
            ref readonly var culling = ref view.GetComponentRO<CullingState>(entity);
            lod = culling.LodLevel;
        }

        // â”€â”€ Apply selection/hover tint override â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Primary selection â†’ green tint; secondary selection â†’ yellow; hover â†’ orange.
        bool isPrimary = isSelected
            && view.HasComponent<SelectionState>(entity)
            && view.GetComponentRO<SelectionState>(entity).IsPrimarySelection;

        Color drawTint = isPrimary  ? Color.Green
                       : isSelected ? Color.Yellow
                       : isHovered  ? Color.Orange
                       :              tint;

        // â”€â”€ Icon / fallback circle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (!string.IsNullOrEmpty(textureName))
        {
            var tex = TryGetTexture(textureName);
            if (tex.HasValue)
            {
                float scale = lod == CullingStateConstants.LodIconOnly
                    ? NedVisualizerAdapterConstants.LodIconOnlyScale
                    : NedVisualizerAdapterConstants.DefaultScale;

                var origin = new Vector2(
                    tex.Value.Width  * scale * 0.5f,
                    tex.Value.Height * scale * 0.5f);

                Raylib.DrawTextureEx(tex.Value, position - origin, 0f, scale, drawTint);
            }
            else
            {
                DrawFallbackCircle(position, drawTint);
            }
        }
        else
        {
            DrawFallbackCircle(position, drawTint);
        }

        // â”€â”€ Label (suppressed at LOD 2 â€” icon-only) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (lod < CullingStateConstants.LodIconOnly && !string.IsNullOrEmpty(labelText))
        {
            Raylib.DrawText(
                labelText,
                (int)position.X,
                (int)(position.Y + NedVisualizerAdapterConstants.LabelOffsetPx),
                NedVisualizerAdapterConstants.LabelFontSize,
                Color.White);
        }

        // â”€â”€ Damage bar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (damage > 0f)
        {
            var barPos = new Vector2(
                position.X - NedVisualizerAdapterConstants.DamageBarHalfWidth,
                position.Y - NedVisualizerAdapterConstants.DamageBarOffsetY);
            DrawDamageBar(barPos, damage);
        }

        // â”€â”€ Selection ring â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (isSelected)
        {
            Color ringColor = isPrimary ? Color.Green : Color.Yellow;
            Raylib.DrawCircleLines(
                (int)position.X,
                (int)position.Y,
                NedVisualizerAdapterConstants.SelectionRadiusPx,
                ringColor);
        }
    }

    /// <inheritdoc/>
    public float GetHitRadius(ISimulationView view, Entity entity)
        => NedVisualizerAdapterConstants.HitRadiusWorldUnits;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the label text stored in <see cref="ResolvedStyle"/> when it is
    /// present and non-empty; otherwise <c>null</c> (no tooltip shown).
    /// </remarks>
    public string? GetHoverLabel(ISimulationView view, Entity entity)
    {
        if (!view.HasComponent<ResolvedStyle>(entity))
            return null;

        ref readonly var style = ref view.GetComponentRO<ResolvedStyle>(entity);
        var label = style.GetLabelText();
        return string.IsNullOrEmpty(label) ? null : label;
    }

    // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void DrawFallbackCircle(Vector2 pos, Color tint)
        => Raylib.DrawCircle(
            (int)pos.X,
            (int)pos.Y,
            NedVisualizerAdapterConstants.FallbackCircleRadiusPx,
            tint);

    private static void DrawDamageBar(Vector2 pos, float damage)
    {
        int width  = NedVisualizerAdapterConstants.DamageBarWidth;
        int height = NedVisualizerAdapterConstants.DamageBarHeight;

        Color fill = damage < NedVisualizerAdapterConstants.DamageGreenThreshold
            ? Color.Green
            : damage < NedVisualizerAdapterConstants.DamageYellowThreshold
                ? Color.Yellow
                : Color.Red;

        Raylib.DrawRectangle(
            (int)pos.X, (int)pos.Y,
            (int)(width * damage / 100f), height,
            fill);

        Raylib.DrawRectangleLines((int)pos.X, (int)pos.Y, width, height, Color.White);
    }

    /// <summary>
    /// Lazily loads and caches the texture for <paramref name="name"/>.
    /// Returns <c>null</c> when the file does not exist so callers can draw
    /// the fallback circle instead.
    /// </summary>
    private Texture2D? TryGetTexture(string name)
    {
        if (_textureCache.TryGetValue(name, out var cached))
            return cached;

        string path = NedVisualizerAdapterConstants.AssetBasePath + name + ".png";
        if (!File.Exists(path))
            return null;

        var tex = Raylib.LoadTexture(path);
        _textureCache[name] = tex;
        return tex;
    }

    private static int? ResolveRenderTraceEntityId()
    {
        if (RenderTraceEntityIdOverride.HasValue)
            return RenderTraceEntityIdOverride.Value;

        var rawValue = Environment.GetEnvironmentVariable(RenderTraceEntityEnvVar);
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        return int.TryParse(rawValue, out var entityId) ? entityId : null;
    }
}
