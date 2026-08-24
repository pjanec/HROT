<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer — one ReflectionPrimer with pluggable handlers, replacing the per-role
  component registries, the 5 gizmo lists, and the MapSchemaPack/MapGizmoPack split. ⛔ Carries no design:
  see DESIGN_Reflection_World_Priming.md.
known-conflict: ⚠ SUBSUMES ST-027's MapSchemaPack (retire it) and CLOSES Q53's MapGizmoPack (never build
  it — the primer IS the answer). Both are this batch's to fold in.
-->
# HANDOFF — **reflection world-priming** *(one scan, pluggable handlers)*

> 📌 **Dispatched at `<STAMP>`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`ST-`**, tracker **Area I** — 📐 series stands at **`ST-030`**, so start at `ST-031`.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Reflection_World_Priming.md`](../../DESIGN_Reflection_World_Priming.md)** — `READY-TO-BUILD`.
⭐ **§2 the inventory, §3 the design (one scan / N handlers / phased), §4 the UML, §5 the rails, §6 scope,
§7 the trade-off.** 📄 The decision that led here: [`Architect_Question_53`](../Architect_Question_53_Gizmo_Pack_Home.md)
*(Option A, ruled)*. ⭐ Report per obligation ③; ⭐⭐ **fold deviations into the design** *(obligation ⑤)*.

⭐⭐ **This is your own thread's payoff.** `ST-020` → `ST-022` *(schema pack)* → `ST-027` *(widened to 15)* →
`ST-028` *(the pack home is a cycle)* → **the answer is not a lower assembly, it is no compile-time
reference at all.**

## 1. ⭐⭐⭐ THE ITEMS

| # | task | design | gate |
|---|---|---|---|
| ⭐⭐ **①** | **`ReflectionPrimer` + `IReflectionPrimingHandler` in `Fdp.Toolkits`** — one cached `AppDomain`/`GetTypes` scan *(System-filtered, `ReflectionTypeLoadException`-safe)*, offering each type to each handler **in phase order** | §3.1 | it compiles and the scan runs once *(assert the scan is not repeated per handler)* |
| ⭐⭐ **②** | **The 3 handlers — component `[ComponentId]`, event `[EventId]`, gizmo `[GizmoProjector]`.** ⭐⭐⭐ **Generalise `RepositoryPriming`, do NOT parallel it** *(ruling 9)* — its component+event logic **becomes** handlers 1–2; `RegisterDiscoveredComponents` stays as a thin call so replaybrowser's site keeps working | §3.2, §3.1 ④ | phase 1 *(component+event)* before phase 2 *(gizmo)* — 📌 reversed throws, `ST-020` |
| 🔴🔴 **③** | ⭐⭐⭐ **Every host calls the primer with the SAME handler list**, retiring: the per-role component registries *(`IgRole`/`Cognitive`/`Combat`/`MuscleRole`/`HrotSharedComponentRegistry`)*, the **5 hand-rolled gizmo lists**, and **`MapSchemaPack`** *(`ST-027` — now subsumed; DELETE it and say so)* | §3.1 ⑤, §6 | ⭐⭐ `ModeStartupRails` **8 / 8** after each host is converted — ⛔ convert + prove per host, not big-bang |
| 🔴🔴 **④** | ⭐⭐⭐ **THE COMPLETENESS RAIL — source-scan count vs runtime count, EVERY mode.** Grep the source for `[ComponentId]`/`[EventId]`/`[GizmoProjector]`, assert the primer registered exactly that many in every mode | §5 | ⛔⛔ **This is the static check reflection gives up — it must be seen to FAIL** *(remove one attribute in a scratch edit ⇒ the rail reddens naming the type)*. ⭐ It also catches the load-order risk |
| ⭐ **⑤** | ⭐⭐ **The two other rails: cross-node layout identical** *(two modes agree on their component→id intersection)* **and cost** *(mode-rail startup delta before/after)* | §5 | ⛔ *"it is free"* is measured, not asserted — 📌 the claim this programme keeps retracting |

⭐ `GizmoSchemaFollowsDeclarationRails` *(invariant `A`, `ST-023`)* **stays**; invariant `B` **becomes** ④'s
completeness rail — ⭐ one mechanism, ⛔ do not keep two.

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
