using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Kernel;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="WeaponChannel"/>.
    ///
    /// <para>
    /// The <see cref="FdpAutoSerializer"/> cannot correctly serialize the
    /// <c>fixed byte Params[]</c> / <c>fixed byte State[]</c> fields of
    /// <see cref="WeaponChannel"/>.  The compiler-generated FixedBuffer backing
    /// struct exposes only its first byte to JSON serialization, so on every
    /// round-trip the buffer is reduced to a single byte (all subsequent bytes
    /// zeroed).  For the <c>AimAndFire</c> action the <see cref="AimAndFireParams"/>
    /// struct occupies the first 12 bytes and embeds an <c>Entity</c> handle:
    /// after round-trip that handle has <c>Generation=0</c> and is always treated
    /// as null by <see cref="EntityRepository.IsAlive"/>, so
    /// <c>AimAndFireExecutor.Execute</c> immediately returns <c>NodeStatus.Success</c>
    /// without publishing a <c>WeaponFireIntent</c>.
    /// </para>
    ///
    /// <para>
    /// This translator replaces the auto-serializer path for the entire
    /// <c>WeaponChannel</c> component.  It serializes all channel header fields
    /// normally plus, when <c>ActiveAction == ActionIdAimAndFire</c>, the
    /// <see cref="AimAndFireParams"/> struct is expanded into explicit JSON keys
    /// with the <c>Target</c> entity stored as a stable GUID string so it
    /// survives the JSON round-trip.
    /// </para>
    /// </summary>
    public sealed unsafe class WeaponChannelTranslator : IEntityScenarioTranslator
    {
        private const string Key = "WeaponChannel";

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(WeaponChannel));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<WeaponChannel>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            // Copy to a local to get a stable stack address for pointer arithmetic.
            WeaponChannel ch = repo.GetComponent<WeaponChannel>(entity);

            var obj = new JsonObject
            {
                ["ActiveAction"]          = (int)ch.ActiveAction,
                ["DoctrineInstanceId"]    = (long)ch.DoctrineInstanceId,
                ["ActionInstanceId"]      = (long)ch.ActionInstanceId,
                ["DispatchedInstanceId"]  = (long)ch.DispatchedInstanceId,
                ["Status"]                = (int)ch.Status,
            };

            // If the channel is dispatching AimAndFire, expand the Params struct
            // so the Entity Target is preserved as a GUID-resolved string.
            if (ch.ActiveAction == CombatConstants.ActionIdAimAndFire)
            {
                WeaponChannel* chPtr = &ch;
                AimAndFireParams p = *(AimAndFireParams*)(&chPtr->Params);

                string targetGuid = p.Target.IsNull || !repo.IsAlive(p.Target)
                    ? string.Empty
                    : resolver.Resolve(p.Target);

                obj["Params_AimAndFire_Target"]       = targetGuid;
                obj["Params_AimAndFire_CooldownTicks"] = p.CooldownTicks;
            }

            return new Dictionary<string, object> { [Key] = obj };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;

            var ch = new WeaponChannel
            {
                ActiveAction         = (ushort)(obj["ActiveAction"]?.GetValue<int>()         ?? 0),
                DoctrineInstanceId   = (uint)  (obj["DoctrineInstanceId"]?.GetValue<long>()  ?? 0L),
                ActionInstanceId     = (uint)  (obj["ActionInstanceId"]?.GetValue<long>()    ?? 0L),
                DispatchedInstanceId = (uint)  (obj["DispatchedInstanceId"]?.GetValue<long>() ?? 0L),
                Status               = (NodeStatus)(obj["Status"]?.GetValue<int>()           ?? 0),
            };

            // If the channel was dispatching AimAndFire, restore the Params struct
            // with the Entity Target resolved from its saved GUID.
            if (ch.ActiveAction == CombatConstants.ActionIdAimAndFire)
            {
                var guidStr = obj["Params_AimAndFire_Target"]?.GetValue<string>() ?? string.Empty;
                Entity target = string.IsNullOrEmpty(guidStr)
                    ? Entity.Null
                    : resolver.Resolve(guidStr);

                int cooldown = obj["Params_AimAndFire_CooldownTicks"]?.GetValue<int>() ?? 0;

                var p = new AimAndFireParams { Target = target, CooldownTicks = cooldown };
                WeaponChannel* chPtr = &ch;
                *(AimAndFireParams*)(&chPtr->Params) = p;
            }

            repo.SetComponent(entity, ch);
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}
