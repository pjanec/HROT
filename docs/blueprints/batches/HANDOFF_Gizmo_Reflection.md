<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer — finish uniform gizmo membership via REFLECTION (Q53 Option A), and
  correct ST-027's component tables to id-only. ⛔ GIZMOS ONLY. Components are NOT touched (role-gated
  stays — DESIGN_Reflection_World_Priming.md). Carries no design: see DESIGN_Uniform_Gizmo_Membership.md §8.
known-conflict: none. Replaces the WITHDRAWN HANDOFF_Reflection_World_Priming.md; unblocks that design's
  §7.3 (item ②).
-->
# HANDOFF — **gizmo membership by reflection** *(finish §7.3, correct `ST-027`)*

> 📌 **Dispatched at `5db5c60bc`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`ST-`**, tracker **Area I** — 📐 series stands at **`ST-030`**, so start at `ST-031`.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Uniform_Gizmo_Membership.md` §8](../../DESIGN_Uniform_Gizmo_Membership.md)** — `READY-TO-BUILD`.
⭐ §8.1 why reflection is safe for gizmos *(and NOT components)*, §8.2 the design, §8.3 the UML, §8.4 the
`ST-027` correction. 📄 The decision: [`Architect_Question_53`](../Architect_Question_53_Gizmo_Pack_Home.md)
*(Option A)*. ⭐ Report per obligation ③; ⭐⭐ fold deviations into the design *(obligation ⑤)*.

✅ **User ruled `2026-08-24`:** Option A (reflection) confirmed *(Option B retired; aggregation is the backlog alternative)*; headless publishing **capability stays** *(gating optional — 📄 design §8.5b/§8.7, NOT this batch)*. ⛔ **The P3 recorder fix (item ⓪) still stands** — it is `.fdp` bloat, separate from the accepted DDS cost.

⛔⛔ **GIZMOS ONLY.** Components/events are **role-gated and untouched** — 📄
[`DESIGN_Reflection_World_Priming.md`](../../DESIGN_Reflection_World_Priming.md) records why *(a component
table costs recorder/DDS/TkbTemplate; a gizmo, being data-free, does not)*. ⛔ **Do NOT reflect components.**

## 1. ⭐⭐⭐ THE ITEMS

| # | task | design | gate |
|---|---|---|---|
| 🔴🔴 **⓪** | ⭐⭐⭐ **MEASURE the `ST-027` exposure, then CORRECT it.** 📐 `MapSchemaPack` registered **15 component tables on every host**. On IG/SimHost the brain ones make `IsComponentTypeRegistered` **true** *(TkbTemplate risk)* and default **recordable** *(schema pollution)*. ⇒ **report whether either is live**, then make the dependency **id-only** *(`GetOrRegisterManaged`)* for hosts that do not simulate the component | §8.4 | ⭐ the measurement stated; brain-tier components **gone from `--mode ig`'s recordable set** |
| 🔴🔴 **①** | ⭐⭐⭐ **`GizmoReflectionRegistrar.RegisterAll(...)` in `Fdp.Toolkits`** — reflect loaded assemblies, register every `[GizmoProjector]`, resolve required component ids **id-only**. Replaces the **5 hand-rolled per-host gizmo lists**. ⛔ No compile-time reference to any host assembly *(the cycle-break — Q53 A over B)* | §8.2, §8.3 | ⭐⭐ `ModeStartupRails` **8 / 8**, per host converted — ⛔ not big-bang |
| 🔴🔴 **②** | ⭐⭐⭐ **THE COMPLETENESS RAIL** — source `[GizmoProjector]` count vs runtime, **every mode** *(this is invariant `B`, §7.4 — assert SEVEN families, §7.2)* | §8.2 ④ | ⛔⛔ **seen to FAIL** *(remove a projector ⇒ reddens naming it)*; catches the load-order risk |
| 🔴 **③** | ⭐⭐ **THE NON-BLOAT RAIL** — assert `--mode ig`/`--mode simhost` static **recordable** set EXCLUDES brain-tier components | §8.4 | ⛔ passes only after ⓪; this is what proves the id-only correction |
| ⭐ **④** | **cost** — mode-rail startup delta before/after *(the reflection scan is the only new per-boot cost)* | §8 | reported, not asserted-free |

⭐ **`GizmoSchemaFollowsDeclarationRails`** *(invariant `A`, `ST-023`)* stays; ② is invariant `B` — one
mechanism, ⛔ do not keep two. ⭐ **`replaybrowser` keeps `RepositoryPriming`** *(inspection exception)* —
⛔ do not touch it.

## 2. ⚠ WHAT WILL BITE

| ⚠ | |
|---|---|
| ⭐⭐⭐ **a gizmo needs the component ID, not a repo TABLE** | `StatelessGizmoRegistry.Register` → `GetId`; `StatelessGizmoSystem` iterates by mask bit *(`:103`)*. ⇒ use `GetOrRegisterManaged` *(id-only)*, ⛔ **not** `repo.RegisterComponent` — that is what `ST-027` got wrong |
| ⛔ **do NOT reflect or widen COMPONENT registration** | components stay role-gated — that is a separate, measured decision |
| ⚠ **`Hrot.IG.Tests`/`Hrot.SimHost.Tests` rotating-flaky** *(`ST-026`/`ST-030`)* | ⛔ gate the stable reds by name, or run isolated — do not quote a total |
| ⚠ **the editor's inline IG registrations** *(`:864`/`:868`, `ST-024`)* | ⭐ these are real component tables the editor DOES simulate — ⛔ leave them unless ⓪ shows them redundant |

## 3. ⛔ LANE & SCOPE

⭐ **Yours:** `Fdp.Toolkits/Diagnostics/Gizmos/` *(the new `GizmoReflectionRegistrar`)* · the 5 hosts' gizmo
call sites · `Hrot.Common/Diagnostics/Gizmos/MapSchemaPack.cs` *(the `ST-027` correction)* ·
`Hrot.ClusterRunner.Tests/GizmoSchemaFollowsDeclarationRails.cs` *(the rails)*.

⚠ **Check for live lanes** — the preview/`HN-` and net part-C lanes touch `Hrot.SystemTests`/`EditorSubsystem.cs`.
⭐ **Rule 4: pull the coordinator branch before your final commit.**

⛔ **Not this batch:** component reflection *(withdrawn)* · `MapInteractionPack`'s non-gizmo half *(UXI-23)* ·
`TagMask` *(UXI-28)* · aggregation *(`Q51` backlog)* · `HN-011`.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs dispatch
sha** · `--no-build` column · every RED pre-existing **by name** · `tracker-counts.py --check` ·
`rulings-check.py` · `design-digest.py --check` · **the `ST-` ids you allocated**, filed in the same commit.

⭐⭐⭐ **Row 8 — touches every host's bootstrap.** Integration gate: **`ModeStartupRails` (all 8)**, per host
converted, plus `Hrot.SystemTests` *(📐 baseline `58 / 58`)*. ⭐⭐ ②'s completeness rail and ③'s non-bloat
rail are the deliverables that prove the change — their revert-goes-red is not optional.

⚠ **Known quirks:** `tracker-counts.py --check` blind to `ST-` rows · `Fdp.Presentation.Tests` ~18–20
pre-existing *(`BP-419`)* · `mermaid-check.mjs` needs `npm install` *(say if skipped)* · 2 known
`rulings-check` staleness WARNs, not yours.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Uniform_Gizmo_Membership.md`](../../DESIGN_Uniform_Gizmo_Membership.md)**
— §8 made true, §7.3/§7.4 resolved, and mark `MapGizmoPack`/Option B **closed by §8**. ⭐ Mark `Q53` and the
`ST-027` correction closed in the tracker. ⛔ Design content in the design; the report points at it.
