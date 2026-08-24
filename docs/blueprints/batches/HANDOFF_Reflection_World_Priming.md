<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: ⛔ REVISED 2026-08-24 — GIZMOS reflect-all; COMPONENTS/EVENTS stay ROLE-GATED (the
  existing group registries — NOT converted to reflection). See DESIGN_Reflection_World_Priming.md §2c/§2d
  for why, MEASURED against code. This handoff carries no design.
known-conflict: ⚠ CORRECTS (not subsumes) ST-027's MapSchemaPack — its 15 repo tables become id-only
  (§2d, item ⓪); CLOSES Q53's MapGizmoPack (the gizmo handler is the answer).
-->
# HANDOFF — **reflection world-priming** *(one scan, pluggable handlers)*

> 📌 **Dispatched at `2364c6c2d` *(re-stamped — rule 1a revision, )*.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`ST-`**, tracker **Area I** — 📐 series stands at **`ST-030`**, so start at `ST-031`.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Reflection_World_Priming.md`](../../DESIGN_Reflection_World_Priming.md)** — `READY-TO-BUILD`.
⭐ **§2 the inventory, §3 the design (one scan / N handlers / phased), §4 the UML, §5 the rails, §6 scope,
§7 the trade-off.** 📄 The decision that led here: [`Architect_Question_53`](../Architect_Question_53_Gizmo_Pack_Home.md)
*(Option A, ruled)*. ⭐ Report per obligation ③; ⭐⭐ **fold deviations into the design** *(obligation ⑤)*.

⭐⭐ **This is your own thread's payoff** — `ST-020` → `ST-022`/`ST-027` *(schema pack)* → `ST-028`
*(the pack home is a cycle)* → **the answer is no compile-time reference: the gizmo handler reflects.**

⛔⛔ **READ [`DESIGN_Reflection_World_Priming.md`](../../DESIGN_Reflection_World_Priming.md) §2c FIRST.** The
design was **revised `2026-08-24`** after the user surfaced the downsides of registering all *components*
everywhere and the coordinator **measured them true**: the recorder/save schema read the **static**
`ComponentTypeRegistry` *(not the repo)*, `IsComponentTypeRegistered` drives TkbTemplate materialisation,
and the static registry is lazy/on-demand ⇒ ⛔ **a repo whitelist does NOT save it, and reflect-all-components
is HARMFUL on simulation hosts.** ⭐⭐ **But a GIZMO needs only the static component ID, not a table** *(it
iterates by mask bit; a projector with no entity draws nothing)* — so the asymmetry is principled.

## 1. ⭐⭐⭐ THE ITEMS

| # | task | design | gate |
|---|---|---|---|
| 🔴🔴 **⓪** | ⭐⭐⭐ **MEASURE + CORRECT the `ST-027` exposure** *(§2d)*. 📐 `MapSchemaPack` does `repo.RegisterComponent<T>()` for **15 components on every host** — full recordable tables. On IG/SimHost/CGF the brain ones *(`BrainBlackboard`/`BehaviorState`/`NavigationIntent`)* now make `IsComponentTypeRegistered` **true** *(TkbTemplate risk)* and default **recordable** *(schema pollution)*. ⇒ **report whether either is live**, then make them **id-only** *(`ComponentTypeRegistry.GetOrRegisterManaged`, no table)* for hosts that do not simulate them | §2c, §2d | ⭐ the measurement stated; the brain components **gone from `--mode ig`'s recordable set** |
| ⭐⭐ **①** | **`HostBootstrapPrimer` + `IPrimingHandler` in `Fdp.Toolkits`** — reflecting/gated handler kinds, phase order, one cached scan when a reflecting handler is present | §3.1 | it compiles; the scan runs once |
| 🔴🔴 **②** | ⭐⭐⭐ **The GIZMO handler (REFLECTING)** — discovers every `[GizmoProjector]`, registers it, resolves required component ids **id-only** *(§3.1 ④)*. Replaces the 5 hand-rolled gizmo lists. ⛔⛔ **The component + event handlers are GATED — wrap the EXISTING role registries** *(`HrotShared`/`Cognitive`/`Combat`/`MuscleRole`/`IgRole`)*; ⛔ **do NOT convert them to reflection and do NOT retire them** | §3.2, §2c | `ModeStartupRails` **8 / 8** per host converted — ⛔ per host, not big-bang |
| 🔴🔴 **③** | ⭐⭐⭐ **THE GIZMO-COMPLETENESS RAIL** — source `[GizmoProjector]` count vs runtime, every mode | §5 | ⛔ seen to FAIL *(remove one projector ⇒ reddens naming it)* |
| 🔴 **④** | ⭐⭐⭐ **THE COMPONENT-NON-BLOAT RAIL** — assert `--mode ig`/`--mode simhost` static recordable set **excludes** brain-tier components | §5, §2d | ⛔ this is the rail that proves the revision — it must pass only after item ⓪ |
| ⭐ **⑤** | **cost + cross-node rails** — startup delta; two modes agree on their id-map intersection | §5 | reported, not asserted-free |

⭐ `replaybrowser` keeps `RepositoryPriming`'s reflect-all as the **inspection exception** *(§3.2)* — ⛔ do not
gate it.

## 2. ⚠⚠ WHAT WILL BITE

| ⚠ | |
|---|---|
| ⭐⭐⭐ **component IDs are EXPLICIT `[ComponentId]`** | 📐 `ComponentType.cs:119`. ⇒ reflection is **layout-safe** — do NOT try to make registration order deterministic, the id is on the type. ⛔ But `ComponentTypeRegistry` is a **process-global static** *(Q52 §6.3)* ⇒ a duplicate `[ComponentId]` **throws**; the completeness rail must run each mode in a **fresh** process/world *(the mode rails already do)* |
| 🔴 **the gizmo handler needs THREE sinks** | `GizmoRegistry` · `StatelessGizmoRegistry` · `GizmoSettingsRegistry` — the component/event handlers need one each *(repo, bus)*. ⭐ Each handler is **constructed with its own sinks**; the primer stays sink-agnostic |
| 🔴🔴 **PHASE ORDER is load-bearing** | gizmo *(phase 2)* validates against components *(phase 1)*. ⛔ One scan, then handlers in phase order over the cached list — §4.2 |
| ⚠ **replaybrowser already reflects** | 📐 `RepositoryPriming` via `ReplayBrowserSubsystem.cs:139`. ⭐ It becomes a primer call; ⛔ do not leave a second reflection path beside it |
| ⚠ **the editor hand-picks IG components inline** *(`:864`/`:868`, `ST-024`)* | ⭐ now redundant — ⛔ **remove them as part of ③'s editor conversion** *(this batch's business, unlike before)* |
| ⚠ **`Hrot.IG.Tests` / `Hrot.SimHost.Tests` rotating-flaky** *(`ST-026`/`ST-030`)* | ⛔ do not quote a total — gate the stable reds by name, or run isolated |
| ⛔ **deferred handlers** *(`[BlueprintRegistrar]`, `[TkbDescriptor]`, DTOs)* | ⭐ the interface is the extension point — ⛔ **do not build them**, and do not touch the editor/hot-reload/picker scans |

## 3. ⛔ LANE & SCOPE

⭐ **Yours:** `Fdp.Toolkits/…/Priming` *(the primer + handlers, generalising `RepositoryPriming`)* ·
`Fdp.Toolkits/Diagnostics/Gizmos/` *(the gizmo handler)* · every host's bootstrap call site
*(`EditorSubsystem` · `IgApplication`/`IgNodeBootstrapper` · `SimHostApp`/`SimHostNodeBootstrapper` ·
`CgfSubsystem`/`CgfApplication` · `ReplayBrowserSubsystem`)* · the rails in `Hrot.ClusterRunner.Tests`.

⚠ **CHECK FOR LIVE LANES before you start** — the preview/`HN-` lane and the net part-C lane may still be
running and both touch `Hrot.SystemTests` and `EditorSubsystem.cs`. ⭐ **Rule 4: pull the coordinator branch
before your final commit.** ⛔ If `EditorSubsystem.cs`'s registration region *(`:857-869`, `:1431-1445`)* has
moved, re-derive against the merged state.

⛔ **Not this batch:** `MapInteractionPack`'s non-gizmo half *(UXI-23)* · the `TagMask` filter *(UXI-28)* ·
aggregation *(`Q51` backlog)* · the deferred handlers · `HN-011`'s loader leak.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs dispatch
sha** · `--no-build` column · every RED pre-existing **by name** · `tracker-counts.py --check` ·
`rulings-check.py` · `design-digest.py --check` · **the `ST-` ids you allocated**, filed in the same commit.

⭐⭐⭐ **Row 8 — this touches EVERY host's bootstrap.** The integration gate is **`ModeStartupRails` (all 8
modes)**, run **per host converted**, plus `Hrot.SystemTests` *(📐 baseline `58 / 58`)* and each converted
host's unit suite. ⭐⭐ **And ④'s completeness rail is the deliverable that proves the reflection is
trustworthy — its revert-goes-red is not optional.**

⚠ **Known quirks:** `tracker-counts.py --check` blind to `ST-` rows · `Fdp.Presentation.Tests` ~18–20
pre-existing *(`BP-419`)* · `mermaid-check.mjs` needs `npm install` *(say if skipped)* · 2 known
`rulings-check` staleness WARNs, not yours.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Reflection_World_Priming.md`](../../DESIGN_Reflection_World_Priming.md)**
— the handler interface as built, §4's diagrams made true, and **which per-host registries were retired**.
⭐ Mark `MapSchemaPack`'s deletion and `Q53`'s `MapGizmoPack` as **closed by subsumption** in the tracker.
⛔ Design content in the design; the report points at it.
