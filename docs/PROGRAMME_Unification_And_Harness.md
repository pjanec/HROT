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
| **2** | ⭐ **Keep building the harness** so it supports §2's four jobs | — |
| **3** | ⭐⭐ **Set up the baseline + tests, generate the goldens — and prove the harness actually works** | ⛔ **the net must be trusted before it is relied on**; a green suite that encodes nothing is worse than none |
| **4** | **Then** decide **which features to port when** | 🔒 *"Only then we will start thinking what features to port when"* — ⛔ **not before**, or we port without a net |

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
| [`DESIGN_Perspective_Unification.md`](DESIGN_Perspective_Unification.md) | ⭐⭐ **step 1** — `D1`/`D2` designed: Part A *(the rename + the unknown-id refusal it depends on)* is `READY-TO-BUILD`; Part B *(CGF grows the asset perspectives)* is the target |
| [`blueprints/Architect_Question_51_Project_Consolidation.md`](blueprints/Architect_Question_51_Project_Consolidation.md) | ⛔ **project consolidation — DECLINED by the user `2026-08-23`** *(the measured win was ~10–15 s of MSBuild overhead; not worth the disruption)*. Kept for its measurements: the DAG is 17 deep, and depth not count is what costs build time |
| [`DESIGN_Stride_Port.md`](DESIGN_Stride_Port.md) | step 0 — ✅ **INTEGRATED `2026-08-23`** (`477b31f52`) |
| [`UX/`](UX/) | ⭐⭐ **the unification intent per feature** — start at [`UX_Glossary_Host_Mode_Subsystem.md`](UX/UX_Glossary_Host_Mode_Subsystem.md) *(process · mode · subsystem · perspective)* and [`UX_Feature_Cgf_Brain_Diagnostics.md`](UX/UX_Feature_Cgf_Brain_Diagnostics.md) |

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
