using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Look.Vehicles
{
    /// <summary>A box in a vehicle's BODY frame (metres; x forward, y left, z up from the box bottom — the kit's own frame): its centre and half extents.</summary>
    public readonly record struct BodyBox(Vector3 Centre, Vector3 Half)
    {
        public float Top => Centre.Z + Half.Z;
        public float Bottom => Centre.Z - Half.Z;
        public float Front => Centre.X + Half.X;
        public float Rear => Centre.X - Half.X;
    }

    /// <summary>The boxes the socket rule reads: the hull, and — when the kind has them — the turret and the cab (the first glass part).</summary>
    public readonly record struct VehicleBoxes(BodyBox Hull, BodyBox? Turret = null, BodyBox? Cab = null);

    /// <summary>
    /// A thin panel hinged on one edge: <see cref="Hinge"/> the middle of the hinge edge, <see cref="Along"/> the half-vector along the hinge, <see cref="Across"/>
    /// the full vector from the hinge to the far edge when CLOSED, rotated about <see cref="Axis"/> (unit, right-handed) by <c>progress × </c><see cref="OpenRadians"/>.
    /// <see cref="OnTurret"/>: it sits on the turret and turns with it.
    /// </summary>
    public readonly record struct HingedPanel(Vector3 Hinge, Vector3 Along, Vector3 Across, Vector3 Axis, float OpenRadians, float Thickness, bool OnTurret)
    {
        /// <summary>The panel's far-edge vector at <paramref name="progress"/> (0 closed … 1 open).</summary>
        public Vector3 AcrossAt(float progress)
            => Vector3.Transform(Across, Quaternion.CreateFromAxisAngle(Axis, Math.Clamp(progress, 0f, 1f) * OpenRadians));

        /// <summary>The panel's centre at <paramref name="progress"/>.</summary>
        public Vector3 CentreAt(float progress) => Hinge + AcrossAt(progress) * 0.5f;

        /// <summary>The panel as a box at <paramref name="progress"/>: its centre, its rotation (the box's x along <see cref="Along"/>, y along the opened
        /// <see cref="Across"/>), its half extents.</summary>
        public (Vector3 Centre, Matrix4x4 Basis, Vector3 Half) BoxAt(float progress)
        {
            var across = AcrossAt(progress);
            var ex = Vector3.Normalize(Along);
            var ey = across.LengthSquared() > 1e-8f ? Vector3.Normalize(across) : Vector3.UnitY;
            var ez = Vector3.Normalize(Vector3.Cross(ex, ey));
            var basis = new Matrix4x4(ex.X, ex.Y, ex.Z, 0, ey.X, ey.Y, ey.Z, 0, ez.X, ez.Y, ez.Z, 0, 0, 0, 0, 1);
            return (Hinge + across * 0.5f, basis, new Vector3(Along.Length(), across.Length() * 0.5f, Thickness * 0.5f));
        }
    }

    /// <summary>One light point: where, its colour, its drawn radius (m).</summary>
    public readonly record struct LightPoint(Vector3 At, Rgba Colour, float Radius, bool OnTurret = false);

    /// <summary>A crewman's head and shoulders out of an open hatch: the shoulders box (centre, half extents) and the head (centre, radius).</summary>
    public readonly record struct HeadAndShoulders(Vector3 Shoulders, Vector3 ShouldersHalf, Vector3 Head, float HeadRadius);

    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §7d D-1…D-4; mockup docs/UX/vehicle-openings-lights.svg) — WHERE a vehicle
    /// type's openings and lights sit, from the socket NAME its TKB gives (<c>bodies.json</c> / <c>Gen.Openings</c> / <c>Gen.Lights</c>) and three boxes of its shape
    /// (hull, turret, cab). ⛔ No model carries these sockets: the rule IS the placement, and it lives here so every renderer (our 3-D map, a gateway's) puts a hatch
    /// in the same place. Pure; 0 B.
    /// </summary>
    public static class VehicleSockets
    {
        /// <summary>A hatch's lid side (m) and how far it opens (100°, up about its rear edge).</summary>
        public const float HatchSide = 0.6f, HatchOpenDegrees = 100f;
        /// <summary>A ramp opens down about its bottom edge (95°); a side door out about its front edge (70°); a rear door out (100°); a canopy up (50°).</summary>
        public const float RampOpenDegrees = 95f, DoorOpenDegrees = 70f, RearDoorOpenDegrees = 100f, CanopyOpenDegrees = 50f;
        /// <summary>Panel thickness (m).</summary>
        public const float LidThickness = 0.05f;
        /// <summary>A light's core and halo radius (m).</summary>
        public const float LightRadius = 0.07f, HaloRadius = 0.18f;

        private const float Rad = MathF.PI / 180f;

        /// <summary>The hinged panel of an opening at <paramref name="socket"/> (its kind the fallback when the name is unknown); false for a socket with no place.</summary>
        public static bool Opening(string? socket, OpeningKind kind, in VehicleBoxes boxes, out HingedPanel panel)
        {
            var hull = boxes.Hull;
            var roof = boxes.Turret ?? hull;
            bool onTurret = boxes.Turret.HasValue;
            switch (socket)
            {
                case "hatch_commander": panel = Hatch(new Vector3(roof.Centre.X, roof.Centre.Y + roof.Half.Y * 0.5f, roof.Top), onTurret); return true;
                case "hatch_gunner": panel = Hatch(new Vector3(roof.Centre.X, roof.Centre.Y - roof.Half.Y * 0.5f, roof.Top), onTurret); return true;
                case "hatch_driver": panel = Hatch(new Vector3(hull.Centre.X + hull.Half.X * 0.7f, hull.Centre.Y + hull.Half.Y * 0.4f, hull.Top), false); return true;
                case "ramp_rear": panel = Ramp(hull); return true;
                case "door_front_left": panel = SideDoor(hull, +1f, 0.25f); return true;
                case "door_front_right": panel = SideDoor(hull, -1f, 0.25f); return true;
                case "door_cockpit": panel = SideDoor(hull, +1f, 0.6f); return true;
                case "door_cabin": case "door_crew": panel = SideDoor(hull, +1f, 0f); return true;
                case "door_rear": panel = RearDoor(hull); return true;
                case "canopy": panel = Canopy(boxes.Cab ?? new BodyBox(new Vector3(hull.Centre.X + hull.Half.X * 0.5f, hull.Centre.Y, hull.Centre.Z), hull.Half * new Vector3(0.3f, 0.6f, 1f))); return true;
            }
            switch (kind)   // an unknown socket name: by kind
            {
                case OpeningKind.Hatch: panel = Hatch(new Vector3(roof.Centre.X, roof.Centre.Y, roof.Top), onTurret); return true;
                case OpeningKind.Ramp: panel = Ramp(hull); return true;
                case OpeningKind.CargoDoor: panel = RearDoor(hull); return true;
                case OpeningKind.Door: panel = SideDoor(hull, +1f, 0f); return true;
            }
            panel = default;
            return false;
        }

        private static HingedPanel Hatch(Vector3 centre, bool onTurret)
        {
            // hinged at its rear edge, lies forward when shut; opens UP about the left axis (+y): forward rotates toward +z
            var hinge = centre + new Vector3(-HatchSide * 0.5f, 0f, LidThickness * 0.5f);
            return new HingedPanel(hinge, new Vector3(0f, HatchSide * 0.5f, 0f), new Vector3(HatchSide, 0f, 0f), -Vector3.UnitY, HatchOpenDegrees * Rad, LidThickness, onTurret);
        }

        private static HingedPanel Ramp(in BodyBox hull)
        {
            // the rear face: hinged at the bottom, stands up when shut; opens DOWN backward (up rotates toward −x)
            float height = hull.Half.Z * 2f * 0.8f;
            var hinge = new Vector3(hull.Rear - LidThickness * 0.5f, hull.Centre.Y, hull.Bottom + hull.Half.Z * 0.15f);
            return new HingedPanel(hinge, new Vector3(0f, hull.Half.Y * 0.7f, 0f), new Vector3(0f, 0f, height), -Vector3.UnitY, RampOpenDegrees * Rad, LidThickness * 1.6f, false);
        }

        private static HingedPanel SideDoor(in BodyBox hull, float side, float forward)
        {
            // on the side (side +1 left, −1 right), hinged at its FRONT edge, the leaf runs backward; opens outward
            const float Width = 0.9f;
            float height = MathF.Min(1.0f, hull.Half.Z * 2f * 0.75f);
            float frontX = hull.Centre.X + hull.Half.X * forward + Width * 0.5f;
            var hinge = new Vector3(frontX, hull.Centre.Y + side * (hull.Half.Y + LidThickness * 0.5f), hull.Bottom + hull.Half.Z * 0.2f + height * 0.5f);
            return new HingedPanel(hinge, new Vector3(0f, 0f, height * 0.5f), new Vector3(-Width, 0f, 0f), side * -Vector3.UnitZ, DoorOpenDegrees * Rad, LidThickness, false);
        }

        private static HingedPanel RearDoor(in BodyBox hull)
        {
            // the rear face, hinged at its LEFT edge; opens outward (backward)
            float height = hull.Half.Z * 2f * 0.7f, width = hull.Half.Y * 2f * 0.6f;
            var hinge = new Vector3(hull.Rear - LidThickness * 0.5f, hull.Centre.Y + width * 0.5f, hull.Bottom + hull.Half.Z * 0.2f + height * 0.5f);
            return new HingedPanel(hinge, new Vector3(0f, 0f, height * 0.5f), new Vector3(0f, -width, 0f), -Vector3.UnitZ, RearDoorOpenDegrees * Rad, LidThickness, false);
        }

        private static HingedPanel Canopy(in BodyBox cab)
        {
            // the cab's top, hinged at its rear; opens up
            var hinge = new Vector3(cab.Rear, cab.Centre.Y, cab.Top + LidThickness * 0.5f);
            return new HingedPanel(hinge, new Vector3(0f, cab.Half.Y * 0.9f, 0f), new Vector3(cab.Half.X * 2f, 0f, 0f), -Vector3.UnitY, CanopyOpenDegrees * Rad, LidThickness, false);
        }

        /// <summary>A crewman's head and shoulders out of <paramref name="hatch"/>'s opening (its closed footprint), standing on its centre.</summary>
        public static HeadAndShoulders ExposedCrew(in HingedPanel hatch)
        {
            var at = hatch.Hinge + hatch.Across * 0.5f;   // the opening's centre (closed position)
            var shoulders = at + new Vector3(0f, 0f, 0.14f);
            return new HeadAndShoulders(shoulders, new Vector3(0.13f, 0.21f, 0.11f), shoulders + new Vector3(0f, 0f, 0.24f), 0.11f);
        }

        /// <summary>
        /// The light points of a group at <paramref name="socket"/> (its kind the fallback) into <paramref name="into"/> (≥ 2); returns how many (0, 1 or 2 — head,
        /// tail, brake and nav lights are pairs).
        /// </summary>
        public static int Lights(string? socket, LightKind kind, in VehicleBoxes boxes, Span<LightPoint> into)
        {
            var h = boxes.Hull;
            var roof = boxes.Turret ?? h;
            var k = socket switch
            {
                "light_head" => LightKind.Head, "light_tail" => LightKind.Tail, "light_brake" => LightKind.Brake, "light_convoy" => LightKind.Convoy,
                "light_nav" => LightKind.Nav, "light_beacon" => LightKind.AntiCollision, "light_landing" => LightKind.Landing,
                "light_search" => LightKind.Searchlight, "light_interior" => LightKind.Interior, _ => kind,
            };
            float mid = h.Centre.Z - h.Half.Z * 0.1f;
            switch (k)
            {
                case LightKind.Head: return Pair(into, new Vector3(h.Front, h.Centre.Y, mid), h.Half.Y * 0.7f, ColourOf(k, true), ColourOf(k, false));
                case LightKind.Tail: return Pair(into, new Vector3(h.Rear, h.Centre.Y, mid), h.Half.Y * 0.75f, ColourOf(k, true), ColourOf(k, false));
                case LightKind.Brake: return Pair(into, new Vector3(h.Rear, h.Centre.Y, mid + h.Half.Z * 0.15f), h.Half.Y * 0.75f, ColourOf(k, true), ColourOf(k, false));
                case LightKind.Nav: return Pair(into, new Vector3(h.Centre.X, h.Centre.Y, h.Centre.Z), h.Half.Y, ColourOf(k, true), ColourOf(k, false));
                case LightKind.Convoy: into[0] = new LightPoint(new Vector3(h.Rear, h.Centre.Y, h.Centre.Z - h.Half.Z * 0.3f), ColourOf(k, true), LightRadius); return 1;
                case LightKind.AntiCollision: into[0] = new LightPoint(new Vector3(roof.Centre.X, roof.Centre.Y, roof.Top + LightRadius), ColourOf(k, true), LightRadius, boxes.Turret.HasValue); return 1;
                case LightKind.Landing: into[0] = new LightPoint(new Vector3(h.Centre.X + h.Half.X * 0.6f, h.Centre.Y, h.Bottom), ColourOf(k, true), LightRadius); return 1;
                case LightKind.Searchlight: into[0] = new LightPoint(new Vector3(h.Centre.X + h.Half.X * 0.8f, h.Centre.Y - h.Half.Y * 0.6f, h.Top + LightRadius), ColourOf(k, true), LightRadius); return 1;
                case LightKind.Interior:
                    var cab = boxes.Cab ?? h;
                    into[0] = new LightPoint(cab.Centre, ColourOf(k, true), LightRadius); return 1;
            }
            return 0;
        }

        private static int Pair(Span<LightPoint> into, Vector3 centre, float halfSpan, Rgba left, Rgba right)
        {
            into[0] = new LightPoint(centre + new Vector3(0f, halfSpan, 0f), left, LightRadius);
            into[1] = new LightPoint(centre - new Vector3(0f, halfSpan, 0f), right, LightRadius);
            return 2;
        }

        /// <summary>A light's colour by kind (D-4); <paramref name="left"/> picks a nav light's red (left) or green (right).</summary>
        public static Rgba ColourOf(LightKind kind, bool left) => kind switch
        {
            LightKind.Head => new Rgba(255, 244, 214, 255),
            LightKind.Tail => new Rgba(200, 20, 20, 255),
            LightKind.Brake => new Rgba(255, 40, 40, 255),
            LightKind.Convoy => new Rgba(120, 140, 255, 255),
            LightKind.Nav => left ? new Rgba(230, 30, 30, 255) : new Rgba(30, 220, 60, 255),
            LightKind.AntiCollision => new Rgba(255, 30, 30, 255),
            LightKind.Landing or LightKind.Searchlight => new Rgba(255, 255, 255, 255),
            LightKind.Interior => new Rgba(255, 190, 90, 255),
            _ => new Rgba(255, 255, 255, 255),
        };
    }
}
