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
            if (view is not EntityRepository repo)
                return 0UL;

            var dis = repo.GetDisType(entity);
            if (dis.Value != 0UL)
                return dis.Value;

            // Header DisType is unset (e.g. the entity was spawned via TKB, or loaded without
            // the DisEntityType translator reaching this repository). Fall back to the TKB
            // template keyed by TkbType — mirroring DisEntityTypeTranslator's extract-time
            // fallback — so the shape still resolves to the correct DIS profile.
            if (repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity)
                && repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>())
            {
                var tkb = repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
                ref readonly var tkbId = ref repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity);
                if (tkb != null && tkb.TryGetByType(tkbId.TkbType, out var template))
                    return template.DisType.Value;
            }
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
            // MakeSemanticShape builds from default(DebugPrimitive), which leaves Color at
            // (0,0,0,0) — fully transparent, so the avatar would draw invisibly. Set an
            // explicit opaque color so the shape (and the magenta fallback) is visible.
            prim.Color = new Rgba32(100, 220, 255, 255);
            draw.EmitRaw(in prim);
        }
    }
}
