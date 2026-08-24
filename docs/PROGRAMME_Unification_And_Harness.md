<!--STATUS
state: LIVE
doc-type: programme charter — the GOALS and the ORDER, in the user's own framing. Not a buildable design,
  so no build-state/UML gate; the buildable designs are the ones §6 points to.
updated: 2026-08-23
current-answer: the whole file. ⭐ Point a fresh session HERE first — it says what we are doing and why,
  before any design or batch. §3 is the running order; §4 is what is already decided.
known-conflict: none.
-->
# PROGRAMME — share the editor's features with CGF, without breaking them on the way

> ⭐⭐⭐ **User, `2026-08-23`, the goal in one line:** *"unify the internal implementation of various
> features; by sharing as much as possible across subsystems."*

## 1. ⭐⭐ THE GOAL

| | |
|---|---|
| **Where we are** | ⭐ Many features exist in **editor mode only** — notably **asset editing**, several with their own perspective *(BTree · HSM · Blueprint)* |
| ⭐⭐ **Where we are going** | **share the debugging / monitoring / editing features with the CGF subsystem** — 🔒 *"CGF is the brain of the simulation, handling all the scenarios, blueprints, hsms, btrees"* ⇒ it should be **as capable as the editor**, just doing it in a **distributed** setup rather than the editor's all-in-one |
| ⛔ **The risk** | the unification is a large internal refactor, and **much of what must stay identical is VISUAL** ⇒ `R-21` keeps human visual checks suspended, so the proof has to be **machine-readable and headless** |

## 2. ⭐⭐⭐ WHAT THE HARNESS MUST DO — **four jobs, in the user's words**

| # | job |
|---|---|
| **①** | **Capture how stuff works in editor mode** *(the reference behaviour, before anything moves)* |
| **②** | **Run a curated set of scenarios and assert the status** against **predefined goldens / hard-coded conditions** |
| **③** | ⭐⭐ **Check that this does not change as we proceed with the internal refactors** leading to the unification |
| **④** | ⭐ **Once the same feature is implemented in CGF, check it works / looks the same as in the editor** |

> ⭐⭐⭐ **THE SHAPE THIS FORCES — user, verbatim:** *"As we will unify part by part, feature by feature, the
> checks need to be very granular, scoped to little parts only, and there will be many of them."*
>
> ⇒ ⛔ **Not a few big per-scenario dumps.** ⭐ **Many small checks**, each scoped to one feature, so that
> **one feature's change moves one file** and the diff is readable. 📌 This is a design constraint, not a
> preference — see §4's golden-granularity decision.

## 3. ⭐⭐⭐ THE ORDER — **user-set, `2026-08-23`**

| # | step | why HERE |
|---|---|---|
| **0** | ✅ **DONE `2026-08-23`** — Stride integrated at `477b31f52`; visual check run by the user, one defect found and fixed *(`R-132`)* | 🔒 *"so that we can do bigger (namespace/project structure) refactors freely"* — ⛔ do it **before** the refactors, not after, or every one of them conflicts with it |
| **1** | ⭐⭐ **Unify the perspective naming**, and **allow for the features CGF does not have yet** — 📄 **DESIGNED**: [`DESIGN_Perspective_Unification.md`](DESIGN_Perspective_Unification.md) | this is what makes editor and cluster **comparable at all** *(§4)* |
| **2** | ✅ **DONE `2026-08-24`** — the harness core: `N0`–`N2` *(perspective reach · determinism rail · golden store)* built and merged. 📄 [`DESIGN_Regression_Net.md`](DESIGN_Regression_Net.md) §7b | — |
| **3** | ✅ **DONE `2026-08-24`** — `N3`–`N6` built: 6 paired goldens across all 4 perspectives, behaviour assertions, and ⭐⭐ **the MUTATION TABLE** *(`DESIGN_Regression_Net.md` §8b)* — two mutations each reddening exactly one case, incl. a golden catching an **un-asserted** field. 🔒 **The net is proven to fail on demand.** Suite `58 → 76` | ⭐ *the net is now TRUSTED — a green suite that encodes nothing is worse than none, and this one encodes and was shown to go red* |
| **4** | **Then** decide **which features to port when** — ⛔⛔ **AND IT IS LARGELY ALREADY DESIGNED: START FROM [`UX/`](UX/), DO NOT RE-DERIVE IT** *(added `2026-08-23` after the user pointed at it)*. 📐 **37 issues · 21 designed**, with their own tracker, rulings ledger and corrections log. ⭐⭐⭐ **`UX_Feature_Cgf_Brain_Diagnostics.md` (UXI-37) IS this step for CGF's brain tier**, and its verdict is *"⭐ **this is a wiring design, not a capability design**"* — 📐 measured: `Hrot.Editor.AiShared` is **already on CGF's build graph**, the breakpoint manager / trace log / blackboard renderers are **already registered**, `PreviewClusterOpHandler` is **already referenced**. ⇒ ⭐ **step 4 is mostly ADOPTION, not porting** | 🔒 *"Only then we will start thinking what features to port when"* — ⛔ **not before**, or we port without a net. ⚠⚠ **BUT reading the design is not "starting step 4"** — ⛔ **step 4's designs CONSTRAIN steps 1–3**: 📌 `2026-08-23`, `Q52` reached the wrong mechanism for `--mode ig` because it never opened `UX_Feature_Map_Parity.md` §3.2 / `UX_Feature_Map_Layers.md` §2.2, **which already ruled on it** |

⭐⭐⭐ **STEPS 2–3 COMPLETE `2026-08-24` — THE NET EXISTS AND IS TRUSTED.** ⇒ **step 4 (feature porting) is UNBLOCKED**, and from here every port runs against the net *(and an intentional behaviour change re-blesses goldens under review — the gizmo membership change was the first such, landed `2026-08-24`)*.

⚠ **Nothing in step 4 starts while step 3 is unfinished.** ⭐ That ordering is the whole point of the
programme: the net exists to make the port safe, so building it is not preparation for the work — **it is
the first half of the work.**

## 4. ⭐⭐ DECIDED — **do not re-litigate these**

| # | decision | detail |
|---|---|---|
| **D1** | ⭐⭐⭐ **CGF presents the ASSET perspectives, not one `CGF` perspective** | 🔒 *"whenever the cluster runner includes the cgf subsystem, it will not be represented by one 'cgf' perspective, but 4 asset specific ones"* — **Scenario · BTree · HSM · Blueprint**, all still belonging to the CGF subsystem. ⭐ **Extensible**: more CGF-owned perspectives may follow if the asset ones prove insufficient |
| **D2** | ⭐⭐ **RENAME the editor's perspective id `Editor` → `Scenario`** *(user chose this over aliasing)* | ⭐ Today `"Scenario"` is only a **display label** over the `Editor` **id**, so the ids would NOT have matched. ⛔ **The committed layout carries no code migration cost** — 📐 measured: `layout/default/fdp_windows.json` names a perspective in **exactly one field** *(`ActivePerspective`, currently `"Blueprint"`)* and `layout/default/imgui.ini` names none. ⇒ update those two files if the default changes, and rename in code |
| **D3** | ⭐⭐ **The lifted debug API must ACCEPT ABSENT capabilities** | 🔒 *"many editor features are not yet available in the cgf (like the preview). They will be, but later. So the 'lifted' debugApi need to accept nulls in many cases"* |
| **D4** | ⭐⭐⭐ **A CAPABILITY MANIFEST — and it is a teaching surface, not a flag list** | 🔒 the user's requirement: each capability carries **① what it DOES · ② which endpoints are available · ③ their SCHEMA** ⇒ *"all helps the MCP server user to orient and learn without prior knowledge."* ⭐⭐ **And it makes D3 testable**: *"preview is absent in CGF"* becomes an **assertable fact** instead of a 404 to interpret — the harness asserts absent today, present tomorrow, and the flip is a deliberate, reviewed change |
| **D5** | ⭐ **Goldens are per `PanelKind`, not per scenario × perspective** | §2's granularity requirement ⇒ one feature's change moves one file. ⚠ **Supersedes** the coarser `Goldens/<scenario>/<perspective>.json` shape in the runbook — cheap to change now, while no goldens exist |
| ⭐⭐⭐ **D6** 📄 **[DESIGN](DESIGN_Deterministic_Network_Ids.md)** | **MAKE NETWORK IDS DETERMINISTIC — ⛔ do NOT normalise them out of the goldens** *(resolved with the user `2026-08-23`)* | 🔒 User: *"for determinism and comparing the snapshots from different runs i need the network ids to stay the same. is anything preventing us to reset the network id allocator on scenario start? Anyway the whole world resets."* ⇒ 📐 **Measured: nothing prevents it, and the machinery is already there.** ⭐ **`Reset` exists on EVERY implementation** — `SequentialIdAllocator` *(`Hrot.Core.Network`)* · `IgSequentialIdAllocator` · `DdsIdAllocator` · `BlockIdManager` · **and the editor's own nested one** — and ⛔⛔ **it has ZERO production callers** *(the single call in the repo is a DDS unit test)*. ⭐⭐ **The hook exists too:** `ScenarioFileService.RegisterWorldResetObserver(Action)` *(`:84`)*, and `:100` publishes `WorldResetEvent` right after `ClearAll`. ⇒ ⭐⭐⭐ **a dormant capability plus an existing seam — not a new mechanism.** ⭐ **Why deterministic BEATS normalised:** a normalised id is **erased**, so *"the wrong entity got the wrong id"* — a real replication defect — becomes invisible; a deterministic id stays **assertable**. ⚠ It also fixes a harness trap already on record *("reloading rebuilds entities with fresh network ids, so listing during another case's reload yields a dropped id")*. ⛔⛔ **SUPERSEDED `2026-08-23` — the DECISION stands, the MECHANISM NOTES ABOVE DO NOT.** 📄 **[`DESIGN_Deterministic_Network_Ids.md`](DESIGN_Deterministic_Network_Ids.md)** is the owning design; 🔴 **this row was written without reading [`docs/designs/mgmt-1/DESIGN.md` §5.7](designs/mgmt-1/DESIGN.md)**, which **designs AND BUILDS** the reset as a **master-owned, DDS-broadcast** operation *(`Req_Reset`/`Resp_Reset`, clients flush their pool)*. ⇒ ⛔ *"a dormant capability plus an existing seam"* **understates it**, and ⛔ **caveats ②/③ are replaced by that design's §3**: 📐 caveat ② *("distributed mode is a hazard")* — it is **a designed protocol, already built**; 📐 caveat ③ — the real hazard is that **`Reset()`'s default is not the construction value on EITHER allocator, and they differ** *(`Hrot.Core.Network`: pre-increment from `1`; the editor's nested: post-increment from `1000`)* ⇒ ⭐⭐ **always `Reset(explicitStart)`**. ⭐⭐⭐ **AND THE REQUIREMENT IS SCENARIO PREVIEW, NOT SCENARIO LOAD** *(🔒 user `2026-08-23`: "the reset is still wanted feature in scenario preview — when we finish the preview the world resets but not so the network id allocator")*: 📐 **authored** entities take their ids from the scenario FILE and never touch the allocator, but **preview SPAWNS at runtime** and `PreviewClusterOpHandler` rewinds **only the `EntityRepository`** ⇒ ⛔ **the counter survives, so preview N+1 does not repeat preview N.** ⛔⛔ **AND THIS ROW'S SEAM IS MEASURABLY WRONG FOR IT: preview exit publishes NO `WorldResetEvent`**, so `RegisterWorldResetObserver` is not on that path. ⇒ ⭐ **save/restore around the preview bracket** — 📄 **[`DESIGN_Deterministic_Network_Ids.md`](DESIGN_Deterministic_Network_Ids.md)** §1–§6, and ⚠ **it is a CLASS of bug**: `NetworkEntityMap` is outside the repo too |
| ⭐⭐⭐ **D7** | **GOLDENS AND ASSERTIONS DO DIFFERENT JOBS — use both, and PAIR them** *(resolved with the user `2026-08-23`)* | ⭐ **Golden answers *"did anything change?"*** *(job ③, refactor safety — right precisely because you cannot know in advance which field a refactor disturbs)*; ⭐ **assertion answers *"is it right?"*** *(jobs ① and ②)*. ⭐⭐⭐ **THE PAIRING RULE: every panel with a golden also gets 1–3 assertions on the fields that MEAN something** ⇒ a bulk re-bless may change the noise but ⛔ **cannot silently change the meaning.** ⭐ **Which one, per panel, is decided by FIELD COUNT and semantic density — ⛔ not by the panel's importance**: 3–10 meaningful fields ⇒ assertions only; a large derived dump *(a table, a tree, 200 rows)* ⇒ golden **plus** its pairing assertions. ⚠⚠ **And the golden count is a DELIBERATE BUDGET, never a byproduct of instrumentation coverage** — ⛔ *"free to create"* is how a repo ends up with 150 golden files nobody reads. 📄 **Full reasoning + the worked `R-132` example: `DESIGN_Regression_Net.md` §4** |

## 5. ⭐ MEASURED, `2026-08-23` — **facts worth not re-deriving**

| fact | consequence |
|---|---|
| ⭐⭐ **Perspectives are DERIVED, not declared** — `WindowManager.GetPerspectives()` returns the distinct `OwningPerspective` of registered `PerspectiveBound` windows | ⇒ **there is no registry to extend, and an EMPTY perspective is not representable.** ⭐ CGF's perspective list therefore grows **feature by feature, automatically**, as each window lands. `perspectiveMap` *(perspective → subsystem, many→one)* and the gizmo map are the only central touchpoints |
| ⭐ **The design already blesses D1** — *"perspective is the finer key and degenerates to subsystem for the cluster roles"*; **the Editor subsystem already owns four perspectives** | ⇒ D1 is **applying the existing model to CGF**, not inventing one |
| ⭐ **`Hrot.CGF` already references `Hrot.Blueprints.Editor`** | ⇒ **Blueprint is the cheapest first feature to unify** — the assembly wall is already down for it |
| 🔴 **`WindowManager.SwitchPerspective` validates NOTHING** — any string is accepted, and every bound window then stops drawing | ⛔⛔ **This is a PREREQUISITE of D2**: a user's own stored layout naming `Editor` would, after the rename, select a perspective with no windows ⇒ **a blank UI with no error.** ⚠ `UX_Feature_Perspective_Restore.md` §3 designs the refusal *("log and no-op")* — **it is not implemented** |
| ⭐ **`--mode cluster` does not exist** — it is **`--mode all`** *(= `orchestrator,simhost,ig,excon,cgf`, FIVE subsystems)*; an unknown mode throws | fix any doc that says otherwise |
| ⭐ **Stride merges cheaply** — 📐 4 commits, 138 files, `+40851` lines; a dry-run merge onto the current head yields **exactly ONE conflict**, in `Blueprint_Issues_Tracker.md`. ⛔ **No code conflicts** | ⚠ **But a clean textual merge is not a working build** — step 0 still owes a full build + the gate suites |
| ⭐ **`PanelId` = address, `PanelKind` = type**, both host-supplied; a shared `PanelIds` constants class exists and cross-host hosts already cite it *(e.g. `PanelIds.Mission` from both the editor and ExCon)* | ⇒ conformance groups by **kind**, addresses by **id**. ⚠ Kind agreement is convention-backed, so a suite must assert it compared **more than zero** kinds |

## 6. WHERE THE DETAIL LIVES

| doc | owns |
|---|---|
| [`DESIGN_Headless_Testability.md`](DESIGN_Headless_Testability.md) | the test taxonomy, the one-binary architecture, conformance, sequencing |
| [`TESTING_Harness_And_Goldens.md`](TESTING_Harness_And_Goldens.md) | ⭐ the runbook — how to write a test, the perspective protocol, golden maintenance |
| [`DESIGN_UI_Observability_Snapshot.md`](DESIGN_UI_Observability_Snapshot.md) | the `PanelSnapshot` contract *(the pixel-free read model)* |
| [`MCP_Integration.md`](MCP_Integration.md) | the debug/MCP API surface |
| ⭐⭐⭐ [`DESIGN_Regression_Net.md`](DESIGN_Regression_Net.md) | ⭐⭐ **steps 2–3** — the net itself: the perspective endpoint, the determinism rail, the golden store, the first slice, **the mutation proof**, and the behaviour assertions. `build-state: READY-TO-BUILD` |
| [`DESIGN_Perspective_Unification.md`](DESIGN_Perspective_Unification.md) | ⭐⭐ **step 1** — `D1`/`D2` designed: Part A *(the rename + the unknown-id refusal it depends on)* is `READY-TO-BUILD`; Part B *(CGF grows the asset perspectives)* is the target |
| [`blueprints/Architect_Question_51_Project_Consolidation.md`](blueprints/Architect_Question_51_Project_Consolidation.md) | ⛔ **project consolidation — DECLINED by the user `2026-08-23`** *(the measured win was ~10–15 s of MSBuild overhead; not worth the disruption)*. Kept for its measurements: the DAG is 17 deep, and depth not count is what costs build time |
| [`DESIGN_Stride_Port.md`](DESIGN_Stride_Port.md) | step 0 — ✅ **INTEGRATED `2026-08-23`** (`477b31f52`) |
| 🔴🔴🔴 [`UX/`](UX/) | ⭐⭐⭐ **THE UNIFICATION INTENT PER FEATURE — A WHOLE PARALLEL PROGRAMME, AND THIS ROW WAS SKIPPED FOR A WEEK.** 📌 `2026-08-23`: the user had to say *"could you pls look in docs/UX documents? I think i have been already solving the issue"* — ⛔ **and this row already named the corpus.** ⇒ ⭐⭐ **a pointer is not a habit**; the table below is the habit. ⛔ **Do not answer a "how should <surface> work once unified" question without checking it** |
| ↳ **its entry points** | ⭐ [`UX_Issues.md`](UX/UX_Issues.md) *(the **`UXI-` register** — 37 issues, status per row)* · ⭐⭐ [`UX_RESUME_INTERACTION.md`](UX/UX_RESUME_INTERACTION.md) *(**the rulings ledger** — numbered user rulings, ~66+)* · ⭐⭐ [`UX_Tasks_Detail.md#corrections`](UX/UX_Tasks_Detail.md) *(**the corrections log** — 50+ measured retractions; 📌 **Correction 47** is the `RegisterComponent<T>` ≠ *entity carries it* conflation)* · [`UX_Glossary_Host_Mode_Subsystem.md`](UX/UX_Glossary_Host_Mode_Subsystem.md) *(process · mode · subsystem · perspective)* · [`SHARED_SURFACES.md`](UX/SHARED_SURFACES.md) *(co-ownership — ⚠ its closing question **"does the other side read this?"** was answered **no**, by me)* |
| ↳ ⭐⭐ **the MAP once unified** | 📄 [`UX_Feature_Map_Parity.md`](UX/UX_Feature_Map_Parity.md) — 🔒 **`MapInteractionPack`**: **one** registration entry point for all five map hosts; ruled `2026-08-10` *"all hosts share the FULL set… never set membership"*, so ⭐ **membership is uniform, visibility is earned**. ⛔ **DESIGNED, NOT BUILT** *(zero `.cs` occurrences — measured `2026-08-23`)* · 📄 [`UX_Feature_Map_Layers.md`](UX/UX_Feature_Map_Layers.md) + [`Architect_Question_28_Map_Layers.md`](UX/Architect_Question_28_Map_Layers.md) — ⭐⭐ **tags not partitions; a per-gizmo `TagMask` over *entities · perception · **ai-helpers***; ALL-semantics hiding** ⇒ **this is how a host shows fewer gizmos, NOT a dropped registrar call** · 📄 [`UX_Map_Parity_Baseline.md`](UX/UX_Map_Parity_Baseline.md) *(the inventory)* · 📄 [`UX_Feature_Map_Viewport.md`](UX/UX_Feature_Map_Viewport.md) |
| ↳ ⭐⭐⭐ **step 4 for the brain tier** | 📄 [`UX_Feature_Cgf_Brain_Diagnostics.md`](UX/UX_Feature_Cgf_Brain_Diagnostics.md) *(UXI-37)* — ⭐ *"a **wiring** design, not a capability design"*; ⭐ **diagnostics AND authoring are one scope** *(ruling 65)*; pause/resume settled in [`Design_Question_30_Debug_Pause_Resume.md`](UX/Design_Question_30_Debug_Pause_Resume.md) |
| ↳ ⚠ **and these bear on steps 1–3** | 📄 [`UX_Feature_Perspective_Restore.md`](UX/UX_Feature_Perspective_Restore.md) *(§3 designs the unknown-perspective refusal `D2` needs)* · 📄 [`UX_Feature_Curated_Scenarios.md`](UX/UX_Feature_Curated_Scenarios.md) *(**BUILT** — the curated set the net's goldens run against)* · 📄 [`UX_Feature_Authority_Aware_Writes.md`](UX/UX_Feature_Authority_Aware_Writes.md) *(UXI-29 — a **prerequisite** of CGF map parity)* |

## 7. ⭐ BACKLOG — **wanted, designed enough to start, deliberately NOT scheduled**

⛔ **No ids here** *(rule 3 — the implementing session numbers them)*. ⭐ Each row says what it is, why it is
valuable, and what it is waiting for.

| item | ⭐ why it is worth doing | waiting on |
|---|---|---|
| ⭐⭐⭐ **The ANIMATION INTENT diagnostic** — a CGF/editor panel showing **what animations SHOULD be running** on the IG/Stride side | 🔒 **User, `2026-08-23`:** *"that would be of high value."* ⭐⭐ **And it needs NO new data model** — 📐 `Hrot.MuscleCharacter.Animation/Components/ReplicatedComponents.cs` already carries the backend-agnostic truth: **`AnimationChannel`** *(action id · issuing behaviour instance · instance token · lifecycle status · 32-byte action params · 32-byte executor state — playback progress and blend weights)*, **`LookAtChannel`**, and stance *(Standing/Crouched/Prone + transition phase)*; `AnimationStateReporterSystem` emits `MontageStarted`/`Ended`/`SectionAdvanced`/`StanceChanged` in `PostSimulation`. ⇒ ⭐⭐⭐ **it gives a real parity axis: INTENT (CGF) vs PLAYBACK (Stride)** — exactly this programme's shape | ⭐ **§3 step 3** — the net. ⚠ It is **new capability, not a port**, so building it now means building it with no golden to catch its regressions. ⛔ Cheap and additive, so this is a sequencing call rather than a risk |
| ⚠ **The four DORMANT windows** — register them, or retire them | 📄 `DESIGN_Perspective_Unification.md` **§1g**: ⛔ **they are NOT deletable** — the comparison feature's backend *(`sanitizerRegistry` · `exportBuilder`)* is passed into **every** perspective registrar, and `UtilityDecisionWindow`'s project is referenced by `Fdp.Toolkits`. ⭐ **ROUTE, don't DELETE** | a decision on whether each feature is wanted |

⚠⚠ **On the animation panel, one thing measured and worth not re-deriving:** ⛔ **do NOT convert
`FakeAnimBackendInspectorWindow` into it.** 📐 It reads `repo.GetComponentRO<FakeAnimBackendState>(e)` — the
**Fake backend's own component**, which does not exist on a node running the real Stride backend. ⭐ **Reuse
its SHAPE** *(entity list · selected-entity detail · "Copy JSON Snapshot"; it already publishes to
`PanelSnapshot`)*, ⛔ **not its data source.** ⚠ And note it was *designed* to be registered by
`SimHostSubsystem.RegisterWindows` *(`DD-Fake` §7.3)* and never was — so it is built-and-unwired, not dead.

### ⚠⚠ D6's four caveats — **all measured, all must survive into the design**

| # | caveat | |
|---|---|---|
| **①** | ⭐⭐⭐ **RESET IS NECESSARY, NOT SUFFICIENT.** A reset counter gives the same ids only if the **ALLOCATION ORDER** is the same, and that depends on spawn order during scenario load | ⇒ ⛔ **this must be MEASURED, not assumed**: load the same scenario twice in two fresh processes and diff the whole id→entity mapping. ⭐ **That measurement is the net's first step and it comes BEFORE any golden** — if order is not deterministic we fix THAT, ⛔ we do not paper over it with normalisation |
| **②** | 🔴 **DISTRIBUTED MODE IS A HAZARD, not a symmetry.** Resetting a local allocator on one node while others run **collides ids** and corrupts `NetworkEntityMap` replication. ⭐ `DdsIdAllocator.Reset` does carry a cluster-wide protocol, ⚠ but that is a different code path with real blast radius | ⇒ ⭐⭐ **scope the reset to OFFLINE / editor / preview first** *(which is where the net starts anyway)*; ⛔ **cluster-wide reset is a separate, later question** |
| **③** | ⚠ **A DEFAULT-PARAMETER TRAP.** Every signature is **`Reset(long startId = 0)`**, but the editor's allocator **starts at `1000`** ⇒ ⛔ **calling `Reset()` silently rebaselines to `0`**, not to the baseline | ⇒ ⭐ **always pass the baseline explicitly**, and keep it in **ONE** place — ⛔ not duplicated at each call site. ⭐ Better: a `ResetToBaseline()` that cannot be got wrong |
| **④** | ⚠ **A NAME COLLISION to know about before editing.** `EditorSubsystem` has its **own private nested `SequentialIdAllocator`** *(`:552-558`, `_next = 1000`)* which **shadows** `Hrot.Core.Network.SequentialIdAllocator`. ⚠ Two types, one name; the editor's `Reset` is a plain `_next = startId` where the other uses `Interlocked.Exchange` | ⇒ ⛔ **make sure the reset lands on the one the editor actually uses.** 📌 The same shape as the three `IMissionEditorService` that already bit this repo |
