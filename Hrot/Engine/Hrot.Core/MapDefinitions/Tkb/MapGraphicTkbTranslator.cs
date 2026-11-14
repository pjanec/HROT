using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception;

namespace Hrot.Map.Definitions.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-2324</c> — the TKB descriptor of a MAP GRAPHIC type (a route, an area, a fire line, a weather region, a stone source):
    /// drawn on the map, positioned, but not a thing in the world. 📄 docs/DESIGN_Entity_Interactions.md §7.2c.
    /// </summary>
    public sealed class MapGraphicDef
    {
    }

    /// <summary>
    /// ⭐ <c>CE-2324</c> — a type carrying <see cref="MapGraphicDef"/> is not a body: stamp <see cref="MapGraphic"/>, which
    /// <see cref="PhysicalPresence.IsHidden"/> reads. Runs wherever the base translators run — on the creator and on every ghost
    /// promotion — so every node knows it (CE-2125's <see cref="CompositeUnitTkbTranslator"/> is the same shape).
    /// </summary>
    public sealed class MapGraphicTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(MapGraphicDef);
        }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(MapGraphic);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            if (template.GetDescriptor<MapGraphicDef>() == null || !repo.IsComponentTypeRegistered<MapGraphic>()) return;
            if (!repo.HasComponent<MapGraphic>(entity)) repo.AddComponent(entity, new MapGraphic());
        }
    }
}
