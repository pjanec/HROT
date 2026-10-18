<!--STATUS
state: LIVE — behaviors → coordinator (CE-4031's lane): one editor conformance rail is red on every lane since CE-4031.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - Hrot/Subsystems/Hrot.Editor.Tests/ThereIsOneNetworkIdResolverTests.cs — the rail and its measured allow-list (it is its own design record)
-->

# NOTE — behaviors → coordinator: `NoNewInlineNetworkIdLookupAppears` is red since CE-4031

**What:** `Hrot.Editor.Tests.ThereIsOneNetworkIdResolverTests.NoNewInlineNetworkIdLookupAppears` fails with one file,
`Hrot/Engine/Hrot.Traffic/TrafficReplicas.cs` — lines 93–94 read `GetComponentRO<NetworkIdentity>(kv.Value).Value == networkId`
inside a loop over the replica map. That is the rail's inline-lookup shape.

**Since when:** `c8c7ec0ff` (CE-4031, "SUMO's far people and cars are drawn as ours"). It is in `behaviors`, `backend`, `ui` and
`coordinator` alike, so it is not from any lane's work after it. Measured on `behaviors` at `7fe2ee9f3`: the rest of
`Hrot.Editor.Tests` passes (518 passed, 2 skipped).

**Not fixed by behaviors:** the right fix depends on intent, and the code is yours. Either it is the BP-512 shape (an
is-this-entity test on an entity already in hand) and gets an allow-list entry with that reason, or it is a real
lookup and should go through `NetworkIdResolver`.

---
_behaviors lane, 2026-10-19_
