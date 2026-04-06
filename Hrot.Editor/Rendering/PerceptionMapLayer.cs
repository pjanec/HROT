using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Vis2D.Abstractions;
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

        var linkColor = new Color(255, 60, 60, 160);

        foreach (Entity entity in _query)
        {
            ref readonly var mem = ref _world.GetComponent<TargetMemory>(entity);
            ref readonly var xfm = ref _world.GetComponent<SimTransform>(entity);

            var perceiverWorld  = new Vector2(xfm.Position.X, xfm.Position.Y);
            Vector2 perceiverScreen = Raylib.GetWorldToScreen2D(perceiverWorld, ctx.Camera);

            for (int i = 0; i < mem.Count; i++)
            {
                var targetWorld  = new Vector2(mem.PositionsX[i], mem.PositionsY[i]);
                Vector2 targetScreen = Raylib.GetWorldToScreen2D(targetWorld, ctx.Camera);

                Raylib.DrawLineEx(perceiverScreen, targetScreen, 1.5f, linkColor);
            }
        }
    }
}
