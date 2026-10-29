<!--STATUS
state: LIVE — coordinator → ui: the armed-mode MVP — finish the unification, then prove record-in-the-editor → film-in-the-Replay-Browser end to end
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../PLAN_Run_Modes_And_Content_Build.md — §4 the MVP scope (what is in, what waits)
  - ../Architect_Question_108_Run_Mode_Picker.md — D12a (R-3068, the armed mode cluster-wide), §2.8 / §2.11a (the run choice and the door, as built)
  - ../../DESIGN_Film_Director.md — the director in the Replay Browser (takes, reels)
  - NOTE_Backend_To_Ui_Armed_Mode_Wire_Built.md — backend's CE-3341 half (on `backend`)
-->
# HANDOFF — coordinator → ui: the armed-mode MVP

**Dispatched at** the commit that adds this line (`coordinator`); scope frozen there. Merge `coordinator` and `backend` first; push an empty
`chore: started armed-mode MVP at <sha>`.

🔒 **User, `2026-10-29`:** *"focus on making the armed mode switching work with whatever is requires, but in 'minimal viable' way - meaning
avoiding not absolutely necessary parts like change detection, autosave. excone retirement can wait. first let's finish the remaining parts of the
unification. bt-window-duel scenario failures are out of scope of this armed mode effort (the goal here is to make it possible to easily record
exercise so we can prepare movie clips in using director in replay browser - replay browser must be able to load the exercise recordings created
from the live run in the editor...)"*

## The tasks, in this order — one Sonnet agent at a time

| # | task | who | done when |
|---|---|---|---|
| 1 | **CE-1441** — the small unification leftovers (two Recent lists, four user-folder JSON writers, the second toast queue, the `Camera.Offset` seeds, the `"View/Settings/…"` literals, the two idle-visibility checks) | Sonnet | each pair is one; the row closed |
| 2 | **CE-3341, ui half** (R-3068, AQ108 D12a) — `RunModeChoice` READS the armed mode from `ClusterView.ArmedMode`; the ONE shared operator load path sends `RunArmedDefaults.For(hostKind)` as a `SetRunSettings{Armed}` before the unchanged load; the indicator and `POST /run-mode` set it through the same op; HTTP loads send none | your own model for where the send sits, Sonnet for the rest | every host shows the same armed mode; a CGF-started load arms Live and an editor-started one Preview on every host; `/run-mode` changes it everywhere |
| 3 | ⭐⭐ **CE-4102 — the MVP, proven end to end** (below) | your own model drives it; Sonnet for the rail | the chain below works, automated and live |

### CE-4102 — the chain that must work

```
editor: open a scenario → arm Live (the indicator) → ▶ → the run records (Record live runs ON, the default)
      → ■ → the exercise is on disk
Replay Browser (a separate process): lists that exercise → opens it → the director plays a take on it
```

| step | what to check / measure first | likely owner of a break |
|---|---|---|
| the recording lands | where the editor's live run writes (`ReferenceLiveLoadHandler` storage, `OrchestrationConstants.ResolveStagingRoot()` → `{FDP_STAGING_ROOT or tmp}/FDP_Temp`) | backend |
| the Replay Browser finds it | it scans `RecordingsRoot = ResolveStagingRoot()` via `RecordingGroupInventory.Scan` — ⚠ the same root by code, ⛔ **never proven end to end** | ui |
| it opens and plays | the recording's scenario (CE-4091's copy, `ContentLayout.Opened`) and terrain (CE-4089 staged from the NAS) resolve on the Replay Browser | backend for paths, ui for the open |
| the director films it | a take and a reel play on it (`DESIGN_Film_Director.md`) | ui |

- **A rail** through the real hosts (headless editor + the Replay Browser subsystem over one staging root) asserting each step.
- **A live run** of the two processes (`run_Editor` + `run_ReplayBrowser`, or HTTP) — the report names the exercise id and the take played.
- **A break in backend's code** ⇒ a NOTE to backend with the measurement; backend fixes it (it is on call for exactly this).

## ⛔ Out of scope for now (user) — do not start

CE-4099 autosave · any further change-detection work · CE-4100 ① ③ (the keep/discard prompt, the DAR start default) · CE-3339 (the two hashes for
AAR → Preview) · CE-4095 · CE-4092 · the ExCon retirement (R-3067) · CE-3342 (`bt-window-duel`).

Report per task with the gate contract (CLAUDE.md, Rule 8); fold any deviation into the owning design.

_coordinator lane, 2026-10-29_
