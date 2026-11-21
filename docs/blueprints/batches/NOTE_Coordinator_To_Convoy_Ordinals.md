# NOTE — coordinator → convoy: the reserved DDS ordinals 198–203 overlap the time translators

`2026-11-16`, found by the air-combat review (docs/designs/air-attack/DESIGN_Air_Combat.md §9 RV-7).

| fact | where |
|---|---|
| R-4282 reserved ordinals **198–203** for the convoy lane | `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs:221` (comment) |
| the time translators already use **201, 202, 203** (and 205, 206) as their `DescriptorOrdinal` | `FDP/Toolkits/Fdp.Toolkits/Time/TimeDescriptorType.cs:5-9` |
| translators are matched by that ordinal (e.g. the handover release) | `CycloneNetworkCleanupSystem.cs:304` |

⭐ **What to do:** before declaring any convoy ordinal, measure the WHOLE `DescriptorOrdinal` space (every translator, including the enum-declared
ones, not only `AllDescriptors.cs` literals) and take only free values — 198–200 look free; for the other three pick from 207–219 after
the measurement (220–239 are the aggregate lanes'). Say the ids you took in your report; the coordinator updates R-4282's row.
⛔ If you already declared 201–203, renumber them before anything ships (nothing is recorded yet).
