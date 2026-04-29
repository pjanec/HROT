using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Layers;
using Raylib_cs;

namespace Hrot.Presentation.Adapters
{
    public sealed class ProjectileVisualizerAdapter : IVisualizerAdapter
    {
        public Vector2? GetPosition(ISimulationView view, Entity entity)
        {
            if (!view.HasComponent<SimTransform>(entity)) return null;
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            return new Vector2(tf.Position.X, tf.Position.Y);
        }

        public void Render(ISimulationView view, Entity entity, Vector2 position, RenderContext ctx, bool isSelected, bool isHovered)
        {
            if (view.HasComponent<SimVelocity>(entity))
            {
                ref readonly var vel = ref view.GetComponentRO<SimVelocity>(entity);
                var velocity2D = new Vector2(vel.Linear.X, vel.Linear.Y);
                var tailPos = position - (velocity2D * 0.05f);
                Raylib.DrawLineEx(tailPos, position, 1.0f, Color.Orange);
            }

            Raylib.DrawCircleV(position, 1.0f, Color.Orange);
        }

        public float GetHitRadius(ISimulationView view, Entity entity) => 1.0f;
    }

    public static class ProjectileLayerFactory
    {
        public static EntityRenderLayer CreateLayer(EntityRepository repo, ISelectionState selection, MapCanvas canvas)
        {
            var query = repo.Query()
                .With<BallisticProjectile>()
                .With<SimTransform>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            return new EntityRenderLayer(
                name: "Projectiles",
                layerBitIndex: -1,
                view: repo,
                query: query,
                adapter: new ProjectileVisualizerAdapter(),
                selection: selection)
            {
                Canvas = canvas
            };
        }
    }
}
