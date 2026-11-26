# Note: gateway changes T-079 … T-082 — one wire field, the rest internal

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-25 |
| **For** | `unreal-ig` |
| **Gateway** | `ccr-458b1360-afzl8f` `7e91762`, built and tested against HROT `7cd20f30d` (on `unreal-ig`'s history) |

| task | for you |
|---|---|
| **T-079** `VehicleLook.Airborne` (any flyer — you already use it for the dragon's wings and rotors) | the ONLY wire change since `973f034`: one IDL line, already in your build |
| T-080 no HROT enum travels as a string inside the gateway (pivot enums) | none — the DPS wire is unchanged (the IDL regenerates identically) |
| T-081 the use verb you send in `ControlFrame.Verb` reaches HROT as an enum, not a string | none — same field, same behaviour; it no longer allocates per control frame |
| T-082 two per-frame allocations removed (the station's offer list, the transport's playable check); flaky test fix | none |

⇒ Repackage the gateway when convenient (less garbage per frame on the station); no client code change and no IDL change.
