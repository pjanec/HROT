# HANDOFF — Hill-Attack-json (Slice 3: Behavior-scope shared working state MVP)

**Date:** 2026-07-12 · **Branch:** `main` (direct-push convention) · **HEAD at handoff:** `73c990e8`
**Owner docs (source of truth — read these, this file only orients):**
- Plan/checklist: [`.dev/btree-ai-action-binding/TASK-TRACKER.md`](./TASK-TRACKER.md) (Slice 3 section)
- Per-batch specs + test conditions: [`.dev/btree-ai-action-binding/TASK-DETAIL.md`](./TASK-DETAIL.md) (S3-1 … S3-G)
- Design of record: [`docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md`](../../docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md) §4.4 (esp. the **key-formula resolution note**)
- Batch instructions written so far: `batches/BATCH-12-INSTRUCTIONS.md` (S3-1), `batches/BATCH-13-INSTRUCTIONS.md` (S3-2)

---

## 1. The goal (why this work exists)

Hill Attack (`PlatoonHillAttack` + `HullDownAttackRun`) has already been **jsonized to `.btree.json`** and runs entirely from JSON (params-only) — see `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/*.btree.json` and `AiBehaviorFactory.cs`. The one remaining hack: its shared mutable state (`HillAttackMutableState`, 120 B: wave/slot bitmasks + SoA attacker arrays) is still accessed via a hardcoded `ctx.World.GetComponentRW<Blackboard1024>() + Unsafe.As`, shared across `CalculateSegments` / `DispatchWave` / `IsWaveCompleted` on the commander entity.

**Slice 3 makes shared working state a first-class, user-authored, monitorable blackboard variable** so that hack disappears and Hill Attack is *fully* jsonized. The mechanism generalizes: every blackboard variable gets a **role** (`input`=param / `state`=mutable) and, for state, a **scope** (`Node` = per-node local [the existing Slice-2 case] / `Behavior` / `Entity`). **MVP = `Behavior` scope**: multiple nodes on one entity share **one** working-state slot. `HillAttackMutableState` becomes a `Behavior`-scoped shared variable; the three nodes bind the 4-param `ThreeParamReusableStateful` shape `(ref Params, ref HillAttackMutableState, ref BehaviorTreeState, ref TCtx)`.

Storage reuses Slice-2's partitioned tier (`BlueprintBlackboard{1024,4096,16384}` + `BlueprintBlackboardPartitions`) **unchanged** — only the **slot key** and **provisioning granularity** change.

---

## 2. RESOLVED design decision (was the only open question) — key formula

The AIB-DD §4.4 table originally said `Behavior = FNV(assetId, entityId)`. **Corrected 2026-07-12 (architect-proxy, code-grounded):**

| Scope | Key | Notes |
|---|---|---|
| `Node` | `FNV-1a(assetId, nodeVisualId)` — **unchanged** | Slice-2 keys preserved; do not add variableId |
| **`Behavior`** (MVP) | **`FNV-1a(assetId, variableId)`** | `variableId` = binding's `ExpressionTargetField` (variable Name) |
| `Entity` (post-MVP) | `FNV-1a(variableId)` — no assetId | survives behavior switch |

Two corrections, both important:
1. **Drop `entityId`** — the partitioned tier is a **per-entity ECS component** (`GetComponentRW<BlueprintBlackboard*>(ctx.Self)`; `TryGetSlotOffset(byte* memory, …)` scans only that entity's slot table). The key only disambiguates slots *within one entity*; entity is implicit.
2. **Add `variableId`** — a behavior may declare >1 Behavior-scoped variable; without it they collide onto one slot.

**Big consequence:** both key inputs are compile-time constants → **keys stay baked `const`s. There is NO runtime key computation.** The emitted thunk keeps its shape; a Behavior-scoped node just bakes the `(assetId, variableId)`-derived value, and co-bound nodes bake the *same* value → same per-entity slot. This collapsed what was originally feared to be a runtime-key thunk rewrite (S3-3) into a one-line derivation change. Keys are ephemeral runtime ids (never persisted) → **no byte-identity impact**; Node keys unchanged → Slice-2 untouched. (Memory: `project-behavior-scope-key-formula.md`.)

---

## 3. What's DONE (committed + pushed + independently verified)

| Batch | Commit | What | Verified |
|---|---|---|---|
| §4.4 key formula | `7e94fc08` | Resolved + docs updated | — |
| **S3-1** (BATCH-12) | `5eb8f1da` | Authoring: `Role`+`Scope` on DTOs + editor model + Variables-panel selectors; enums in `BlackboardVariableEnums.cs`; omit-when-default (byte-stable) | Persistence.Tests **136/0**, AiShared.Tests **1110/0** |
| **S3-2** (BATCH-13) | `58c9aabd` | Scope-aware `ComputeStatefulSlotKey(assetId, scope, nodeVisualId, variableId)` overload + tests | slot-key suite **6/6**; `SlotKey_Node_MatchesLegacy` = byte-identical |
| tracker checkoff | `73c990e8` | — | — |

`WorkingStateScope { Node, Behavior, Entity }` + `BlackboardVariableRole { Input, State }` live in `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/BlackboardVariableEnums.cs` (namespace `Hrot.AiEditor.Persistence`). The scope-aware key overload is in `BTreeBridgeEmitCore.cs` (next to the legacy 2-arg `ComputeStatefulSlotKey`).

---

## 4. What's LEFT (remaining Slice-3 batches — design fully resolved, no open questions)

Details + exact success conditions in TASK-DETAIL.md. Dependencies noted.

- **S3-3** — *Scope-aware baked const.* In `EmitStatefulActionThunks` (`BTreeBridgeEmitCore.cs:443–551`), derive the baked `const int __slotKey` per the binding's scope (Behavior → from `assetId`+`variableId` via S3-2's overload; Node unchanged). **Trivial now** — thunk shape unchanged. *Depends S3-2 ✓ → UNBLOCKED.*
- **S3-4** — *Shared-slot provisioning/dedup.* `ProvisionStatefulSlots`/manifest (`BehaviorIngressSystem.cs`, `StatefulSlotInfo` in `Fdp.Toolkits/Behavior/BehaviorRegistry.cs`, `EmitStatefulWorkingSlotsArray`): provision **one** slot per distinct Behavior-scoped variable per entity (not per node); dedupe by scope key. The meatier batch. *Depends S3-2 ✓ → UNBLOCKED.*
- **S3-5** — *`ClearBehaviorEvent` detach fix.* `BehaviorIngressSystem.cs:168–184` — capture prev behavior id + call `DetachStatefulSlots` on clear (today only switch detaches → clear-without-successor leaks). *Depends S3-4.*
- **S3-6** — *Fix-3 guard extension.* `HsmValidator.CheckConcurrentStatefulSubtrees` (`Hrot.Hsm.Editor/Validation/HsmValidator.cs:228`) — flag two stateful nodes in concurrent HSM regions resolving to the same Behavior/Entity slot key. Dormant for purely-sequential Behavior use. *Depends S3-2 ✓ → UNBLOCKED.*
- **S3-7** — *Monitoring (v1-mandatory).* Thread `Role`/`Scope` into `StatefulSlotInfo`; `StatefulWorkingStateProjection.RenderWorkingState` (`Hrot.Presentation/Renderers/StatefulWorkingStateProjection.cs:42`) groups/labels by scope; live read-only inspector shows shared-slot current values. *Depends S3-4.*
- **S3-G** — *DEMO GATE.* Convert `HillAttackMutableState` to a `Behavior`-scoped shared variable in `PlatoonHillAttack.btree.json`; rebind the 3 nodes to 4-param `ThreeParamReusableStateful`; remove the `Blackboard1024`+`Unsafe.As` hack; end-to-end proof test `T30_BehaviorScopedShared_ProofTests` (generate→compile→provision→tick). *Depends all.*

**Suggested next step:** S3-3 + S3-4 can run in **parallel** (different areas: emitter thunk vs. provisioning/manifest), both unblocked. S3-4 is the harder one. The user was deciding "S3-3+S3-4 together vs S3-4 solo first" at handoff — no decision made yet.

---

## 5. Working conventions (IMPORTANT — follow these)

- **Batch workflow:** maintain TASK-TRACKER/TASK-DETAIL, write a `batches/BATCH-NN-INSTRUCTIONS.md` per batch (next is **BATCH-14**), check off the box when done. Execute each batch via a **sonnet sub-agent in an isolated git worktree**.
- **Trust nothing — verify:** sub-agent edits can leak or not persist. After each agent: hard-review the diff, **independently re-run** the touched test projects, and only then integrate to `main`. Confirm out-of-scope files weren't touched.
- **Integration:** agents work in worktrees off `main`; bring changes in via `git -C <wt> diff | git apply` + copy untracked files; watch for shared new types across parallel batches (e.g. the `WorkingStateScope` enum duplicate between S3-1/S3-2 — keep one).
- **Direct-push to `main`** is the project convention. Commit trailer: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Byte-identity gate** is sacred for the codegen batches (S3-3/S3-4/S3-G): clean rebuild 0 errors; `ByteStabilityTests` green; `dotnet build-server shutdown` before codegen verification.

### Environment gotchas (cost real time if unknown)
- **`NU1301 "local source './nugets' doesn't exist"`** → `mkdir -p ./nugets` in the repo/worktree root first (nuget.config references a local feed absent from this checkout).
- **`CS2012` DLL file-lock** → never run two `dotnet build`/`dotnet test` **concurrently in the same tree**; run serially. (Cross-worktree separate output dirs are OK-ish but still prefer serial.)
- Test summaries: run `dotnet test <proj>.csproj -c Debug --nologo` and grep `Passed!|Failed!`. Filtered runs: `--filter "FullyQualifiedName~Name"`.

---

## 6. Parked / out of scope (do NOT silently re-green)

Test-health items left intentionally red — tracker: [`.dev/test-health/DEFERRED-ITEMS.md`](../test-health/DEFERRED-ITEMS.md):
- **D-13** DistributedTank (7) + ComponentDamage (5) — **fixture** translator-wiring gap, NOT an engine regression (`7c35badb` exonerated). 2-translator partial fix found; a third gap needs `FdpLog` tracing. (Memory: `project-distributedtank-fixture-gap.md`.)
- **D-8** Presentation `ctx.Resources` — production guards get 149/162; 13 entity-local/hit tests need a real `MapCamera` in the test `MakeCtx`, plus one host-crashing test to isolate.

Everything else in the TH-4 architect-decision pass (D-1..D-12, D-14) is fixed + pushed; the broad test suite is otherwise green.

---

## 7. First actions for the next session
1. `git log --oneline -8` (confirm HEAD ≈ `73c990e8` or later) and `git worktree list` (should be `main` only).
2. Read TASK-TRACKER.md Slice-3 section + TASK-DETAIL.md S3-3/S3-4.
3. Ask the user (or proceed if authorized): queue **S3-3 + S3-4** in parallel, or S3-4 solo. Write `batches/BATCH-14-INSTRUCTIONS.md` (and 15) and launch sonnet agent(s) in worktree(s).
4. Verify → integrate → commit → push → check off. Repeat through S3-G.
