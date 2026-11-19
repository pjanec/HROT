<!--STATUS
state: LIVE — a cross-lane convention (R-4297), FYI to every lane
updated: 2026-11-19
current-answer: the whole file
related-designs:
  - ../../DESIGN_System_State_Inventory.md — §9 owns it (UML, the decisions); the ledger tables there retire
-->
# NOTE ui → every lane: a system's state field is classified WHERE IT IS WRITTEN — an attribute (R-4297, CE-1980)

🔒 **User, `2026-11-18` (via coordinator, `DECISIONS_Ui.md` CE-3451 ③):** the classification moves into the CODE; the central hand ledger
(`docs/DESIGN_System_State_Inventory.md` §4 / §5) retires.

**What you do from now on** — when you add a field to a system, a module or a network translator that can carry state between frames (a collection, a
mutable scalar, a reference to a class), put ONE of these on it (namespace `Fdp.Core`), with a real reason:

| attribute | class | means | e.g. |
|---|---|---|---|
| `[FrameScratch("…")]` | A | rebuilt before use in every run — ⭐ also needs no reset at the world boundary | a list cleared at the top of `Execute` |
| `[WorldDerived("…")]` | A | a cache / index derived from the world | a lookup rebuilt on demand |
| `[ProtocolCache("…")]` | B | what was sent / received / asked; ⚠ its owner must react to `WorldEpoch.Moved` (or use `PublishedInstances<T>`) — the rail reads the compiled code | an ingress's pending samples |
| `[NotState("…")]` | K | a dependency or configuration fixed at start-up | a TKB, an options object |
| `[SessionState("…")]` | S | cluster / transport / process state a restore leaves | a cluster cache |
| `[SimulationMemory("…")]` / `[ExternalState("…")]` | C / X | ⛔ simulation memory outside the ECS — a LISTED move; a new one fails | — |

⭐ A class held in fields can carry the attribute once, on the class: every field holding it is classified. An auto-property: `[field: WorldDerived("…")]`.

**The rails** (`Hrot.ClusterRunner.Tests`): `EverySystemStateFieldIsClassifiedTests` (③ every field classified; ④ every `ProtocolCache` owner reacts to a
world replacement, read from its IL) and `EverySystemForgetsTheLastWorldTests` (② an entity-holding field forgets at the boundary unless it is
`[FrameScratch]`). ⭐ To see which of your fields still need one: `HROT_STATE_DUMP=/tmp/state.tsv dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests
--no-build --filter FullyQualifiedName~EverySystemStateFieldIsClassifiedTests.Dump` (the 6th column is empty for an unclassified field).

**What ui does in CE-1980:** moves every existing ledger row onto its field as an attribute (a scripted, attribute-only edit across every lane's files —
no behaviour change), classifies the backlog, and resets the entity-holding fields ② names. ⚠ The edit touches many of your files with one-line
attribute insertions above field declarations; a merge conflict there keeps both sides.
