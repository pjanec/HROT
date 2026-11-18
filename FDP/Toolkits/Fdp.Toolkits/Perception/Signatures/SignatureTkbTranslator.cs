using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> — stamps an entity's signatures from its TKB <see cref="SignaturesDto"/>. Skips each component its node
    /// does not register (the same contract as every TKB translator).
    /// </summary>
    public sealed class SignatureTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(SignaturesDto);
        }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(ThermalState);
            yield return typeof(AcousticEmitter);
            yield return typeof(Fdp.Toolkit.Perception.Radar.RadarSignature);   // ⭐ CE-4617
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<SignaturesDto>();
            if (dto?.Thermal is { } th && repo.IsComponentTypeRegistered<ThermalState>() && !repo.HasComponent<ThermalState>(entity))
                repo.AddComponent(entity, new ThermalState
                {
                    BaseSignature        = th.BaseSignature,
                    RunningHeatPerSecond = th.RunningHeatPerSecond,
                    FiringHeatPerShot    = th.FiringHeatPerShot,
                    CooldownPerSecond    = th.CooldownPerSecond,
                    ReferenceSpeed       = th.ReferenceSpeed,
                    IdleHeat             = th.IdleHeat,        // ⭐ CE-4657 — the calculator's type data (TD-3)
                    RunningHeat          = th.RunningHeat,
                    HasPowerPlant        = th.HasPowerPlant,
                });
            if (dto?.Acoustic is { } ac && repo.IsComponentTypeRegistered<AcousticEmitter>() && !repo.HasComponent<AcousticEmitter>(entity))
                repo.AddComponent(entity, new AcousticEmitter
                {
                    MovingAudibleRange     = ac.MovingAudibleRange,
                    ReferenceSpeed         = ac.ReferenceSpeed,
                    FiringAudibleRange     = ac.FiringAudibleRange,
                    DetonationAudibleRange = ac.DetonationAudibleRange,
                    MovingClass            = (byte)ac.MovingClass,
                    FiringClass            = (byte)ac.FiringClass,
                    DetonationClass        = (byte)(ac.DetonationClass == SoundSourceClass.Unknown ? SoundSourceClass.Explosion : ac.DetonationClass),
                    Family                 = (byte)ac.Family,   // ⭐ CE-3283
                });
            // ⭐ CE-4617 (RS-2, RV-12) — the radar cross-section, on every node that registers it (the solver's snapshot reads it, no TKB there)
            if (dto?.Radar is { } rd && repo.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Radar.RadarSignature>()
                && !repo.HasComponent<Fdp.Toolkit.Perception.Radar.RadarSignature>(entity))
                repo.AddComponent(entity, new Fdp.Toolkit.Perception.Radar.RadarSignature { RcsM2 = rd.RcsM2 });
        }
    }
}
