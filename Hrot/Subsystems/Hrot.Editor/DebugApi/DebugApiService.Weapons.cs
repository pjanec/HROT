using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Utility;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-3089</c> (G7) — <c>GET /entities/{networkId}/weapons[?target=&lt;networkId&gt;]</c>: a unit's weapon MOUNTS as the Brain
    /// sees them (the owner = mount 0, then the mount children), each with ammo, cooldown and its TKB numbers; with a target,
    /// every WeaponSelection input per mount and the mount <see cref="WeaponChoice"/> would fire. 📄
    /// <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §12. Read it on the BRAIN perspective (<c>Scenario</c> on a cluster).
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary><c>GET /entities/{networkId}/weapons</c>.</summary>
        public JsonNode GetEntityWeapons(long networkId, long? targetNetworkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };
            Entity target = Entity.Null;
            if (targetNetworkId is long t && (!_entityMap.TryGetEntity(t, out target) || !_world.IsAlive(target)))
                return new JsonObject { ["error"] = $"Target {t} not found." };

            Span<Entity> mounts = stackalloc Entity[8];
            int n = _world.IsComponentTypeRegistered<WeaponState>() ? WeaponMountQuery.EnumerateMounts(_world, unit, mounts) : 0;
            var list = new JsonArray();
            for (int i = 0; i < n; i++)
            {
                var m = mounts[i];
                bool child = !m.Equals(unit);
                int index = child && _world.HasComponent<WeaponMountInfo>(m) ? _world.GetComponentRO<WeaponMountInfo>(m).MountIndex : 0;
                ref readonly var ws = ref _world.GetComponentRO<WeaponState>(m);
                var dto = CombatTkb.MountOf(_world, unit, index);
                var node = new JsonObject
                {
                    ["index"] = index, ["on"] = child ? "child" : "owner",
                    ["ammo"] = ws.Ammo, ["maxAmmo"] = ws.MaxAmmo, ["cooldown"] = Math.Round(ws.CooldownSecondsRemaining, 3),
                    ["tkb"] = dto == null ? null : new JsonObject
                    {
                        ["range"] = dto.Range, ["penetration"] = dto.Penetration, ["damage"] = dto.DamagePerHit, ["muzzleVelocity"] = dto.MuzzleVelocity,
                    },
                };
                if (!target.IsNull)
                {
                    var ctx = new UtilityInputCtx { Repo = _world, Self = m, Context = target };
                    node["inputs"] = new JsonObject
                    {
                        ["hasAmmo"]       = StandardInputs.WeaponHasAmmo(in ctx),
                        ["rangeFit"]      = Math.Round(StandardInputs.WeaponRangeBandFit(in ctx), 4),
                        ["effectiveness"] = Math.Round(StandardInputs.WeaponEffectivenessVsTarget(in ctx), 4),
                        ["readiness"]     = StandardInputs.WeaponReadiness(in ctx),
                        ["roundsLeft"]    = Math.Round(StandardInputs.RoundsLeft(in ctx), 4),
                    };
                }
                list.Add(node);
            }

            var result = new JsonObject
            {
                ["networkId"] = networkId,
                ["mountChildrenEnabled"] = _world.IsComponentTypeRegistered<WeaponMountInfo>(),
                ["count"] = n,
                ["mounts"] = list,
            };
            if (!target.IsNull) result["choice"] = WeaponChoice.Choose(_world, unit, target, out _);
            return result;
        }
    }
}
