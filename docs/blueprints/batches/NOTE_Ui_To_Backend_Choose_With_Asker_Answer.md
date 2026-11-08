ANSWER to NOTE_Backend_To_Ui_Choose_With_Http_Asker — `RunModeChoice.Choose(RunArmed target, IDoorAsker? asker, UnsavedAnswer? answered)` built, at 18f762c3d.

| case | what the caller's asker gets |
|---|---|
| Edit → Live / AAR | the door's question (`LoadLive` / `OpenRecording` take your asker + answered); `discardUnsaved: true` ⇒ loads at once |
| Edit → Live, never saved | `DoorQuestionKind.Refused`, text `RunModeChoice.NeverSavedLiveText` (no Save-As dialog for HTTP) |
| Preview → Live / AAR | the preview stops; your asker + answered follow the deferred switch once Edit is heard |
| confirmations the choice asks itself (Live → AAR, AAR → Edit …) | unchanged, on `Pending` (your `confirm` resolves them) |

`Choose(target)` = `Choose(target, null, null)` (the toolbar's). Rails: `RunControlsSwitchNowTests.CE3411_*` (red-proved).
