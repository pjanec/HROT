using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Map.Definitions.Tkb
{
    /// <summary>
    /// Projects <see cref="VisualDefinitionDto"/> into presentation ECS components
    /// (<see cref="VisualData"/> and <see cref="EntityInfo"/>).
    ///
    /// <para>⭐ <b>UXI-23 <c>S1</c>:</b> this used to live in <c>Hrot.IG.Translators</c> and was
    /// listed by <c>IgNodeBootstrapper</c> alone. 📐 Measured 2026-08-28: that made <c>VisualData</c>
    /// an <b>IG-private</b> product, so entities built from the TKB on SimHost carried none — and the
    /// shared entity gizmos, which project from it, found nothing to draw. It now sits beside the
    /// component it writes, with the shape of its five peer translators.</para>
    ///
    /// <para>⚠⚠ <b>It returns immediately when <see cref="VisualData"/> is not registered on this
    /// node.</b> That keeps it safe to include on any topology — ⛔ but it also means a host that
    /// adds the translator and <i>forgets the component registration</i> gets <b>no error and no
    /// component</b>. 🔒 Assert the component is PRESENT on a TKB-built entity; do not assert merely
    /// that the translator is in the list.</para>
    /// <para>
    /// <see cref="ForceId"/> is derived from the MIL-STD-2525 affiliation character
    /// in <c>SymbolCode[1]</c>: 'F' = Friend, 'H' = Hostile, anything else = Neutral.
    /// </para>
    /// </summary>
    public sealed class PresentationTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(VisualDefinitionDto);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            if (!repo.IsComponentTypeRegistered<VisualData>()) return;

            var dto = template.GetDescriptor<VisualDefinitionDto>();
            if (dto == null) return;

            if (!repo.HasComponent<VisualData>(entity))
            {
                repo.AddComponent(entity, new VisualData
                {
                    SymbolCode  = new FixedString32(dto.SymbolCode),
                    ModelPath   = new FixedString64(dto.ModelPath),
                    ColorHex    = new FixedString32(dto.ColorHex),
                    MapShapeName = new FixedString32(dto.MapShapeName ?? string.Empty)
                });
            }

            if (repo.IsComponentTypeRegistered<EntityInfo>() && !repo.HasComponent<EntityInfo>(entity))
            {
                var forceId = DeriveForceId(dto.SymbolCode);
                repo.AddComponent(entity, new EntityInfo { ForceId = forceId });
            }
        }

        // MIL-STD-2525: character at index 1 is the affiliation.
        // 'F' = Friend, 'H' = Hostile. All others default to Neutral.
        private static ForceId DeriveForceId(string symbolCode)
        {
            if (symbolCode.Length < 2) return ForceId.Neutral;
            return symbolCode[1] switch
            {
                'F' or 'f' => ForceId.Friend,
                'H' or 'h' => ForceId.Hostile,
                _           => ForceId.Neutral
            };
        }
    }
}
