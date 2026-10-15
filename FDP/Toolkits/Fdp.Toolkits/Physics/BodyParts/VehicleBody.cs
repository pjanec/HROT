using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>
    /// ⭐ <c>CE-3211</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §9 S2-3, HL-3) — a ground vehicle's part ids,
    /// the same for every vehicle plan (a byte on the wire, an index into <see cref="PartDamage"/>).
    /// </summary>
    public static class VehiclePart
    {
        public const byte Hull = 0, Turret = 1, Gun = 2, RunningGearL = 3, RunningGearR = 4, Engine = 5;
        public const int Count = 6;

        public static string NameOf(byte part) => part switch
        {
            Hull => "Hull", Turret => "Turret", Gun => "Gun", RunningGearL => "RunningGearL", RunningGearR => "RunningGearR", Engine => "Engine",
            _ => "None",
        };
    }

    /// <summary>A kit part's shape (ui's <c>PartShape</c>).</summary>
    public enum KitShape : byte { Box, Cylinder, Cone }

    /// <summary>A cylinder's or cone's axis (ui's <c>PartAxis</c>).</summary>
    public enum KitAxis : byte { X, Y, Z }

    /// <summary>How a kit part moves: never (the hull), with the turret, or with the gun (ui's <c>PartRole</c>, the colliding ones).</summary>
    public enum KitRole : byte { Hull, Turret, Gun }

    /// <summary>Which end of a vehicle's main hull box holds its engine (S2-4).</summary>
    public enum EngineEnd : byte { None, Front, Rear }

    /// <summary>
    /// ⭐ <c>CE-3211</c> (S2-2) — one colliding part of a vehicle kit, as ui's <c>KitPart</c> states it: centre and extent are FRACTIONS of
    /// the type's length, width and height from the footprint centre on the ground. <see cref="EngineBox"/> marks the kit's main hull box,
    /// where the engine region lies (S2-4).
    /// </summary>
    public readonly record struct VehicleKitPart(byte Part, KitShape Shape, Vector3 Centre, Vector3 Extent, KitAxis Axis, float PitchDegrees,
                                                 float YawDegrees, KitRole Role, bool EngineBox = false);

    /// <summary>⭐ <c>CE-3211</c> — a vehicle kit: its default size (m), where its turret turns and its gun's trunnion (fractions), its parts.</summary>
    public sealed record VehicleKit(string Name, Vector3 DefaultSize, Vector2 TurretPivot, Vector2 GunTrunnionXZ, VehicleKitPart[] Parts)
    {
        public bool IsArticulated { get; } = Array.Exists(Parts, p => p.Role != KitRole.Hull);
    }

    /// <summary>⭐ <c>CE-3211</c> — what a vehicle TYPE collides with: its kit, its size, the turret and gun placed from the TKB, the body
    /// profile's engine end. Built once per template (<see cref="VehicleBody.Of(TkbTemplate)"/>).</summary>
    public sealed class VehicleShape
    {
        public required VehicleKit Kit { get; init; }
        public required BodyPlan Plan { get; init; }
        public required EngineEnd EngineEnd { get; init; }
        /// <summary>The type's size (m) — an entity's own <c>VehicleParams</c> length and width win (<see cref="VehicleBody.SizeOf"/>).</summary>
        public required Vector3 Size { get; init; }
        /// <summary>The kit's box offset from the reference point (a <c>Body.Geometry</c> type's), else zero.</summary>
        public required Vector3 KitToBody { get; init; }
        /// <summary>The TKB pivot's offset into the kit's box: a type with a whole <c>Body.Geometry</c> box (ui's <c>KitFromTkb</c> shift).</summary>
        public Vector3 PivotShift { get; init; }
        /// <summary>The main turret (TKB), or null — the kit's own pivots are used.</summary>
        internal TurretDto? Turret { get; init; }
        /// <summary>The barrel the kit draws (TKB), or null.</summary>
        internal WeaponMountDto? MainGun { get; init; }
        /// <summary>The bounding sphere at <see cref="Size"/>, in the BODY frame, covering every turret and gun pose (S2-9).</summary>
        public Vector3 BoundCentre { get; init; }
        public float BoundRadius { get; init; }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3211</c> (§9) — a ground vehicle's colliding boxes, posed by its turret: the SAME geometry ui's 3-D map draws (the kits are
    /// generated from <c>ShapeKits</c> into <c>VehicleBody.Tables.g.cs</c>, and <c>VehicleBodyParityTests</c> compares the posed boxes with
    /// ui's <c>KitPart.BodyTransform</c> per catalog type). The transform below is a port of that method and of <c>KitFromTkb</c>.
    /// </summary>
    public static partial class VehicleBody
    {
        /// <summary>The engine-candidate marker a posed main hull box carries until the hit's local point says Engine or Hull.</summary>
        internal const byte EngineBoxMarker = 254;

        /// <summary>The most boxes a vehicle kit poses.</summary>
        public const int MaxBoxes = 16;

        private static readonly ConditionalWeakTable<TkbTemplate, VehicleShape?[]> s_cache = new();
        private static float s_maxBoundRadius;

        /// <summary>The largest bound radius of any vehicle shape built so far (m) — the cell walk's pad (S2-9).</summary>
        public static float MaxBoundRadius => Volatile.Read(ref s_maxBoundRadius);

        /// <summary>The kit named <paramref name="name"/> (generated), or null.</summary>
        public static VehicleKit? KitNamed(string? name)
        {
            if (name == null) return null;
            foreach (var k in Kits) if (k.Name == name) return k;
            return null;
        }

        /// <summary>The shape of <paramref name="template"/>, or null when its body profile names no vehicle kit.</summary>
        public static VehicleShape? Of(TkbTemplate? template)
        {
            if (template == null) return null;
            var slot = s_cache.GetValue(template, _ => new VehicleShape?[1]);
            if (slot[0] is { } cached) return cached;
            var built = Build(template);
            slot[0] = built;
            return built;
        }

        /// <summary>The shape of <paramref name="entity"/>'s type, or null.</summary>
        public static VehicleShape? Of(EntityRepository world, Entity entity)
        {
            if (!world.IsComponentTypeRegistered<TkbIdentity>() || !world.HasComponent<TkbIdentity>(entity)
                || !world.HasSingletonManaged<ITkbDatabase>() || world.GetSingletonManaged<ITkbDatabase>() is not { } db
                || !db.TryGetByType(world.GetComponentRO<TkbIdentity>(entity).TkbType, out var t)) return null;
            return Of(t);
        }

        /// <summary>The size <paramref name="entity"/> is drawn and collides at: the type's, with its own <c>VehicleParams</c> length and width
        /// (ui's <c>SizeOf</c>).</summary>
        public static Vector3 SizeOf(EntityRepository world, Entity entity, VehicleShape shape)
        {
            if (!world.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>() || !world.HasComponent<global::CarKinem.Core.VehicleParams>(entity))
                return shape.Size;
            ref readonly var p = ref world.GetComponentRO<global::CarKinem.Core.VehicleParams>(entity);
            return new Vector3(p.Length > 0f ? p.Length : shape.Size.X, p.Width > 0f ? p.Width : shape.Size.Y, shape.Size.Z);
        }

        /// <summary>The type's size (m), as ui's <c>EntityBodyLayer3D.Resolve</c> (S2-6): <c>Body.Geometry</c> → the kinematics length and width
        /// with the shape's height → the kit's default.</summary>
        public static Vector3 TemplateSize(TkbTemplate t, VehicleKit kit)
        {
            var size = kit.DefaultSize;
            if (t.GetDescriptor<BodyGeometryDto>() is { Length: > 0f, Width: > 0f, Height: > 0f } g) return new Vector3(g.Length, g.Width, g.Height);
            var kin = t.GetDescriptor<VehicleParametersDto>();
            float length = kin is { Length: > 0f } ? kin.Length : size.X;
            float width = kin is { Width: > 0f } ? kin.Width : size.Y;
            float height = t.GetDescriptor<StrideRenderModelDefDto>() is { ShapeHeight: > 0f } r ? r.ShapeHeight : size.Z;
            return new Vector3(length, width, height);
        }

        private static VehicleShape? Build(TkbTemplate t)
        {
            if (BodyZones.Of(t) is not { Kit: { } kitName } body || KitNamed(kitName) is not { } kit) return null;
            var size = TemplateSize(t, kit);
            var geometry = t.GetDescriptor<BodyGeometryDto>();
            var turret = t.GetDescriptor<TurretSuiteDto>() is { Turrets.Count: > 0 } suite ? suite.Turrets[0] : null;
            var shape = new VehicleShape
            {
                Kit = kit, Plan = body.Plan, EngineEnd = body.EngineEnd, Size = size,
                KitToBody = geometry == null ? Vector3.Zero : new Vector3(geometry.BodyCentreX, geometry.BodyCentreY, geometry.BodyCentreZ - size.Z / 2f),
                PivotShift = geometry is { Length: > 0f, Width: > 0f, Height: > 0f } g
                    ? new Vector3(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ - size.Z / 2f) : Vector3.Zero,
                Turret = turret, MainGun = turret != null ? MainGunOf(t) : null,
            };
            var (c, r) = Bound(shape, size);
            var withBound = new VehicleShape
            {
                Kit = shape.Kit, Plan = shape.Plan, EngineEnd = shape.EngineEnd, Size = shape.Size, KitToBody = shape.KitToBody,
                PivotShift = shape.PivotShift, Turret = shape.Turret, MainGun = shape.MainGun, BoundCentre = c, BoundRadius = r,
            };
            float seen;
            while ((seen = Volatile.Read(ref s_maxBoundRadius)) < r && Interlocked.CompareExchange(ref s_maxBoundRadius, r, seen) != seen) { }
            return withBound;
        }

        /// <summary>ui's <c>KitFromTkb.MainGun</c>: the first mount on turret 0 with a forward muzzle offset.</summary>
        private static WeaponMountDto? MainGunOf(TkbTemplate t)
        {
            if (t.GetDescriptor<WeaponSuiteDto>() is not { } weapons) return null;
            foreach (var m in weapons.Mounts)
                if (m.TurretIndex == 0 && m.MuzzleOffsetX > 0f) return m;
            return null;
        }

        /// <summary>
        /// The kit's parts and pivots for a vehicle of <paramref name="size"/> — ui's <c>KitFromTkb.For</c>: the turret centred on the TKB's
        /// pivot, the barrel from the trunnion to the main gun's muzzle. A type without turret data keeps the kit's own.
        /// </summary>
        public static (VehicleKitPart[] Parts, Vector2 TurretPivot, Vector2 GunTrunnionXZ) Placed(VehicleShape shape, Vector3 size)
        {
            var kit = shape.Kit;
            if (shape.Turret is not { } turret || shape.MainGun is not { } mount || !kit.IsArticulated || size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
                return (kit.Parts, kit.TurretPivot, kit.GunTrunnionXZ);
            var pivot = new Vector3(turret.PivotX, turret.PivotY, turret.PivotZ) - shape.PivotShift;
            var trunnion = pivot + new Vector3(turret.TrunnionX, 0f, turret.TrunnionZ);
            var parts = (VehicleKitPart[])kit.Parts.Clone();
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p.Role == KitRole.Turret)
                    parts[i] = p with { Centre = new Vector3(pivot.X / size.X, pivot.Y / size.Y, p.Centre.Z) };
                else if (p.Role == KitRole.Gun && p.Shape == KitShape.Cylinder && p.Axis == KitAxis.X)
                {
                    var centre = trunnion + new Vector3(mount.MuzzleOffsetX / 2f, mount.MuzzleOffsetY, mount.MuzzleOffsetZ);
                    parts[i] = p with { Centre = centre / size, Extent = new Vector3(mount.MuzzleOffsetX / size.X, p.Extent.Y, p.Extent.Z) };
                }
            }
            return (parts, new Vector2(pivot.X / size.X, pivot.Y / size.Y), new Vector2(trunnion.X / size.X, trunnion.Z / size.Z));
        }

        /// <summary>
        /// ui's <c>KitPart.BodyTransform(size, pivots, pose)</c> (no motion): the unit box — or the unit cylinder / cone along +Z from 0 to 1 —
        /// to the BODY's frame, with the turret turned by <paramref name="azimuth"/> about its pivot and the gun raised by
        /// <paramref name="elevation"/> about its trunnion.
        /// </summary>
        public static Matrix4x4 PartTransform(in VehicleKitPart part, Vector3 size, Vector2 turretPivot, Vector2 gunTrunnionXZ, float azimuth, float elevation)
        {
            var centre = part.Centre * size;
            Matrix4x4 local;
            if (part.Shape == KitShape.Box)
                local = Matrix4x4.CreateScale(part.Extent * size);
            else
            {
                float length, diameter;
                Matrix4x4 lay;
                switch (part.Axis)
                {
                    case KitAxis.X: length = part.Extent.X * size.X; diameter = part.Extent.Z * size.Z; lay = Matrix4x4.CreateRotationY(MathF.PI / 2f); break;
                    case KitAxis.Y: length = part.Extent.Y * size.Y; diameter = part.Extent.Z * size.Z; lay = Matrix4x4.CreateRotationX(-MathF.PI / 2f); break;
                    default: length = part.Extent.Z * size.Z; diameter = part.Extent.X * size.X; lay = Matrix4x4.Identity; break;
                }
                local = Matrix4x4.CreateTranslation(0, 0, -0.5f) * Matrix4x4.CreateScale(diameter, diameter, length) * lay;
            }
            if (part.PitchDegrees != 0f) local *= Matrix4x4.CreateRotationY(part.PitchDegrees * MathF.PI / 180f);
            if (part.YawDegrees != 0f) local *= Matrix4x4.CreateRotationZ(part.YawDegrees * MathF.PI / 180f);
            var m = local * Matrix4x4.CreateTranslation(centre);
            if (part.Role == KitRole.Gun && elevation != 0f)
            {
                var trunnion = new Vector3(gunTrunnionXZ.X * size.X, 0f, gunTrunnionXZ.Y * size.Z);
                m *= Matrix4x4.CreateTranslation(-trunnion) * Matrix4x4.CreateRotationY(-elevation) * Matrix4x4.CreateTranslation(trunnion);
            }
            if (part.Role is KitRole.Turret or KitRole.Gun && azimuth != 0f)
            {
                var axis = new Vector3(turretPivot.X * size.X, turretPivot.Y * size.Y, 0f);
                m *= Matrix4x4.CreateTranslation(-axis) * Matrix4x4.CreateRotationZ(azimuth) * Matrix4x4.CreateTranslation(axis);
            }
            return m;
        }

        /// <summary>The oriented box a part's transform <paramref name="m"/> spans (a cylinder's or cone's bounding box), in the same frame.</summary>
        public static BodyBox BoxOf(byte part, KitShape shape, Matrix4x4 m, float faceYaw = 0f)
        {
            var r1 = new Vector3(m.M11, m.M12, m.M13);
            var r2 = new Vector3(m.M21, m.M22, m.M23);
            var r3 = new Vector3(m.M31, m.M32, m.M33);
            float l1 = r1.Length(), l2 = r2.Length(), l3 = r3.Length();
            var unitCentre = shape == KitShape.Box ? Vector3.Zero : new Vector3(0f, 0f, 0.5f);
            var centre = Vector3.Transform(unitCentre, m);
            var rot = new Matrix4x4(r1.X / l1, r1.Y / l1, r1.Z / l1, 0f,
                                    r2.X / l2, r2.Y / l2, r2.Z / l2, 0f,
                                    r3.X / l3, r3.Y / l3, r3.Z / l3, 0f,
                                    0f, 0f, 0f, 1f);
            return new BodyBox(part, centre, new Vector3(l1, l2, l3) * 0.5f, Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rot)), faceYaw);
        }

        /// <summary>
        /// Poses <paramref name="shape"/> at <paramref name="size"/> by its turret's <paramref name="azimuth"/> / <paramref name="elevation"/>
        /// (radians, ui's convention — <c>TurretPose</c> passes straight through) into <paramref name="boxes"/>, in the BODY frame (the
        /// reference point at the origin). The main hull box carries <see cref="EngineBoxMarker"/> until <see cref="PartOf"/> resolves it.
        /// </summary>
        public static int Pose(VehicleShape shape, Vector3 size, float azimuth, float elevation, Span<BodyBox> boxes)
        {
            var (parts, pivot, trunnion) = Placed(shape, size);
            if (!shape.Kit.IsArticulated) { azimuth = 0f; elevation = 0f; }
            var toBody = Matrix4x4.CreateTranslation(shape.KitToBody);
            int n = 0;
            foreach (var p in parts)
            {
                if (n >= boxes.Length) break;
                var m = PartTransform(p, size, pivot, trunnion, azimuth, elevation) * toBody;
                byte id = p.EngineBox && shape.EngineEnd != EngineEnd.None ? EngineBoxMarker : p.Part;
                boxes[n++] = BoxOf(id, p.Shape, m, p.Role == KitRole.Hull ? 0f : azimuth);
            }
            return n;
        }

        /// <summary>The part a hit on a posed box is: the engine region of the main hull box is <see cref="VehiclePart.Engine"/> (S2-4).</summary>
        public static byte PartOf(VehicleShape shape, byte boxPart, Vector3 localPoint)
        {
            if (boxPart != EngineBoxMarker) return boxPart;
            return shape.EngineEnd switch
            {
                EngineEnd.Rear when localPoint.X < -1f / 6f => VehiclePart.Engine,
                EngineEnd.Front when localPoint.X > 1f / 6f => VehiclePart.Engine,
                _ => VehiclePart.Hull,
            };
        }

        /// <summary>The bound sphere at <paramref name="size"/> covering every turret and gun pose: each articulated part's reach from its
        /// pivot (and trunnion), plus the pivot's distance from the centre — conservative by the triangle inequality (S2-9).</summary>
        public static (Vector3 Centre, float Radius) Bound(VehicleShape shape, Vector3 size)
        {
            var (parts, pivotF, trunnionF) = Placed(shape, size);
            var centre = new Vector3(0f, 0f, size.Z / 2f) + shape.KitToBody;
            float radius = 0f;
            foreach (var p in parts)
            {
                var box = BoxOf(p.Part, p.Shape, PartTransform(p, size, pivotF, trunnionF, 0f, 0f) * Matrix4x4.CreateTranslation(shape.KitToBody));
                float reach = box.Half.Length();
                var pivot = new Vector3(pivotF.X * size.X, pivotF.Y * size.Y, box.Centre.Z);
                var trunnion = new Vector3(trunnionF.X * size.X, 0f, trunnionF.Y * size.Z) + shape.KitToBody;
                float r = p.Role switch
                {
                    KitRole.Turret => Vector3.Distance(box.Centre, pivot) + reach + Vector3.Distance(pivot, centre),
                    KitRole.Gun => Vector3.Distance(box.Centre, trunnion) + reach + Vector3.Distance(trunnion, pivot) + Vector3.Distance(pivot, centre),
                    _ => Vector3.Distance(box.Centre, centre) + reach,
                };
                radius = MathF.Max(radius, r);
            }
            return (centre, radius);
        }
    }
}
