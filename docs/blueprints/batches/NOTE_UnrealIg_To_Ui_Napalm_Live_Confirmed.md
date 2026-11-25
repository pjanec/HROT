# Note: the napalm strike works live through the gateway and Unreal — thank you

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-25 |
| **For** | `ui` (and backend, CE-11057) |
| **Answers** | [NOTE_Ui_To_UnrealIg_Napalm_Live_Fixed.md](NOTE_Ui_To_UnrealIg_Napalm_Live_Fixed.md) |

Re-run with the same recipe ([NOTE_UnrealIg_To_Ui_How_We_Run_Scenarios.md](NOTE_UnrealIg_To_Ui_How_We_Run_Scenarios.md) §1) on HROT `ui` `c6ed1c7e9`,
gateway `973f034` repackaged against that build, the Unreal client on `basic_desert_look`.

| what the client received | when (wall clock after play) |
|---|---|
| the Mk 77 round (munition 6071) | ~19 s |
| `FireSpots`: holder 1008 (1 spot) and 1009 (2 spots), r 15 m, 300 s | ~27 s |
| `PersonState.Burning` on Line Man 1, 2, 3 | ~27 s, cleared ~6 s later |

Drawn: the fireball, the three men burning, the ground fire burning on after them.

**Still as you said:** the Late Man stops at the Far Car (599, 351) and never walks into the fire. `va-pop-smoke` not re-run (still open on your side).

**One thing for whoever rebuilds HROT next to a packaged gateway:** after moving HROT to `c6ed1c7e9`, HROT refused the extension built against
`037e62a8d` (`[Extensions] extension 'simnetgateway': refused - … built against Hrot.Common '0.1.1+037e62a8d…'`). Correct behaviour —
just note that every HROT rebuild needs the gateway repackaged.
