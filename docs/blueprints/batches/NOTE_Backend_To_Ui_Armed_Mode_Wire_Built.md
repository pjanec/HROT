<!--STATUS
state: LIVE — backend → ui: CE-3341 row ① is BUILT — the armed mode is one cluster-wide fact, held by the master and readable on every node
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
supersedes: the "when ① lands" promise in NOTE_Backend_To_Ui_Armed_Mode_Cluster_Wide.md (rows ② – ④ there are still ui's)
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — D12a (amended with the as-built line), D22 (HTTP stays explicit)
  - ../../DESIGN_Cluster_View.md — the node's ClusterView, where `ArmedMode` now lives
  - ../RULINGS.md — R-3068
  - NOTE_Backend_To_Ui_Armed_Mode_Cluster_Wide.md — the request this answers
-->
# NOTE — backend → ui: the cluster-wide armed mode is built (CE-3341 ①)

| what | where / value |
|---|---|
| ⭐ the enum | `Fdp.Toolkit.Orchestration.ClusterArmedMode` (`FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/ClusterArmedMode.cs`): **`Live = 0`, `Preview = 1`, `AAR = 2`** — ⛔ permanent wire values (R-42). It lives in the toolkit because `RunArmed` (`Hrot.Presentation`) is out of the toolkit's reach: ⭐ map **by name** (`Enum.Parse<RunArmed>(mode.ToString())` — same three names), ui may later drop `RunArmed` for it. Live is 0 so a sender that never sets it reads as the default |
| ⭐ READ it | **`ClusterView.ArmedMode`** — on every node (master process included); `ClusterSlave.ArmedMode` and `ClusterUiCache.ArmedMode` are views onto it (no second copy). `Live` before any broadcast |
| ⭐ SET it | the ClusterOp **`SetRunSettings`** (wire value 24, unchanged). In-process: publish `SetRunSettingsIntent { RequestId, Armed = ClusterArmedMode.X }` (`RecordLiveRuns` is now `bool?` and `Armed` is `ClusterArmedMode?` — **absent = no change**; an old sender changes nothing). Payload JSON: `{"Armed":"Preview"}` (names, not numbers). One op may carry either field or both |
| master | `ClusterMaster.ArmedMode`, default **Live** (D12: Live everywhere but the editor, which sends Preview with its load). ⛔ never reset by a load; republished on the cluster-state broadcast **at once** when it changes |
| wire | `ClusterStateTopic.ArmedMode` (int; additive; 0 = Live, an unknown value reads as Live); encode `ClusterOpMasterTranslator.StateTopicOf`, decode `ClusterFactsIngress.StateOf` |
| HTTP | `GET /run-settings` → `{recordLiveRuns, armed}` (`armed` = `preview` \| `live` \| `aar`, the CLUSTER's mode — not `/status.armedMode`, which is still the host's `RunModeChoice` until ui's ②); `POST /run-settings {armed?, recordLiveRuns?}` (at least one; 400 otherwise) |
| ⛔ untouched | `POST /run-mode` and `RunModeChoice` (ui's); **no load route sends an armed mode** (D22) — the only production `new SetRunSettingsIntent` is the `/run-settings` route |

⚠ One behaviour change: an **unreadable** `SetRunSettings` payload used to mean "recording ON"; it now changes nothing (the op carries two settings, so a no-op is the safe reading).

ui's rows ② – ④ (RunModeChoice reads the view, the shared operator load path sends `armed` first, the indicator and `/run-mode` go through the op) are unchanged and can be built against this now.

_backend lane, 2026-10-29_
