using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace HrotStrideApp
{
    /// <summary>
    /// Augments the NED-catalog platform/infantry templates with Stride render + collision
    /// descriptors (generic placeholders: Box for vehicles, mannequin for infantry — the two
    /// models the Stride app ships). Called once on the editor's authoritative spawn TkbDb in
    /// hosted mode so StrideVisualBindingSystem and VehicleKinematicsTkbTranslator resolve the
    /// SAME templates the scenario spawns from. NED composites (301/302/303) and tactical
    /// graphics (8801/8802/8803) are intentionally left with no render-def: they are abstract
    /// HQ markers / map overlays with no 3D body.
    /// </summary>
    public static class StrideNedRenderDescriptors
    {
        public static void Apply(ITkbDatabase tkb)
        {
            if (tkb == null) return;

            // ── Vehicles → OrientedBox / Box2x1x1 (half-extents from NedTkbCatalog dims) ──
            AddVehicle(tkb, 100, halfX: 3.97f, halfY: 1.83f, halfZ: 1.22f, height: 2.44f); // Tank_M1Abrams
            AddVehicle(tkb, 101, halfX: 3.28f, halfY: 1.80f, halfZ: 1.49f, height: 2.98f); // IFV_Bradley
            AddVehicle(tkb, 102, halfX: 2.29f, halfY: 1.08f, halfZ: 0.92f, height: 1.83f); // Truck_HMMWV
            AddVehicle(tkb, 103, halfX: 3.48f, halfY: 1.80f, halfZ: 1.12f, height: 2.23f); // Tank_T72

            // ── Infantry → Capsule / mannequinModel ──
            AddInfantry(tkb, 200); // Infantry_Rifleman
            AddInfantry(tkb, 201); // Infantry_Officer (registered only if catalog defines it; guarded)
        }

        private static void AddVehicle(ITkbDatabase tkb, long tkbType,
                                       float halfX, float halfY, float halfZ, float height)
        {
            if (!tkb.TryGetByType(tkbType, out var t) || t == null) return;
            if (t.HasDescriptor<StrideRenderModelDefDto>()) return;
            t.AddDescriptor(new StrideRenderModelDefDto
            {
                ModelAssetRef = "Models/Box2x1x1",
                ShapeKind     = CollisionShapeKind.OrientedBox,
                ShapeHeight   = height,
                BoxHalfX      = halfX,
                BoxHalfY      = halfY,
                BoxHalfZ      = halfZ,
            });
        }

        private static void AddInfantry(ITkbDatabase tkb, long tkbType)
        {
            if (!tkb.TryGetByType(tkbType, out var t) || t == null) return;
            if (t.HasDescriptor<StrideRenderModelDefDto>()) return;
            t.AddDescriptor(new StrideRenderModelDefDto
            {
                ModelAssetRef    = "Models/mannequinModel",
                SkeletonAssetRef = "Models/mannequinModel Skeleton",
                ShapeKind        = CollisionShapeKind.Capsule,
                ShapeRadius      = 0.3f,
                ShapeHeight      = 1.8f,
            });
        }
    }
}
