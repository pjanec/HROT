<!--STATUS
state: LIVE — ui → coordinator: the armed-mode MVP report (your HANDOFF_Coordinator_To_Ui_Armed_Mode_MVP.md, tasks 1–3)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
known-rot: task 3's AUTOMATED rail is still being written when this note is pushed — its row says PENDING; a follow-up commit fills it
related-designs:
  - HANDOFF_Coordinator_To_Ui_Armed_Mode_MVP.md — the ask (scope frozen at 27acb9b6e)
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.14 CE-3341 ui half (design + as-built)
  - ../../RUNBOOK_Filming.md — §0b the record-then-film HTTP sequence (new)
  - NOTE_Backend_To_Ui_Armed_Mode_Wire_Built.md — backend's CE-3341 half, built against
-->
# NOTE — ui → coordinator: the armed-mode MVP

**Started** at `22153b0ad` (merged `coordinator@27acb9b6e`, `backend@5a8c81438`; conflicts in the tracker id lines, AQ108's `updated`/D-rows, one test,
`ScenarioNewAssetService` — both sides kept). One Sonnet agent at a time throughout.

## The three tasks

| # | task | state | where |
|---|---|---|---|
| 1 | **CE-1441** (small unification leftovers) | ✅ done, row closed | `93a8fc7e0` — one Recent store · `SafeTextFile` + `UserFiles` for the 5 user-file writers (old files still load) · `SettingsMenuPath` everywhere · `MapCamera` centres a never-placed offset (3 host seeds deleted) · `RunModeToast` → `EditorNotification` on a `ToastQueue` (⚠ the choice's OWN queue — joining the editor's waits for the toolbar to draw it) · one `IsAllowedInIdle` |
| 2 | **CE-3341 ui half** (R-3068: the armed mode is one cluster fact) | ✅ done | design + as-built [AQ108 §2.14](../Architect_Question_108_Run_Mode_Picker.md); `4f286cd08` |
| 3 | **CE-4102** (record in the editor → film in the Replay Browser) | ✅ **proven LIVE** · ⏳ automated rail PENDING (Sonnet, in progress) | below; [RUNBOOK_Filming §0b](../../RUNBOOK_Filming.md) |

## CE-3341 ui half — decision

`RunModeChoice.Armed` reads `ClusterView.ArmedMode` (through `IRunTransport.ClusterArmed`); every local set is ONE `Arm()` that sends
`SetRunSettings{Armed}`; the operator's open (the door's new `OperatorOpening`, raised only when the BOUND asker answers) sends the host default
(editor Preview, CGF Live), a live load Live, a recording AAR; HTTP opens pass their own asker ⇒ send none (D22); the per-host D12 reset on
`OperatingEdit` is deleted. ■ in Live uses the raw `OpenForEdit` ⇒ Live stays armed (R2).
*Rejected:* sending from `ScenarioLoadSequence` — HTTP uses it, so every agent load would re-arm · keeping the per-host reset beside the op — two writers.
⚠ **Correction to D12a:** "same writer ⇒ ordered" is not needed — the egress reads `TransitionStateIntent` before `SetRunSettingsIntent`
in one frame (`ClusterOpEgressTranslator.cs:91,187`), and that is harmless because a load never resets the master's armed mode
(`ClusterMaster.RunSettings.cs:62`).
⚠ **Known gap (accepted for the MVP):** before the first arm/open, each host shows its OWN default (the editor Preview, the cluster Live —
measured live: `/status.armedMode` preview vs `/run-settings.armed` live); they agree from the first open onwards.

## CE-4102 — the live proof (two processes, one `FDP_STAGING_ROOT`)

| step | measured |
|---|---|
| editor `--mode editor` :8120 · open `hill-attack` · `POST /run-mode {armed:live}` | `/run-settings.armed` followed preview → live (the op round-trips through the master) |
| `POST /scenario/load/live` | `OperatingLive`, `recording:true`, 103 s sim |
| `POST /sim/stop` (■) | `OperatingEdit` at the start, `armedMode` still **live** (R2); `nodes/node-1/exercises/b79a28f5-…/node_1.fdp` + `.meta.json` (ScenarioName hill-attack) |
| Replay Browser `-m replaybrowser` :8121 · `GET /exercises` | listed the exercise — ⚠ with `scenario: null` ⇒ **fixed** (`26960d56f` + `3f7785c24`: `RecordingGroup.ScenarioName` from the sidecars) |
| `POST /replay {exerciseId}` | `OperatingReplay`, 33 entities |
| `POST /director/play {scenario, recording, take:{2 shots}}` | `Done`, report `ok`, `problems: []`, cuts on shots 1 and 2 with the recorded units (M1 Abrams, Tank Platoon) — **exercise `b79a28f5-365e-4c50-9a44-0dfc838754de`, take `_inline`** |

⚠ The run had no combat (`/director/moments` empty), so the take is time-anchored, not drafted; the live run was started by the HTTP live load
(an HTTP `/sim/play` from Edit is the raw dry run, D22) — the UI ▶ in Live armed is the automated rail's job. **No break in backend's code** — no
NOTE to backend was needed.

## Gates (Rule 8)

| gate | command | result | base |
|---|---|---|---|
| post-merge build | `dotnet build <Presentation.Tests / Editor.Tests / ClusterRunner.Tests> --no-restore` after `dotnet restore` | 0 errors ×3 | — |
| CE-1441 (Sonnet's run, `--no-build` after building each test project) | Presentation.Tests · Fdp.Presentation.Tests · NodeEditor.UI.Tests · AiShared.Tests · IG.Tests · SimHost.Tests · ClusterRunner.Tests · Editor.Tests | 1136/0/59 · 657/0/1 · 170/0 · 2178/1/10 · 456/0/1 · 2054/0 · 383/2/0 · 675/2/3 | reds pre-existing: AiShared `CE1150_TheBundle…`, ClusterRunner `EverySystem…` ×2, Editor `CE4059_P3_*` (load-flaky, passes alone) + `CE4101_Create_FromSeed…` ⇒ **mine, from the backend merge** (recipe rename) — fixed `1fb19602d`, `ScenarioNewAssetTests` 10/0 |
| CE-3341 (Sonnet's run) | Presentation.Tests · Editor.Tests · Orchestrator.Tests · ClusterRunner.Tests | 1140/0/59 · 674/3/3 · 377/0 · 383/2/0 | Editor's 3 reds (`TheUnsavedCheckRoutesTests.CheckUnsaved_…`, `TheRunRecordingGatesAnswer409Tests…`, `CE4059_P3_*`) pass alone (18/18) — load-flaky |
| CE-3341 integration (`TheRunChoiceIsOnEveryHostTests`) | run as a class: 4/9 red `DdsException: Failed to create participant` | ⭐ **re-run each alone (me): 4/4 green** ⇒ DDS participant contention, not the change | — |
| CE-4102 scenario name | `Fdp.Toolkits.Tests --filter RecordingGroupInventory` · `Hrot.ReplayBrowser.Tests` | 3/0 (red-proved: the group built without `scenario` ⇒ the new rail fails) · 67/0/5 | — |
| CE-4102 rail | ⏳ PENDING | | |
| mermaid | `scripts/mermaid-check.mjs` AQ108 · RUNBOOK_Filming | 26/26 · 2/2 | — |

**Ids allocated:** none (CE-4102's rail lands under its own row). **Tracker:** CE-1441 closed; CE-3341 / CE-4102 rows are yours/backend's —
not ticked here.

_ui lane, 2026-10-29_
