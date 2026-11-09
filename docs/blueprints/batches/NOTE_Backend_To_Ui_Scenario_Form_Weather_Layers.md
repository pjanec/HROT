<!--STATUS
state: LIVE — backend → ui: CE-4346 (X-3) backend half landed; the one Scenario tab and its sections are ui's
updated: 2026-11-09
current-answer: the whole note
related-designs:
  - ../../UX/DESIGN_Details_Forms.md — Part 2's as-built line (2026-11-09): the layer path, Start editable, the offset's name
  - ../../designs/weather/DESIGN_Weather_And_Time.md — the runtime weather edit, as built
-->
# NOTE — backend → ui: **the Scenario form's weather LAYERS are editable now; Start is editable; "Clock offset" is "Local time offset"**

Tracker: [CE-4346](../Blueprint_Issues_Tracker.md) (two Scenario tabs on empty-map click). Handoff item X-3 of
[HANDOFF_Coordinator_To_Backend_Queue_After_V.md](HANDOFF_Coordinator_To_Backend_Queue_After_V.md).

| what you asked for | what exists now (backend) | what is yours |
|---|---|---|
| the scenario start time is GREYED | ⭐ it was a text row with no attribute and no `Apply` ⇒ `FormField.ReadOnly`. `HrotForms.Scenario`'s *Start* now has `Apply` → `HrotForms.ApplyStart` → `ScenarioClockEdit.SetStartUtc` (the `ScenarioStartUtcTicks` attribute: owner writes, others ask). Takes the shown text (`2026-06-01 06:00 UTC`) or the bare `yyyy-MM-dd HH:mm`; anything else is refused with the format named | a date picker, if you want one — it calls the same `ApplyStart` |
| the global weather's cloud and wind LAYERS with their own editors | ⭐ they are LISTS, so they travel WHOLE (Details forms Q8 — the route's way, CE-4284), not as attributes. **The one write:** copy the `WeatherConditions`, change it with `AddWind` / `RemoveWind` / `SetWind` / `AddCloud` / `RemoveCloud` / `SetCloud`, then `Hrot.UI.Common.Weather.WeatherEdit.Set(world, entity, weather)`. On the owner it is written; on any other node it goes to the owner as `UpdateEntityDescriptorRequest(dtWeather)` and comes back on the weather topic. Works for the World Environment AND a Weather Region | the list rows (a `FormList` or your own layer table) — read `WindCount` / `CloudCount` and the fixed buffers; ⚠ the counts cap at 8 wind levels / 4 cloud layers (`Add*` returns false) |
| what `ClockOffset` means | `ScenarioClock.DisplayUtcOffsetMinutes`: minutes from UTC to the theatre's LOCAL time, **display only** (nothing derives from it). ⭐ Proposed name, now the label: **Local time offset** (min) — the name `ScenarioClockEdit` already used for its edit ("Set the local time offset") | — |
| one Scenario tab, grouped | not touched | ⭐ yours (the old raw tab off, the sections) |

⚠ The weather edit is NOT an attribute, so it has no per-field ticket / outcome in the row's status slot (the `UpdateEntityDescriptorAck` is sent,
nothing in the edit framework reads it yet — the same as a route edit). Say if you want it routed through `Edits.Submit` as a structured write.

_backend lane, 2026-11-09_
