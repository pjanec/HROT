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
                    // ⭐ CE-3136 P-5 — the magazine beside the total (0 = no magazine)
                    ["magazineRounds"] = ws.MagazineRounds, ["magazineSize"] = ws.MagazineSize,
                    ["reloadRemaining"] = Math.Round(ws.ReloadSecondsRemaining, 3),
                    ["turret"] = dto is { TurretIndex: >= 0 } ? dto.TurretIndex : null,   // ⭐ CE-1307 — the turret it rides, or none
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
            result["turrets"] = TurretsOf(unit);   // ⭐ CE-1307 — where each turret points and what its muscle was told
            if (!target.IsNull) result["choice"] = WeaponChoice.Choose(_world, unit, target, out _);
            // ⭐ CE-3136 (T3) — the weapon row of the unit's ActionStatus, the words the map's Actions gizmo draws
            if (_world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>()
                && _world.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(unit))
            {
                var row = _world.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(unit).Weapon;
                result["status"] = new JsonObject
                {
                    ["actionId"] = row.ActionId, ["reason"] = Fdp.Toolkit.Behavior.Diagnostics.ActionStatusOf.Label(row.Reason),
                    ["progress"] = Math.Round(row.Progress, 3), ["needed"] = Math.Round(row.Needed, 3),
                    ["target"] = row.Target.IsNull ? null : (_entityMap.TryGetNetworkId(row.Target, out long tn) ? tn : (long?)null),
                    ["at"] = Math.Round(row.At, 3),
                };
            }
            return result;
        }

        /// <summary>⭐ <c>CE-1307</c> — each turret of <paramref name="unit"/>'s type: its pose (degrees off the hull), the
        /// <see cref="WeaponTask"/> its muscle runs and the muscle's last report. A turret with no part yet reads <c>part:false</c>.</summary>
        private JsonArray TurretsOf(Entity unit)
        {
            var list = new JsonArray();
            if (!_world.IsComponentTypeRegistered<TurretPose>()) return list;
            for (int i = 0; i < 8 && CombatTkb.TurretOf(_world, unit, i) != null; i++)
            {
                var part = TurretParts.Find(_world, unit, i);
                var node = new JsonObject { ["index"] = i, ["part"] = !part.IsNull };
                if (!part.IsNull)
                {
                    var pose = _world.GetComponentRO<TurretPose>(part);
                    node["azimuthDeg"] = Math.Round(pose.Azimuth * 180.0 / Math.PI, 1);
                    node["elevationDeg"] = Math.Round(pose.Elevation * 180.0 / Math.PI, 1);
                    if (_world.IsComponentTypeRegistered<WeaponTask>() && _world.HasComponent<WeaponTask>(part))
                    {
                        var task = _world.GetComponentRO<WeaponTask>(part);
                        node["task"] = new JsonObject
                        {
                            ["mode"] = task.Mode.ToString(), ["mount"] = task.MountIndex, ["epoch"] = task.Epoch,
                            ["target"] = task.Target.IsNull ? null : (_entityMap.TryGetNetworkId(task.Target, out long tn) ? tn : (long?)null),
                            ["point"] = task.Target.IsNull ? new JsonArray(Math.Round(task.Point.X, 2), Math.Round(task.Point.Y, 2), Math.Round(task.Point.Z, 2)) : null,
                            ["roundsGranted"] = task.RoundsGranted,
                        };
                    }
                    if (_world.IsComponentTypeRegistered<WeaponTaskStatus>() && _world.HasComponent<WeaponTaskStatus>(part))
                    {
                        var st = _world.GetComponentRO<WeaponTaskStatus>(part);
                        node["muscle"] = new JsonObject
                        {
                            ["state"] = st.State.ToString(), ["failure"] = st.Reason.ToString(), ["epoch"] = st.Epoch, ["roundsFired"] = st.RoundsFired,
                        };
                    }
                }
                list.Add(node);
            }
            return list;
        }
    }
}
