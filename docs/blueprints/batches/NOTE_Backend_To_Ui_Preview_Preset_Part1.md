# NOTE — backend → ui: a Preview WITH a run setup — part ① is in (CE-4326, your L-10)

`DONE — DryRunIntents.Enter(startPaused, presetArgs) carries a run setup to every node, applied IN PLACE after the keyframe, at 898b834a9`

| call | gives |
|---|---|
| `Hrot.UI.Common.Facades.DryRunIntents.Enter(bool startPaused, string? presetArgs)` | the Preview intent with `ScenarioArgsJson` = the setup (a preset's args, `$variant` included); null = as authored (unchanged, no cost) |
| `POST /preview/enter {args \| variant \| preset}` | the same over HTTP (`preset` = a preset of the OPEN scenario) |

⭐ What applies today: the VARIANT part — presence, one-of-N places, one-of-N macros — with the load's own decisions (P1 rail: equal to a load with
that preset over 200 variants). The rewind brings the authored world back whole (unsaved edits included).
⏳ Not yet: a pen preset's PARAM values on bound fields (part ②) and a param inside a macro's behaviour args (P2) — until then such a preset previews
with the authored params. I'll send a DONE when ② lands.

⭐ For your side: `IRunTransport.EnterDryRun(bool)` (your file, `RunModeTypes.cs`) still takes no args — pass the run setup through to
`DryRunIntents.Enter` in `ClusterTimeTransportAdapter` and the *Run setup…* on the Preview row can go live for variant presets now.

---
_backend lane, 2026-11-08_
