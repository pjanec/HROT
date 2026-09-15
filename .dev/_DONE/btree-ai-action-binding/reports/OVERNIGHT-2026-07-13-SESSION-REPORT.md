# Overnight session report — 2026-07-13 → 07-14

> **Orchestration:** Opus planned/reviewed; Sonnet agents did the coding under hard per-commit gates.
> **Branch:** `claude/hill-attack-json-slice-3-7fbaf4`. **Rule honored:** every code commit left the
> deterministic gates green (BehaviorRegistry, HillAttack 58, Generators 103); nothing red was committed.

---

## Headline

**The thing that was actually breaking HillAttack — the behavior-registry double-registration — is fixed.**
Root cause: `AiHotReloadCoordinator.ScanForRegistrars` invokes every `[BlueprintRegistrar]`, so both
`AiBehaviorFactory` (id 3014, **with** the geo `ParseParams`) and the generated `PlatoonHillAttackRegistrar`
(GUID-derived id, **without** `ParseParams`) register the name "PlatoonHillAttack". They run in type-name
order, so the generated (ParseParams-less) def registered second and silently overwrote the name→id map;
at runtime the scenario resolved the behavior by name to the ParseParams-less id, so params were never
parsed (the "vehicles drive to origin" symptom).

## Commits landed (verified)

| Commit | What | Gate |
|---|---|---|
| `b1f3f6e` | **fix(behavior):** order-independent anti-shadow rule in `BehaviorRegistry.Register` — a `ParseParams`-bearing def can never be shadowed by one lacking it (either order); non-fatal `Debug.WriteLine` on collision. + 4 deterministic tests. | BehaviorRegistry 10/10, HillAttack 58/58, Generators 103/103 |
| `3dbc832` | **docs:** corrected stale `HillAttackMutableState` XML doc (it claimed the removed `Unsafe.As`/`Blackboard1024` hack); SUPERSEDED banner on `S3-G-BLOCKER.md`; added `MoveToAndFire-Bug-Triage-2026-07-13.md`. | build clean |
| `66a3c05` | **test(blueprints):** re-enabled `BTreeTick_FirstCall_…` (now passes); kept `BTreeTick_AfterChannelComplete_…` skipped with an accurate reason (see below); replaced the stale 7-bug comment. | MoveToAndFire filter: 13 pass, 1 skip, 1 pre-existing fail |

(Design docs `c20dda6`, `e4f7e2b`, `9dd8698` from earlier were already pushed.)

## Notable finding — reviewing hard caught an agent over-claim

The triage agent concluded "all 7 MoveToAndFire bugs are fixed." Six are. But when I actually un-skipped
both tests and ran them, **`BTreeTick_AfterChannelComplete_ReturnsSuccess` fails**: on Tick 2 (channel
complete) it returns **Failure instead of Success**. That is a **real, still-open bug** in the
channel-complete/Return lowering (the old "NodeStatus enum mismatch" family). I did **not** ship it green —
that test stays skipped with an accurate reason. Only the genuinely-passing first-tick test was re-enabled.

## Pre-existing failures (NOT caused by this session — baseline)

- `Fdp.Toolkits.Tests`: 2 unrelated Gizmo tests — `SC_GZ022_2_Register_UnregisteredType_Throws`,
  `SC_GZ004_2_Register_UnregisteredComponent_Throws` (expect a throw that no longer happens).
- `SimHost.Tests`: staging-extractor ×2 (`Extract_InitialUnitSubordinateIntent_…`,
  `Extract_WithChildEntity_…`), `EditLoadClusterOpHandlerTests.LoadExistingScenario_SpawnsCorrectEntityCount`
  (scenario references unknown component `EditLoadTestPos`), `FullBranchPipelineTests.BranchedRecording_…`.
  The failing count varied run-to-run (2↔4) → some are **flaky/timing-dependent**.
- `Hrot.Blueprints.Tests`: `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` — a **stale
  golden `.cs.txt` snapshot** (emission legitimately changed, e.g. added `DebugProbe.NodeEnter` calls).

## Parked for the morning (with reasons)

1. **`3013` magic-literal dedup (G-2).** The clean fix (reference `BehaviorIds.HullDownAttackRun_BT`) is
   blocked: `BehaviorIds` is `internal` in Hrot.Core, so `HillAttackCommanderNodes` (a different assembly)
   can't see it. Options: make `BehaviorIds` public (small API decision), or — better — do the design's real
   fix: **name-derived `ActiveBehaviorHash` = FNV(name)**, which retires the magic int entirely. Needs your
   call; cosmetic, not urgent.
2. **`BTreeTick_AfterChannelComplete` Success→Failure bug.** Real codegen bug in the channel-complete/Return
   path. Sits in the blueprint-AiPrimitive emission the team already parked (Phase 5 / DEBT-AIB-025); best
   fixed with you available to confirm intended semantics.
3. **`MoveToAndFire_GeneratedSource_Snapshot` golden staleness.** Regenerating a golden file is the test's
   source of truth — I won't auto-regenerate it (could mask a real regression). One-line human decision:
   confirm the emission change (debug probes) is intended, then update the snapshot.
4. **Phase-1 resolver / retire `AiBehaviorFactory` (G1/G3/G6).** Now **non-urgent** — the anti-shadow fix
   makes the factory/generated coexistence safe, so this dropped from "bug fix" to "designed improvement."
   Large; the design (`Behavior_Parameter_Resolver_Detailed_Design.md`) is still yours to finalize.
5. **I1 — route AiPrimitive action/condition registration into the `ActionRegistry` the interpreter reads**
   (`BTree_AiActionParameterBinding_Detailed_Design_Status.md` §I1). The real remaining gap for
   blueprint-authored BTree actions; large, cross-cutting, needs sign-off.

## Verification environment

All gates run headless on this Linux container (dotnet 8): `Generators.Tests` 103/103, `SimHost.Tests`
`~HillAttack` 58/58, `Fdp.Toolkits.Tests` `~BehaviorRegistry` 10/10, `Blueprints.Tests` `~MoveToAndFire`
13 pass / 1 skip / 1 pre-existing fail. `MigrationEquivalenceTests` (byte-identity/equivalence) is inside
the green Generators suite.
