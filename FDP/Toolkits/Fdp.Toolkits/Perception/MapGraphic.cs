using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// ⭐ <c>CE-2324</c> — this entity is a MAP GRAPHIC (a route, an area, a fire line, a weather region, a stone source): drawn on the map
    /// and given a position, but not a thing in the world. <see cref="PhysicalPresence.IsHidden"/> hides it from every candidate door, so
    /// it is never seen, found, targeted, hit, or counted as a neutral on the line of fire. Stamped from the TKB descriptor on every node
    /// (a type-level fact, like <see cref="Fdp.Core.CommandHierarchy.UnitEntity"/>), so not saved and not sent.
    /// 🔒 User, 2026-10-20: <i>"the insurgents in the urban battle open fire also on 'Avenue E north' marker entity"</i> (a route).
    /// 📄 docs/DESIGN_Entity_Interactions.md §7.2c.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MapGraphic)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct MapGraphic
    {
        /// <summary>Reserved (a component carries at least one field).</summary>
        public byte Reserved;
    }
}
