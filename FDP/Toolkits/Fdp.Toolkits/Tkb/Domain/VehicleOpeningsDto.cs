using System.Collections.Generic;
using System.ComponentModel;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ <c>CE-4670</c> (VA-8) — what an opening is: a hatch a crewman's head comes out of, a door, a ramp, a cargo door.</summary>
    public enum OpeningKind : byte
    {
        Hatch = 0,
        Door = 1,
        Ramp = 2,
        CargoDoor = 3,
    }

    /// <summary>
    /// ⭐ <c>CE-4670</c> (VA-S2, VA-8; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §4) — ONE opening of a vehicle type: its id (the
    /// renderer's key, stable per type), its kind, the SEAT ROLES it serves (a class template cannot know a type's seat indices — a man boarding a seat of a
    /// served role opens it), how long it takes to open, and the model socket a renderer maps it to.
    /// </summary>
    public record OpeningDto
    {
        [Description("The opening's id, stable per type (a renderer keys its model part by it).")]
        public byte Id { get; init; }

        [Description("Hatch, Door, Ramp or CargoDoor.")]
        public OpeningKind Kind { get; init; }

        [Description("The seat roles it serves: a man boarding or leaving a seat of one of these roles opens it.")]
        public List<SeatRole> Roles { get; init; } = new();

        [Description("Seconds to open fully (and to close).")]
        public float OpenSeconds { get; init; } = 1f;

        [Description("The model socket a renderer maps the opening to.")]
        public string? Socket { get; init; }

        [Description("A type's override only: true drops the class template's opening of this id.")]
        public bool Remove { get; init; }
    }

    /// <summary>
    /// ⭐ <c>CE-4670</c> (VA-8; HL-12's rule — 🔒 "not for each tkb type separately (class templates?), but customizable per tkb type if really needed") — a TKB
    /// type's SPARSE override of its class's openings (the class template rides on the body profile matched by DIS class, <c>bodies.json</c>): an entry with
    /// the id of a class opening replaces it, <see cref="OpeningDto.Remove"/> drops it, a new id adds one.
    /// </summary>
    [TkbDescriptor("Gen.Openings")]
    public record VehicleOpeningsDto
    {
        public List<OpeningDto> Openings { get; init; } = new();
    }
}
