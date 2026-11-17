using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Interfaces;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-4647 (docs/designs/air-attack/DESIGN_Air_Combat.md §4d, AC-15; mockup <c>air-combat-stores.svg</c>) — the air-to-air MISSILES a fighter still carries, on their rails: the A-10's bombs' rounds-left rule
/// (<c>EntityBodyLayer3D.BombsLeft</c>, CE-1092) for a second kind of round. Two IR missiles OUTBOARD (grey, 3.0 m) and two radar missiles INBOARD (light, 3.7 m) per jet — the F-16 on the wingtips and under the wings,
/// the MiG on two pairs of underwing rails — one missile per round left on the mount that carries them, the LEFT rail first (a pair that has fired once keeps its left missile).
/// <para>⛔ The numbers are the mockup's (<c>scripts/gen-air-combat-mockup.py</c> F16_STORES / MIG_STORES): change one, change both. ⭐ HOT PATH: the part arrays are built once per (type, size, load) and cached; a frame allocates nothing.</para>
/// <para>🔧 <c>AirMunitionStandIn</c>: which mount holds which missile is told by its AMMUNITION type — the air lane's contract ids 6060 (AIM-9X) 6062 (R-73) = IR, 6061 (AIM-120) 6063 (R-77) = radar
/// (<c>DESIGN_Air_Combat.md</c> §7). ⭐ Re-bind: read the air lane's constants instead of these four literals.</para>
/// </summary>
public static class JetStores
{
    /// <summary>The rails of a jet: how many missiles of each kind it carries at most.</summary>
    public const int PerKind = 2;

    /// <summary>🔧 AirMunitionStandIn — an AMMUNITION TKB id is an IR missile (AIM-9X 6060, R-73 6062).</summary>
    public static bool IsIr(ulong ammo) => ammo is 6060UL or 6062UL;
    /// <summary>🔧 AirMunitionStandIn — … a radar missile (AIM-120 6061, R-77 6063).</summary>
    public static bool IsRadar(ulong ammo) => ammo is 6061UL or 6063UL;

    /// <summary>Whether <paramref name="family"/> carries air-to-air missiles on rails at all.</summary>
    public static bool Carries(VisualFamily family) => family is VisualFamily.Jet or VisualFamily.TwinEngineJet;

    /// <summary>(kind 0 = IR / 1 = radar, y, z, x) of each rail, fractions of the family's box; the LEFT (y &gt; 0) rail of a kind first.</summary>
    private static (int Kind, float Y, float Z, float X)[] Rails(VisualFamily family) => family == VisualFamily.TwinEngineJet
        ? [(0, 0.36f, 0.30f, -0.08f), (0, -0.36f, 0.30f, -0.08f), (1, 0.215f, 0.27f, -0.04f), (1, -0.215f, 0.27f, -0.04f)]
        : [(0, 0.487f, 0.38f, -0.12f), (0, -0.487f, 0.38f, -0.12f), (1, 0.30f, 0.31f, -0.04f), (1, -0.30f, 0.31f, -0.04f)];

    /// <summary>The pylons — drawn whether loaded or not.</summary>
    public static KitPart[] Pylons(VisualFamily family)
    {
        var rails = Rails(family); var parts = new KitPart[rails.Length];
        for (int i = 0; i < rails.Length; i++)
            parts[i] = ShapeKits.Box(rails[i].X, rails[i].Y, rails[i].Z + 0.045f, 0.06f, 0.008f, 0.05f, ColourRole.Dark);
        return parts;
    }

    /// <summary>The missile on rail <paramref name="slot"/> (0, 1 = the IR pair, left first; 2, 3 = the radar pair): its body, its nose, its tail fins.</summary>
    public static KitPart[] Missile(VisualFamily family, int slot, Vector3 size)
    {
        var (kind, y, z, x) = Rails(family)[slot];
        float ln = kind == 0 ? 3.0f : 3.7f, dm = kind == 0 ? 0.17f : 0.22f;
        var body = kind == 0 ? ColourRole.Metal : ColourRole.Light;
        return
        [
            ShapeKits.Cyl(PartAxis.X, x, y, z, ln / size.X, 0f, dm / size.Z, body),
            ShapeKits.Cone(PartAxis.X, x + ln / size.X / 2f + 0.1f / size.X, y, z, 0.2f / size.X, 0f, dm / size.Z, ColourRole.Dark),
            ShapeKits.Box(x - ln / size.X * 0.4f, y, z, 0.35f / size.X, dm * 2.4f / size.Y, 0.02f, ColourRole.Dark),
        ];
    }

    /// <summary>⭐ AC-15 step 5 — the throttle (0…1) from which the AFTERBURNER lights, and the g from which the wingtips shed VORTEX trails (DESIGN_Air_Combat §4d: "above 6 g").</summary>
    public const float BurnerFrom = 0.9f, VortexFromG = 6f;

    /// <summary>The afterburner flames of a jet: a cone behind each nozzle (one on the F-16, two on the MiG), the tip trailing — amber, 5 m long. The shape is the mockup-free part of the look: sized to the nozzle.</summary>
    public static KitPart[] Flames(VisualFamily family, Vector3 size)
    {
        float len = 5.0f / size.X;
        if (family == VisualFamily.TwinEngineJet)
        {
            float d = 0.85f / size.Z, x = -0.48f - len / 2f;
            return [Flame(x, 0.075f, 0.38f, len, d), Flame(x, -0.075f, 0.38f, len, d)];
        }
        return [Flame(-0.49f - len / 2f, 0f, 0.40f, len, 1.0f / size.Z)];
    }

    private static KitPart Flame(float x, float y, float z, float len, float dia)
        => new(PartShape.Cone, new Vector3(x, y, z), new Vector3(len, 0f, dia), ColourRole.Amber, PartAxis.X, PitchDegrees: 180f);   // pitch 180: the cone's tip points BACK

    /// <summary>The wingtip vortex trails of a jet pulling hard: a thin pale tube from each wingtip, 40 m straight back along the body (a stateless picture — it never allocates; the real vapour trail curves with the path, this is the straight-flight look).</summary>
    public static KitPart[] Vortices(VisualFamily family, Vector3 size)
    {
        float tipX = family == VisualFamily.TwinEngineJet ? -0.208f : -0.17f, tipY = family == VisualFamily.TwinEngineJet ? 0.453f : 0.448f, z = family == VisualFamily.TwinEngineJet ? 0.37f : 0.38f;
        float len = 40f / size.X, centre = tipX - len / 2f, dia = 0.16f / size.Z;
        return [ShapeKits.Cyl(PartAxis.X, centre, tipY, z, len, 0f, dia, ColourRole.Light), ShapeKits.Cyl(PartAxis.X, centre, -tipY, z, len, 0f, dia, ColourRole.Light)];
    }

    /// <summary>The kit (<paramref name="body"/>) with its effects: the pylons and the missiles left (<paramref name="ir"/> IR and <paramref name="radar"/> radar ones, 0 … <see cref="PerKind"/> each;
    /// <c>ir &lt; 0</c> ⇒ the type carries none, no rails), the afterburner flames, the vortex trails. Built once per call — cache it.</summary>
    public static KitPart[] Load(VisualFamily family, Vector3 size, KitPart[] body, int ir, int radar, bool burner = false, bool vortex = false)
    {
        var parts = new List<KitPart>(body);
        if (ir >= 0)
        {
            parts.AddRange(Pylons(family));
            for (int i = 0; i < Math.Min(ir, PerKind); i++) parts.AddRange(Missile(family, i, size));
            for (int i = 0; i < Math.Min(radar, PerKind); i++) parts.AddRange(Missile(family, 2 + i, size));
        }
        if (burner) parts.AddRange(Flames(family, size));
        if (vortex) parts.AddRange(Vortices(family, size));
        return parts.ToArray();
    }

    /// <summary>
    /// How many IR and radar missiles <paramref name="jet"/> still carries: each mount whose AMMUNITION is an air-to-air missile counts its rounds from the replicated <see cref="WeaponAmmo"/> mirror
    /// (CE-1079) — the full load where no mirror exists yet. <c>false</c> when the type carries no air-to-air missile (an F-16 before the air lane gave it any): nothing is drawn. No allocation.
    /// </summary>
    public static bool Left(EntityRepository world, Entity jet, ITkbDatabase? tkb, out int ir, out int radar)
    {
        ir = 0; radar = 0; bool any = false;
        if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, jet, out var template) || template.GetDescriptor<WeaponSuiteDto>() is not { } suite) return false;
        bool mirror = world.IsComponentTypeRegistered<WeaponAmmo>() && world.HasComponent<WeaponAmmo>(jet);
        for (int i = 0; i < suite.Mounts.Count; i++)
        {
            ulong ammo = suite.Mounts[i].AmmoGuid;
            bool isIr = IsIr(ammo), isRadar = IsRadar(ammo);
            if (!isIr && !isRadar) continue;
            any = true;
            int rounds = suite.Mounts[i].InitialAmmunition;
            if (mirror && i < WeaponAmmo.MaxMounts && world.GetComponentRO<WeaponAmmo>(jet) is { } a && i < a.Count) rounds = a.AmmoOf(i);
            rounds = Math.Clamp(rounds, 0, PerKind);
            if (isIr) ir += rounds; else radar += rounds;
        }
        ir = Math.Min(ir, PerKind); radar = Math.Min(radar, PerKind);
        return any;
    }
}

/// <summary>
/// 🔧 <c>FighterStateStandIn</c> (CE-4647) — a fighter's replicated <c>FighterPilotState</c> (<c>Throttle</c> 0…1, <c>GLoad</c>) is the air lane's component (DESIGN_Air_Combat §3 AC-2, §4d); until its contract is merged this ONE seam says what
/// it would say: the afterburner, the wingtip vortex and the fighter HUD's g read it here. <c>null</c> source = no state (a plain jet). ⭐ Re-bind: read the component in <see cref="Read"/>, delete <see cref="Source"/> (rails set it; they run serially).
/// </summary>
public static class FighterStateStandIn
{
    public static Func<EntityRepository, Entity, (float Throttle, float GLoad)?>? Source { get; set; }

    public static (float Throttle, float GLoad)? Read(EntityRepository world, Entity e) => Source?.Invoke(world, e);
}
