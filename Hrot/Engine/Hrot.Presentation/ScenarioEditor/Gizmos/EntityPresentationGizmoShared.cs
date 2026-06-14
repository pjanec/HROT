using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos
{
    public static class EntityPresentationGizmoShared
    {
        // Synthetic DIS-type values that map to named profiles via DefaultEntityShapeLibrary's
        // existing decode logic (kind/domain/cat bit fields). Used when Map2DFootprint.Shape
        // provides an authoritative category and no real DIS type is available.
        private const ulong ProfileIdHumanoid      = 0x0300_0000_0000_0000UL; // kind=3 → humanoid
        private const ulong ProfileIdGroundVehicle  = 0x0101_0000_0000_0000UL; // kind=1, domain=1 → ground_vehicle
        private const ulong ProfileIdFixedWing      = 0x0102_0100_0000_0000UL; // kind=1, domain=2, cat=1 → fixed_wing
        private const ulong ProfileIdRotaryWing     = 0x0102_1400_0000_0000UL; // kind=1, domain=2, cat=20 → rotary_wing

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

        /// <summary>
        /// Emits a SemanticShape primitive. When the entity carries a <see cref="Map2DFootprint"/>
        /// component (BATCH-S2-G2), its dims and shape category override the caller-supplied
        /// <paramref name="length"/>, <paramref name="width"/>, and <paramref name="profileId"/>.
        /// Falls back to the caller-supplied values when the component is absent, so existing
        /// behavior for entities without a footprint is exactly preserved.
        /// </summary>
        public static void DrawSemanticShape(
            IDebugDrawBuilder draw,
            ISimulationView view,
            Entity entity,
            long networkId,
            ulong profileId,
            float length,
            float width,
            uint conditionMask,
            byte layer = 0)
        {
            // BATCH-S2-G3 Part 1: prefer Map2DFootprint when registered and present.
            if (view is EntityRepository repo
                && repo.IsComponentTypeRegistered<Map2DFootprint>()
                && view.HasComponent<Map2DFootprint>(entity))
            {
                ref readonly var fp = ref view.GetComponentRO<Map2DFootprint>(entity);
                length    = fp.LengthM;
                width     = fp.WidthM;
                ulong fpProfileId = CategoryToProfileId(fp.Shape);
                if (fpProfileId != 0UL)
                    profileId = fpProfileId;
                // else: Unknown → keep the DIS-type profileId from the caller as fallback.
            }

            var prim = DebugPrimitive.MakeSemanticShape(
                entity.Index,
                (ushort)entity.Generation,
                networkId,
                profileId,
                length,
                width,
                conditionMask,
                layer: layer);
            draw.EmitRaw(in prim);
        }

        /// <summary>
        /// Maps a <see cref="GizmoShapeCategory"/> to a synthetic DIS-type ulong that
        /// <c>DefaultEntityShapeLibrary.GetShape</c> decodes to the correct named profile.
        /// Returns 0 for <see cref="GizmoShapeCategory.Unknown"/> so the caller can fall
        /// back to the real DIS type.
        /// </summary>
        private static ulong CategoryToProfileId(GizmoShapeCategory category) => category switch
        {
            GizmoShapeCategory.Humanoid      => ProfileIdHumanoid,
            GizmoShapeCategory.GroundVehicle => ProfileIdGroundVehicle,
            GizmoShapeCategory.FixedWing     => ProfileIdFixedWing,
            GizmoShapeCategory.RotaryWing    => ProfileIdRotaryWing,
            _                                => 0UL, // Unknown → let DIS fallback decide
        };
    }
}
