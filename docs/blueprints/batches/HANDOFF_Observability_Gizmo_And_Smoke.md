<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for the UI lane — finish the observability programme: U-obs-3 (the gizmo/map
  peer feed into PanelSnapshot) and U-obs-4 (the single-host smoke suite's T2 reads PanelSnapshot). The panel
  sweep (U-obs-1/2/5) is DONE; these are the two remaining pieces that give the snapshot its in-process consumer.
known-conflict: none.
-->
# HANDOFF — UI lane · **observability leftovers: `U-obs-3` (gizmo feed) + `U-obs-4` (smoke reads snapshot)**

> 📌 **Dispatched at `bd18bd596`** *(the joined commit — your panel sweep + the time lane's HN-121 are merged;
> solution builds 0 errors)*. ⭐ Branch **fresh from the coordinator branch** *(rule 7)*; **rule 1b: started-marker
> FIRST.** ⛔ **Scope FROZEN at this sha.** ⭐ ids **`BP-`**, tracker **Area K** *(Panel observability)*.

## 0. ⭐ WHERE YOU ARE

📄 [`DESIGN_UI_Observability_Snapshot.md`](../../DESIGN_UI_Observability_Snapshot.md) §AS-BUILT — **`U-obs-1/2/5`
are BUILT** *(53 panels declare, 48 publish)*. ⛔ Its STATUS names the two that remain: **`U-obs-3`** *(the gizmo
peer feed)* and **`U-obs-4`** *(the smoke suite reading `PanelSnapshot`)* — and notes *"the snapshot currently has
NO consumer."* This batch gives it its **in-process** consumer *(the time lane is building the HTTP one, Group T)*.

## 1. ⭐⭐ THE TASKS

| # | task | design | gate |
|---|---|---|---|
| **U-obs-3** | ⭐ **the gizmo/map PEER FEED** — register **`DebugPrimitiveBuffer.GetFrame()`** into `PanelSnapshot` under a well-known id/`PanelKind`, so a single `DumpAll()` includes the map's primitives beside the panels *(both live in `Fdp.Diagnostics.Contracts` — no new dependency)* | `DESIGN_UI_Observability_Snapshot.md` §Adoption `U-obs-3`; umbrella §"shared substrate" | a test: after a frame, `PanelSnapshot.DumpAll()` carries the gizmo primitives under its id |
| **U-obs-4** | ⭐⭐ **the single-host smoke suite's T2 reads `PanelSnapshot`** — close `DESIGN_Smoke_Suite.md`'s **G-c** *(SUPERSEDED: do NOT build a bespoke panel layer)*. ⭐ The `EditorHarness`-based fixture reads a panel's model via `PanelSnapshot` *(build the VM without drawing — the build/render split makes this possible headless)* and asserts the T2 row-strings | `DESIGN_Smoke_Suite.md` §2 (T1/T2/T3), §G-c/`S3` *(both marked SUPERSEDED)*; umbrella §taxonomy | ≥1 T2 case green: load a fixture, pump frames, read the panel model, assert a field |

⭐ **Obligation ③:** check what you build against both designs' UML; **⑤:** fold any deviation back.
⚠ **U-obs-4 is genuine work, not mechanical** — it decides how the in-process harness obtains panel VMs
*(call `BuildViewModel` directly, vs run a headless frame)*. ⭐ **Hands-on** *(Opus)*; the mechanical part, if any,
can go to a Sonnet subagent, Opus reviewing.

## 2. ⛔ LANE — the two consumers are SEPARATE, don't cross

⭐ **Your surface:** `Fdp.Diagnostics.Contracts` *(the gizmo feed into `PanelSnapshot`)* and the **in-process**
smoke suite *(the `EditorHarness` fixture, `Hrot.ClusterRunner.Integration.Tests`-side / `DESIGN_Smoke_Suite.md`)*.
⛔ **Do NOT touch:** the time lane's **subprocess** harness *(`Hrot/Runner/Hrot.SystemTests`)*, `Hrot.Editor/DebugApi`
*(the `GET /panels` endpoints are the time lane's Group T — do not build them here)*, the engine, the parked Stride
tree. ⚠ A cross-lane edit is STOP-and-report *(`R-128`)*.
⭐ **The two `PanelSnapshot` consumers are complementary:** yours reads it **in-proc** *(U-obs-4)*; the time lane
reads it **over HTTP** *(Group T)*. Both read the same singleton; ⛔ neither modifies the contract.

## 3. ⭐ AFTER THIS

⭐ With `U-obs-3/4` done, the observability programme is complete on the editor host. The **cross-host** half
*(the debug-API read subset on CGF/SimHost + the conformance suite that diffs models by `PanelKind`)* is the TIME
lane's next major batch *(umbrella §Conformance, steps 6–7)* — ⛔ not yours. ⚠ **The `BP-399` tail** *(Q49 option D
→ S4 → S5)* is **parked by the user** — not this batch.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · delta vs base
`bd18bd596` · `--no-build` column · every RED confirmed pre-existing · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on any design
you fold a deviation into · the **`BP-` ids you allocated** · `R-106` verdicts. ⭐ Rule 4/7: re-sync + pull the
coordinator branch around the batch. ⭐ Rule 1b: started-marker before code.
