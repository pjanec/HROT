using System.Collections.Generic;
using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ <c>CE-4670</c> (VA-3, VA-8) — what a light group is (the DIS appearance vocabulary, VA-11).</summary>
    public enum LightKind : byte
    {
        Head = 0,
        Tail = 1,
        Brake = 2,
        Interior = 3,
        Convoy = 4,
        Searchlight = 5,
        Nav = 6,
        AntiCollision = 7,
        Landing = 8,
    }

    /// <summary>⭐ <c>CE-4670</c> (VA-S3, VA-8) — ONE light group of a vehicle type: its id (stable per type), kind, and the model socket a renderer maps it to.</summary>
    public record LightDto
    {
        [Description("The group's id, stable per type.")]
        public byte Id { get; init; }

        [Description("Head, Tail, Brake, Interior, Convoy, Searchlight, Nav, AntiCollision or Landing.")]
        public LightKind Kind { get; init; }

        [Description("The model socket a renderer maps the group to.")]
        public string? Socket { get; init; }

        [Description("A type's override only: true drops the class template's group of this id.")]
        public bool Remove { get; init; }
    }

    /// <summary>⭐ <c>CE-4670</c> (VA-8; HL-12's rule) — a TKB type's SPARSE override of its class's light groups (the class template rides on the body profile by DIS
    /// class, <c>bodies.json</c>), merged by id exactly as <see cref="VehicleOpeningsDto"/>.</summary>
    [TkbDescriptor("Gen.Lights")]
    public record VehicleLightsDto
    {
        public List<LightDto> Lights { get; init; } = new();
    }
}
