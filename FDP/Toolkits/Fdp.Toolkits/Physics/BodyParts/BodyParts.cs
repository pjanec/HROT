using System;
using System.Numerics;
using Fdp.Toolkit.Combat;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>
    /// ⭐⭐ <c>CE-3209</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-3) — the body plans: each has its own fixed
    /// part ids (a byte), the same on every node and on the wire. S1 builds <see cref="Person"/>; the vehicle plans are S2.
    /// </summary>
    public enum BodyPlan : byte { None = 0, Person = 1, TrackedVehicle = 2, WheeledVehicle = 3, Rotorcraft = 4, WeaponSystem = 5, FixedWing = 6 }   // ⭐ CE-3215 — FixedWing appended

    /// <summary>
    /// ⭐ <c>CE-3209</c> (HL-3) — a PERSON's part ids: the block figure's shared part order (Map 3D §6, <c>BlockFigure</c>), so the drawing,
    /// the hit test, the damage and the marks name a part the same way.
    /// </summary>
    public static class PersonPart
    {
        public const byte ThighL = 0, ShinL = 1, ThighR = 2, ShinR = 3, Torso = 4, Head = 5, UpperArmL = 6, ForearmL = 7, UpperArmR = 8, ForearmR = 9;

        /// <summary>The parts a person has.</summary>
        public const int Count = 10;

        /// <summary>No part — a hit with no geometry (an area effect, a vehicle until S2).</summary>
        public const byte None = 255;

        /// <summary>A readable name (diagnostics, the shot log).</summary>
        public static string NameOf(byte part) => part switch
        {
            ThighL => "ThighL", ShinL => "ShinL", ThighR => "ThighR", ShinR => "ShinR", Torso => "Torso", Head => "Head",
            UpperArmL => "UpperArmL", ForearmL => "ForearmL", UpperArmR => "UpperArmR", ForearmR => "ForearmR", _ => "None",
        };
    }

    /// <summary>
    /// ⭐ <c>CE-3209</c> (HL-1) — one oriented box of a body, in the BODY frame (metres; x forward, y left, z up, the feet's centre at the
    /// origin) for a figure of <see cref="PersonBody.ReferenceHeight"/>: its part, centre, half-extents and rotation.
    /// </summary>
    /// <para>⭐ <c>CE-3211</c> (S2-7, D-G lifted) — <paramref name="FaceYaw"/>: the yaw (radians, about the body's up) of the frame the
    /// face entered is judged in — a turret's own azimuth, so a side shot at a turret turned toward it meets the turret's FRONT. 0 = the
    /// body's frame.</para>
    public readonly record struct BodyBox(byte Part, Vector3 Centre, Vector3 Half, Quaternion Rotation, float FaceYaw = 0f);

    /// <summary>The result of a segment meeting a body: where along it (0..1), which part, the face it entered, the point in the part.</summary>
    public readonly record struct BodyPartHitResult(float T, byte Part, Facing Face, Vector3 LocalPoint);

    /// <summary>
    /// ⭐ <c>CE-3215</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §12 S2c-2) — an AIRCRAFT's part ids (plans
    /// <see cref="BodyPlan.Rotorcraft"/> and <see cref="BodyPlan.FixedWing"/>). Rotors and gear do not collide.
    /// </summary>
    public static class AircraftPart
    {
        public const byte Fuselage = 0, Cockpit = 1, Wing = 2, Engine = 3, Tail = 4, Turret = 5, Gun = 6, Stores = 7;
        public const int Count = 8;

        public static string NameOf(byte part) => part switch
        {
            Fuselage => "Fuselage", Cockpit => "Cockpit", Wing => "Wing", Engine => "Engine", Tail => "Tail", Turret => "Turret", Gun => "Gun",
            Stores => "Stores", _ => "None",
        };

        /// <summary>Is <paramref name="plan"/> an aircraft's?</summary>
        public static bool IsAircraft(BodyPlan plan) => plan is BodyPlan.Rotorcraft or BodyPlan.FixedWing;
    }
}
