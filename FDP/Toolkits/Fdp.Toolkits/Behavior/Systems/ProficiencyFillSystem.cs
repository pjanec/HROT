using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐ <c>CE-2273</c> DA-A (DA-1) — the UNIT setting reaches the men: each owned man with a commander gets
    /// <see cref="CombatProficiency.Unit"/> = the nearest commander's <see cref="CombatProficiency.Authored"/> (up
    /// <see cref="UnitSubordinate.Commander"/>, at most <see cref="MaxChainDepth"/> levels), cleared when none has one. ⭐ It WRITES the man
    /// rather than <see cref="ProficiencyOf"/> walking the chain, so a reader needs nothing but the man (the hit model, backend BR-6).
    /// Idempotent: writes only on a change. 📄 Defend &amp; Attack §8a.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ProficiencyFillSystem : IEcsModuleSystem
    {
        public const int MaxChainDepth = 8;

        private readonly bool _gateOnAuthority;
        private EntityQuery? _men;

        public ProficiencyFillSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<CombatProficiency>() || !repo.IsComponentTypeRegistered<UnitSubordinate>()) return;
            _men ??= repo.Query().With<UnitSubordinate>().Build();
            foreach (var man in _men)
            {
                // on a cluster only the man's brain owner writes him (a man with no brain has no owner here to write him)
                if (_gateOnAuthority && !(repo.HasComponent<BehaviorState>(man) && repo.HasAuthority<BehaviorState>(man))) continue;
                bool found = TryUnitValue(repo, man, out float value);
                bool has = repo.HasComponent<CombatProficiency>(man);
                if (!has)
                {
                    if (!found) continue;
                    repo.AddComponent(man, new CombatProficiency { Unit = value, Set = CombatProficiency.UnitSet });
                    continue;
                }
                ref var p = ref repo.GetComponentRW<CombatProficiency>(man);
                bool set = (p.Set & CombatProficiency.UnitSet) != 0;
                if (found && (!set || p.Unit != value)) { p.Unit = value; p.Set |= CombatProficiency.UnitSet; }
                else if (!found && set) { p.Unit = 0f; p.Set = (byte)(p.Set & ~CombatProficiency.UnitSet); }
            }
            FillSideTiers(repo);
        }

        private EntityQuery? _brains, _amplifiers;

        /// <summary>
        /// ⭐⭐ Macros S3b B3 (SR-100) — every man with a brain gets his SIDE's amplifier tiers (<see cref="CombatProficiency.SideTiers"/>) from the
        /// world's <see cref="SkillAmplifier"/> (on the World Environment). ⭐ By SIDE, not by command chain — a man with no commander and a man a macro
        /// inserted are reached the same way (he is filled the tick after he exists). Writes only on a change; adds the component only for a non-zero
        /// amplifier, so a scenario that never sets one changes nothing.
        /// </summary>
        private void FillSideTiers(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<SkillAmplifier>() || !repo.IsComponentTypeRegistered<EntityInfo>() || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            SkillAmplifier amp = default;
            _amplifiers ??= repo.Query().With<SkillAmplifier>().Build();
            foreach (var e in _amplifiers) { amp = repo.GetComponentRO<SkillAmplifier>(e); break; }
            _brains ??= repo.Query().With<BehaviorState>().With<EntityInfo>().Build();
            foreach (var man in _brains)
            {
                if (_gateOnAuthority && !repo.HasAuthority<BehaviorState>(man)) continue;
                int tiers = amp.TiersOf(repo.GetComponentRO<EntityInfo>(man).ForceId);
                if (!repo.HasComponent<CombatProficiency>(man))
                {
                    if (tiers != 0) repo.AddComponent(man, new CombatProficiency { SideTiers = (sbyte)tiers });
                    continue;
                }
                if (repo.GetComponentRO<CombatProficiency>(man).SideTiers != tiers)   // ⭐ RW only on a change: a write is what egress and the recorder see
                    repo.GetComponentRW<CombatProficiency>(man).SideTiers = (sbyte)tiers;
            }
        }

        /// <summary>The nearest commander's authored proficiency above <paramref name="man"/>.</summary>
        public static bool TryUnitValue(EntityRepository repo, Entity man, out float value)
        {
            value = 0f;
            var at = man;
            for (int depth = 0; depth < MaxChainDepth; depth++)
            {
                if (!repo.HasComponent<UnitSubordinate>(at)) return false;
                var up = repo.GetComponentRO<UnitSubordinate>(at).Commander;
                if (up.IsNull || !repo.IsAlive(up)) return false;
                if (repo.HasComponent<CombatProficiency>(up))
                {
                    ref readonly var p = ref repo.GetComponentRO<CombatProficiency>(up);
                    if ((p.Set & CombatProficiency.AuthoredSet) != 0) { value = p.Authored; return true; }
                }
                at = up;
            }
            return false;
        }
    }
}
