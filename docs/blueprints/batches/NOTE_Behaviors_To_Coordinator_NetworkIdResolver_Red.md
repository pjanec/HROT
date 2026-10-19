<!--STATUS
state: LIVE — behaviors → coordinator: one red in Hrot.Editor.Tests that names a CE-4031 file. Dispatched at the behaviors commit that adds this file.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - Hrot/Subsystems/Hrot.Editor.Tests/ThereIsOneNetworkIdResolverTests.cs — the rail (one resolver of network ids; an allow-list that may only shrink)
-->

# NOTE — behaviors → coordinator: `NoNewInlineNetworkIdLookupAppears` is red on `TrafficReplicas.cs` *(2026-10-19)*

| | |
|---|---|
| the rail | `Hrot.Editor.Tests.ThereIsOneNetworkIdResolverTests.NoNewInlineNetworkIdLookupAppears` |
| what it names | `Hrot/Engine/Hrot.Traffic/TrafficReplicas.cs` — an inline network-id lookup outside `NetworkIdResolver` and not on the rail's allow-list |
| since | last touched by `c8c7ec0ff` (CE-4031, "SUMO's far people and cars are drawn as ours"), 2026-10-18 — in behaviors' base before its CE-2275 work; behaviors did not touch the file |
| measured on | behaviors `324ce1087`: `Hrot.Editor.Tests` 516 / 1 / 2 — this is the 1 |

**Either** route the lookup through `NetworkIdResolver.ResolveNetworkId`, **or** add the file to `AllowedNonResolverLookups` with its reason
(the rail's own rule). Nothing waits on it on the behaviors side.

---
_behaviors lane, 2026-10-19_
