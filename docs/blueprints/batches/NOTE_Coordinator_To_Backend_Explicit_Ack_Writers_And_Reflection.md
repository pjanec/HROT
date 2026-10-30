<!--STATUS
state: LIVE — coordinator → backend (behaviors for CE-4108 ② ⑤): CE-4107 the ack writers declared + railed, CE-4108 the other fragile reflection
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12f row 37 (W3: the node waits for its writers' acks before "prepared" / "seek done") — fold CE-4107's as-built there
  - ../Blueprint_Issues_Tracker.md — CE-4107, CE-4108 (the full measurements and leans)
-->
# NOTE — coordinator → backend: declare the ack writers (CE-4107); the other fragile reflection (CE-4108)

🔒 **User, `2026-10-30`:** *"What is DiscoverAckWaiters for and why does it use fragile and ugly reflection scan instead of interface based
marking"* · *"Cand every translator simply provide the list of ack waiters somehow, so irnis all explicit? And checked by rail that each does?"* ·
*"Yea. Ans check for other similar ugly fragile reflecrion scans"*

## CE-4107 — explicit, and a rail that each translator declares (the user's shape)

| # | what |
|---|---|
| 1 | `IDescriptorTranslator.AckWaiters { get; }` — ⛔ **no default body**: every translator must answer at compile time; one without writers returns an explicit empty list |
| 2 | `AckWaiter.Of(name, writer)` wraps a `DdsWriter<T>` or the HROT adapter into a `Func<TimeSpan, bool>` — the generic is resolved at compile time; ⛔ no CycloneDDS.NET change needed |
| 3 | the writers held OUTSIDE translators (`PeerLifecycleStatusEgressSystem`, `UpdateEntityAttributeRequestSystem`, `DdsLiveWriter`, the gateways …) implement the same small contract; the node gathers them — no `RegisterAckWaiter` to remember |
| 4 | `CycloneNetworkCleanupSystem` collects the declarations; `DiscoverAckWaiters` and its namespace filter are deleted |
| 5 | ⭐ **the rail**: per host composition (CGF, SimHost, IG, editor, Traffic), build the real node; for every translator and writer-holding system, reflection — in the TEST — finds every writer it holds (collections and helper objects included) and asserts found = declared, failing with class + field; red-proved by deleting one declaration |
| 6 | the four `IDdsWriter<T>` interfaces (`Hrot.Core/Dds`, `Hrot.IG/Abstractions`, `Fdp.Diagnostics.Network`, `GizmoMap.Network/Transport`) become one |

⭐ Your own model for 1, 2 and the rail; then **Sonnet** for the ~70 translator declarations (one agent at a time). Fold the as-built into Replay
Time Axis §12f row 37.

## CE-4108 — the fragile ones the sweep found (the tracker row has every file)

⭐ Most reflection in the tree is legitimate (the serializers, StructEdit, JSON readers, the replay search compiler, `nameof` + generic dispatch,
the data-breakpoint field paths, the wire inspector). ⛔ Do not touch those. The fragile kind:

| # | what | lean |
|---|---|---|
| ① | `StagingEntityExtractor` calls `EntityRepository`'s **private** `"RegisterUnmanagedComponent"` by a string, to skip the DataPolicy branch | an explicit internal API (InternalsVisibleTo) or the public one |
| ② | the editor's hot-reload coordinator matches registrars by **attribute name** and **method name** (`"Register"` / `"RegisterAll"`) | an interface / the attribute type from a shared assembly — ⚠ measure first why by name (a reloaded load context) — **behaviors** owns it |
| ③ | the "scan every loaded assembly" loop hand-rolled **~22 times** | ONE loaded-types scanner the registries call; attribute / interface discovery stays |
| ④ | `"Hrot.AI.Behaviors"` found by name **four times** (CGF, editor ×2, the blueprint editor) | one resolver |
| ⑤ | **two** `AiHotReloadCoordinator` classes (Fdp.Toolkits and Hrot.Editor) | measure whether one concept is built twice — **behaviors** |

Order: CE-4107 first (it is on the replay / DAR path); CE-4108 ① ③ ④ after it. ⭐ ② and ⑤ are tracked separately as **CE-4109** for the behaviors lane — not yours. Each with a rail in its
feature's suite; report with the gate contract.

_coordinator lane, 2026-10-30_
