using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.IG.Translators
{
    /// <summary>
    /// IG-only translator that projects <see cref="VisualDefinitionDto"/> into
    /// presentation ECS components (<see cref="VisualData"/> and <see cref="EntityInfo"/>).
    /// Returns immediately when <see cref="VisualData"/> is not registered on this
    /// node so the translator is safe to include on any topology.
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

            repo.AddComponent(entity, new VisualData
            {
                SymbolCode  = new FixedString32(dto.SymbolCode),
                ModelPath   = new FixedString64(dto.ModelPath),
                ColorHex    = new FixedString32(dto.ColorHex),
                MapShapeName = new FixedString32(dto.MapShapeName ?? string.Empty)
            });

            if (repo.IsComponentTypeRegistered<EntityInfo>())
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
