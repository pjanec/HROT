# REPORT — Batch 67: conflict coverage · service singletons · HSM state

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `506e666` *(coordinator dispatch)*
> **Rule 7** re-synced from `claude/blueprint-authoring-status-gm0akp` at start ·
> **rule 4** re-fetched before the final commit — ⭐ **nothing new on the coordinator branch**, so no
> handoff or design file changed under this run.

---

## 0. 🔴 `StructureHash` — **unchanged for all 43.** `persistence-shape.txt` — **unchanged.**

⭐ **Stated first, as asked.** The tree was **clean after every suite run** — `git status --short`
empty, no `.bp.json` and no golden file regenerated. ⚠ **Item 4 changes emitted output**, but it
changes **HSM** emission, and the golden corpus is 43 `.bp.json` assets with **zero** HSM coverage
(§4). ⇒ the two facts are consistent, and §4 says what that costs.

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** *(full rebuild — baseline exactly)* |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3649 / 3639 / 0 / 10** *(was 3638/3628 ⇒ **+11**)* |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1216 / 1216 / 0 / 0** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **612 / 612 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| Generators | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **203 / 203 / 0 / 0** *(**+7**)* |
| **Hsm.Editor** *(not a standing gate — the diff reaches it)* | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **517 / 517 / 0 / 0** *(**+7**)* |
| Toolkits | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | ⚠ **two samples, see §2** *(**+7**)* |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208 / 0 / 0** ⭐ **no `--no-build`, honoured** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131 / 0 / 0** ⭐ **no `--no-build`, honoured** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 61 / done 139 (+1 refuted)** |

---

## 2. ⚠ Toolkits — **the new rule applied, and it earned its keep**

| run | command | result |
|---|---|---|
| sample 1 | full suite | 🔴 **1958 / 1957 / 1** — `DangerAreaProviderTests.FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup` |
| sample 2 | full suite, identical binary | 🔴 **1958 / 1957 / 1** — ⚠ **a DIFFERENT test**: `StatelessGizmoRegistryTests.SC_GZ022_2_Register_UnregisteredType_Throws` |
| isolation A | `--filter "FullyQualifiedName~DangerAreaProviderTests"` | ✅ **4 / 4** |
| isolation B | `--filter "FullyQualifiedName~StatelessGizmoRegistryTests"` | ✅ **2 / 2** |

⭐⭐ **Exactly the shape `DEBT-AIB-030`/`-010` describes** — *"non-deterministic in the FULL unfiltered
suite… pass under filter and in isolation"*, cause **xUnit cross-collection parallelism + process-global
registry state**. ⛔ **Neither red is signal**, and — per the same rule — ⛔ **neither would a single
green have been evidence.** 📌 **Two different members on two runs of one binary is the strongest
sample this programme has taken of it.** ⚠ **Both are registry/allocation-shaped**, as in Batches 65
and 66; none is in a file this batch touched.

---

## 3. Items 1–3 — conflicts and singletons

### `W7c` — **rule 9's rail DID fail before the change.** ⭐ *(asked explicitly)*

🔴 **Yes.** Two parallel regions writing one variable through the **locally-bound**
`ExpressionTargetField` style produced **no diagnostic at all**: the writer set was built from
**aliases only** (`GetAliasesFor`), and the locally-bound style is persisted on `HsmAssetDto` (`:104`
/`:124`) and never consulted. **4 of 4 new tests redden on revert.**

⚠ **Two pre-existing tests taught the shape mid-item, and both corrections are in the code:**
- ⛔ Filtering non-writers out of the site list **narrowed** the shipped rule — §9.6 permits concurrent
  **readers**, not a reader racing a writer. ⇒ `IsWriter` became a **field on the site**, tested per
  **group**.
- ⛔ Extracting `IsWritingFqn` dropped `HasWritingAction`'s `_schema == null ⇒ true` short-circuit,
  reddening four more. Restored with the reason written down.

📌 **The §9.5 Sync-Out half is NOT in this batch and that is a boundary, not an omission:**
`IBTreeSyncableAsset` exists only on `BehaviorTreeAsset` and **`StateNode.SubtreeAssetId` is not
persisted** — `DEBT-AIB-028`'s scope, matching the file's own pre-existing TODO.

### `W7a` — one key, both callers

`BlackboardConflictKey.ForWriterPair` extracted from the alias validator's inline construction; the HSM
validator calls it. ⚠ **Sorting the FORMATTED strings, not the `Guid`s, is deliberate** — that is the
shipped comparison, and changing it would silently invalidate every suppression already persisted.
Suppression consulted **per pair** with `continue`, so suppressing A↔B leaves A↔C reported.
**3 of 3 redden on revert.**

### `G3` — **ONE mechanism, not two.** ⭐ *(asked explicitly)*

📐 **Measured, not assumed.** `IGeographicTransform` carries
`[ComponentId(GlobalComponentIds.IGeographicTransform)]` and is published with `SetSingletonManaged` at
**three** production sites — `CgfSubsystem:249`, `SimHostApp:488`, `EditorSubsystem:624` — the
**identical** mechanism `NetworkEntityMap` uses. ⛔ **Nothing to invent and nothing to propose.**

⚠ **Correcting the dispatch's premise:** *"constructor-injected ⇒ unreachable from the world"* names a
second **CONSUMER** (`GeographicModule`, `CoordinateTransformSystem` — they exist **before** the world
does), not a second mechanism.

🔴 **What was genuinely missing is the rail**, and it is now three tests over the real shipped resolver.
🔴🔴 **The positive one passed VACUOUSLY at first:** `PickableGeoPoint` serialises as a **`[lat, lon]`
ARRAY**, so an object-shaped fixture deserialised to all zeros and `0 != 14` satisfied the weak
assertion. ⇒ the fixture is array-shaped and the test pins the **real converted value**.

---

## 4. 🔴 Item 4 — **the corpus decision: (b), stated**

> ⭐ **(b) — accept unit-test-only cover, with the follow-up below.**

📐 **The measurement that decides it:** `persistence-shape.txt` is **43 assets, all `.bp.json`**;
`grep -ci "hsm\|btree"` ⇒ **0**. ⇒ **(a) is not "add some assets"** — there is no HSM golden harness at
all: no HSM corpus, no HSM shape file, no HSM structure-hash gate. **Building one is a batch of its
own**, and it is the same batch that would give `E3`–`E7` their regression floor.

⭐ **The follow-up, stated so it is not lost:** *when Track E next needs a golden gate, build the HSM
corpus + shape file FIRST and backfill `E1` into it.* ⛔ **Until then, HSM emission changes are covered
by unit tests only, and this report is where that is written down.**

### What the unit cover actually asserts

| rail | claim |
|---|---|
| **emission (7)** | manifest emitted · **key equals BTree's algorithm for the same inputs, COMPUTED by calling it** *(a pasted literal would still pass if both sides drifted together)* · N variables ⇒ N **distinct** slots · `Role=Input` ⇒ no slot · `Node` scope skipped · no variables ⇒ byte-identical output |
| **provisioning (5)** | N state variables ⇒ N slots **through the production ingress** · slots start **zeroed on a REUSED free-list block** *(pre-dirtied `0xAA`, so not vacuously true of fresh memory)* · **one manifest, both tiers, identical slot offsets** |

⭐⭐ **`E2` needed no code**, and the rail says why rather than asserting it by proxy:
`BehaviorIngressSystem:142-154` provisions from `def.StatefulWorkingSlots` **without consulting
`BrainTier`**. ⛔ Emitting the manifest without provisioning would have been dead data — which is why
the two ship together and why the tier-agnosticism is asserted **directly**.

🔴 **Revert probes:** dropping the emit call reddens **4 of 7** emission tests *(the 3 negative ones
correctly stay green)*; gating provisioning on `BrainTierBTree` reddens **5 of 5** provisioning tests.

---

## 5. Item 5 — the ECB reflection rail

⭐ **Discovery, not a checklist:** it walks the build closure beside the test binary and reads the
**interface MAP** — a target method still declared on `IEntityCommandBuffer` is one inheriting the
throwing default. ⭐ Mocks exempt **by assembly** (`*.Tests`), not by name. ⭐ A second test fails if
the scan finds fewer than two implementers, **because a rail that discovers nothing cannot fail.**

⚠⚠ **Closure boundary — stated, not hidden.** The gated suite sees `Fdp.Core` and `Fdp.Toolkits`, ⛔
**not `Hrot.SimHost`**, whose `CognitiveSpatialModule.PerceptionScopedCommandBuffer` is the third
production implementer. It delegates correctly today (`:145`, read), but it is **outside this rail**;
covering it needs the same scan from a suite whose closure includes SimHost.
🔴 **Revert probe: 1 of 2 reddens, and the failure message NAMES the type.**

---

## 6. ⏭ Item 6 — the carried latency rail. ⚠⚠ **The premise was wrong, and the answer is smaller**

> 🔴 **The `Condition` row is not missing. It has shipped as `BP1101` all along.**

`V_AiPrimitiveIntent` (`Stage2_Validate.cs:588`) has forbidden latent nodes in a Condition-intent
AiPrimitive since the validator was written — matching on its **own inline list of three node TYPES**.

⛔⛔ **A `BP1675` was written, measured as a duplicate, and deleted before commit.** 📌 It was caught
the honest way: two pre-existing tests went red because `Stage2_Validate.Run` **stops at the first
fatal error**, so the new code short-circuited the rule it was duplicating.

⭐ **What shipped instead: routing into `MacroLatency`**, whose own doc says *"do not write a second
latent-detection rule"* — **this inline list was the last surviving copy.**

| | |
|---|---|
| 🔴 **the one real gap closed** | a **`ChannelCommandNode` carrying an `ActionFqn`** is an inline action that `WaitLowering` suspends **exactly like a `Delay`** — a fourth **SHAPE of an already-listed type**, which is why a type-match missed it. Such a condition compiled clean and would read **false while it waited**, then flip true with `__phase` mid-sequence |
| 📌 **NOT a gap** | *"latency behind a macro call"*. A call-following arm was written and **removed**: the scan already covers macro bodies, so it produced **two diagnostics for one defect** |
| ⚠ **pinned, not endorsed** | an **uncalled** latent macro in a Condition asset is refused too — the scan is per-graph and never asks who calls whom. Narrowing that is a design call, not something to smuggle into a widening |
| ⛔ **deliberately unbuilt** | the **Action rows** (HSM `Entry`/`Exit`/`Timer` cannot re-enter either) — **speculative until `E5`** |

📌 `TestAssets/Invalid/ConditionWithDelay.bp.json`, **unreferenced since the fixture set was written**,
is finally the subject of a test. 🔴 **Revert probe: restoring the three-type list reddens 1 of 8.**

---

## 7. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-260` · `BP-261` · `BP-262` · `BP-263` · `BP-264` · `BP-265`** |
| blueprint diagnostics | ⛔ **NONE.** `BP1675` was written and **deleted** — see §6. **`BP1675` is FREE.** |
| analyzer diagnostics | ⛔ none |
| architect questions | ⛔ none |

---

## 8. ⭐⭐ The carried question, finished — **the 21 NON-`AIB` debt ids**

Batch 66 triaged all 18 open `DEBT-AIB` rows. ⭐ **This is the remainder**: `BCP` · `BF` · `MVE`
*(`TEST`/`ARCH`/`UX`/`NOTE` resolve to mentions inside other trackers, not to rows of their own)*.

### 🔴🔴 One row is directly in this batch's blast radius

| id | claim | why it is ours |
|---|---|---|
| 🔴🔴 **`DEBT-BF-04`** *(= `VE-DEBT-001`, filed twice)* | HSM `ExpressionTargetField` — the type-filtered picker — was added to **transitions and global transitions only, NOT to states.** A state has **4 action slots** (Entry/Exit/Activity/Timer), so *"one DTO → one binding"* does not fit it | ⭐⭐⭐ **This is the other half of `W7c`.** `W7c`'s writer set reads the locally-bound style **from transitions and globals** — and this row is the reason there is nothing to read on a state. ⇒ **`W7c`'s coverage is complete with respect to what can be AUTHORED**, and it will need widening the moment `BF-04` is built. ⛔ **Read it before extending rule 9** |

### The rest, by track

| track | ids | one line |
|---|---|---|
| **Track C / persistence seam** | `BCP-005` | wire-dropped nodes carry populated **in-memory `node.Pins`**; ⚠ **if the editor SAVES, an editor PROJECTION persists** — the projection-only invariant, which is `U-9`'s subject |
| **Track C / editor surface** | `BCP-003` | `assets.by-type` and `enum.values` picker sources are **placeholders that return empty** — the same *"offered but not shown"* shape as `BP-254`/`BP-255` |
| **parameter seam** | `BF-01` | vector/`Quaternion` inline-default literal materialization **skipped**; enum defaults **assume int-backed**. ⇒ `BP-247`/`BP1674`'s neighbour — same *"a default that has no literal"* family |
| **parameter seam** | `BF-02` | `DD-1..DD-4`, **partially addressed**; `DD-4` (StructEdit param grid) folds into `BB1` |
| **noise, not debt** | `BCP-004` | ~26 pre-existing test-project warnings on full rebuild. 📌 **Included in our standing 69** — not a regression, not ours |
| **stale / already closed** | `BCP-006` · `BCP-002` · `BF-03` · `MVE-003` · `MVE-004` | `BCP-006` resolved by `blueprint-finalize/BATCH-06`; `BF-03` and `MVE-004` resolved by `BP-2 Stage0_Rehydrate`; `MVE-003` resolved by `BF01`; `BCP-002` is *"lightly tested"*, not a defect. ⭐ **Their trackers were never re-flagged** — which is itself the finding |

📌 **Recommendation:** ⭐ **read `DEBT-BF-04` before any further work on rule 9 or on HSM state
binding** — it is the single row here that changes what a later batch must build. Everything else is
either Track C's to schedule or already closed and merely un-ticked.

---

## 9. What this batch did **not** do

⛔ `W7b` *(the "Allow concurrent writes" UX)* · `E3`–`E7b` · the rest of Track C *(table, dialog,
Watch, `C-outline`)* · the Instance params seam · multi-occurrence · `G7`+`W10` · the **§9.5 Sync-Out**
half of `W7c` *(blocked — `DEBT-AIB-028`)* · the **HSM golden corpus** *(§4's stated follow-up)* · the
**Action rows** of the latency rail *(speculative until `E5`)*.
