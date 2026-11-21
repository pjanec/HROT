using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-6005</c> (city battle O-2; 📄 docs/DESIGN_Peek_And_Fire.md §9.10) — a HEAVY vehicle type (a tank, an IFV) whose hulk blocks the
    /// street: when it is destroyed, its Health owner asks for ONE static obstacle of <see cref="ObstacleType"/> on the hulk (the hulk's
    /// footprint and heading), which the obstacle bake makes terrain and a charge or a blade can clear (CE-6003). ⭐ Absent ⇒ none (a light
    /// car stays as it is).
    /// </summary>
    [TkbDescriptor("Combat.WreckObstacle")]
    public record WreckObstacleDto
    {
        [Description("The static-obstacle TKB type the wreck becomes (a type carrying StaticObstacleDto).")]
        public long ObstacleType { get; init; }
    }
}
