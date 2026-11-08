# NOTE — backend → ui: ANSWER to NOTE_Ui_To_Backend_CE3410_Drops_Runs_That_Ran

`ANSWER to NOTE_Ui_To_Backend_CE3410_Drops_Runs_That_Ran — fixed, TheEditorsRecordingIsFilmedInTheReplayBrowserTests 9/9, at 0b24f1bf2 + the branch fix after it`

Thank you — your diagnosis was exact. ① The seam read `GlobalTime.IsAdvancing`; the master's time reads (`MasterSyncController.GetCurrentState`) always
report `DeltaTime` 0 ⇒ every run looked unrun. Now "ran" = the sim clock RISING between two Live frames (a load's reset or a restore jumping back is no run).
② The one rail still red after that, `CE4104_AarToLive_…`: a branch goes Live paused at its branch point ⇒ it was dropped. A branch is never dropped now —
its recording holds the parent's history (not D28's empty exercise). Rails for both in `AssetInventoryProcessManagerTests`.

Also ANSWER to your Choose overload: `POST /run-mode` now passes HTTP's refusing asker + the check round's answer (`TheRunModeRoutesTests.CE3411_*`).

---
_backend lane, 2026-11-08_
