# NOTE — backend → ui: a `Choose` overload that takes the caller's door asker — REQUEST

`REQUEST — RunModeChoice.Choose(RunArmed target, IDoorAsker? asker, UnsavedAnswer? answered), for backend's B-5 (CE-3411); I continue with the rest of B-5 and your checkpoint-label request meanwhile`

**Why.** Since R-1, `POST /run-mode {armed: live|aar}` from Edit reaches `SwitchToLive` / `SwitchToAar` → `_door.LoadLive(…)` / `OpenRecording(…)` with the
choice's OWN bound asker (the operator UI's `ConfirmingAsker`) and no `answered:` ⇒ the door starts the cluster's unsaved check round and its
Save / Discard / Cancel appears in the operator's UI a few frames AFTER the route already answered 200. ⛔ An HTTP caller must never leave a question
standing in someone else's UI; ⭐ every other HTTP open passes HTTP's refusing asker (`DebugApiService.HttpAsker(gate)`, CE-4101) and an `answered`
(`gate.Answer ?? Unchanged`), so the door decides at once: a run stopped, a loss refused with the 409 text, `discardUnsaved: true` accepted.

**The ask** (your file — the handoff gives ui the toolbar's `RunModeChoice`):

| member | behaviour |
|---|---|
| `Choose(RunArmed target, IDoorAsker? asker = null, UnsavedAnswer? answered = null)` | as today; `asker` / `answered` (when given) are handed to the door calls `SwitchFromEdit` makes (`LoadLive`, `OpenRecording`, `Open`) instead of the bound ones. ⛔ No other change. The confirmations `Choose` asks ITSELF (Live → AAR "Ends the live session…") stay on `Pending`, which the route already resolves with `confirm` |

⭐ Once it exists I pass `HttpAsker(gate)` + `gate.Answer` from `SetRunMode` and answer 409 with `asker.Refusal`. Until then the route keeps today's
behaviour (it switches; an unsaved question lands in the UI).

---
_backend lane, 2026-11-08_
