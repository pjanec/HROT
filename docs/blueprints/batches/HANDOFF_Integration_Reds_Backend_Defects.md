<!--STATUS
state: LIVE
build-state: FRAME — BACKEND lane. Investigation-led defect batch (no new-capability UML needed).
  Close the BACKEND-OWNED refiled integration reds; the cross-lane ones stay refiled.
updated: 2026-08-26
current-answer: this handoff is the FRAME. The refile table with root causes + design citations lives in
  REPORT_Integration_Reds_Triage.md §3 and TESTING_Harness_And_Goldens.md §8.3.
known-conflict: ⛔ QA-023 (blueprint serialization) is now the MCP lane's — do NOT touch BlueprintStateTranslator.
-->
# FRAME-HANDOFF — **Close the backend-owned refiled integration reds** *(BACKEND lane, `QA-`)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm)*. ⭐ Rule 7; **rule 1b started-marker before code.** ⛔ No PR.
> ⭐ Continue `QA-` ids, tracker Area N; you allocate them *(rule 5)*.
> ⭐⭐ **Investigation-led** *(like QA-013)* — root-cause on a clean base, fix, **inverse-edit red-prove**. This is a
> DEFECT batch, ⛔ not a new-capability design — no UML doc required unless a fix introduces a real new structure.

## 1. ⭐⭐⭐ THE FRAME — your targets *(the backend-ownable refiled reds)*
📄 Root causes + design citations already written: **[`REPORT_Integration_Reds_Triage.md`](REPORT_Integration_Reds_Triage.md) §3** + **`TESTING_Harness_And_Goldens.md` §8.3**. Take these:
| id | n | the frame |
|---|---:|---|
| ⭐⭐⭐ **`QA-017`** | 7 | **cluster transition 2PC never leaves state `0`.** ⚠ Two causes already refuted *(roster IS populated; bootstrap latch is NOT the gate)*, and `MCP_Integration.md` §Group U AS-BUILT shows `--mode all` reaching `OperatingLive` ⇒ ⭐ **suspect the IN-PROCESS integration harness's drive, not the state machine.** Biggest bounded group |
| ⭐⭐ **`QA-024`** | 3 | **EQS phase machine never reaches `_AwaitingRaycasts` / never publishes.** Design: `docs/designs/eqs-2/EQS_Design_v1.3_final.md` |
| ⭐ **`QA-022`** | 3 | map/area authoring — creation tool never activates / `EditablePolyline` never attaches. ⚠ **measure if this is presentation (UI-lane) before fixing** — if it touches viewport-tool production, STOP and coordinate |

⛔⛔ **NOT yours this batch — stay refiled / their owners:** `QA-020` *(17 replication — CROSS-LANE Axis-B)* · `QA-019` *(editor `SwitchToExternal` — UI neighbour)* · `QA-021` *(MissionControlRequest→DDS — MCP neighbour)* · `QA-023` *(blueprint serialization — **the MCP lane owns it this batch**)* · `QA-026` *(EcsPatchContext — Q59/Axis-B owner)* · `QA-025` *(9 environmental — ⛔ NOT defects, do not "fix")*.

## 2. ⭐ APPROACH *(you own the how)*
- Root-cause each on a **clean base worktree** *(never infer from a diff)*; the fix is proven through the rail that reddened, ⛔ not a full-suite re-run *(the T2/T3 rules)*. Build the AFFECTED project only; build once then `--no-build`.
- ⭐ **A red you cannot fix in-lane is a re-file, not a shrug** *(R-131)* — hand it back with a repro + the owning lane. ⛔ No new skip/quarantine.
- ⚠ If a target turns out CROSS-LANE once measured *(e.g. QA-022 → viewport)*, STOP that ITEM and report *(R-106)* — do the others.

## 3. ⭐ ACCEPTANCE
Each taken red: fixed + red-proved, OR re-filed with a repro + owner. The integration suite still finishes + is stable across repeats; base-sha stated per pre-existing; gates *(rule 8)*: counts, `--no-build`, `tracker-counts.py`, `rulings-check.py`, the `QA-` ids. Fold root causes into `TESTING_Harness_And_Goldens.md` §8.
