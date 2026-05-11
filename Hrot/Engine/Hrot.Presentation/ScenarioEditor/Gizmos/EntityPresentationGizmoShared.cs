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

        public static void EmitPickBox(IDebugDrawBuilder draw, Entity entity, long networkId, in Vector3 position)
        {
            var pickBox = default(DebugPrimitive);
            pickBox.Shape = DebugPrimitiveShape.Box2D;
            pickBox.Space = CoordinateSpace.World;
            pickBox.TargetView = PipelineTarget.Map2D;
            pickBox.BoxCenterX = position.X;
            pickBox.BoxCenterY = position.Y;
            pickBox.BoxExtentX = 8f;
            pickBox.BoxExtentY = 8f;
            pickBox.Color = new Rgba32(0, 0, 0, 0);
            pickBox.AnchorIndex = entity.Index;
            pickBox.AnchorGeneration = (ushort)entity.Generation;
            pickBox.BoxAnchorId = networkId;
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
            uint conditionMask)
        {
            var prim = default(DebugPrimitive);
            prim.Shape = DebugPrimitiveShape.SemanticShape;
            prim.AnchorIndex = entity.Index;
            prim.AnchorGeneration = (ushort)entity.Generation;
            prim.BoxAnchorId = networkId;
            prim.ProfileId = profileId;
            prim.LengthMeters = length;
            prim.WidthMeters = width;
            prim.ConditionMask = conditionMask;
            draw.EmitRaw(in prim);
        }
    }
}
