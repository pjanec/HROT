using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos
{
    public static class EntityPresentationGizmoShared
    {
        public static void DrawSpatialAnchorFromRotation(
            IDebugDrawBuilder draw,
            long networkId,
            in Vector3 position,
            in Quaternion rotation)
        {
            var (yawDeg, pitchDeg, rollDeg) = SimMath.ToYawPitchRollDeg(rotation);
            draw.DrawSpatialAnchor(networkId, position.X, position.Y, position.Z, yawDeg, pitchDeg, rollDeg);
        }

        public static void EmitPickBox(IDebugDrawBuilder draw, Entity entity, long networkId, in Vector3 position, byte layer = 0)
        {
            var pickBox = DebugPrimitive.MakeBox2D(
                new Vector2(position.X, position.Y),
                new Vector2(8f, 8f),
                new Rgba32(0, 0, 0, 0),
                entity.Index,
                (ushort)entity.Generation,
                networkId,
                target: PipelineTarget.Map2D,
                layer: layer);
            draw.EmitRaw(in pickBox);
        }

        public static void TryGetVehicleDimensions(ISimulationView view, Entity entity, out float length, out float width)
        {
            length = 0f;
            width = 0f;
            if (!view.HasComponent<VehicleParams>(entity)) return;

            ref readonly var vp = ref view.GetComponentRO<VehicleParams>(entity);
            length = vp.Length;
            width = vp.Width;
        }

        public static ulong ResolveProfileId(ISimulationView view, Entity entity)
        {
            if (view is EntityRepository repo)
                return repo.GetDisType(entity).Value;
            return 0UL;
        }

        public static void DrawSemanticShape(
            IDebugDrawBuilder draw,
            Entity entity,
            long networkId,
            ulong profileId,
            float length,
            float width,
            uint conditionMask,
            byte layer = 0)
        {
            var prim = DebugPrimitive.MakeSemanticShape(
                (int)networkId,
                (ushort)entity.Generation,
                networkId,
                profileId,
                length,
                width,
                conditionMask,
                layer: layer);
            draw.EmitRaw(in prim);
        }
    }
}
