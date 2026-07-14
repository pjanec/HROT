# Behavior Architecture — Sequenced Implementation Plan

> **Progress (2026-07-14, cont.):** **Phase 3 partially landed —** **I1** (AiPrimitive BTree
> actions register into the FastBTree `ActionRegistry` and run through a real interpreter tick;
> canary `MoveToAndFire_InterpreterTick_Tests`) and **G2 R1+R2** (Library blueprint functions are
> runtime-invocable by name via `BlueprintDefinition.Functions` + `LibraryFunctionDelegate`; test
> `LibraryFunction_InvokeTests`) are **DONE** and green. **Remaining Phase 3/4 is editor-gated
> (needs the Windows box to verify):** **I4** (discovery so blueprint actions appear in the node
> palette — the `ActionSchemaExporter` derives its DTO from the first ref param, which doesn't fit
> the AiPrimitive thunk shape, so it needs an attribute-carried DTO or a parallel catalog; payoff is
> the palette), **I2/I3** (compose a blueprint action as a host-BTree node with partition-slot
> working state — runtime/codegen is headless-testable, but its authoring is the editor), **G2 R4 +
> §8.3** (world-services-into-function + the authored↔usable adapter that complete the fully-visual
> resolver, with G7 authoring UX), and all of **Phase 4**. Handed off at the Windows boundary.
>
> **Progress (2026-07-14):** **Phase 1 (name-as-identity) — DONE. Phase 2 (resolver + retire
> `AiBehaviorFactory`) — DONE (2a, 2b, 2c). Phase 1e (duplicate-name hard error) — DONE.**
> The factory is deleted; every behavior self-registers under its unique name via `[BlueprintRegistrar]`
> discovery (`CgfCuratedBehaviorRegistrar` for the FbtTreeCatalog/HSM topologies; generated registrars
> for JSON assets). Curated resolvers are bound by name through `BehaviorRegistry.RegisterResolver`
> (name-keyed overlay), which let the generated `HullDownAttackRun`/`PlatoonHillAttack` registrars own
> their topology outright — eliminating the curated↔generated double-registration, so `Register` now
> **hard-errors** on a duplicate name. Resolvers reach geo/entity context through world singletons
> (`IGeographicTransform` gained `[ComponentId]` = 75) rather than a registration-time closure.
> Gates green: HillAttack 58/58 + scanner 4/4, Generators 103/103 (byte-identity), Fdp.Toolkits
> Behavior 169/169, full solution 0 errors. **Remaining: Phase 3 (blueprint actions/conditions
> runnable, Library resolver) and Phase 4 (editor authoring, Windows-verified).** Not headlessly
> verified: editor hot-reload threading and the live game/cluster startup path.
>
> **Status:** approved to execute (2026-07-14). Sequenced, dependency-ordered plan for the
> name-identity / resolver / blueprint-action architecture. Design is finalized in the docs below;
> this is the *how and in what order*.
> **Execution model:** Opus orchestrates and reviews every change hard (diff + build + gates before
> commit); Sonnet agents do the mechanical, well-specified tasks marked **[S]**. Design-sensitive,
> combat-critical, or cross-cutting tasks are marked **[O]** (Opus authors, or Sonnet drafts under
> close Opus review).
> **Invariant:** every commit leaves the deterministic gates green — `SimHost ~HillAttack` (58),
> `Generators` (103, includes the byte-identity `MigrationEquivalenceTests`), `Fdp.Toolkits ~BehaviorRegistry`,
> and `Blueprints.Tests` (0 failed). No red is committed; each task is independently landable.
> **Verification reach:** Phases 1–3 (runtime / codegen / tests) are fully verifiable headless on Linux.
> Phase 4 (editor UI, ImGui) cannot be exercised headless — it is verified on the Windows box.
> **Related canonical docs:** `Behavior_Parameter_Resolver_Detailed_Design.md` (resolver, §7 G1–G7, §8 R/E),
> `BTree_AiActionParameterBinding_Detailed_Design.md` (§3.2 composition, §4.4 scopes),
> `BTree_AiActionParameterBinding_Detailed_Design_Status.md` (I1–I4, E1–E6 gap status).

---

## 1. Principles

1. **Green at every commit.** Run the gates above after each task; commit only on green vs. the known baseline (2 pre-existing Gizmo failures in Fdp.Toolkits; the flaky staging/scenario SimHost set — both unrelated).
2. **Name-identity first.** It is load-bearing: it structurally collapses the double-registration and makes the interim anti-shadow rule (`b1f3f6e`) upgrade to a hard error.
3. **Additive-then-flip.** Where a change is cross-cutting (the id scheme), introduce the new path additively (dual-key), migrate call sites, then remove the old path — so no single commit is a big-bang.
4. **[O]/[S] split.** [S] = mechanical/well-specified (test migrations, additive registration, attribute emission, recipe files, golden regens). [O] = registry re-keying design, resolver signature+seam, I1 wiring, adapter emission, anything touching combat semantics. Opus reviews all [S] output against the gates before commit.

## 2. Phase map & dependency (the crux)

```
Phase 1  Name = identity ──────────────► unblocks G4 hard-error, factory retirement, magic-int removal
   │            (FNV(name) id; both producers mint the SAME id ⇒ dup collapses)
   ▼
Phase 2  Resolver + retire AiBehaviorFactory
   │            (needs Phase 1's single-record-per-name; geo/entity singletons)
   ▼
Phase 3  Blueprint actions/conditions runnable (I1–I3) + Library-resolver path (G2)
   │            (shares the S3-G adapter rail; today's self-probe fix already unblocked Library emit)
   ▼
Phase 4  Editor authoring (G7, I4, E1–E6) ── Windows-verified
```

The single most important realization: **once id = `FNV(name)`, `AiBehaviorFactory` (was 3014) and the generated `PlatoonHillAttackRegistrar` (was a GUID-derived id) mint the identical id for "PlatoonHillAttack".** The two records converge on one key, the anti-shadow rule stops mattering, and a *remaining* duplicate name becomes a genuine error. That is why Phase 1 precedes everything.

## 3. Phase 1 — Name as identity (foundation)

| # | Task | Key files / seams | Owner | Gate |
|---|---|---|---|---|
| 1a | Add canonical `BehaviorHash.FromName(string) => FNV-1a(name)` helper; make `BehaviorRegistry` resolve definitions by the name-hash while **keeping** the existing int-id path (dual-key, additive). | `Fdp.Toolkits/Behavior/BehaviorRegistry.cs`, new `BehaviorHash.cs` | [O] | Fdp.Toolkits ~BehaviorRegistry; all suites unchanged |
| 1b | Both producers mint `id = FromName(name)`: replace `AiBehaviorFactory`'s hardcoded 3001–3014 and the generated registrar's `DeterministicIdFromGuid` with `FromName`. Same-name ⇒ same id ⇒ dup collapses. | `AiBehaviorFactory.cs`, `BTreeBridgeEmitCore.DeterministicIdFromGuid`, generated `*.Registrar.g.cs` | [O] | HillAttack 58; Generators 103; Behavior suites |
| 1c | `ActiveBehaviorHash` set consistently via `FromName`: reconcile the `TryGetId(name)`, raw `evt.BehaviorHash`, and DTO `DefaultBehaviorHash` set paths. | `BehaviorIngressSystem.cs` (:125,:210), `BehaviorTkbTranslator.cs`, `AssignBehaviorHashEvent` producers | [O] | Behavior + SimHost suites |
| 1d | Replace magic-int behavior refs with name-derived constants (**#3-proper**): `HillAttackCommanderNodes` `3013`, `HillAttackGizmo`/tests `3014`, etc. → `FromName("HullDownAttackRun")` etc. | `HillAttackCommanderNodes.cs` (:38,470,481), `Gizmos/HillAttackGizmo.cs` | [O] for combat nodes; [S] for gizmos | HillAttack 58; byte-identity |
| 1e | Flip `BehaviorRegistry.Register` dup handling from the interim anti-shadow **warn** (`b1f3f6e`) to a **hard error** on true duplicate name (now safe: same behavior ⇒ same id, not a collision). | `BehaviorRegistry.cs` | [O] | new dup-name test throws; existing green |
| 1f | Migrate tests that hardcode `ActiveBehaviorHash = <int>` (3013/3014/1001/42/999/…) to `FromName(name)` or the registered name. ~40 sites across Fdp.Toolkits.Tests, SimHost.Tests, IG.Tests, examples. | many `*Tests.cs` | [S] | each project's suite green |
| 1g | Retire / repoint the magic-int id tables (`BehaviorIds`, `CgfBehaviorIds`) — keep only as `name→hash` convenience if still needed, or delete. | `BehaviorIds.cs`, `CgfBehaviorIds.cs` | [O] | full build + suites |

Land order within Phase 1: 1a → 1b → (1c,1d together) → 1e → 1f → 1g. After 1e the double-registration is gone by construction, not by heuristic.

## 4. Phase 2 — Resolver + retire the factory

| # | Task | Key files / seams | Owner | Gate |
|---|---|---|---|---|
| 2a | **G3** — register the geographic transform as a world singleton (`SetSingletonManaged`), alongside `NetworkEntityMap` (already a singleton). Additive; no consumer yet. | `Hrot.SimHost/SimHostApp.cs`; geo transform type | [S] | build + SimHost suites |
| 2b | **G1** — evolve `ParseParamsDelegate` into a resolver: pass `ISimulationView` + `Entity self`; split generic auto-deserialize (keyed by `ParamsDtoType`) from the resolve step; invoke at the `BehaviorIngressSystem` seam (~:119, after parse-commit, before slot provisioning). Convert `ParsePlatoonHillAttackParams` into a **named hardcoded resolver** that reaches geo/entity via singletons (no factory closure). | `BehaviorRegistry.cs` (delegate), `BehaviorIngressSystem.cs`, `HillAttackCommanderNodes.ParsePlatoonHillAttackParams` | [O] | HillAttack 58 + integration; resolve-once test |
| 2c | **G6** — retire `AiBehaviorFactory`: each behavior self-registers under its name carrying its resolver reference; delete the factory + its closures. Curated ParseParams now expressed as named resolvers. | `AiBehaviorFactory.cs` (delete), generated registrars, `CgfBehaviorSetup` | [O] | full build; HillAttack + all behavior suites |

Phase 2 depends on Phase 1 (single record per name) and delivers the "behavior owns its contract" runtime. Resolvers here are **hardcoded** (C# functions named on the behavior); the blueprint-authored resolver comes in Phase 3.

## 5. Phase 3 — Blueprint actions/conditions runnable + Library resolver

| # | Task | Key files / seams | Owner | Gate |
|---|---|---|---|---|
| 3a | **I1** — route AiPrimitive action/condition thunks into the `ActionRegistry<BrainBlackboard,BTreeContext>` the FastBTree interpreter actually reads (string key `{fqn}@{offset}[@{slotKey}]`), instead of the orphaned `BehaviorRegistry` int-keyed dicts. | `CSharpEmitter.EmitAiPrimitiveRegistration`, `AiPrimitiveEmitter`, `Interpreter.BindActions` contract | [O] | un-skip a minimal MoveToAndFire interpreter-tick test → green |
| 3b | **I2** — emit the per-node adapter ("BTree owns layout, blueprint provides `TickCore`") by reusing `BTreeBridgeEmitCore.EmitStatefulActionThunks` with a "MethodFqn = generated `TickCore`" case. | `BTreeBridgeEmitCore.cs` | [O] | byte-identity; new blueprint-action tick test |
| 3c | **I3** — move blueprint working state onto the S3-G partition-slot rail (`BlueprintBlackboardPartitions` / `StatefulWorkingSlots`), replacing the fixed `Blackboard1024`+offset-8 in `AiPrimitiveEmitter`. Lifts the one-stateful-primitive-per-entity cap. | `AiPrimitiveEmitter.cs`, `BehaviorIngressSystem` provisioning | [O] | T20/T30-style stateful proof; Generators |
| 3d | **G2 / §8.3** — the **Library-function resolver** path: make Library functions runtime-invocable (delegate + `Functions` table on `BlueprintDefinition`, registrar emission — R1/R2) so a resolver can be authored as a blueprint, marshalled via the Phase-2 seam. (Today's self-probe fix already made Library emit compile.) | `BlueprintDefinition.cs`, `CSharpEmitter` Library registration, `LibraryEmitter` | [O] | Roslyn-compile + invoke test for a Library resolver |

## 6. Phase 4 — Editor authoring (Windows-verified)

Editor UI is not headless-verifiable here; land these with unit tests where possible (schema mutations, catalog population) and **verify interactively on the Windows box**.

| # | Task | Owner |
|---|---|---|
| 4a | **I4** — emit discovery attributes on generated thunks (or a parallel AiPrimitive catalog) so blueprint actions appear in `ActionSchemaExporter` / `BTreeNodeCatalog`. | [O] emit; [S] catalog wiring |
| 4b | **E1** — "New > Library / AiPrimitive" flow + `create-function` handler; a Condition recipe. Start with a Library recipe file (works today, zero code). | [S] |
| 4c | **E3** — typed param binding + Promote for blueprint actions (falls out of I4's DTO-type availability). | [S] |
| 4d | **G7** — resolver authoring UX: "detach authored shape", divergence detection, "Parameter resolver: None/Pick/Create" with Library scaffolding (reuses `BehaviorUiCompiler` DTO reflection). | [O] design; [S] pieces |
| 4e | **E4–E6** — condition authoring UI, cross-asset action picker, HSM action-name source (the `HsmActionDispatcher.AllActions` gap). | [O]/[S] mixed |

## 7. Delegation & review protocol

- Each task = one Sonnet agent (for [S]) or Opus-authored change (for [O]), on the branch, **committed only after Opus review**: read the diff, `dotnet build`, run the task's gate suite(s) + byte-identity, confirm no new failures vs baseline.
- Serial within a phase (small, reviewable commits); independent [S] test-migration batches (1f) may run in parallel across different test projects since they don't overlap files.
- Sonnet agents always report the `git diff` + build/test output; Opus never trusts a summary over the diff (cf. the AfterChannelComplete over-claim caught in the pre-work).
- Land the design-doc updates (mark G1–G7 / I1–I4 done as each ships).

## 8. Risk register

- **1b/1c (id re-key)** — highest cross-cutting risk; the additive dual-key in 1a is the mitigation (migrate, then remove). Watch: any persisted/serialized `ActiveBehaviorHash` (replay/flight-recorder, scenario TKB `DefaultBehaviorHash`) — if ordinals were persisted, a hash change reinterprets old data; audit before 1c.
- **2b (resolver seam)** — combat-critical (HillAttack params). Gate on the full HillAttack integration suite, not just node tests; keep the resolve-once-at-activation semantics (assignment ≡ activation) exact.
- **3a–3c (blueprint action wiring)** — the `MoveToAndFire` demo is the canary; un-skip incrementally as each piece lands.
- **Phase 4** — editor verification gap on Linux; do not claim UI correctness from headless runs — defer visual confirmation to Windows.
