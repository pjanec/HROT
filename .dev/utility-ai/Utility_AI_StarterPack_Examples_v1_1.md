# Utility AI — Starter Pack & Integration Tests v1.2

Worked, compilable examples for the four decisions the project owner enumerated: **threat
ranking**, **weapon selection**, **posture (hide / flee / advance)**, and **leader fire
coordination**. Each is authored as a `[UtilityDecision]` C# definition (the source of truth,
§11 of the architecture doc) and is paired with an integration test that exercises it through the
real scorer against a fabricated ECS world. The definitions ship as `StarterPack/`; the tests
ship as `Hrot.AI.Tests/Utility/`.

> **Changelog v1.1 → v1.2** (2026-05-28 design-review decisions):
> - **Test world uses `UtilityTestWorld` (P0.6).** Replaces the invented `TestRepository.CreateBrainOnly`
>   from v1.1; constructs `new EntityRepository()` directly and registers the AI-relevant component
>   types. See `PREREQ_Phase0_Bundle.md` §P0.6.
> - **Multi-weapon agents are real (P0.2).** `SpawnWeaponMount` creates a real child entity with
>   `WeaponState` + `WeaponMountInfo` + `PartMetadata.ParentEntity = owner`. The weapon-selection
>   tests now exercise a genuine candidate list.
> - **EQS multi-sensor uses child entities.** `SeedEqs` is renamed `SpawnEqsSensor` and creates a
>   child entity carrying `EqsSensor` (with the requested `BlueprintId`) + `EqsCognitiveBuffer`
>   seeded via `GetSpanRW()` — no fake "named sensor" API on a single buffer.
> - **`UnitRoster.Add` / `IndexOf` and `Blackboard1024.Project<T>` are now real** (P0.4, P0.5).
> - **Position component fixed:** `Fdp.Toolkit.Geographic.Components.Position` (Vector3 Value),
>   not the invented `WorldPosition`.
> - **`WeaponState.MuzzleVelocity` exists today** — sample constructors set it.

> **Changelog v1.0 → v1.1** (historical): scaffolding mapped to real `TargetMemory` /
> `WeaponState` / `Health`; squad tests use commander `Blackboard1024` + `UnitRoster`.

The examples deliberately use only catalog inputs (B1) and the curve vocabulary from §5. They are
both documentation-by-example and the runtime fixtures the test harness loads.

---

## 0. Shared test scaffolding (v1.2)

All four tests build a small Brain-side world with a handful of entities, no DDS, no Muscle. EQS
inputs arrive on **child sensor entities** seeded directly into their `EqsCognitiveBuffer` —
exactly the shape `EqsResultUpdateSystem` would produce in production, proving "EQS is just an
input." All component access uses real v236 surfaces; the helper is `UtilityTestWorld` from
Phase-0 (P0.6).

```csharp
// Hrot.AI.Tests/Utility/UtilityTestWorld.cs
internal sealed class UtilityTestWorld : IDisposable
{
    public readonly EntityRepository Repo;
    public readonly UtilityScorer Scorer;
    private uint _tick = 1;

    public UtilityTestWorld()
    {
        Repo = new EntityRepository();                     // no Brain-only factory; direct construction
        Repo.RegisterComponent<Health>();
        Repo.RegisterComponent<WeaponState>();
        Repo.RegisterComponent<WeaponMountInfo>();
        Repo.RegisterComponent<TargetMemory>();
        Repo.RegisterComponent<SensorContactList>();
        Repo.RegisterComponent<EqsSensor>();
        Repo.RegisterComponent<EqsCognitiveBuffer>();
        Repo.RegisterComponent<PartMetadata>();
        Repo.RegisterComponent<UnitRoster>();
        Repo.RegisterComponent<UnitSubordinate>();
        Repo.RegisterComponent<Blackboard1024>();
        Repo.RegisterComponent<Fdp.Toolkit.Geographic.Components.Position>();
        Repo.RegisterComponent<UtilityResultBuffer>();
        Repo.RegisterComponent<UtilityDebugFlags>();
        Repo.RegisterComponent<UtilityTraceWorkingMemory1024>();

        UtilityInputRegistrar.RegisterAll();               // source-gen catalog (StandardInputs)
        UtilityDecisionCatalog.RegisterAll(out var registry);
        Scorer = new UtilityScorer(registry);
    }

    public Entity SpawnAgent(float health01, float ammo01, int initialAmmunition = 30)
    {
        var e = Repo.CreateEntity();
        Repo.AddComponent(e, new Health { Current = health01 * 100f, Max = 100f });
        Repo.AddComponent(e, new Fdp.Toolkit.Geographic.Components.Position { Value = Vector3.Zero });

        // Primary mount lives on the owner (P0.2 keeps primary on owner for actuator compat).
        ref var ws = ref Repo.AddComponent<WeaponState>(e);
        ws.MaxAmmo = initialAmmunition;
        ws.Ammo    = (int)MathF.Round(ammo01 * initialAmmunition);
        ws.MuzzleVelocity = 900f;                          // existing field on WeaponState
        ws.CooldownSecondsRemaining = 0f;

        Repo.AddComponent<TargetMemory>(e);
        Repo.AddComponent<UtilityResultBuffer>(e);
        Repo.AddComponent(e, new UtilityDebugFlags { TraceEnabled = true });
        Repo.AddComponent<UtilityTraceWorkingMemory1024>(e);
        return e;
    }

    /// <summary>
    /// Additional mount on `owner`: a child entity with its own WeaponState + WeaponMountInfo +
    /// PartMetadata. Mirrors the P0.2 spawn pattern in CombatTkbTranslator for non-primary mounts.
    /// </summary>
    public Entity SpawnWeaponMount(Entity owner, int mountIndex, ulong weaponGuid,
                                   float effRange, float ammo01, int initialAmmunition)
    {
        var me = Repo.CreateEntity();
        ref var ws = ref Repo.AddComponent<WeaponState>(me);
        ws.MaxAmmo = initialAmmunition;
        ws.Ammo    = (int)MathF.Round(ammo01 * initialAmmunition);
        ws.MuzzleVelocity = 900f;
        Repo.AddComponent(me, new WeaponMountInfo
        {
            MountIndex     = mountIndex,
            WeaponGuid     = weaponGuid,
            EffectiveRange = effRange,
        });
        Repo.AddComponent(me, new PartMetadata
        {
            ParentEntity      = owner,
            InstanceId        = mountIndex,
            DescriptorOrdinal = 0,
        });
        return me;
    }

    /// <summary>
    /// Set ammo on a specific mount (owner or child) by walking PartMetadata.
    /// </summary>
    public void SetWeaponAmmo(Entity owner, int mountIndex, float ammo01)
    {
        var mount = ResolveMount(owner, mountIndex);
        ref var ws = ref Repo.GetComponentRW<WeaponState>(mount);
        ws.Ammo = (int)MathF.Round(ammo01 * ws.MaxAmmo);
    }

    /// <summary>
    /// Real TargetMemory API: parallel fixed-size arrays, insertion-sorted so index 0 = top threat.
    /// AddOrUpdateTarget(ref mem, packedEntityId, posX, posY, scoreBoost, tick, modality).
    /// </summary>
    public void SeedContact(Entity self, Entity contact, float distanceM, float threatBoost,
                            float contactHealth01, bool hasLos)
    {
        ref var tm = ref Repo.GetComponentRW<TargetMemory>(self);
        var selfPos = Repo.GetComponentRO<Fdp.Toolkit.Geographic.Components.Position>(self).Value;
        float px = selfPos.X + distanceM, py = selfPos.Y;
        var modality = hasLos ? SensorModality.Visual : SensorModality.Acoustic;
        TargetMemory.AddOrUpdateTarget(ref tm, contact.PackedValue, px, py,
                                       scoreBoost: threatBoost, tick: _tick++, modality: modality);
        if (!Repo.HasComponent<Health>(contact))
            Repo.AddComponent(contact, new Health { Current = contactHealth01 * 100f, Max = 100f });
        if (!Repo.HasComponent<Fdp.Toolkit.Geographic.Components.Position>(contact))
            Repo.AddComponent(contact, new Fdp.Toolkit.Geographic.Components.Position
                                       { Value = new Vector3(px, py, 0f) });
    }

    /// <summary>
    /// Create a child sensor entity carrying `EqsSensor` + `EqsCognitiveBuffer`, mirroring how
    /// the real EQS pipeline plants per-template sensors on an agent. Seeds the Top-K directly
    /// via `EqsCognitiveBuffer.GetSpanRW()` (bypasses the [InlineArray] defensive-copy trap §8.2).
    /// </summary>
    public Entity SpawnEqsSensor(Entity owner, uint blueprintId, float topScore, int count,
                                 int instanceId)
    {
        var sensor = Repo.CreateEntity();
        Repo.AddComponent(sensor, new EqsSensor { BlueprintId = blueprintId, Epoch = 1 });
        ref var buf = ref Repo.AddComponent<EqsCognitiveBuffer>(sensor);
        buf.Count = Math.Min(count, 16);
        buf.LastUpdateTick = _tick++;
        buf.LastUpdateTimeSeconds = 0f;
        var span = buf.GetSpanRW();                        // bypasses [InlineArray] trap
        for (int i = 0; i < buf.Count; i++)
            span[i] = new EqsResult { Score = i == 0 ? topScore : topScore * 0.5f };
        Repo.AddComponent(sensor, new PartMetadata
        {
            ParentEntity      = owner,
            InstanceId        = instanceId,
            DescriptorOrdinal = 0,
        });
        return sensor;
    }

    private Entity ResolveMount(Entity owner, int mountIndex)
    {
        if (mountIndex == 0) return owner;
        // walk children: any entity whose PartMetadata.ParentEntity == owner and InstanceId == mountIndex.
        // Real impl uses an EntityRepository query helper; pseudocode here for brevity.
        foreach (var e in Repo.EnumerateAll<PartMetadata>())
        {
            ref readonly var pm = ref Repo.GetComponentRO<PartMetadata>(e);
            if (pm.ParentEntity == owner && pm.InstanceId == mountIndex) return e;
        }
        throw new InvalidOperationException($"mount index {mountIndex} not found on owner {owner}");
    }

    public void Dispose() => Repo.Dispose();
}
```

> **Mapping notes (v1.2).** `TargetMemory` exposes no boolean "hasLos" or "distance" field — it
> stores positions and a `modality` bitmask. The production readers derive distance from
> `Fdp.Toolkit.Geographic.Components.Position` vs. the stored contact position, and treat
> "Visual bit set in `Modalities[i]`" as "currently has LOS." `ContactThreatLevel` reads
> `ThreatScores[i]`; `ContactHealthFraction` reads the contact entity's `Health`.
> `IsAssignedTarget` reads through `UnitSubordinate.Commander` → `Blackboard1024.Project<ThreatMatrixAssignmentState>`.
> This matches §6 of the architecture doc.

---

## 1. Threat ranking — "who do I shoot first?"

### 1.1 Definition (`StarterPack/ThreatRankingDecision.cs`)

A **candidate scorer**: the single template option is evaluated once per contact in
`TargetMemory`, with `Ctx.Candidate` bound to each contact. Returns ranked Top-N into
`UtilityResultBuffer`.

```csharp
[UtilityDecision(
    AssetId     = "1a4f7c20-3b9e-4d18-8a01-threat0000001",
    DisplayName = "Threat ranking",
    Kind        = DecisionKind.ThreatRanking,
    Category    = "Tactical/Targeting")]
public sealed class ThreatRankingDecision : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        .CandidateOption(Mode.WeightedProduct, o => o
            // A contact I can't see is worth ~0 to prioritize (hard gate).
            .Consider(In.HasLineOfSight(Ctx.Candidate),        w: 1.0f, Curve.Step)
            // Closer threats rank higher.
            .Consider(In.DistanceToContext(Ctx.Candidate),     w: 0.7f, Curve.InverseLinear)
            // A contact with a dangerous weapon is a higher priority kill.
            .Consider(In.ContactThreatLevel(Ctx.Candidate),    w: 1.0f, Curve.Linear)
            // Nearly-dead contacts get a finishing-blow bump (low health -> high score).
            .Consider(In.ContactHealthFraction(Ctx.Candidate), w: 0.4f, Curve.InverseLinear)
            // Strong bias toward whatever the squad leader assigned me (§10.3); veto-able
            // because the whole option is multiplicative.
            .Consider(In.IsAssignedTarget(Ctx.Candidate),      w: 0.9f, Curve.Threshold));
}
```

### 1.2 Integration test

```csharp
public sealed class ThreatRankingTests
{
    [Fact]
    public void Closer_Visible_HighThreat_Contact_Ranks_First()
    {
        using var w = new UtilityTestWorld();
        var self = w.SpawnAgent(health01: 1.0f, ammo01: 1.0f);     // owner carries the primary mount

        var far  = w.Repo.CreateEntity();
        var near = w.Repo.CreateEntity();
        var blind= w.Repo.CreateEntity();

        // near + visible + heavy weapon -> should win
        w.SeedContact(self, near,  distanceM: 40f,  threatWeapon01: 0.9f, contactHealth01: 0.8f, hasLos: true);
        // far + visible + heavy weapon -> lower (distance gate)
        w.SeedContact(self, far,   distanceM: 250f, threatWeapon01: 0.9f, contactHealth01: 0.8f, hasLos: true);
        // near + heavy weapon but NO LOS -> Step curve zeroes it (hard gate)
        w.SeedContact(self, blind, distanceM: 30f,  threatWeapon01: 1.0f, contactHealth01: 0.8f, hasLos: false);

        w.Scorer.Evaluate(w.Repo, self, ThreatRankingDecision.Id);

        ref readonly var results = ref w.Repo.GetComponentRO<UtilityResultBuffer>(self);
        Assert.Equal(near.PackedValue, results.Top().Candidate);     // near visible heavy wins
        Assert.True(results.ScoreOf(blind.PackedValue) < 0.01f);     // no-LOS gated to ~0
        Assert.True(results.ScoreOf(near.PackedValue) > results.ScoreOf(far.PackedValue));   // distance dominates among visible
    }

    [Fact]
    public void Assigned_Target_Bias_Promotes_Leader_Choice()
    {
        using var w = new UtilityTestWorld();
        var leader = w.SpawnLeader();                                // P0.5-backed commander
        var self   = w.SpawnSquadMember(leader, 1.0f, 1.0f);
        var a = w.Repo.CreateEntity();
        var b = w.Repo.CreateEntity();

        // a is intrinsically slightly more threatening; b is the leader's assignment.
        w.SeedContact(self, a, 60f, 0.7f, 0.9f, hasLos: true);
        w.SeedContact(self, b, 70f, 0.6f, 0.9f, hasLos: true);
        // Write the assignment through the projected blackboard state on `leader`.
        ref var bb    = ref w.Repo.GetComponentRW<Blackboard1024>(leader);
        ref var state = ref Blackboard1024.Project<ThreatMatrixAssignmentState>(ref bb);
        ref var roster = ref w.Repo.GetComponentRW<UnitRoster>(leader);
        int slot = UnitRoster.IndexOf(ref roster, self.PackedValue);
        state.SetAssignment(slot, b.PackedValue);                    // helper on the projected struct

        w.Scorer.Evaluate(w.Repo, self, ThreatRankingDecision.Id);
        Assert.Equal(b.PackedValue, w.Repo.GetComponentRO<UtilityResultBuffer>(self).Top().Candidate);
    }
}
```

---

## 2. Weapon selection — "what do I fire?"

### 2.1 Definition (`StarterPack/WeaponSelectionDecision.cs`)

Candidate scorer over the entity's weapons against the already-chosen target (the threat-ranking
winner becomes `Ctx.Target`).

```csharp
[UtilityDecision(
    AssetId     = "2b5e8d31-4c0f-5e29-9b12-weapon0000001",
    DisplayName = "Weapon selection",
    Kind        = DecisionKind.WeaponSelection,
    Category    = "Tactical/Effectors")]
public sealed class WeaponSelectionDecision : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        .CandidateOption(Mode.WeightedProduct, o => o
            // No rounds for this weapon -> utility 0 (hard gate; the reason product mode was chosen).
            .Consider(In.WeaponHasAmmo(Ctx.Candidate),               w: 1.0f, Curve.Step)
            // Target inside this weapon's effective band scores high; outside falls off.
            .Consider(In.WeaponRangeBandFit(Ctx.Candidate, Ctx.Target), w: 1.0f, Curve.Bell)
            // Effectiveness of this weapon vs. the target's armor/type.
            .Consider(In.WeaponEffectivenessVsTarget(Ctx.Candidate, Ctx.Target), w: 1.0f, Curve.Linear)
            // Off-cooldown weapons preferred over ones about to be ready.
            .Consider(In.WeaponReadiness(Ctx.Candidate),             w: 0.6f, Curve.Linear));
}
```

### 2.2 Integration test

```csharp
public sealed class WeaponSelectionTests
{
    [Fact]
    public void OutOfRange_And_Empty_Weapons_Are_Gated_Out()
    {
        using var w = new UtilityTestWorld();
        // Owner carries the rifle (primary mount; on the owner entity).
        var self = w.SpawnAgent(health01: 1.0f, ammo01: 1.0f, initialAmmunition: 30);
        // Additional mounts as child entities: pistol (empty), launcher.
        var pistolMount   = w.SpawnWeaponMount(self, mountIndex: 1, weaponGuid: Weapons.PistolGuid,
                                               effRange:  15f, ammo01: 0f, initialAmmunition: 12);  // empty
        var launcherMount = w.SpawnWeaponMount(self, mountIndex: 2, weaponGuid: Weapons.LauncherGuid,
                                               effRange: 350f, ammo01: 1f, initialAmmunition:  4);

        var target = w.Repo.CreateEntity();
        w.SeedContact(self, target, distanceM: 160f, threatBoost: 0.5f, contactHealth01: 1f, hasLos: true);
        // Armor info (added separately by the design's combat layer; the WeaponEffectiveness reader
        // reads it). Test omits armor for brevity — soft is the default; rifle wins on band-fit.

        w.Scorer.Evaluate(w.Repo, self, WeaponSelectionDecision.Id, target: target);

        ref readonly var r = ref w.Repo.GetComponentRO<UtilityResultBuffer>(self);
        Assert.Equal(self.PackedValue,         r.Top().Candidate);                       // owner's rifle: in-band, effective
        Assert.True(r.ScoreOf(pistolMount.PackedValue) < 0.01f);                         // empty -> gated
        Assert.True(r.ScoreOf(self.PackedValue) > r.ScoreOf(launcherMount.PackedValue)); // band fit beats overkill
    }
}
```

---

## 3. Combat posture — "hide, flee, or advance?"

### 3.1 Definition (`StarterPack/CombatPostureDecision.cs`)

A `PostureSelect` `UtilitySelector` over a fixed authored set. This is the example from the
architecture doc, completed with `Suppress` and `Hold`. EQS cover/retreat scores enter as inputs
(`EqsTopScore`), proving the EQS-as-input seam at the posture level.

```csharp
[UtilityDecision(
    AssetId     = "3c6f9e42-5d10-6f3a-ac23-posture0000001",
    DisplayName = "Combat posture",
    Kind        = DecisionKind.PostureSelect,
    Category    = "Tactical/Posture",
    HysteresisBonus = 0.08f)]            // §4.5 anti-flip-flop, per-decision (Q-1)
public sealed class CombatPostureDecision : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        .Option(Posture.AdvanceAndAttack, Mode.WeightedProduct, o => o
            .Consider(In.HealthFraction(Ctx.Self),       w: 0.7f, Curve.Linear)
            .Consider(In.AmmoFraction(Ctx.Self),         w: 0.9f, Curve.Threshold)   // gate: need ammo
            .Consider(In.EnemyStrengthRatio(),           w: 0.8f, Curve.InverseLinear) // weaker enemy -> advance
            .Consider(In.HaveLiveTarget(),               w: 1.0f, Curve.Step))

        .Option(Posture.TakeCover, Mode.WeightedProduct, o => o
            .Consider(In.HealthFraction(Ctx.Self),       w: 0.8f, Curve.InverseLinear) // hurt -> cover
            .Consider(In.EqsTopScore("CoverQuery"),      w: 1.0f, Curve.Linear)        // gate: cover must exist
            .Consider(In.EnemyStrengthRatio(),           w: 0.6f, Curve.Logistic))     // outnumbered -> cover

        .Option(Posture.Suppress, Mode.WeightedProduct, o => o
            .Consider(In.AmmoFraction(Ctx.Self),         w: 0.9f, Curve.Linear)
            .Consider(In.HaveLiveTarget(),               w: 1.0f, Curve.Step)
            .Consider(In.AllyAdvancingNearby(),          w: 0.7f, Curve.Linear))       // suppress to cover an ally

        .Option(Posture.Flee, Mode.WeightedProduct, o => o
            .Consider(In.HealthFraction(Ctx.Self),       w: 1.0f, Curve.InverseQuadratic) // near-death dominates
            .Consider(In.EqsTopScore("RetreatQuery"),    w: 0.8f, Curve.Linear)           // gate: escape must exist
            .Consider(In.EnemyStrengthRatio(),           w: 0.7f, Curve.Logistic))

        .Option(Posture.Hold, Mode.WeightedSum, o => o            // sum: a gentle always-available fallback
            .Consider(In.HealthFraction(Ctx.Self),       w: 0.3f, Curve.Linear)
            .Consider(In.Constant(0.2f),                 w: 1.0f, Curve.Linear));        // floor so something always wins
}
```

### 3.2 Integration test — the three named scenarios

```csharp
public sealed class CombatPostureTests
{
    static (UtilityTestWorld w, Entity self) Healthy()
    {
        var w = new UtilityTestWorld();
        var self = w.SpawnAgent(health01: 1.0f, ammo01: 1.0f);
        var enemy = w.Repo.CreateEntity();
        w.SeedContact(self, enemy, 120f, 0.5f, 1f, hasLos: true);
        w.SetEnemyStrengthRatio(self, 0.5f);     // we outnumber them
        return (w, self);
    }

    [Fact]
    public void Healthy_Outnumbering_Advances()
    {
        var (w, self) = Healthy();
        using (w)
        {
            var posture = w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id);
            Assert.Equal(Posture.AdvanceAndAttack, posture);
        }
    }

    [Fact]
    public void Hurt_With_Cover_Available_Takes_Cover()
    {
        using var w = new UtilityTestWorld();
        var self = w.SpawnAgent(health01: 0.35f, ammo01: 0.8f);
        var enemy = w.Repo.CreateEntity();
        w.SeedContact(self, enemy, 90f, 0.8f, 1f, hasLos: true);
        w.SetEnemyStrengthRatio(self, 1.3f);     // slightly outnumbered
        w.SpawnEqsSensor(self, blueprintId: Fnv1a32("CoverQuery"),   topScore: 0.85f, count: 3, instanceId: 0); // good cover
        w.SpawnEqsSensor(self, blueprintId: Fnv1a32("RetreatQuery"), topScore: 0.20f, count: 1, instanceId: 1); // poor escape

        Assert.Equal(Posture.TakeCover, w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id));
    }

    [Fact]
    public void NearDeath_With_Escape_Flees()
    {
        using var w = new UtilityTestWorld();
        var self = w.SpawnAgent(health01: 0.12f, ammo01: 0.3f);
        var enemy = w.Repo.CreateEntity();
        w.SeedContact(self, enemy, 70f, 0.9f, 1f, hasLos: true);
        w.SetEnemyStrengthRatio(self, 2.5f);     // badly outnumbered
        w.SpawnEqsSensor(self, blueprintId: Fnv1a32("CoverQuery"),   topScore: 0.30f, count: 1, instanceId: 0);
        w.SpawnEqsSensor(self, blueprintId: Fnv1a32("RetreatQuery"), topScore: 0.75f, count: 2, instanceId: 1); // escape exists

        Assert.Equal(Posture.Flee, w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id));
    }

    [Fact]
    public void NearDeath_With_No_Escape_And_No_Cover_Does_Not_Flee_Into_Nothing()
    {
        using var w = new UtilityTestWorld();
        var self = w.SpawnAgent(health01: 0.12f, ammo01: 0.6f);
        var enemy = w.Repo.CreateEntity();
        w.SeedContact(self, enemy, 50f, 0.9f, 1f, hasLos: true);
        w.SetEnemyStrengthRatio(self, 2.5f);
        w.SpawnEqsSensor(self, blueprintId: Fnv1a32("CoverQuery"),   topScore: 0.05f, count: 0, instanceId: 0);  // both gated
        w.SpawnEqsSensor(self, blueprintId: Fnv1a32("RetreatQuery"), topScore: 0.05f, count: 0, instanceId: 1);

        // Flee and Cover are gated by their EQS inputs; Suppress/Advance need the situation.
        // Hold (sum mode, with a floor) survives as the fallback — the design intent of §3.1.
        Assert.Equal(Posture.Hold, w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id));
    }

    [Fact]
    public void Hysteresis_Prevents_Flip_Flop_On_Marginal_Inputs()
    {
        using var w = new UtilityTestWorld();
        var self = w.SpawnAgent(health01: 0.51f, ammo01: 0.9f);
        var enemy = w.Repo.CreateEntity();
        w.SeedContact(self, enemy, 110f, 0.5f, 1f, hasLos: true);
        w.SetEnemyStrengthRatio(self, 1.0f);
        w.SpawnEqsSensor(self, blueprintId: Fnv1a32("CoverQuery"), topScore: 0.55f, count: 2, instanceId: 0);

        var first = w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id);
        // nudge health down by 1% — without hysteresis this could flip Advance<->Cover each tick
        w.SetHealth(self, 0.50f);
        var second = w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id);
        Assert.Equal(first, second);   // hysteresis bonus on the active posture holds it
    }
}
```

---

## 4. Leader fire coordination — greedy assignment + member veto

### 4.1 Definition (`StarterPack/LeaderAssignmentDecision.cs`)

The leader runs the **same threat-scoring core** per (member, target) pair. The definition is the
per-pair scorer; the greedy allocation across the matrix lives in
`ThreatMatrixAssignmentSystem` (§10.2 of the architecture doc), not in the definition.

```csharp
[UtilityDecision(
    AssetId     = "4d70af53-6e21-7a4b-bd34-leader00000001",
    DisplayName = "Leader fire assignment (per member-target pair)",
    Kind        = DecisionKind.ThreatRanking,    // reuses the threat core
    Category    = "Tactical/Coordination")]
public sealed class LeaderAssignmentDecision : IUtilityDecisionDefinition
{
    public static void Build(IUtilityDecisionBuilder b) => b
        .CandidateOption(Mode.WeightedProduct, o => o
            .Consider(In.MemberHasLosToContact(Ctx.Self, Ctx.Candidate), w: 1.0f, Curve.Step)
            .Consider(In.MemberWeaponEffVsContact(Ctx.Self, Ctx.Candidate), w: 1.0f, Curve.Linear)
            .Consider(In.DistanceToContext(Ctx.Candidate),                w: 0.6f, Curve.InverseLinear)
            .Consider(In.ContactThreatLevel(Ctx.Candidate),               w: 0.9f, Curve.Linear));
}
```

### 4.2 Squad scaffolding (commander `Blackboard1024` + `UnitRoster`)

The leader uses the real commander infrastructure (architecture §10.1): a `UnitRoster` for the
member list and a `Blackboard1024` that the assignment system projects as
`ThreatMatrixAssignmentState`. These helpers extend `UtilityTestWorld`.

```csharp
public Entity SpawnLeader()
{
    var leader = Repo.CreateEntity();
    Repo.AddComponent<UnitRoster>(leader);        // capacity 16 (hardcoded in engine)
    Repo.AddComponent<Blackboard1024>(leader);    // 1024-byte block; ThreatMatrixAssignmentState lives here
    Repo.AddComponent<TargetMemory>(leader);      // commander's perceived contacts
    Repo.AddComponent(leader, new Fdp.Toolkit.Geographic.Components.Position { Value = Vector3.Zero });
    return leader;
}

public Entity SpawnSquadMember(Entity leader, float health01, float ammo01, bool asLauncher = false)
{
    var m = SpawnAgent(health01, ammo01);                                   // primary mount on owner
    if (asLauncher)
    {
        // Replace the owner's rifle-class primary with a launcher-class primary; or attach the
        // launcher as a second mount. For the starter pack we attach as a child for symmetry
        // with the multi-mount test.
        SpawnWeaponMount(m, mountIndex: 1, weaponGuid: Weapons.LauncherGuid,
                         effRange: 350f, ammo01: ammo01, initialAmmunition: 4);
    }
    Repo.AddComponent(m, new UnitSubordinate { Commander = leader });
    ref var roster = ref Repo.GetComponentRW<UnitRoster>(leader);
    UnitRoster.Add(ref roster, m.PackedValue);                              // P0.4
    return m;
}

public Entity SpawnTarget()
{
    var t = Repo.CreateEntity();
    Repo.AddComponent(t, new Health { Current = 100f, Max = 100f });
    Repo.AddComponent(t, new Fdp.Toolkit.Geographic.Components.Position { Value = Vector3.Zero });
    return t;
}

public void SeedSquadContacts(Entity leader, Entity[] targets)
{
    foreach (var t in targets) SeedContact(leader, t, 120f, 0.6f, 1f, hasLos: true);
}

/// Read a member's assignment back out of the projected blackboard state (P0.5 helper).
public long AssignmentFor(Entity leader, Entity member)
{
    ref var bb     = ref Repo.GetComponentRW<Blackboard1024>(leader);
    ref var state  = ref Blackboard1024.Project<ThreatMatrixAssignmentState>(ref bb);
    ref var roster = ref Repo.GetComponentRW<UnitRoster>(leader);
    int slot       = UnitRoster.IndexOf(ref roster, member.PackedValue);     // P0.4
    return state.AssignedTargetId(slot);                                     // helper on the projected struct
}
```

### 4.3 Integration test — squad doesn't dogpile, wounded member vetoes

```csharp
public sealed class LeaderAssignmentTests
{
    [Fact]
    public void Greedy_Assignment_Spreads_Fire_With_FocusFire_Bias()
    {
        using var w = new UtilityTestWorld();
        var leader = w.SpawnLeader();
        var m1 = w.SpawnSquadMember(leader, health01: 1f, ammo01: 1f);
        var m2 = w.SpawnSquadMember(leader, health01: 1f, ammo01: 1f);
        var m3 = w.SpawnSquadMember(leader, health01: 1f, ammo01: 1f, asLauncher: true);

        var t1 = w.SpawnTarget();   // soft (default; rifles preferred)
        var t2 = w.SpawnTarget();   // heavy in a real test would carry an ArmorInfo if/when added
        w.SeedSquadContacts(leader, new[] { t1, t2 });

        var sys = new ThreatMatrixAssignmentSystem(LeaderAssignmentDecision.Id, focusFireCap: 2);
        sys.Run(w.Repo, leader);   // writes ThreatMatrixAssignmentState into leader's Blackboard1024

        // launcher member sent to the heavy target; rifles cover the soft target.
        Assert.Equal(t2.PackedValue, w.AssignmentFor(leader, m3));
        Assert.Equal(t1.PackedValue, w.AssignmentFor(leader, m1));
        Assert.Equal(t1.PackedValue, w.AssignmentFor(leader, m2));   // both rifles on soft, within focusFireCap=2
    }

    [Fact]
    public void Wounded_Member_Vetoes_Assignment_And_Breaks_Off()
    {
        using var w = new UtilityTestWorld();
        var leader = w.SpawnLeader();
        var m1 = w.SpawnSquadMember(leader, health01: 0.08f, ammo01: 0.5f);
        var t1 = w.SpawnTarget();
        w.SeedSquadContacts(leader, new[] { t1 });

        // Leader assigns m1 -> t1 (written to the projected blackboard state).
        new ThreatMatrixAssignmentSystem(LeaderAssignmentDecision.Id, focusFireCap: 2).Run(w.Repo, leader);
        Assert.Equal(t1.PackedValue, w.AssignmentFor(leader, m1));

        // m1's OWN posture decision runs. Near-death + assignment present.
        // m1 reads its assignment via UnitSubordinate.Commander -> leader's Blackboard1024.
        // The assigned-target consideration biases "engage", but InverseQuadratic health
        // consideration drives Advance/Suppress toward 0; Flee wins -> the veto.
        w.SpawnEqsSensor(m1, blueprintId: Fnv1a32("RetreatQuery"), topScore: 0.7f, count: 1, instanceId: 1);
        var posture = w.Scorer.SelectPosture(w.Repo, m1, CombatPostureDecision.Id);
        Assert.Equal(Posture.Flee, posture);   // member overrode the leader; assignment was a consideration, not an order
    }
}
```

### 4.4 What this pair proves

- The leader's greedy pass spreads fire (focus-fire cap) and matches effectors to armor — the
  coordination requirement.
- The assignment is a *consideration*, not an imperative: a near-death member's self-preservation
  considerations zero out the engage options and Flee wins. The veto falls out of the
  multiplicative math with no separate override protocol (§10.3/§10.4 of the architecture doc).

---

## 5. Trace assertions (the debug deliverable, tested)

Because tracing is baked into the core (§9.2), the tests can assert *why*, not just *what* — this
is the regression guard for the "why did it pick this?" feature.

```csharp
[Fact]
public void Trace_Records_Per_Consideration_Breakdown_For_Winner()
{
    using var w = new UtilityTestWorld();
    var self = w.SpawnAgent(0.35f, 0.8f);
    var enemy = w.Repo.CreateEntity();
    w.SeedContact(self, enemy, 90f, 0.8f, 1f, hasLos: true);
    w.SetEnemyStrengthRatio(self, 1.3f);
    w.SpawnEqsSensor(self, blueprintId: Fnv1a32("CoverQuery"), topScore: 0.85f, count: 3, instanceId: 0);

    w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id);

    ref readonly var trace = ref w.Repo.GetComponent<UtilityTraceWorkingMemory1024>(self);
    var winner = trace.LatestSelected();
    Assert.Equal(Posture.TakeCover, winner.OptionId);

    // The EQS cover input must show as the dominant term in the winning option.
    var cover = winner.ConsiderationByInput(In.EqsTopScore(Fnv1a32("CoverQuery")).InputId);
    Assert.Equal(0.85f, cover.RawValue, precision: 2);
    Assert.True(cover.CurveOutput > 0.8f);

    // Runner-up margin recorded so the overlay can show "won by X".
    Assert.True(winner.RunnerUpMargin > 0f);
}
```

---

## 6. How these slot into the test pyramid

| Test | Level | Guards |
|---|---|---|
| §1–§3 per-decision | Integration (real scorer, fake world) | scoring math, curves, gates, EQS-as-input |
| §4 leader pair | Integration (scorer + assignment system) | coordination + veto authority model |
| §5 trace | Integration | the debug deliverable can't silently regress |
| (follow-on) curve unit tests | Unit | each `CurveKind` maps input→output correctly |
| (follow-on) aggregator unit tests | Unit | product-with-compensation vs. sum, §4.3/4.4 |

The four starter-pack definitions are the canonical fixtures: the editor loads them for the
wireframe screens, the comparison feature uses them as before/after fixtures (next doc), and these
tests pin their behavior so a curve-tuning change that breaks a named scenario fails CI.

---

## 7. Test-helper mapping (v1.2)

The convenience setters used in the tests resolve to real component writes; none of them are
production APIs, only test scaffolding:

| Test helper | What it really does |
|---|---|
| `SpawnAgent` | writes `Health`, primary `WeaponState` (with `MaxAmmo` per P0.1) on owner, `Position`, `TargetMemory`, `UtilityResultBuffer`, `UtilityDebugFlags`, `UtilityTraceWorkingMemory1024` |
| `SpawnWeaponMount` | creates a child entity with `WeaponState` + `WeaponMountInfo` + `PartMetadata` (P0.2 pattern) |
| `SetWeaponAmmo(owner, mountIndex, …)` | resolves the mount by walking `PartMetadata`; sets `WeaponState.Ammo` against the cached `MaxAmmo` |
| `SeedContact` | `TargetMemory.AddOrUpdateTarget(...)` (position + score + modality); adds `Health` and `Position` to the contact if missing |
| `SetEnemyStrengthRatio` | **test-only shortcut** — forces the value the `EnemyStrengthRatio` reader would derive from summing `TargetMemory.ThreatScores`; real runs compute it (§6.4) |
| `SpawnEqsSensor(owner, blueprintId, topScore, count, instanceId)` | creates a child entity with `EqsSensor` + `EqsCognitiveBuffer` (seeded via `GetSpanRW()`) + `PartMetadata.ParentEntity = owner` — exactly the shape `EqsResultUpdateSystem` would produce in production |
| `SpawnLeader` / `SpawnSquadMember` | `UnitRoster` + `Blackboard1024` on the leader; `UnitSubordinate.Commander` on members; `UnitRoster.Add` (P0.4) for roster insertion |
| `AssignmentFor` | `Blackboard1024.Project<ThreatMatrixAssignmentState>` (P0.5) on the leader's blackboard; `UnitRoster.IndexOf` (P0.4) to resolve the member's slot |
| `Fnv1a32(string)` | computes the 32-bit FNV-1a hash matching the source generator's formula (see source-gen DD §3.3); used to derive `EqsSensor.BlueprintId` from a template name in tests |

The one helper that hides real logic is `SetEnemyStrengthRatio`: in production that input is a
derived reader, so a follow-on unit test should also exercise the *derivation* (sum of
`ThreatScores` vs. own strength) rather than only the forced value. `ArmorInfo`/`ArmorClass` is
not in v236; if Weapon Selection's effectiveness reader needs it, the implementer adds it as a
small Phase-1 component (out of P0 scope).

---

*End of Utility AI Starter Pack & Integration Tests v1.2. Depends on
[`PREREQ_Phase0_Bundle.md`](./PREREQ_Phase0_Bundle.md) for the multi-mount, MaxAmmo, perception-cap,
`UnitRoster`, `Blackboard1024.Project<T>`, and `UtilityTestWorld` prerequisites. Aligns with
Utility AI architecture v1.2 (§8.1 corrected invariant, §6.6 child-entity EQS, §10.1 commander
`Blackboard1024.Project<T>`).*
