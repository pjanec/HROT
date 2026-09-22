<!--STATUS
state: LIVE
doc-type: RESUMPTION for P4 — retiring BrainBlackboard and Blackboard1024.
  ⚠ A STATE doc, not canon. Every "measured" line is dated; ⛔ VERIFY against git before acting.
updated: 2026-09-22
build-state: n/a — a build resumption. The DESIGN is DESIGN_Occurrence_Scoped_Storage.md §30;
  §30.18 is the slice table, and §30.22..§30.27 are the as-built (they SUPERSEDE parts of §30.14,
  §30.15 and §30.18 — read them before quoting any of those).
current-answer: 🔒 **NOTHING HERE IS A TO-DO. `P4` IS CLOSED AND SO IS EVERYTHING IT SPAWNED.**
  ⛔ Do not resume FROM this file — it is a RECORD. ⭐⭐ **A fresh session wanting the next piece of
  work goes to `RESUME_Occurrence_Storage.md`; the open row there is `O7c`** (delete `BrainHsm64`/
  `BrainHsm128`, move the ROOT brain state into occurrence slots, `F9`'s tick-system reshape —
  188 refs across 18 production files, rated L).
  ✅ All five slices + all four follow-ups: `P4`-④ = `CE-307` §30.25 · `CE-316` §30.26 ·
  `CE-314` §30.27 · struct deleted §30.28 · `CE-315` closed with the tick-query gate ·
  `CE-317` done (the phantom component) · the vendored fork removed (§7).
  ✅ **CLUSTER ACCEPTANCE PASSED on the running product after the WHOLE of `P4`** (§4) —
  `523.06 525.22 529.22 530.99`, all Success, 0 FastBTree warnings. ⚠ QUIET MACHINE; §4 says why.
  ⭐⭐ **THE REUSABLE HALF IS §3 (traps) AND §7's inference table.** ⚠ §4's Blueprints row carries a
  RETRACTED claim — read it before quoting any skip count from this file.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — §30.18 the slice table · §30.20 P4-① · §30.21 P4-②
    (the zero-fallback rail) · §30.22 CE-312 · §30.23 CE-308 · §30.24 CE-313 · §30.25 CE-307 ·
    §30.26 CE-316 · §30.27 CE-314 · §30.28 the struct deletion. It wins on any disagreement here.
  - RESUME_Occurrence_Storage.md — the PROGRAMME-level resume (P0–P4).
  - Blueprint_Issues_Tracker.md — CE-303 and CE-307..CE-317 ALL DONE (CE-303 closed 2026-09-22,
    structurally: the type is gone, so no identity-keyed surface could have survived a compile).
-->

# RESUME — `P4`: retire `BrainBlackboard` and `Blackboard1024`

> 🔒 **The goal, in the user's words:** *"we will retire it unless we find a true need and do not see
> any. Being part of ABI is no reason, ABI can and must change."*
> 🔒 **And the guard rail:** *"'no real users' does not mean 'not needed', be cautious before deleting
> anything."*

---

## 1. ✅ WHAT IS DONE — **do not redo this**

⭐ **`behaviors` @ `70d40c0d2`, clean. Solution builds 0 `error CS`; all five suites at baseline.**

| slice | |
|---|---|
| **`P4`-①** | ✅ **`Blackboard1024` IS DELETED.** 54 code files. Component id **74 RESERVED**, not reused. **2/2 cluster GOLD** |
| **`P4`-⑤** | ✅ the stale corpus |
| **`P4`-②** | ✅ **`TBlackboard` bound to `byte`** + the **zero-fallback rail** + **2/2 cluster GOLD** *(§30.21)* |
| **`P4`-③** | ✅ **all six identity-keyed surfaces re-homed** *(`CE-303`)*, **`CE-308`** *(the search axis)*, **`CE-313`** *(the builder generic)* — and it uncovered **`CE-312`** |
| **`P4`-④** | ✅ **DONE `2026-09-22`** *(`CE-307`, §30.25)* — the cap was **seven sites, not three**, and two read 100 as a **WIDTH**. Shadow buffer sized per behaviour FIRST, then the bound repointed to **16 096**. ⭐ 3 positive rails. 🔴 **And it uncovered §2 ⓪** |

### 🔴🔴 THE BIG FINDING OF `P4`-③ — **`CE-312`, the FOURTH dead-storage instance**

📐 **`BrainBlackboard` is ATTACHED BUT NEVER FILLED.** One site attaches it —
`BehaviorTkbTranslator.cs:125`, `AddComponent(entity, new BrainBlackboard())` — an **EMPTY** one.
**Nothing** has filled `BehaviorParameters` since `P3`; every remaining production mention is a doc
comment. ⇒ four debug surfaces projected a permanently ZERO region, and StructEdit bound **editable**
fields to it.

⭐⭐⭐ **Worst of the four, and the generalisation is the point:** `CE-304` read zeros silently,
`CE-310` returned `null`, `CE-311` would **throw**, **`CE-312` RENDERS PLAUSIBLE ZEROS AND ACCEPTS
EDITS.** 🔒 **An instance's danger is set by WHAT ITS READER DOES with an unfilled region, not by how
central the code is.**

⇒ 📄 **§30.22** has the full as-built. All four are re-homed onto the root params slot, with a
**POSITIVE** rail (`RootParamsProjectionTests`) that writes known values into a real store and asserts
they read back — ⛔ the old six rails asserted only REFUSALS, which a zero-filled component satisfies
by construction.

---

## 2. ⭐⭐⭐ WHAT IS LEFT

### ⓪ ✅ **FIXED `2026-09-22` as `CE-316`** — `P4`-② had silently stopped generating six BTree assets

> ✅ **RESOLVED — 📄 §30.26 is the as-built and SUPERSEDES the framing below.** 🔴 **The repair did NOT
> need the §2 ③ decision**, contrary to what this section first concluded. ⭐ `CE-313` made the emitted
> registrar `ActionRegistry<byte, TCtx>`; `BTreeMethodCompatibilityValidator:45` kept resolving `TBB`
> from **`dto.BlackboardTypeName`** ⇒ **a one-line source-of-truth error, `CE-313`'s unfixed twin.**
> 🔒 **The rule: a validator is keyed on what the EMITTER WRITES, never on what the ASSET DECLARES.**
> ⛔ `dto.BlackboardTypeName` untouched — it feeds `SubtreeSyncIdentity.Derive` (§30.19). ⇒ **§2 ③ is
> still OPEN, on its own merits, and no longer blocks anything.** ⭐ Verified: **0 skipped assets**, and
> the 6 stale goldens regenerated.

⛔ **The diagnosis below is kept because the THREE BLIND SPOTS are the reusable part.**

### ⓪a 🔴🔴 HOW IT SHIPPED — **`P4`-② SILENTLY STOPPED GENERATING SIX BTREE ASSETS** *(found `2026-09-22`, §30.25 ⑤)*

📐 **Measured, and confirmed by the repo itself.** `P4`-② changed hand-written `[BTreeAction]` methods
in `CgfNodes.cs` from `ref BrainBlackboard` to `ref byte`. ⛔ **The 26 asset `.json` files still declare
`"BlackboardTypeName": "…BrainBlackboard"`**, `BTreeMethodCompatibilityValidator` compares the two, and
`BTreeJsonGenerator` treats an incompatible leaf as a **WHOLE-ASSET SKIP** — `BTREE0002`, no generated
code at all.

**The six, all binding `Action_Wander`:** `BTreeRenderShowcase` · `CombatShowcase` ·
`T04_DecoratorRepeater` · `T05_DecoratorStack` · `T06_ObserverSelector` · `T08_ActionLeaf`.

⭐⭐⭐ **THE CONFIRMATION, and it needs no build:** `CE-313` regenerated goldens and **exactly 6 of the 26
still say `Interpreter<BrainBlackboard`** — and they are **the same six**. ⇒ they generated before; the
regeneration had nothing to write for them.

| ⛔ why nothing caught it — **three independent blind spots** | |
|---|---|
| 🔴 **the warnings appear only on a REAL recompile** | an incremental build prints nothing. ⭐ `touch Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs` first, then build |
| 🔴🔴 **`P4`-②'s own zero-fallback rail is green BY CONSTRUCTION** | it asserts *every REGISTERED node binds*. ⭐⭐ **A skipped asset never registers** ⇒ the rail cannot see it. ⚠ Same shape as `CE-312`'s six refusal-only rails |
| ⚠ **the golden suite did not redden** | it left the six stale rather than failing on them |

⚠ **The 2/2 cluster gold is NOT contradicted** — `hill-attack-close`'s behaviours are not among the six.

🔴 **The first conclusion drawn here was WRONG and is recorded as such:** *"this is §2 ③ already biting…
it is a repair, not a decision."* ⛔ Half right — it WAS a repair, but **not of the asset schema**.
⭐ Measuring `BTreeMethodCompatibilityValidator` instead of reasoning from the error text found a
one-line fix. 🔒 **The error message named the asset's type, so the asset LOOKED like the problem** —
⚠ the same *"a principle where a `file:line` should be"* failure the canon warns about, caught only by
opening the validator.

### ① ✅ `P4`-④ — **DONE `2026-09-22`. The 100-byte cap was NOT a deletion** *(`CE-307`)*

> ✅ **LANDED — 📄 §30.25 is the as-built and supersedes the plan below.** Kept because the ORDER
> argument is the reusable part. ⭐ **Seven sites, not three.** ⭐ `BehaviorConstants.MaxRootParamsByteSize`
> = **16 096** *(`BlueprintTierLadder.Tier16384PayloadSize`)*, mirrored in the analyzer and both packers.
> ⛔ **`MaxBehaviorParamByteSize` stays at 100** — it is the `fixed byte[]` width and the escape hatch;
> it dies with the struct. 🔴 **Two sites the row below did not name:** `BTreeBlackboardPackHelper`
> → `BTreeJsonGenerator:257` **skips the whole asset**, and the ingress shadow read 100 as a WIDTH.

> 🔒 **User:** *"Capping no longer needed as we allocate as much as we need, no?"* — ⭐ **Correct as
> far as the CAP goes.**

📐 **Where it was needed**, in the analyzer's own words *(`BehaviorParameterSizeAnalyzer.cs:31`)*:
*"exceeding the 100-byte `BehaviorParameters` region. **This would corrupt the SoftAdvice and Interrupt
registers in `BrainBlackboard`**."* ⇒ params lived inline in a fixed-layout struct **with neighbours
after them**; overflow silently overwrote unrelated state. It was a **buffer-overrun guard**, not a
budget. ⭐ Params now land in a slot sized `RootParamsBytes(def)` with `ResolveTier` promoting up the
ladder *(176 / 800 / 3808 / 16096 B)* and `TryAttach` failing structurally. **No neighbours, no cap.**

🔴🔴 **BUT TWO LIVE CONSUMERS READ THE NUMBER AS A *WIDTH*, NOT A LIMIT:**

| site | what it does with 100 |
|---|---|
| 🔴 `BehaviorIngressSystem.cs:57,97,98` | `stackalloc byte[BrainBlackboardByteSize]` — the **shadow buffer**, seeded from the previous slot with `Math.Min(prevLen, 100)`. It exists for **partial-parse semantics** *(an emitted parser writes only the variables the JSON mentions)*. ⛔ **If params ever exceed 100 B the carry-over SILENTLY TRUNCATES** — impossible today **only because the analyzer caps at 100** |
| `RootParamsAccess.RootParamsBytes` | returns 100 as the **reservation width** for a behaviour with `ParseParams` but neither manifest nor layout — the documented escape hatch. Its own comment says returning `0` drops the parse on the floor |

⇒ 🔒 **ORDER MATTERS: make the shadow buffer slot-sized FIRST, then remove the cap.** ⛔ Removing the
guard first converts an impossible case into a silent data bug.

📐 **The cap's surface:** source of truth `BehaviorConstants.cs:32` *(+ `:24` aliases it as
`BrainBlackboardByteSize`)*, **3 mirrors** — `BehaviorParameterSizeAnalyzer.cs:26` *(a real
`private const 100`, the netstandard wall)*, `BlackboardBinPacker.cs:87`,
`BTreeBlackboardPackHelper.cs:19` — pinned by `InlineBudgetConstantAgreementTests`.

### ② THEN THE STRUCT ITSELF — **32 production code lines, mostly strings**

📐 Measured `2026-09-22`: 136 files mention it, **401 lines, 237 code, and only 32 production**.
⛔ **Do not estimate from the file count** *(the `HN-037` trap)*.

| real work | |
|---|---|
| 🔴 **`BTreeTickSystem.cs:88` — `.With<BrainBlackboard>()` in the TICK QUERY** | a **live gate**. Redundant for production *(`BehaviorTkbTranslator` adds it unconditionally beside `BrainBTreeState`)* — ⛔ **but NOT for `BehaviorValidationScenario`**: it attaches `BrainBTreeState` at `:259` and only **registers** `BrainBlackboard` at `:125`, never attaches it ⇒ **that example's agent is excluded from the BTree tick today.** ⚠ Dead storage as a GATE. Worth its own row |
| the mechanical set | `CognitiveComponentRegistry.cs:44` + 3 example `RegisterComponent<>` · `BehaviorTkbTranslator` ×3 · `HrotRoleComponentSets.cs` · `BrainBlackboardTranslator` + its factory site · `GlobalComponentIds.cs` id **23 → RESERVE** · the struct · ~10 diagnostic strings |

### ③ 🔴 THE BLOCKER — **`BrainBlackboard` is a STRING in 26 ASSET FILES**

📐 `AiEmitCoreBase.cs:28` `DefaultBlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard"`,
and **26 asset `.json` files** *(21 authoring + 5 built)* name it explicitly. Only 4 assets use their own
`*_Blackboard` — ⚠ and those four names **do not exist as C# anywhere**, so for HSM assets the field is
inert.

⭐⭐ **`CE-313` already removed one of the three consumers** — the builder generic is now a literal
`byte` *(§30.24)*. **Two remain:**

| consumer | state |
|---|---|
| `BTreeNewAssetService.cs:123,159` | stamps the default into **every new asset** |
| `BTreeEmitCore.cs:304,336` namespace collector | still reads the asset's type to add a `using` |
| `BTreeOrchestratorEmitCore.cs:108` | `ref {bbShort} master` + `master.{VarName}` — **needs real fields**. ⭐ **LATENT:** 📐 **zero** generated `HostedSubtree.Tick` sites exist, so nothing orchestrates today |

⇒ **The decision needed before the struct can go: what does a BTree asset's `BlackboardTypeName` point
at?** ⚠ **Not a blind rename** — §30.19: it mangles into params-layout struct names **and**
`SubtreeSyncIdentity.Derive`, **which MATCHES SUBTREES**.

---

## 3. ⛔⛔ TRAPS ALREADY PAID FOR — **do not re-pay**

| | |
|---|---|
| 🔴🔴🔴 **THE DEAD-STORAGE PATTERN — now FOUR instances** | `CE-304` *(read zeros)* · `CE-310` *(returned null)* · `CE-311` *(would throw)* · 🔴 **`CE-312` (renders plausible zeros AND accepts edits)**. ⛔ No static signal sees it. ⭐ **The check: *"which production site PROVISIONS the storage this reads?"* — never *"who calls this?"*** ⭐⭐ **And the danger is set by what the READER DOES with an unfilled region** |
| 🔴🔴 **THE SILENT-DEFAULT RULE FIRES ON DELETIONS TOO** | 📌 `CE-312`: three hosts wire the inspector; `EditorSubsystem` set BOTH registry accessors, **CGF and ReplayBrowser set only the deleted renderer's** ⇒ removing it would have left two hosts with an inert panel. ⭐ Fixed structurally — **one static cannot be half-set**. ⚠ The usual shape is *a caller that has a dependency and does not pass it*; this is *a caller that STOPS passing it because the callee went away* |
| ⛔⛔ **A RAIL THAT ASSERTS ONLY REFUSALS CANNOT SEE DEAD STORAGE** | 📌 `CE-312`'s six rails were green **by construction** — they handed the renderer a `new BrainBlackboard()`, which IS the broken state. ⭐ **A positive rail that writes known values and asserts they READ BACK is the only shape that catches it** — ⛔ "the lookup returned true" is not evidence |
| ⛔⛔ **NEVER retarget the asset's `BlackboardTypeName`** | it mangles into the params-layout struct name **and** `SubtreeSyncIdentity.Derive`, which **MATCHES SUBTREES** ⇒ 11 structs across 44 files, and matching breaks silently. 📄 §30.19 |
| ⚠ **~~the generated BUILDER keeps the asset's type~~ — SUPERSEDED** | ⛔ §30.18 said `byte` was impossible because selector-form bindings need fields. 📐 **Measured: 26 generated builders, ALL nodes bound by STRING KEY, ZERO selector lambdas** ⇒ the type argument was never read. `CE-313` made it `byte`. ⭐ **Hand-written trees DO use the selector form and still need a struct** |
| ⭐⭐ **the per-test fix depends on WHICH THUNK the test runs** | hand-written thunk ⇒ the test owns a plain `byte[]`; generated thunk resolves via `RootParamsAccess` ⇒ take `RootParamsAccess.RootRef(world, entity)`. 🔒 Why `P4`-②'s tail was NOT batch-substituted |
| ⛔ **borrowing an ECS component as a scratch buffer** | the habit that hid `CE-310`/`CE-311`. ⭐ A plain `byte[]` cannot be mistaken for a storage path |
| 🔴 **`G3` under-measured the asset-driven generator** | there are **TWO** BTree generators. `BTreeActionGenerator` (analyzer) is polymorphic; `BTreeBridgeEmitCore` derived the dispatch type from the ASSET. ⭐ Fixed at `:310`, **not** in the asset |
| 🔴 **ANY COMMAND-LINE PATTERN MATCH KILLS YOUR OWN SHELL** | the pattern is in your own command line *(exit 144)*. 📌 Hit **twice**: `ps \| awk '/Hrot\.Cluster/'` and **`pkill -f Xvfb`**. ⛔ Not an `awk` quirk — **matching on the full command line at all**. ⭐ Filter on `comm`: `ps -eo pid,comm --no-headers \| awk '$2=="dotnet"{print $1}' \| xargs -r kill` |
| 🔴🔴 **THE CLUSTER ACCEPTANCE IS LOAD-SENSITIVE** | 📐 `2026-09-22`: a trial run with 6 `dotnet` daemons alive drifted **+4.01** from gold; the same build on a quiet machine matched within **0.83**. ⭐ The sim runs on WALL CLOCK, so background load changes frame deltas and the integration diverges. ⛔⛔ **I nearly wrote that drift up as an archetype-reordering consequence of deleting a component** — a plausible mechanism for a number whose real cause was the machine. 🔒 **Kill background `dotnet` BEFORE the trial, and never reason about code from a loaded run** |
| 🔴🔴 **A RELOAD IS NOT A RESET** | re-POSTing `/scenario/load/live` on the same process answered **`sawWorldChange: false`** with `totalTime: 84.9` ⇒ the "trial 2" numbers were trial 1 drifting. ⭐ **Restart between trials**; require `sawWorldChange: true` **and** `totalTime ≈ 0` at play |
| ⛔⛔ **`Fdp.Presentation.Tests` CANNOT BE GATED WHOLE** | `BP-419` / `CE-259aa`: a native SIGSEGV aborts the run **and still prints `Passed!`**. ⭐ Gate by `--filter` *(`~ReplayBrowser` ⇒ **93/0**)*. ⚠ **And its `bin` had NO xunit adapter** — the project had never been restored here, so `--no-restore` produced an unrunnable assembly. `dotnet restore` fixes it |
| ⚠ **the LOAD-FLAKY family keeps growing** | `BP-534` · `LiveFromReplayTests.Teardown…` · `SquadInputsP3Tests.AllReaders_ZeroAlloc…` · `T35_SharedWorkingState_ProofTests` · ⭐ **and three more confirmed `2026-09-22`: `EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule` · `NodeBootstrapperReplayTests.ClusterSlaveDispatch_…RoutesToReplayBranch` · `TransientSpawnTagRails.ATransientEntity_IsAbsentFromTheSavedScenario_AndANormalOneIsPresent` *(2/2 alone, 9/9 beside the fixture that was suspected of perturbing it, and a full re-run came back **2310/0** — three independent checks before calling it a flake)*.** ⛔ **Confirm any extra red IN ISOLATION before calling it a regression** — 📐 it paid again: a full SimHost run showed **6** reds, isolation showed **3**, and the 3 were the documented ones |
| ⚠ **an over-broad substitution corrupts DOC COMMENTS** | ⭐ Always `git diff --name-only \| grep -v Tests` after a scripted edit |
| ⚠ **`NETSDK1004` × ~60 is PRE-EXISTING** | unrestored `Stride/` projects. ⭐ Filter on `error CS` |
| ⚠ **build the TEST project, not the production one** | `--no-build` against a production-only build runs a stale binary |
| ⛔ **do NOT "fix" `RW-S` tracker rows** | invisible to `tracker-counts.py` — known gap `CE-259at`. ⭐ **Corollary measured `2026-09-22`:** adding/closing `RW-S` rows leaves `--check` reporting the SAME counts. That is CORRECT, not a stale gate |
| 🔴🔴🔴 **A GENERATOR WARNING ONLY APPEARS ON A REAL RECOMPILE** | 📌 `2026-09-22`: six assets had been silently skipped since `P4`-② and every incremental build printed nothing. ⭐ **`touch` the source that changed, then build** — otherwise "no warnings" means "nothing recompiled" |
| 🔴🔴 **A RAIL OVER THE *REGISTRY* CANNOT SEE A SKIPPED *ASSET*** | 📌 `P4`-②'s zero-fallback rail asserts *every REGISTERED node binds*. ⭐⭐ **An asset the generator skipped never registers**, so the rail is green by construction — ⚠ **the same blind spot as `CE-312`'s refusal-only rails, one level up.** 🔒 **A rail over a DERIVED collection cannot see items that never entered it** |
| ⭐⭐ **STALE GOLDENS ARE EVIDENCE, and they are free** | 📌 `CE-313` regenerated goldens; **exactly 6 of 26 still said `Interpreter<BrainBlackboard`, and they were exactly the 6 skipped assets.** ⇒ *"which goldens did NOT move when they should have"* answered a causation question a build could not |
| 🔴🔴 **A WORKTREE BUILD LEAKS GENERATOR OUTPUT ACROSS TREES** | 📌 `2026-09-22`: a worktree at `05d15ec5f` emitted `Interpreter<byte, …>` — a shape that commit's source cannot produce. ⛔ **The experiment was INVALID and its result was discarded.** ⚠ New instance of *"a reload is not a reset"*: ⭐ **before trusting a historical build, check its output for something only the NEW code could emit** |
| ⚠ **`Hrot.Editor.AiShared.Tests` is ALSO unrestored here** | `NETSDK1004`, same as `Fdp.Presentation.Tests`. ⭐ `dotnet restore <proj>` first. ⭐⭐ **`quick-check.sh` REFUSES to test a failed build** — that refusal is what surfaced it, instead of a stale-binary `PASSED` |

---

## 4. ⭐ GATES — **the baseline, all MET at `70d40c0d2`**

| suite | baseline |
|---|---|
| solution build *(156 projects)* | **0 `error CS`** |
| `Fdp.Toolkits.Tests` | **2310 / 0** ⚠ was 2307; **+3 = `CE-307`'s positive rails** in `BehaviorIngressSystemTests` |
| `Hrot.Blueprints.Tests` | **4017 / 0** *(skipped count VARIES — see below)*. 🔴🔴 **A CLAIM MADE HERE ON `2026-09-22` IS RETRACTED.** It read: *"`CE-316` MOVED 8 TESTS FROM SKIPPED TO PASSING — 4025/0 with 10 skipped, was 4017/0 with 18, same total 4035."* ⛔ **Not supported.** 📐 Re-measured after the struct deletion: **18 skipped again**, while the six assets **demonstrably still generate** *(forced recompile, 0 `BTREE0002`, 0 skipped assets)*. ⭐⭐ **The skipped tests were then LISTED, which is what settled it** — they are ALC/compile-and-load tests, ImGui dialog tests, zero-allocation measurements, `ReflectionSharedStructTypeProvider`, `Validate_SpawnEqsSensor…`: ⛔ **not one of them touches BTree asset generation.** ⇒ these are **ENVIRONMENT-GATED** skips that flip with machine load, GC mode and headless state. 🔒 **The lesson is about the claim, not the tests: I wrote the mechanism as INFERRED and then reported the number as if the inference were established.** ⭐ Listing the skips cost one command and would have refuted it immediately. ⚠ **Treat this suite's skip count as NOISE unless the skipped NAMES change** 
| `Hrot.SimHost.Tests` | **1005 / 3** *(+3 skipped, total 1011)*. ⭐⭐ **THE 3 ARE NAMED NOW** *(`2026-09-22` — the row used to say only "the 3 documented", which costs a session every time)*: `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete` · `MapPresentationParityRails.EveryTkbSpawningHost_ObtainsTheSharedTranslatorSet(EditorStrideSubsystem.cs)` · `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`. ⚠ **A full run typically shows 6** — the extra three are the load-flaky family *(`EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule` · `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState` · `NodeBootstrapperReplayTests.ClusterSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch`)*, ⭐ **measured `2026-09-22`: all three PASS in isolation while the 3 above still FAIL** — which is how to tell them apart in one command |
| `Hrot.Presentation.Tests` | **299 / 0** |
| `Hrot.AiEditor.Generators.Tests` | **282 / 4** *(the 4 documented — total **286**)* ⚠ `CE-314` removed one test — `CE-307`'s pinned exception `TheEditorPackersCopy_IsTheKnownException_UntilCe314`, whose whole purpose was to fail when `CE-314` landed. ⚠ was 281/4 total 285; **+1 = `NoTierIsLargerThanTheCeiling`**, **+1 = a golden case for an asset `CE-316` un-skipped**. ⭐⭐ **The 4 are NAMED:** `T30_BehaviorScopedShared_ProofTests.HillAttack_SharedState_PersistsAcrossNodes` · `S3_BehaviorScopedThunkTests.BehaviorScoped_TwoNodes_ShareOneSlot` · `S3_SharedSlotProvisioningTests.Assign_BehaviorScoped_ProvisionsOneSlot_ForSharedVar` · `S3_SharedSlotProvisioningTests.Assign_MixedNodeAndBehaviorScope_SlotCountsCorrect`. ⛔ **A REGEN run (`AI_REGENERATE_SNAPSHOTS=1`) reports 6 — goldens are rewritten mid-run; ALWAYS re-run without the flag before believing a count.** ⛔ **`CE-307` re-pointed two rails that PINNED the retired cap** — `ManagedAsset_MasterDtoOverTheParamsCeiling_IsSkipped` and `StructDtoVariable_AggregateOverTheParamsCeiling_SkipsWithBtree0002`; their fixtures now size themselves FROM the constant, so the skip-on-overflow MECHANISM is still tested and the threshold can move again without a hand edit |
| `Fdp.Presentation.Tests` | ⛔ **filter only** — `--filter "FullyQualifiedName~ReplayBrowser"` ⇒ **93 / 0** |
| ⭐ `Hrot.Editor.AiShared.Tests` | **2048 / 0** *(was 2058 — `CE-314` deleted 13 heavy-tier tests and added 3 replacement rails)* — ⚠ **NEW gate row `2026-09-22`.** It was never gated because the project was **unrestored** *(`NETSDK1004`)*; `dotnet restore` once and it runs. ⭐⭐ **It is the bin packer's own suite, so `CE-314` must run it** |
| docs | `design-digest --check` · `rulings-check` **38/38** · `tracker-counts --check` · `mermaid-check` |

⭐ **Golden regeneration switches:** `BLUEPRINT_REGENERATE_SNAPSHOTS=1` *(Blueprints)* ·
`AI_REGENERATE_SNAPSHOTS=1` *(AiEditor.Generators)* — ⚠ deliberately separate.
⚠ **A regen run itself reports reds** *(the goldens are rewritten mid-run)* — ⭐ **always re-run
without the flag** before believing a count.

### ⭐⭐ THE CLUSTER ACCEPTANCE — **`hill-attack-close`**

```bash
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj --no-restore
cat > /tmp/launch-all.sh <<'SH'
#!/bin/bash
cd /home/user/HROT
exec env HROT_DEBUG_API_PORT=8111 xvfb-run -a --server-args="-screen 0 1600x1000x24" \
  dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode all
SH
chmod +x /tmp/launch-all.sh
setsid nohup bash /tmp/launch-all.sh > /tmp/cluster.log 2>&1 < /dev/null & disown
until grep -qiE "Debug API asset creation attached|Aborted" /tmp/cluster.log; do sleep 3; done

B=http://localhost:8111
curl -s --noproxy '*' -m 90 -X POST $B/scenario/load/live -H 'Content-Type: application/json' \
     -d '{"name":"hill-attack-close","waitForReady":true}'      # REQUIRE sawWorldChange: true
curl -s --noproxy '*' -m 20 -X POST $B/sim/play -H 'Content-Type: application/json' -d '{}'
# wait /sim/state totalTime >= 72, POST /perspective {"name":"Scenario"}, then read
#   /entities/1001..1004 -> data.Components.SimTransform.Position[0] + LocomotionChannel.Status
#   /entities/1006,1007  -> data.Components.Health.Current
# teardown: ps -eo pid,comm --no-headers | awk '$2=="dotnet"{print $1}' | xargs -r kill
```

⭐ **PASS** = `1001`–`1004` at **x ≈ 523–531**, all `LocomotionChannel.Status: Success`, `1006`/`1007`
at `Health.Current: 0`, entity count **8** *(dead bodies stay — `CE-272`)*.
📐 **Gold after `P4`-②:** `523.10 524.91 528.32 531.23` · `523.03 525.27 528.46 531.14`.
✅ **Gold CONFIRMED after the whole of `P4` — including the struct deletion** *(`2026-09-22`, quiet
machine)*: `523.06 525.22 529.22 530.99`, all four `Success`, `1006`/`1007` health **0**, count **8**,
**0** `FastBTree] Warning`, 0 exceptions. ⭐⭐ **This is the one check that could see `CE-315`'s failure
mode** — removing `.With<BrainBlackboard>()` from `BTreeTickSystem`'s query could have made it select
NOTHING, which is silent non-execution no unit rail detects. **The agents ticked.**
⭐⭐ **Also check `grep -c "FastBTree] Warning" /tmp/cluster.log` is `0`** — the zero-fallback rail's
claim, observed on the running product.
⚠ **ONE TRIAL PER CLUSTER PROCESS** *(§3)*. ⚠ Always `--noproxy '*'` and the `localhost` hostname.

🔴🔴 **AND RUN IT ON A QUIET MACHINE — measured `2026-09-22`, it costs a trial otherwise.**
```bash
ps -eo pid,comm --no-headers | awk '$2=="dotnet"{print $1}' | xargs -r kill   # BEFORE launching
```
📐 **The evidence:** a trial launched with **6 `dotnet` daemons alive** *(straight after building the
ClusterRunner)* put the tanks at `524.38 527.87 532.40 532.12` — **up to +4.01 off gold**. The next
trial, on a torn-down machine, gave `523.06 525.22 529.22 530.99` — **within 0.83**. ⇒ three quiet runs
*(gold₁, gold₂, trial 2)* agree within **0.83**; the one loaded run deviates by **4.01**.
⭐ **The sim advances on WALL CLOCK at `timeScale 1`**, so under load the frame deltas differ and a
75-second integration drifts. 🔒 **The positional gold is only meaningful on an idle machine** — ⛔ a
deviation measured under load is not evidence about the code.

---

## 5. ⚠ STILL OPEN

| | |
|---|---|
| ✅ **THE FORK — REMOVED `2026-09-22`** | 📄 **§7 is the as-built.** ⛔⛔ **AND THIS ROW'S OWN MEASUREMENTS WERE WRONG — all three, corrected by building:** it said *"7 touch the generator"* *(it is **5**, plus `SampleProjectTests` via `examples/`)*; it implied `Fbt.Tests` would need fixing for `behav-diag-1`'s `ITreeTracer` constraint *(it **builds clean** — `T6.1` was done)*; and it framed the cascade as generator-only *(**`examples/` too** — `Fbt.Tests` referenced them)*. ⭐ It DID get the important thing right: *"needs **one build**, not a guess."* ⚠ It needed **four**. 📐 Final cost: **233 → 196 tests, 224 → 188 passing**, against **9 pre-existing failures** in a suite that runs in **no HROT gate** |
| ✅ **`G4` — CLOSED** | `Idle` registers with no `ParseParams`, no `BlackboardLayoutType`, no manifest ⇒ `RootParamsBytes` = **0** |
| ✅ **`G5` — ANSWERED, and LATENT** | `subBb` is **`master.{VarName}`** — a *field* of the master struct, so orchestration needs a struct, not `byte`. `BTreeOrchestratorEmitCore:108` types `master` on the **asset's** type *(not `byte`)*, which is why the solution builds. 📐 **Zero** generated `HostedSubtree.Tick` sites ⇒ nothing orchestrates today. ⚠ §2 ③ would trip it |
| ✅ **`BehaviorValidationScenario` excluded from the BTree tick** | **FILED as `CE-315`** `2026-09-22`. ⭐ Closes for free when §2 ② deletes the struct; filed separately because the scenario is wrong TODAY and the lesson is its own: **dead storage used as a QUERY PREDICATE fails as silent NON-EXECUTION**, which no value-asserting rail can see |
| ✅ **`CE-314` — DONE `2026-09-22`** *(§30.27)* — the bin packer's heavy arm is REMOVED and `MaxInlineBytes` is **16 096**, so **the editor and the generator finally agree on one ceiling**. ⭐⭐ **The reusable lesson is about the DELETION, not the arm:** of 9 packer tests deleted, **three were the only cover for behaviour that SURVIVES** *(aggregated variables continue the master region's offsets, align across the boundary, and an over-budget pack still resolves every offset)* ⇒ 🔒 **before deleting a test, ask which assertions are about the thing being removed and which merely USED it as a fixture** — a green suite after a deletion proves nothing about what the deletion stopped covering. ⚠ And the boundary fixtures had **already** expired: they hard-coded 25/26 ints against 100, so `CE-307` silently turned them into non-boundary tests that still passed | originally filed `2026-09-22`. ⭐ **Unreachable since `CE-307`** raised the ceiling, so the PATH is closed — ⛔ the code, `PackTier.Heavy`, `MaxHeavyBytes`, and the authoring window's `RequiresHeavyComponent` *(view model + JSON export key)* are not. ⚠ **Low severity, and the reason is `CE-312`'s generalisation:** the one production caller reads only `ByteSize` — `Tier`/`ByteOffset` are discarded — so no offset into the dead component is ever used. What leaks is a **misleading UI claim**, not corrupt data. ⇒ its own slice, its own rails |
| ⚠ **the two §29 unknowns** | the failing runs reaching the firing line with no spawn-time writer; the 100-byte probe passing 2/2 *before* the fix. ⛔ Neither blocks — ⭐ re-read if `P4` reddens the cluster in a way the unit rails miss |

---

## 6. ⭐ THE EXACT FIRST ACTION

1. `git fetch origin behaviors && git status` — expect **clean**, `P4`-④ landed.
2. ⭐⭐ **CONFIRM `CE-316` still holds** — it is one command and an incremental build hides the failure:
   ```bash
   touch Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs
   dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj --no-restore 2>&1 \
     | grep -cE "BTREE0002"      # ⭐ expect 0
   ```
   ⚠ **Make this the habit after ANY change to a `[BTreeAction]` signature or to an emitter's `bbShort`.**
3. ✅ **`CE-314` is DONE** — the heavy arm is gone and the editor/generator ceiling inconsistency is
   closed. ⛔ Nothing to do here.
4. **Then delete `BrainBlackboard`** *(§2 ②)* — ⛔ **re-run §3's dead-storage check FIRST**; it is what
   found `CE-310`, `CE-311` and `CE-312`. ⭐ It also closes `CE-315` for free.
5. **§2 ③ and the fork decision *(§5)* are the two that need the USER** — both still open, neither blocking.

### ⚠ THE RAIL THAT IS STILL MISSING — **file it or build it**

⛔ **`CE-316` was found by reading a build log.** ⭐ The rail that would have caught it asserts over the
**ASSET SET** — *"no `.btree.json` under `Hrot.AI.Behaviors/Assets` is skipped"* — ⛔ **not** over the
registry, which is a DERIVED collection a skipped asset never enters. ⚠ The `CE-316` stub-level rails
pin the validator's rule; they do **not** pin the corpus.

---

## 7. ✅ THE FORK IS REMOVED *(`2026-09-22`)* — **and what "the fork" turned out to mean**

⛔ **It could never mean the whole tree:** `Fbt.Kernel` and `Fbt.Compiler` are in the ROOT solution and
**ship in HROT** — `Fbt.Kernel` is what `P4`-② modified. ⇒ what was removed is the **vendored upstream
PROJECT** around them: its own `FastBTree.sln`, a duplicate source generator, examples, benchmarks,
docs, README/CHANGELOG. ⭐ What remains is two owned libraries, a root-solution demo, and their tests.

| ⭐ kept | why |
|---|---|
| `src/Fbt.Kernel` · `src/Fbt.Compiler` | ship in HROT |
| `demos/Fbt.Demo.Visual` | in the root solution |
| `tests/Fbt.Tests` *(−6 files)* | 188 passing tests of SHIPPED code |
| `.dev-workstream/` *(31 files)* | ⛔ **design record.** *"Unreferenced is not unintentional"* — the one category never to bin |

| ⛔ removed | |
|---|---|
| `src/Fbt.SourceGen` | 📐 **dead to HROT, confirmed three ways:** no project reference, no `OutputItemType="Analyzer"` reference, and `FbtTreeCatalog.g.cs` is emitted by **`Fdp.Toolkits.Analyzers.BTreeDefinitionGenerator`** — FDP's own copy. ⚠ The `CgfNodes.cs` comments crediting `Fbt.SourceGen` are STALE |
| `examples/` · `benchmarks/` · `docs/` | the generator's only other consumers; benchmarks were already broken *(2 errors)* |
| `FastBTree.sln` · README · CHANGELOG · `demos/Fbt.Demo.Visual.Tests` | the scaffolding that made it a vendored PROJECT |

### 📐 THE COST, MEASURED — **not estimated**

| | tests | passed | failed |
|---|---|---|---|
| before | **233** | 224 | **9** |
| after | **196** | 188 | **8** |

⇒ **37 tests removed, 36 of them passing.** ⚠ **The suite carried 9 PRE-EXISTING failures** before any of
this — it was never healthy. 🔴🔴 **And it runs in NO HROT GATE: `Fbt.Tests` is not in the root
solution**, so those 188 passing tests of shipped kernel code **execute nowhere**. ⭐ Wiring it in would
make the coverage real, at the price of adopting 8 failures as gate failures. ⚠ **Left as a decision,
not smuggled into this change.**

### ⛔⛔ THREE WRONG INFERENCES, ALL CORRECTED BY BUILDING

🔒 **The resume doc said this needed "one build, not a guess." It needed four, and every guess was wrong.**

| I predicted | measured |
|---|---|
| `Fbt.Tests` is BROKEN by `behav-diag-1`'s `ITreeTracer` constraint | ⛔ **builds clean** — `T6.1` was done |
| **7** generator-coupled test files | ⛔ **5** |
| removing the generator is the whole cascade | ⛔ **`examples/` too** — `Fbt.Tests` referenced them, and `Fbt.Examples.FluentBTree.Trees.dll` was visible in its `bin` the whole time |

⚠⚠ **The third is the one worth keeping.** The 199/8 figure was measured **before** `examples/` was
deleted — ⛔ **a measurement against a tree that no longer existed**, the same shape as the stale
ClusterRunner dll and the stale fixture registration. 🔒 **The solution build stayed green through all
of it, because none of this is in the root solution** — ⇒ **an out-of-solution project needs its own
explicit run; it can never ride on the solution gate.**
