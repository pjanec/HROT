using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat.Translators
{
    /// <summary>
    /// ⭐⭐ <c>CE-2155</c> (R-263 D1 / R-2011, docs/DESIGN_Injury_States.md) — projects a type's <see cref="InjuryDefDto"/> (<c>Gen.Injury</c>)
    /// onto its entity on every node as <see cref="InjuryThresholds"/>. A type without the descriptor gets nothing: ok until dead (I2).
    /// </summary>
    public sealed class InjuryTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(InjuryDefDto);
        }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(InjuryThresholds);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<InjuryDefDto>();
            if (dto == null || !repo.IsComponentTypeRegistered<InjuryThresholds>()) return;

            if (!(dto.IncapacitatedBelow > 0f && dto.IncapacitatedBelow <= dto.WoundedBelow && dto.WoundedBelow <= 1f))
                throw new InvalidOperationException(
                    $"TKB type '{template.Name}' declares Gen.Injury with WoundedBelow {dto.WoundedBelow} and IncapacitatedBelow " +
                    $"{dto.IncapacitatedBelow}; they must satisfy 0 < IncapacitatedBelow <= WoundedBelow <= 1.");

            // ⭐ A pure TYPE fact: always (re)written from the TKB, never trusted from a saved scenario.
            var thresholds = Thresholds(dto);
            if (repo.HasComponent<InjuryThresholds>(entity)) repo.SetComponent(entity, thresholds);
            else repo.AddComponent(entity, thresholds);
        }

        /// <summary>The component a descriptor projects to.</summary>
        public static InjuryThresholds Thresholds(InjuryDefDto dto) => new()
        {
            WoundedBelow = dto.WoundedBelow,
            IncapacitatedBelow = dto.IncapacitatedBelow,
            BleedPerSecond = dto.BleedOutSeconds > 0f ? dto.IncapacitatedBelow / dto.BleedOutSeconds : 0f,
            WoundedSpeedFactor = dto.WoundedSpeedFactor,   // ⭐ CE-1082
        };
    }
}
