# Main Toolbar 2 — Task Tracker

**Detail:** [TASK-DETAIL.md](./TASK-DETAIL.md) · **Design:** [DESIGN.md](./DESIGN.md) · **Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)
**Process:** one task = one batch (Zoo focus); sequential T1→T7; gate = build 0-warn + `Failed: 0` (no regen flag) +
lead hard-review. Status: `[ ]` todo · `[~]` in progress · `[x]` done.

## Phase — File Operations & Toolbar Polish

- [x] **MTB2-T1** Generic icon UX: 90% inset + hover/toggle + `ComputeIconRect` test — Item 4 — **BATCH-30** (`pro`) [details](./TASK-DETAIL.md#mtb2-t1)
- [x] **MTB2-T2** Save icon in MainToolbar (`shell.save`, next to Open Asset) + `shell/save` cell — Item 2 — **BATCH-31** (`pro`) [details](./TASK-DETAIL.md#mtb2-t2)
- [x] **MTB2-T3** `Func<string>? DynamicDisplayName` on `EditorCommandDescriptor` + menu/toolbar adapters — Item 3 — **BATCH-32** (`pro`) [details](./TASK-DETAIL.md#mtb2-t3)
- [x] **MTB2-T4** Active-save-target resolver + `Save Scenario`/`Save Scenario As` + dynamic Save label — Item 3 — **BATCH-33** (`pro`) [details](./TASK-DETAIL.md#mtb2-t4)
- [x] **MTB2-T5** Unified File menu + perspective display-label "Scenario" (no key rename) — Item 3 — **BATCH-34** (`pro`) [details](./TASK-DETAIL.md#mtb2-t5)
- [ ] **MTB2-T6** `RecipePickerSource` (per-kind recipes incl. "Empty") — Item 1 — **BATCH-35** (`pro`) [details](./TASK-DETAIL.md#mtb2-t6)
- [ ] **MTB2-T7** `NewAssetLauncher` + File/New + New toolbar button; retire `RecipeCreateModal` wiring — Item 1 — **BATCH-36** (`pro`) [details](./TASK-DETAIL.md#mtb2-t7)

**Dependencies:** T3 → T4 (dynamic label needs the descriptor field); T4 → T5 (menu wires the save/scenario commands);
T6 → T7 (launcher needs the source). T1, T2 independent (do first).

**Progress:** 0 / 7 done.

## Done definition
- **Task done** = its TASK-DETAIL success conditions met, verified by the lead against the diff + an independent test
  run (no regen flag), tracker box flipped, batch committed (one batch per commit).
- **Workstream done** = all 7 `[x]`, lead runtime checks pass, all DEBT-TRACKER items resolved.
