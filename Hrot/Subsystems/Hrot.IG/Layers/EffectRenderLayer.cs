using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.IG.Components;
using Raylib_cs;

namespace Hrot.IG.Layers;

/// <summary>
/// Map layer that renders ephemeral <see cref="VisualEffectState"/> entities spawned
/// by <see cref="Hrot.IG.Systems.EventToEffectSystem"/>.
///
/// <list type="bullet">
///   <item><b>Explosion</b> — filled circle at the detonation position, sized by
///   <see cref="VisualEffectState.Scale"/> and faded by <see cref="VisualEffectState.Alpha"/>.</item>
///   <item><b>Tracer</b> — line from the shooter position to the <see cref="TracerTarget"/> endpoint,
///   faded by <see cref="VisualEffectState.Alpha"/>.</item>
/// </list>
///
/// Registered on both the offline <c>EditorSubsystem</c> canvas and the networked
/// <c>IgApplication</c> canvas so both execution paths share identical rendering.
/// </summary>
public sealed class EffectRenderLayer : IMapLayer
{
    /// <inheritdoc/>
    public string Name => "Visual Effects";

    /// <inheritdoc/>
    /// <remarks>-1 = always visible (no layer-mask culling).</remarks>
    public int LayerBitIndex => -1;

    private readonly EntityRepository _world;
    private readonly EntityQuery _query;

    /// <param name="world">The ECS world that owns the visual effect entities.</param>
    public EffectRenderLayer(EntityRepository world)
    {
        _world = world;
        _query = world.Query()
            .With<SimTransform>()
            .With<VisualEffectState>()
            .Build();
    }

    /// <inheritdoc/>
    public void Update(float dt) { }

    /// <inheritdoc/>
    public void Draw(RenderContext ctx)
    {
        foreach (var entity in _query)
        {
            ref readonly var tf     = ref _world.GetComponentRO<SimTransform>(entity);
            ref readonly var effect = ref _world.GetComponentRO<VisualEffectState>(entity);

            byte alpha  = (byte)(effect.ColorA * effect.Alpha);
            var  color  = new Color(effect.ColorR, effect.ColorG, effect.ColorB, alpha);
            var  worldPos = new Vector2(tf.Position.X, tf.Position.Y);
            var  screenPos = Raylib.GetWorldToScreen2D(worldPos, ctx.Camera);

            if (effect.Type == EffectType.Explosion)
            {
                float radius = effect.Scale * ctx.Zoom;
                Raylib.DrawCircleV(screenPos, radius, color);
            }
            else if (effect.Type == EffectType.Tracer
                  && _world.HasComponent<TracerTarget>(entity))
            {
                ref readonly var tracer = ref _world.GetComponentRO<TracerTarget>(entity);
                var targetScreenPos = Raylib.GetWorldToScreen2D(
                    new Vector2(tracer.EndX, tracer.EndY), ctx.Camera);

                Raylib.DrawLineEx(screenPos, targetScreenPos,
                    VisualEffectStateConstants.EffectLineWidthPx, color);
            }
        }
    }

    /// <inheritdoc/>
    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;

    /// <inheritdoc/>
    public bool HandleHover(Vector2 worldPos) => false;

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MouseButton button) => false;

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

    /// <inheritdoc/>
    public bool HandleKeyPressed(KeyboardKey key) => false;

    /// <inheritdoc/>
    public Entity? PickEntity(Vector2 worldPos) => null;
}
