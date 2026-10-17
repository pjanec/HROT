using System;
using System.Numerics;
using System.Threading;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-3229</c> (AW-15) — what an observer concludes about a man's weapons. Ordered: a stronger answer is a larger value.</summary>
    public enum ArmedJudgement : byte
    {
        /// <summary>Nothing he can see is a weapon (nothing visible, or only things that look non-lethal).</summary>
        Nothing = 0,
        /// <summary>Something he cannot identify at this range, light and optics — warn and confirm, never fire on this alone.</summary>
        Unidentified = 1,
        /// <summary>A weapon he identifies as lethal (a rifle, a pistol, a toy gun that looks like one, a knife within its reach).</summary>
        IdentifiedLethal = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-3229</c> (B-7, AW-15 / A-4) — how well an observer can see a man right now: the light and the visibility AT THE MAN, and the
    /// observer's optics (1 = the naked eye; binoculars and night sights raise it, B-12).
    /// </summary>
    public readonly struct SeeingConditions
    {
        public SeeingConditions(float relativeLight, float visibilityM, float optics)
        {
            RelativeLight = relativeLight; VisibilityM = visibilityM; Optics = optics;
        }

        /// <summary>Light relative to a clear noon, 0…1 (<see cref="Fdp.Toolkit.Weather.WeatherSample.RelativeLight"/>).</summary>
        public float RelativeLight { get; }
        /// <summary>Horizontal visibility, m (fog, rain).</summary>
        public float VisibilityM { get; }
        /// <summary>The observer's optics, a multiplier of the identification range; 1 = the naked eye.</summary>
        public float Optics { get; }

        /// <summary>A clear day, no optics — what a world without a World Environment reads (every scenario that predates the clock).</summary>
        public static SeeingConditions ClearDay => new(1f, float.PositiveInfinity, 1f);

        /// <summary>
        /// The conditions for <paramref name="observer"/> looking at <paramref name="man"/>: the weather AT the man
        /// (<see cref="Fdp.Toolkit.Weather.WeatherField.At(EntityRepository, Vector3)"/>), or a clear day when the world has no World
        /// Environment; optics 1 until the held item exists (B-12, <c>CE-3234</c>).
        /// </summary>
        public static SeeingConditions Of(EntityRepository world, Entity observer, Entity man)
        {
            if (!Fdp.Toolkit.Weather.ScenarioTime.TryClock(world, out _) || !world.HasComponent<SimTransform>(man)) return ClearDay;
            var w = Fdp.Toolkit.Weather.WeatherField.At(world, world.GetComponentRO<SimTransform>(man).Position);
            return new SeeingConditions(w.RelativeLight, w.VisibilityM, 1f);
        }
    }

    /// <summary>⭐ <c>CE-3229</c> (B-7, AW-15; Civilians §7.8 A-4) — how far an item of a given size can be identified.</summary>
    public static class Identification
    {
        /// <summary>Metres of identification range per metre of item size, by day, to the naked eye: a 1 m rifle at 300 m, a 0.2 m
        /// pistol at 60 m.</summary>
        public const float MetresPerMetre = 300f;

        /// <summary>The light factor at night (relative light ~0): a tenth of the day range.</summary>
        public const float NightFactor = 0.1f;

        /// <summary>Relative light at and above which the day range holds (the lightest civil twilight and all of the day).</summary>
        public const float FullLight = 0.3f;

        /// <summary>The light factor in [<see cref="NightFactor"/>, 1] for a relative light.</summary>
        public static float LightFactor(float relativeLight)
            => NightFactor + (1f - NightFactor) * Math.Clamp(relativeLight / FullLight, 0f, 1f);

        /// <summary>The range, m, at which an item of <paramref name="sizeM"/> is identified under <paramref name="seeing"/>, capped by the
        /// visibility.</summary>
        public static float RangeM(float sizeM, in SeeingConditions seeing)
            => MathF.Min(MetresPerMetre * MathF.Max(0f, sizeM) * LightFactor(seeing.RelativeLight) * MathF.Max(0f, seeing.Optics),
                         seeing.VisibilityM);
    }

    /// <summary>⭐ <c>CE-3229</c> — an item's appearance with every default resolved (<see cref="ItemAppearances.Of"/>).</summary>
    public readonly struct ItemAppearance
    {
        public ItemAppearance(ItemLook look, float sizeM, bool lethal, float reachM)
        {
            Look = look; SizeM = sizeM; Lethal = lethal; ReachM = reachM;
        }

        public ItemLook Look { get; }
        public float SizeM { get; }
        public bool Lethal { get; }
        public float ReachM { get; }
    }

    /// <summary>⭐ <c>CE-3229</c> (AW-24: an item's appearance DEFAULTS from its weapon category) — the one resolver.</summary>
    public static class ItemAppearances
    {
        /// <summary>The size an item of <paramref name="look"/> has when its data names none, m.</summary>
        public static float DefaultSize(ItemLook look) => look switch
        {
            ItemLook.Rifle => 1.0f,
            ItemLook.Pistol => 0.2f,
            ItemLook.LongObject => 1.0f,
            ItemLook.SmallObject => 0.15f,
            _ => 0f,
        };

        /// <summary>What a weapon of <paramref name="category"/> looks like when its mount names nothing: a shoulder weapon is a rifle; a
        /// thrown one (a grenade, a stone) a small object in the hand; a dispenser and the heavy, vehicle-borne categories show nothing in a
        /// man's hands (their carriers are combatants, judged on status).</summary>
        public static ItemLook DefaultLook(WeaponCategory category) => category switch
        {
            WeaponCategory.SmallArms or WeaponCategory.MachineGun or WeaponCategory.Atgm or WeaponCategory.Sam
                or WeaponCategory.Unknown => ItemLook.Rifle,
            WeaponCategory.Grenade => ItemLook.SmallObject,
            _ => ItemLook.None,
        };

        /// <summary><paramref name="mount"/>'s appearance: its own data where it has some, else by its category and its rounds.</summary>
        public static ItemAppearance Of(EntityRepository world, WeaponMountDto mount)
        {
            var a = mount.Appearance;
            var look = a != null ? a.Look : DefaultLook(mount.Category);
            float size = a is { SizeM: > 0f } ? a.SizeM : DefaultSize(look);
            bool lethal = a?.Lethal ?? FiresLethal(world, mount);
            return new ItemAppearance(look, size, lethal, a?.ReachM ?? 0f);
        }

        /// <summary>
        /// ⭐ AW-2 — does <paramref name="mount"/> fire anything lethal? Lethal unless EVERY round it carries (its own, and each of its loadout
        /// types) is <see cref="WarheadKind.Blunt"/>, <see cref="WarheadKind.Irritant"/>, <see cref="WarheadKind.Stun"/> or
        /// <see cref="WarheadKind.Smoke"/>. A dispenser is never lethal; a mount that names no round is (an old mount is a weapon).
        /// </summary>
        public static bool FiresLethal(EntityRepository world, WeaponMountDto mount)
        {
            if (mount.Category == WeaponCategory.Countermeasure) return false;
            bool any = false;
            if (mount.AmmoGuid != 0) { any = true; if (IsLethal(world, unchecked((long)mount.AmmoGuid))) return true; }
            for (int i = 0; i < mount.Loadout.Count; i++)
            {
                if (mount.Loadout[i].AmmoGuid == 0) continue;
                any = true;
                if (IsLethal(world, unchecked((long)mount.Loadout[i].AmmoGuid))) return true;
            }
            return !any;
        }

        /// <summary>Is a round of munition type <paramref name="ammo"/> lethal (no warhead = a bullet, lethal)?</summary>
        public static bool IsLethal(EntityRepository world, long ammo)
            => CombatTkb.WarheadOf(world, ammo)?.Kind is not (WarheadKind.Blunt or WarheadKind.Irritant or WarheadKind.Stun or WarheadKind.Smoke);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3229</c> (B-7; docs/DESIGN_Civilians_And_Crowd_Control.md §5h AW-15…AW-24, §7.8) — THE armed rule. Two paths that share
    /// nothing below this class:
    /// <list type="bullet">
    /// <item>⭐ <b>STATUS</b> (AW-21) for a combatant — not of the <c>Civilian</c> class and not Neutral: armed exactly as
    /// <c>ThreatDanger</c> read it before (<see cref="ArmedByStatus"/>). One DIS-header read and one side read (AW-24).</item>
    /// <item><b>CONDUCT</b> (AW-15) for the <c>Civilian</c> class of any side, and a neutral non-civilian: what THIS observer can identify on
    /// him — each visible item's appearance, at its identification range for the light, the visibility and his optics. Counted in
    /// <see cref="ConductJudgements"/>, the AW-24 counter rail's probe.</item>
    /// </list>
    /// A pure function of replicated data — no system; every node gives the same answer for the same observer.
    /// </summary>
    public static class Armament
    {
        /// <summary>DIS kind 3 (life form) · domain 1 (land) · category 2 — the <c>Civilian</c> class (<c>EntityClassTable</c>'s
        /// <c>3.1.*.2</c>; a rail keeps the two equal).</summary>
        public const byte CivilianKind = 3, CivilianDomain = 1, CivilianCategory = 2;

        private static long _conductJudgements;

        /// <summary>How many times, process-wide, the CONDUCT path has been entered — ⭐ AW-24's probe: with no <c>Civilian</c>-class entity in
        /// the world it must not move.</summary>
        public static long ConductJudgements => Interlocked.Read(ref _conductJudgements);

        /// <summary>Is <paramref name="dis"/> of the <c>Civilian</c> class (<c>3.1.*.2</c>)?</summary>
        public static bool IsCivilianClass(DISEntityType dis)
            => dis.Kind == CivilianKind && dis.Domain == CivilianDomain && dis.Category == CivilianCategory;

        /// <summary>
        /// ⭐ AW-21 — is <paramref name="man"/> judged on STATUS: not of the <c>Civilian</c> class and not Neutral. An entity with no
        /// <see cref="EntityInfo"/> is a combatant (as <c>ThreatDanger</c> reads it); on a view that is not a repository (no DIS header to
        /// read) everyone is.
        /// </summary>
        public static bool IsCombatant(ISimulationView view, Entity man)
        {
            if (view is not EntityRepository world || man.IsNull || !world.IsAlive(man)) return true;
            if (IsCivilianClass(world.GetDisType(man))) return false;
            return !(world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(man)
                     && world.GetComponentRO<EntityInfo>(man).ForceId == ForceId.Neutral);
        }

        /// <summary>
        /// ⭐ AW-21 — armed by STATUS: exactly the test <c>ThreatDanger</c> made before <c>CE-3229</c> — a <see cref="WeaponState"/> that is not
        /// only a dispenser (CE-2188), or a working controlled weapon system (WS-4, CE-3203). ⛔ Not AW-2's lethality: AW-24 keeps this path
        /// as it was.
        /// </summary>
        public static bool ArmedByStatus(ISimulationView view, Entity man)
            => view.HasComponent<WeaponState>(man) && !Countermeasures.DispenserOnly(view, man)
               || view is EntityRepository r && !WeaponSystems.WeaponOf(r, man).IsNull;

        /// <summary>
        /// ⭐ The TRUTH form (Civilians §5h, note 1) for a reader that is not an observer — "am I armed" (<c>ThreatDanger.OwnStrength</c>):
        /// a built-in mount, or a working controlled weapon system, that fires something lethal (AW-2: non-lethal rounds only arm nobody).
        /// </summary>
        public static bool LethalInHand(ISimulationView view, Entity man)
        {
            if (view is not EntityRepository world) return ArmedByStatus(view, man);
            if (!world.IsAlive(man) || !world.IsComponentTypeRegistered<WeaponState>()) return false;
            if (world.HasComponent<WeaponState>(man) && CombatTkb.TryGetTemplate(world, man, out var t)
                && t.GetDescriptor<WeaponSuiteDto>() is { } suite)
            {
                for (int i = 0; i < suite.Mounts.Count; i++)
                    if (ItemAppearances.FiresLethal(world, suite.Mounts[i])) return true;
            }
            else if (world.HasComponent<WeaponState>(man) && !Countermeasures.DispenserOnly(world, man)) return true;   // no TKB: a weapon
            Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, man, controlled);
            for (int k = 0; k < n; k++)
            {
                var w = controlled[k];
                if (!world.HasComponent<WeaponState>(w) || WeaponSystems.IsBroken(world, w)) continue;
                var mount = CombatTkb.MountOf(world, w, 0);
                if (mount == null || ItemAppearances.FiresLethal(world, mount)) return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐⭐ THE armed rule (AW-15, AW-21): what <paramref name="observer"/> concludes about <paramref name="man"/> — on status for a
        /// combatant, else from what he can identify under the conditions at the man (<see cref="SeeingConditions.Of"/>).
        /// </summary>
        public static ArmedJudgement Judge(ISimulationView view, Entity observer, Entity man)
        {
            if (IsCombatant(view, man)) return ArmedByStatus(view, man) ? ArmedJudgement.IdentifiedLethal : ArmedJudgement.Nothing;
            var world = (EntityRepository)view;   // IsCombatant is true for every other view
            return Conduct(world, observer, man, SeeingConditions.Of(world, observer, man));
        }

        /// <summary><see cref="Judge(ISimulationView, Entity, Entity)"/> under explicit <paramref name="seeing"/> conditions (binoculars,
        /// a night sight, a test's night).</summary>
        public static ArmedJudgement Judge(EntityRepository world, Entity observer, Entity man, in SeeingConditions seeing)
        {
            if (IsCombatant(world, man)) return ArmedByStatus(world, man) ? ArmedJudgement.IdentifiedLethal : ArmedJudgement.Nothing;
            return Conduct(world, observer, man, seeing);
        }

        /// <summary>The CONDUCT path (A-5): the strongest answer over the man's visible items.</summary>
        private static ArmedJudgement Conduct(EntityRepository world, Entity observer, Entity man, in SeeingConditions seeing)
        {
            Interlocked.Increment(ref _conductJudgements);
            if (!world.IsComponentTypeRegistered<WeaponState>()) return ArmedJudgement.Nothing;
            float distance = Distance(world, observer, man);
            var best = ArmedJudgement.Nothing;
            if (world.HasComponent<WeaponState>(man) && CombatTkb.TryGetTemplate(world, man, out var t)
                && t.GetDescriptor<WeaponSuiteDto>() is { } suite)
            {
                for (int i = 0; i < suite.Mounts.Count && best < ArmedJudgement.IdentifiedLethal; i++)
                    best = Max(best, OfItem(ItemAppearances.Of(world, suite.Mounts[i]), distance, seeing));
            }
            Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, man, controlled);
            for (int k = 0; k < n && best < ArmedJudgement.IdentifiedLethal; k++)
            {
                var w = controlled[k];
                if (!world.HasComponent<WeaponState>(w) || WeaponSystems.IsBroken(world, w)) continue;
                if (CombatTkb.MountOf(world, w, 0) is { } mount)
                    best = Max(best, OfItem(ItemAppearances.Of(world, mount), distance, seeing));
            }
            return best;
        }

        /// <summary>A-5 — one visible item at <paramref name="distance"/>: beyond its identification range ⇒ Unidentified; identified and
        /// lethal-looking ⇒ IdentifiedLethal (a REACH item only within its reach, else Unidentified — warn); identified non-lethal ⇒ Nothing.</summary>
        public static ArmedJudgement OfItem(in ItemAppearance item, float distance, in SeeingConditions seeing)
        {
            if (item.Look == ItemLook.None) return ArmedJudgement.Nothing;
            if (distance > Identification.RangeM(item.SizeM, seeing)) return ArmedJudgement.Unidentified;
            if (!item.Lethal) return ArmedJudgement.Nothing;
            return item.ReachM > 0f && distance > item.ReachM ? ArmedJudgement.Unidentified : ArmedJudgement.IdentifiedLethal;
        }

        private static ArmedJudgement Max(ArmedJudgement a, ArmedJudgement b) => a >= b ? a : b;

        private static float Distance(EntityRepository world, Entity observer, Entity man)
        {
            if (observer.IsNull || !world.IsAlive(observer) || !world.HasComponent<SimTransform>(observer) || !world.HasComponent<SimTransform>(man))
                return 0f;   // no observer position (a reader with no self): judge as from up close
            return Vector3.Distance(world.GetComponentRO<SimTransform>(observer).Position, world.GetComponentRO<SimTransform>(man).Position);
        }
    }
}
