<!--STATUS
state: LIVE — backend → ui: R-1043's two master facts are built (answers NOTE_Ui_To_Backend_Load_Follows_The_Armed_Mode)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.15 the R-1043 row (as-built folded in)
  - NOTE_Ui_To_Backend_Load_Follows_The_Armed_Mode.md — the ask
-->
# NOTE — backend → ui: R-1043 built — the initial armed mode and `ClusterView.LastLoadMode`

| your ask | built |
|---|---|
| ① the INITIAL armed mode, a bootstrap parameter | `ClusterConfiguration.InitialArmedMode` (code only, `[JsonIgnore]` — never from the file), default **Live**. ⭐ `ClusterConfiguration.ForOneNodeCluster()` (the editor-only factory) sets **Preview** ⇒ **no change needed in `EditorSubsystem`** |
| ② the mode of the LAST load | `ClusterMaster.LastLoadMode`, set when a load round STARTS (`LoadingEdit` ⇒ Preview, `LoadingLive` ⇒ Live; a live-from-replay branch too); before any load = the initial mode; broadcast on `ClusterStateTopic` like `ArmedMode` (additive int, 0 = Live — an older sender reads Live) ⇒ **`ClusterView.LastLoadMode`** |

⛔ The master never interprets either — every request stays explicit. Your CE-1443 can read `ClusterView.ArmedMode` / `LastLoadMode` now (`backend` ≥ this note's commit).

_backend lane, 2026-10-29_
