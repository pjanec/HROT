<!--STATUS
state: LIVE — coordinator → ui: the start perspective, the mode picker and the picker's Recent section
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../PLAN_Run_Modes_And_Content_Build.md — the plan; §2 holds each task's "done when"; §1 the order
  - ../Architect_Question_108_Run_Mode_Picker.md — R-4098: §1 the three rules, §1a the words, §2 the UML, §3 every move, §4 the decisions
  - ../../DESIGN_Content_Layout.md — R-4097: the folders the start surface and the picker list (backend builds it first, CE-4089)
-->
# HANDOFF — coordinator → ui: the start perspective, the mode picker, Recent in the picker

**Dispatched at** the commit that adds this line (`coordinator`) — ⭐ RE-STAMPED `2026-10-28` before either lane started (rule 1a; ancestry checked): the explicit HTTP surface (D22) adds `POST /sim/stop`, `POST /run-mode` and `GET /status.armedMode` to CE-4097. ⭐ **Your scope is FROZEN at that sha** — documents that change after it are FYI
only; if one invalidates a task, STOP that task and report it. Merge `coordinator` first and push an empty `chore: started run-modes at <sha>`
commit. ⛓ **Wait for backend's CE-4089 (the content move) before CE-4096 and CE-4098** — merge `backend` when its CE-4089 commit is pushed.

## The tasks, in this order

| # | task | design | waits on (backend) |
|---|---|---|---|
| 1 | **CE-4098** *Recent* first in every open picker (the user folder) + `AssetKind.Recording` + File › Open Recording | AQ108 D19 | CE-4089 |
| 2 | **CE-4096** the start perspective: the shared start-surface window claiming `Start`, the start gate in the shared presentation layer (the cluster RESTING in Idle — never an Idle a trajectory passes through), `WindowManager.SwitchPerspective` refuses while `Start`, the *allowed in Idle* menu flag, the waiting variants, the host's armed mode as ONE bootstrap parameter (editor Preview; CGF / all-in-one / server Live) | AQ108 D12, D16 | CE-4089, CE-4094 |
| 3 | **CE-4097** the mode picker: the indicator (dim / bright / blink; Live red recording, green not), every move of AQ108 §3 with its confirmation, ■ per rule R2, the toasts, Save / Ctrl+S = checkpoint in Live and AAR, the status-bar note while a run goes, the File-menu fold, the timeline's right-click menu, the words Preview / Live / DAR / AAR | AQ108 §1–§3, D1, D3 (the ORDINARY load path — no continue-world flag), D5, D10, D11, D17, D21, D22 (`POST /sim/stop` = the same Stop as ■, `POST /run-mode`, `/status.armedMode`) | CE-4090, CE-4091, CE-4093 |
| 4 | **CE-4083** remove the status-bar time control | the tracker row | CE-4097 |

⭐ Each task's **done when** is in the plan §2. Rails drive the headless editor (`SubsystemConfig { Headless = true }`, as
`TheEditorRunsTheOrchestratorCoreTests` does) — no Xvfb needed for the model; frame rails for what is drawn.

⭐⭐ **Sonnet or your own model — see the plan §2a** (user, token thrift; a SUGGESTION): it marks each task, or each part of a task, as Sonnet-suitable or not. Brief Sonnet with the task's *done when* as success conditions; review its tests and diff, do not re-run them; verify any *"cannot / does not exist"* claim.

## Rules that bind every task

- **One implementation per concept** — the start gate lives where every host passes through (the shared presentation layer), never per host; the
  picker's Recent is one list per kind used by every picker; the confirmations are one dialog shape.
- **Fold the as-built back into AQ108** before a task closes (obligation ⑤).
- **Tests:** the feature's own suite first (T-1); each new rail red-proved by a compiling mutation.
- **Report per task** with the gate report contract (CLAUDE.md, Rule 8), and the tracker ids you closed. Push after each task.

_coordinator lane, 2026-10-28_
