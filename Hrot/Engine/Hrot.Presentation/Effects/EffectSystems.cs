using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common.Components;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐⭐ CE-1042 E2 — makes the realism EFFECT entities from the fire and hit events, on EVERY map host (docs/DESIGN_Visual_Effects.md
/// VE-A..VE-E, VE-J, VE-M): built by <c>MapInteractionPack</c>, scheduled with the map's interaction systems, declared in
/// <c>RequiredSystems</c> so a host that skips it is reported (R-254). Replaces IG's <c>EventToEffectSystem</c> on the map hosts.
/// <list type="bullet">
///   <item><b>Fired</b> (<see cref="WeaponFireNotification"/>): a muzzle flash ATTACHED to the shooter's weapon (<see cref="EffectAnchor"/>
///   — no position of its own, VE-J). ⛔ No tracer any more (LR-4): a tracer is a real, replicated round.</item>
///   <item><b>Hit</b> (<see cref="DetonationNotification"/>): an explosion at the hit point, world-fixed; a decal on the GROUND under it
///   (VE-I step 1, draped) — but none for a hit on a vehicle or a person (VE-M).</item>
///   <item><b>Impact</b> (<see cref="RoundImpactNotification"/>, ⭐ CE-1129): a puff and a mark on each surface a round entered, left or
///   stopped on — a wall, a roof, a floor, the ground (VE-I step 2, §10).</item>
/// </list>
/// Which effect types: the round's AMMO type's <c>Effect.Set</c>, else its calibre class (<see cref="EffectTkbCatalog.EffectsFor"/>, VE-D).
/// The events reach every node (local fire, or the NED ingress), so every host makes its own — effects are never replicated.
/// <para>⭐ W1 (§7 VE-Q, VE-R): each effect carries the generic <see cref="Lifespan"/> (made at sim now, lives the type's
/// <c>Duration</c>) — <see cref="LifespanSystem"/> removes it. Effects ARE RECORDED (never saved); ⛔ so during a replay this system is
/// OFF (the world's <see cref="Fdp.Toolkit.Replay.ReplayState"/>): the recording already holds every effect, and spawning from the replayed events would draw each
/// one twice.</para>
/// <para>It also keeps its own population honest, which no generic timer can: an attached effect whose shooter is gone ends at once
/// (VE-J), and decals over <see cref="DecalCap"/> are removed oldest first (VE-H).</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class EffectSpawnSystem : IEcsModuleSystem
{
    /// <summary>The most decals alive at once (VE-H).</summary>
    public const int DecalCap = 256;

    /// <summary>⭐ CE-1113 (§6v AD-V6) — the highest a burst may be above the ground and still scorch it (m).</summary>
    public const float DecalMaxBurstHeight = Hrot.Look.Effects.EffectRules.DecalMaxBurstHeight;   // ⭐ CE-4671 LK-S1 — the rule's

    private readonly List<(double BornAt, Entity Entity)> _decals = new();
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    /// <summary>
    /// ⭐ CE-4148 (Node Features S2) — this node's feature gate, read lazily (a host builds the map before its node exists). The hit memory
    /// (<see cref="HitFeedback"/>) is recorded only while one of its readers — <c>hud.hit-marker</c>, <c>hud.damage-arc</c>, <c>hud.kill-cam</c> — is on
    /// (<see cref="Hrot.Common.Infrastructure.HudFeatures.RecordsHitFeedback"/>). Null provider ⇒ ungated (today's behaviour).
    /// </summary>
    public Func<Fdp.ModuleHost.IFeatureGate?>? FeatureGate { get; set; }

    /// <summary>Is the hit memory kept on this node right now?</summary>
    internal bool RecordsHits => Hrot.Common.Infrastructure.HudFeatures.RecordsHitFeedback(FeatureGate?.Invoke());

    /// <summary>Effects made in the last tick, by kind — the rail's counter.</summary>
    public int Spawned { get; private set; }

    /// <summary>
    /// ⭐ CE-1171 (People Display §5 U10) — a blow struck (backend CE-3266, G3): the striker's swing is drawn by the 3-D bodies, and
    /// (CE-1215, §8j H-1, H-2) a blow is a hit too: the striker's marker, the arc toward him on the one struck.
    /// ⭐ CE-4062 — presentation memory only (no entity, no world write), so it runs in a REPLAY as well: the recorded
    /// <c>StrikeNotification</c> is replayed onto the bus with each played or stepped frame. ⛔ Was inside the spawner's replay gate, so a
    /// replay never drew a swing.
    /// </summary>
    private static void TrackStrikes(ISimulationView view, EntityRepository world, double now, bool recordHits)
    {
        if (!world.Bus.IsRegistered<Fdp.Toolkit.Combat.StrikeNotification>()) return;
        foreach (ref readonly var strike in view.ReadEvents<Fdp.Toolkit.Combat.StrikeNotification>())
        {
            if (strike.Striker != Entity.Null) StrikeTracker.In(world).Record(strike.Striker, strike.Weapon, now);
            if (recordHits && strike.Target != Entity.Null && view.IsAlive(strike.Target) && view.HasComponent<SimTransform>(strike.Target))
                HitFeedback.In(world).Record(strike.Striker, strike.Target, view.GetComponentRO<SimTransform>(strike.Target).Position,
                                             Vector3.Zero, 0f, now);
        }
    }

    /// <summary>⭐ CE-4600 — barriers broken / bumped open whose dust was spawned (a rail).</summary>
    public int ContactBreaks { get; private set; }

    /// <summary>
    /// ⭐⭐ CE-4600 (B-3b; 📄 docs/DESIGN_Vehicle_Breaching.md §7.2 V-17, §7.15; mockup vehicle-breaching.svg A) — every <see cref="Fdp.Toolkit.Terrain.ContactBreachEvent"/> of this frame: the
    /// driver's cue remembered (<see cref="ContactCues"/>); a barrier BROKE (dust, large puff) or was BUMPED open (dust, medium puff) at the contact point, and its crash queued for the
    /// player's audio (the AI's hearing is the sim's own, <c>SoundEmissionSystem.HearBreaks</c>). A push in progress and a wall too strong make no dust.
    /// </summary>
    private void TrackContacts(ISimulationView view, EntityRepository world, IEntityCommandBuffer cmd, ITkbDatabase? tkb, double now)
    {
        if (!world.Bus.IsRegistered<Fdp.Toolkit.Terrain.ContactBreachEvent>()) return;
        foreach (ref readonly var b in view.ReadEvents<Fdp.Toolkit.Terrain.ContactBreachEvent>())
        {
            var kind = (Fdp.Toolkit.Terrain.ContactBreachKind)b.Kind;
            ContactCues.In(world).Record(b.Vehicle, kind, b.Progress, now);
            if (kind is not (Fdp.Toolkit.Terrain.ContactBreachKind.Broke or Fdp.Toolkit.Terrain.ContactBreachKind.Bumped)) continue;
            _specs.Clear();
            Hrot.Look.Effects.EffectRules.ForContactBreach(in b, tkb, _specs);
            SpawnAll(cmd, now);   // ⭐ CE-4671 LK-S1 — the dust from the shared rule
            Hrot.UI.Common.Audio.SoundEvents.Of(world)?.Add(new Hrot.UI.Common.Audio.SoundEvents.Crash(new Vector3(b.X, b.Y, b.Z), kind == Fdp.Toolkit.Terrain.ContactBreachKind.Broke));
            ContactBreaks++;
        }
    }

    /// <summary>⭐ CE-5034 — panes heard shattering this frame (a rail).</summary>
    public int WindowsBroken { get; private set; }

    /// <summary>
    /// ⭐ CE-5034 (glass windows GW-3, D-8; 📄 docs/DESIGN_Glass_Windows.md §6a) — every <see cref="Fdp.Toolkit.Terrain.WindowBrokenEvent"/> of this frame (raised where the window
    /// was decided, carried to every node by its event id) queues the breaking glass for the player's audio. The shards' flight is the 3-D map's (<c>GlassLayer3D</c>, a state
    /// change it sees); the AI's hearing is the sim's own (<c>SoundEmissionSystem</c>).
    /// </summary>
    private void TrackWindows(ISimulationView view, EntityRepository world)
    {
        WindowsBroken = 0;
        if (!world.Bus.IsRegistered<Fdp.Toolkit.Terrain.WindowBrokenEvent>()) return;
        foreach (ref readonly var g in view.ReadEvents<Fdp.Toolkit.Terrain.WindowBrokenEvent>())
        {
            Hrot.UI.Common.Audio.SoundEvents.Of(world)?.Add(new Hrot.UI.Common.Audio.SoundEvents.Glass(new Vector3(g.X, g.Y, g.Z)));
            WindowsBroken++;
        }
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        Spawned = 0;
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<Lifespan>()) return;
        double now = SimClock.Of(world).TotalTime;
        // ⭐ CE-4671 LK-S1 — the player messages and the kill feed moved to HudFeedSystem (they are not effects)
        TrackStrikes(view, world, now, RecordsHits);   // ⭐ CE-4062 — BEFORE the replay gate: a replay's blows are drawn too
        if (Fdp.Toolkit.Replay.ReplayState.IsActive(world)) return;   // ⭐ CE-3329 P2 — the world says it is a replay
        var tkb = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
        var cmd = view.GetCommandBuffer();
        Housekeep(world, cmd, tkb);
        MoveDecoys(world, now);   // ⭐ CE-1115
        TrackContacts(view, world, cmd, tkb, now);   // ⭐ CE-4600 (B-3b, V-17)
        TrackWindows(view, world);   // ⭐ CE-5034 (GW-3, D-8)

        if (world.Bus.IsRegistered<WeaponFireNotification>())
        {
            foreach (ref readonly var fire in view.ReadEvents<WeaponFireNotification>())
            {
                if (!fire.Fired) continue;   // ⭐ Q94 A-2 (R-3018) — a REFUSED shot never flew: nothing to hear, see or fear
                // ⭐ CE-1115 (§9 DV-1) — a flare / chaff release: the decoy, moving along its path; no flash, no blast, no shot
                if (Munitions.IsExpendable(tkb, fire.AmmoGuid)) { SpawnRelease(cmd, tkb, fire, now); continue; }
                if (fire.Shooter == Entity.Null || !view.IsAlive(fire.Shooter) || !view.HasComponent<SimTransform>(fire.Shooter)) continue;
                var mount = CombatTkb.MountOf(world, fire.Shooter, fire.WeaponIndex);
                // ⭐ CE-1115 (§9 DV-5, BS-1 §5.4b F3) — the round actually FIRED (the fire carries it); the mount's only from an older sender
                long fired = fire.AmmoGuid != 0 ? fire.AmmoGuid : mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
                var at = view.GetComponentRO<SimTransform>(fire.Shooter).Position;
                var toward = fire.Target != Entity.Null && view.IsAlive(fire.Target) && view.HasComponent<SimTransform>(fire.Target)
                    ? view.GetComponentRO<SimTransform>(fire.Target).Position : Fdp.Core.WorldPoint.Zero;
                // ⭐ CE-1090 — the 3-D bodies: a launcher on the shoulder; ⭐ CE-1105 — and where it was aimed (the burst stream)
                RecentShots.In(world).Record(fire.Shooter, fire.WeaponIndex, now,
                    StreamToward(toward, AimPointOf(view, fire.Shooter), fire.Origin != Vector3.Zero ? fire.Origin : at, fire.Velocity, mount?.Range ?? 0f));   // ⭐ CE-1357
                // ⭐ CE-3248 (Civilians §7.17a TC-6) — a THROW is recorded (the body's pose reads RecentShots) and is otherwise no gunshot: no
                //   flash at a hand, no shot sound, no blast. 📐 Before: a stone has no effects of its own, so it drew the default small flash.
                if (Fdp.Toolkit.Combat.PersonHands.Threw(view, in fire)) continue;
                // ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-10) — the sound of it, queued only while an FPS station listens
                if (Hrot.UI.Common.Audio.SoundEvents.Of(world) is { } heard && mount?.Category != WeaponCategory.Bomb)
                    heard.Add(new Hrot.UI.Common.Audio.SoundEvents.Shot(fire.Shooter, fire.WeaponIndex, fire.Origin != Vector3.Zero ? fire.Origin : at,
                        fired, mount?.DamagePerHit ?? 0f, FireStreams.IsAutomatic(mount),
                        view.HasComponent<TkbIdentity>(fire.Shooter) ? view.GetComponentRO<TkbIdentity>(fire.Shooter).TkbType : 0L));
                // ⭐ CE-4671 LK-S1 — the flash (none for a bomb, CE-1094, or a silent weapon, CE-4649) and what the shot throws (backblast, ground blast,
                //   launch smoke) come from the SHARED rule; the spawner only makes the entities
                _specs.Clear();
                Hrot.Look.Effects.EffectRules.ForShot(in fire, world, tkb, _specs);
                SpawnAll(cmd, now);
                // ⛔ LR-4 (DESIGN_Visual_Effects.md §8.5 LR-E, R-271) — no decorative tracer any more: a tracer is a REAL round now (a
                //   tracer munition, replicated by the muscle), drawn along its actual flight by ProjectileLayer. The effect type 7131
                //   stays registered, so recordings made before this still draw theirs.
            }
        }

        ImpactsDrawn = 0;
        InertLandings = 0;
        if (world.Bus.IsRegistered<RoundImpactNotification>())   // ⭐ CE-1129 (VE §10) — every surface a round marked
            foreach (ref readonly var impact in view.ReadEvents<RoundImpactNotification>())
                SpawnImpact(cmd, tkb, world, impact, now);

        if (world.Bus.IsRegistered<DetonationNotification>())
        {
            foreach (ref readonly var hit in view.ReadEvents<DetonationNotification>())
            {
                var point = new Vector3(hit.HitX, hit.HitY, hit.HitZ);
                // ⭐ CE-1215 (§8j H-1, H-2) — a round that met someone: the first-person hit marker and damage arc read it
                if (hit.Target != Entity.Null && RecordsHits)   // ⭐ CE-4148 — nothing recorded while every HUD reader of it is off
                    HitFeedback.In(world).Record(hit.Shooter, hit.Target, point, new Vector3(hit.TravelX, hit.TravelY, hit.TravelZ), hit.Damage, now);
                _specs.Clear();
                Hrot.Look.Effects.EffectRules.ForDetonation(in hit, world, tkb, _specs);   // ⭐ CE-4671 LK-S1 — the explosion and its ground mark, or a landing's dust
                SpawnAll(cmd, now);
                if (Hrot.Look.Effects.EffectRules.IsInertHandItem(tkb, hit.Ammo)) { InertLandings++; continue; }   // ⭐ CE-1194 — it landed: no bang
                bool hitABody = hit.Target != Entity.Null && view.IsAlive(hit.Target);
                Hrot.UI.Common.Audio.SoundEvents.Of(world)?.Add(new Hrot.UI.Common.Audio.SoundEvents.Burst(point, hit.Ammo, hit.Damage, hitABody));   // ⭐ CE-1120
            }
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1129 (docs/DESIGN_Visual_Effects.md §10 IM-5 … IM-7) — each surface a round marked (<see cref="RoundImpactNotification"/>):
    /// the round's puff (its set's <c>Explosion</c> — a small-arms round's is the 7111 impact puff) and its mark (its <c>Decal</c>), the mark
    /// turned onto the face it struck (no normal ⇒ back along the travel). A stop that is not a burst adds the ricochet sound (IM-7).
    /// </summary>
    private void SpawnImpact(IEntityCommandBuffer cmd, ITkbDatabase? tkb, EntityRepository world, in RoundImpactNotification hit, double now)
    {
        _specs.Clear();
        Hrot.Look.Effects.EffectRules.ForImpact(in hit, world, tkb, _specs);   // ⭐ CE-4671 LK-S1 — the puff and the mark from the shared rule
        SpawnAll(cmd, now);
        ImpactsDrawn++;
        if (hit.Kind == (byte)ImpactKind.Stop)
            Hrot.UI.Common.Audio.SoundEvents.Of(world)?.Add(new Hrot.UI.Common.Audio.SoundEvents.Burst(new Vector3(hit.X, hit.Y, hit.Z), hit.Ammo, 0f, false));   // the ricochet
    }

    /// <summary>⭐ <c>CE-1194</c> — landings drawn as dust only in the last tick (the rail's counter).</summary>
    public int InertLandings { get; private set; }

    /// <summary>⭐ <c>CE-1194</c> — a thrown inert item (moved to the shared rule, CE-4671 LK-S1: <see cref="Hrot.Look.Effects.EffectRules.IsInertHandItem"/>).</summary>
    public static bool IsInertHandItem(ITkbDatabase? tkb, long ammo) => Hrot.Look.Effects.EffectRules.IsInertHandItem(tkb, ammo);

    /// <summary>⭐ <c>CE-1199</c> — how hard an impact's round strikes (moved to the shared rule, CE-4671 LK-S1).</summary>
    public static float ImpactDamage(EntityRepository world, ITkbDatabase? tkb, in RoundImpactNotification hit) => Hrot.Look.Effects.EffectRules.ImpactDamage(world, tkb, in hit);

    /// <summary>⭐ CE-1129 (IM-4) — the face a mark lies on (moved to the shared rule, CE-4671 LK-S1).</summary>
    public static Vector3 SurfaceOf(in RoundImpactNotification hit) => Hrot.Look.Effects.EffectRules.SurfaceOf(in hit);

    /// <summary>⭐ CE-1129 (IM-6) — a mark's rotation onto its face (moved to the shared rule, CE-4671 LK-S1).</summary>
    public static Quaternion MarkRotation(Vector3 normal) => Hrot.Look.Effects.EffectRules.MarkRotation(normal);

    /// <summary>Impact marks made in the last tick — the rail's counter (CE-1129).</summary>
    public int ImpactsDrawn { get; private set; }

    /// <summary>
    /// ⭐ CE-1115 (§9 DV-1 … DV-3) — a release's decoy: its munition's <c>Release</c> effect, born at the RELEASE (<c>FiredAt</c>) and living
    /// the expendable's <c>LifeSeconds</c> (a late viewer draws it at its true age, or not at all once burnt out), placed by
    /// <see cref="DecoyFlight.At"/> and moved by <see cref="MoveDecoys"/>.
    /// </summary>
    private void SpawnRelease(IEntityCommandBuffer cmd, ITkbDatabase? tkb, in WeaponFireNotification fire, double now)
    {
        long type = EffectTkbCatalog.EffectsFor(tkb, fire.AmmoGuid, 0f).Release;
        if (type == 0 || EffectTkbCatalog.VisualOf(tkb, type) is null || Munitions.ExpendableOf(tkb, fire.AmmoGuid) is not { } exp) return;
        double bornAt = fire.FiredAt > 0 ? fire.FiredAt : now;
        if (now - bornAt >= exp.LifeSeconds) return;
        for (int i = 0; i < Math.Max(1, fire.Quantity); i++)
        {
            var e = cmd.CreateEntity();
            cmd.AddComponent(e, new SimTransform { Position = DecoyFlight.At(fire.Origin, fire.Velocity, exp.SinkMps, (float)(now - bornAt)), Rotation = Quaternion.Identity });
            cmd.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(type) });
            cmd.AddComponent(e, Lifespan.Starting(bornAt, exp.LifeSeconds));
            cmd.AddComponent(e, new EffectFlight { Origin = fire.Origin, Velocity = fire.Velocity, SinkMps = exp.SinkMps, FiredAt = (SimInstant)bornAt });
            cmd.AddComponent(e, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());   // VE-R: recorded, never saved
            Spawned++;
        }
    }

    /// <summary>⭐ CE-1115 (§9 DV-3) — every moving effect to where its path puts it now (the SimHost's decoy is at the same point).</summary>
    private void MoveDecoys(EntityRepository world, double now)
    {
        if (!world.IsComponentTypeRegistered<EffectFlight>()) return;
        if (!ReferenceEquals(world, _flightWorld))
        {
            _flightWorld = world;
            _flights = world.Query().With<EffectFlight>().With<SimTransform>().Build();
        }
        foreach (var e in _flights!)
        {
            var f = world.GetComponentRO<EffectFlight>(e);
            ref var t = ref world.GetComponentRW<SimTransform>(e);
            t.Position = DecoyFlight.At(f.Origin, f.Velocity, f.SinkMps, (float)(now - f.FiredAt));
        }
    }

    private EntityRepository? _flightWorld;
    private EntityQuery? _flights;

    /// <summary>Where <paramref name="shooter"/>'s weapon posture aims (its target, else its point); zero when it aims at nothing.</summary>
    /// <summary>
    /// ⭐ <c>CE-1357</c> — where a shot's burst stream points: its target unit; else the shooter's aim posture; else ALONG THE ROUND'S OWN
    /// LAUNCH (<paramref name="origin"/> + its velocity's direction × the weapon's range, 50…1000 m, 300 when unstated); else nowhere.
    /// 🔴 A weapon SYSTEM (a door M134, a pintle gun) fired at a POINT has no target and no posture of its own, so the stream was drawn to
    /// (0,0,0): the bright stream from the door ran to the map's corner while only its real tracers flew to the aim. 🔒 User, 2026-10-25:
    /// "the majority of the stream of tracers is rendered to the corner of the terrain (maybe 0,0,0 or something) and just few are flying to
    /// where the aim point is". Pure.
    /// </summary>
    public static Vector3 StreamToward(Vector3 target, Vector3 posture, Vector3 origin, Vector3 velocity, float range)
    {
        // ⭐ CE-1960 — a point the round did NOT fly toward is stale (a fighter's AI attack point it has flown past, a target behind it): when the launch is
        //   known, the target / posture point is taken only inside StreamCone of it. 🔒 User, 2026-11-17: "the tracers and the fire is directed behind the
        //   aircraft, not from the nose in front, but from the cockpit to somewhere behind the aircraft, completely opposite direction".
        if (target != Vector3.Zero && Along(target)) return target;
        if (posture != Vector3.Zero && Along(posture)) return posture;
        if (velocity.LengthSquared() < 1e-4f) return Vector3.Zero;   // nothing known: no stream (FireStreams draws none toward zero)
        return origin + Vector3.Normalize(velocity) * (range > 0f ? Math.Clamp(range, 50f, 1000f) : 300f);

        bool Along(Vector3 point)
        {
            var to = point - origin;
            if (velocity.LengthSquared() < 1e-4f || to.LengthSquared() < 1e-4f) return true;   // no launch known: the point is all there is
            return Vector3.Dot(Vector3.Normalize(to), Vector3.Normalize(velocity)) >= StreamConeCos;
        }
    }

    /// <summary>⭐ <c>CE-1960</c> — the cosine of the widest angle (45°) between a shot's launch and the point its stream is drawn to: a fighter's lead or a
    /// rifle's spread stays inside it; a stale aim point behind the shooter does not.</summary>
    public const float StreamConeCos = 0.7071f;

    private static Vector3 AimPointOf(ISimulationView view, Entity shooter)
        => view is EntityRepository world && Fdp.Toolkit.Combat.WeaponPostureOf.TryAimPoint(world, shooter, out var p) ? p : Vector3.Zero;

    private readonly List<Hrot.Look.Effects.EffectSpec> _specs = new();   // ⭐ CE-4671 LK-S1 — the rule's output, reused (0 B steady)

    /// <summary>⭐ CE-4671 LK-S1 — an effect entity for every spec the shared rule gave, in its order (the order effects were always made).</summary>
    private void SpawnAll(IEntityCommandBuffer cmd, double now)
    {
        for (int i = 0; i < _specs.Count; i++)
        {
            var spec = _specs[i];
            var e = cmd.CreateEntity();
            cmd.AddComponent(e, new SimTransform { Position = spec.At, Rotation = spec.Rotation });
            cmd.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(spec.EffectType) });
            cmd.AddComponent(e, Lifespan.Starting(now, spec.Lifetime));
            if (spec.Anchor is { } a) cmd.AddComponent(e, a);
            cmd.AddComponent(e, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());   // VE-R: recorded, never saved with the scenario
            Spawned++;
        }
    }

    /// <summary>VE-J — an attached effect whose shooter is gone ends now; VE-H — decals over <see cref="DecalCap"/> go, oldest first.</summary>
    private void Housekeep(EntityRepository world, IEntityCommandBuffer cmd, ITkbDatabase? tkb)
    {
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<Lifespan>().With<TkbIdentity>().Build();
        }
        bool anchored = world.IsComponentTypeRegistered<EffectAnchor>();
        _decals.Clear();
        foreach (var e in _query!)
        {
            if (anchored && world.HasComponent<EffectAnchor>(e)
                && world.GetComponentRO<EffectAnchor>(e).Shooter is var s && (s == Entity.Null || !world.IsAlive(s)))
            {
                cmd.DestroyEntity(e);
                continue;
            }
            if (EffectTkbCatalog.VisualOf(tkb, world.GetComponentRO<TkbIdentity>(e).TkbType) is { Kind: EffectKind.Decal })
                _decals.Add((world.GetComponentRO<Lifespan>(e).BornAtSimSeconds, e));
        }
        if (_decals.Count <= DecalCap) return;
        _decals.Sort((a, b) => a.BornAt.CompareTo(b.BornAt));   // oldest first
        for (int i = 0; i < _decals.Count - DecalCap; i++)
            cmd.DestroyEntity(_decals[i].Entity);
    }
}

/// <summary>
/// ⭐⭐ W3 (docs/DESIGN_Visual_Effects.md §7 VE-P, R-269 (ui)) — <b>a vehicle that is destroyed burns:</b> when a VEHICLE's Health
/// crosses from above zero to zero, this makes two effect entities attached to its body (<see cref="EffectAnchorKind.Body"/>) —
/// flames (<see cref="EffectTkbCatalog.Fire"/>, ≈ 60 s) and smoke (<see cref="EffectTkbCatalog.Smoke"/>, ≈ 180 s, fading) — each with a
/// <see cref="Lifespan"/>, so <see cref="LifespanSystem"/> removes them. On every map host, like <see cref="EffectSpawnSystem"/>:
/// Health reaches every node, so each host makes its own.
/// <list type="bullet">
///   <item>⭐ CE-4660: flames when the hull's replicated <c>FireState</c> starts burning — or is first SEEN burning (a late-joining host shows the
///   fire it joined). Whether a hull burns is the simulation's (its TKB <c>FireSeconds</c>, lit once at its death crossing by <c>BurnSystem</c>):
///   a wreck loaded already dead has no fire and shows none (VE-P holds), a soldier never burns.</item>
///   <item>⛔ OFF during a replay (the world's <see cref="Fdp.Toolkit.Replay.ReplayState"/>, VE-R): the recording holds the flames; and its memory is dropped, so
///   coming back live cannot mistake the replayed wrecks for fresh kills.</item>
/// </list>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class WreckSpawnSystem : IEcsModuleSystem
{
    private readonly Dictionary<Entity, bool> _wasDestroyed = new();
    private readonly HashSet<Entity> _seen = new();
    private readonly List<Entity> _gone = new();
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;
    private int _epoch;   // ⭐ CE-3294 — the world _wasDestroyed describes

    /// <summary>Effect entities made in the last tick — the rail's counter.</summary>
    public int Spawned { get; private set; }

    public void Execute(ISimulationView view, float deltaTime)
    {
        Spawned = 0;
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<Lifespan>()
            || !world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() || !world.IsComponentTypeRegistered<TkbIdentity>()) return;
        if (Fdp.Toolkit.Replay.ReplayState.IsActive(world)) { _wasDestroyed.Clear(); return; }   // ⭐ CE-3329 P2
        // ⭐ CE-3294 — a reload is a new world in the same repository (its units under the same handles): forget like a new instance
        if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch) | !ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<Fdp.Toolkit.Combat.Components.Health>().With<TkbIdentity>().With<SimTransform>().Build();
            _wasDestroyed.Clear();
        }
        var tkb = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
        var cmd = view.GetCommandBuffer();
        double now = SimClock.Of(world).TotalTime;
        bool fires = world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.FireState>();   // ⭐ CE-4660
        _seen.Clear();
        foreach (var e in _query!)
        {
            _seen.Add(e);
            // ⭐ CE-4660 (backend, a cross-lane edit — NOTE_Backend_To_Ui_Wreck_Reads_Fire.md; thermal drivers TD-6 / TD-16): the burn is SIMULATION state now —
            //   the hull's replicated FireState, lit by its Health owner (BurnSystem). Flames when it STARTS burning — or is first seen burning (a late joiner
            //   sees the fire too, which the Health crossing could never show). ⛔ Was: this node seeing Health cross 0, for a vehicle family.
            bool destroyed = fires && world.HasComponent<Fdp.Toolkit.Perception.Signatures.FireState>(e)
                             && world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.FireState>(e).IsBurning(now);
            if (destroyed && !(_wasDestroyed.TryGetValue(e, out bool was) && was))
            {
                var at = world.GetComponentRO<SimTransform>(e).Position;
                Spawn(cmd, tkb, EffectTkbCatalog.Fire, e, at, now);
                Spawn(cmd, tkb, EffectTkbCatalog.Smoke, e, at, now);
            }
            _wasDestroyed[e] = destroyed;
        }
        if (_wasDestroyed.Count > _seen.Count)
        {
            _gone.Clear();
            foreach (var e in _wasDestroyed.Keys) if (!_seen.Contains(e)) _gone.Add(e);
            foreach (var e in _gone) _wasDestroyed.Remove(e);
        }
    }

    // ⛔ CE-4660: `Burns(type)` (a vehicle family burns) retired — whether a hull burns is the simulation's (TKB FireSeconds, BurnSystem).

    private void Spawn(IEntityCommandBuffer cmd, ITkbDatabase? tkb, long type, Entity wreck, Vector3 at, double now)
    {
        if (EffectTkbCatalog.VisualOf(tkb, type) is not { } look) return;
        var e = cmd.CreateEntity();
        cmd.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        cmd.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(type) });
        cmd.AddComponent(e, Lifespan.Starting(now, look.Duration));
        cmd.AddComponent(e, new EffectAnchor { Shooter = wreck, Kind = EffectAnchorKind.Body });
        cmd.AddComponent(e, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());   // VE-R: recorded, never saved with the scenario
        Spawned++;
    }
}

// ⛔ RETIRED (W1, docs/DESIGN_Visual_Effects.md §7 VE-Q): `EffectLifetimeSystem` — it aged every effect by the frame delta and removed
//    it at its Duration. The generic Fdp.Toolkit.Time.LifespanSystem removes effects now (by sim time, so a replay jump or a rewind
//    is right with no catch-up); the orphan and decal-cap rules it also carried moved into EffectSpawnSystem.Housekeep.
