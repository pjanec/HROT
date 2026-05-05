using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Hrot.Editor.Rendering;

/// <summary>
/// Map rendering layer that visualises target-memory links between perceivers and their
/// tracked targets.  For each entity carrying a <see cref="TargetMemory"/> component a
/// red translucent line is drawn from the perceiver's world position to each valid
/// target slot's last-known position.
/// </summary>
public sealed unsafe class PerceptionMapLayer : IMapLayer
{
    private readonly EntityRepository _world;
    private EntityQuery?              _query;

    /// <inheritdoc/>
    public string Name => "Perception Links";

    /// <inheritdoc/>
    public int LayerBitIndex => 9;

    /// <summary>
    /// Initialises the layer with the editor <see cref="EntityRepository"/>.
    /// </summary>
    public PerceptionMapLayer(EntityRepository world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <inheritdoc/>
    public void Update(float dt) { }

    /// <inheritdoc/>
    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;

    /// <inheritdoc/>
    public Entity? PickEntity(Vector2 worldPos) => null;

    /// <inheritdoc/>
    public void Draw(RenderContext ctx)
    {
        // Lazily build query the first time Draw is called so that both component types
        // are guaranteed to be registered.
        _query ??= _world
            .Query()
            .With<TargetMemory>()
            .With<SimTransform>()
            .Build();

        float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
        float dashLength = 15.0f / zoom;
        float gapLength = 10.0f / zoom;
        uint currentTick = _world.GlobalVersion;

        foreach (Entity entity in _query)
        {
            ref readonly var mem = ref _world.GetComponent<TargetMemory>(entity);
            ref readonly var xfm = ref _world.GetComponent<SimTransform>(entity);

            var perceiverWorld  = new Vector2(xfm.Position.X, xfm.Position.Y);

            for (int i = 0; i < mem.Count; i++)
            {
                uint ageTicks = currentTick >= mem.LastSeenTick[i] ? currentTick - mem.LastSeenTick[i] : 0u;
                if (ageTicks > 60 && currentTick > 0u)
                    continue;

                float ageFade = 1.0f - Math.Clamp(ageTicks / 60.0f, 0f, 1f);
                var targetWorld  = new Vector2(mem.PositionsX[i], mem.PositionsY[i]);
                Vector2 direction = targetWorld - perceiverWorld;
                float totalDist = direction.Length();
                if (totalDist < 0.001f) continue;

                Vector2 normDir = direction / totalDist;
                float currentDist = 0f;

                while (currentDist < totalDist)
                {
                    float startT = currentDist / totalDist;
                    float endDist = MathF.Min(currentDist + dashLength, totalDist);

                    // 255 at source (0% transparent) to 64 at target (75% transparent).
                    byte currentAlpha = (byte)(255 - (191 * startT));
                    byte finalAlpha = (byte)(currentAlpha * ageFade);
                    if (finalAlpha == 0)
                    {
                        currentDist += dashLength + gapLength;
                        continue;
                    }
                    var dashColor = new Color((byte)255, (byte)60, (byte)60, finalAlpha);

                    Vector2 segStart = perceiverWorld + (normDir * currentDist);
                    Vector2 segEnd = perceiverWorld + (normDir * endDist);
                    Raylib.DrawLineEx(segStart, segEnd, 1.5f, dashColor);

                    currentDist += dashLength + gapLength;
                }
            }
        }
    }
}
