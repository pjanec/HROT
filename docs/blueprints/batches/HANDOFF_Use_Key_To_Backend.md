<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file (a frame — the design carries the substance)
stale-below: nothing
related-designs:
  - docs/DESIGN_Entity_Control.md — §8h, the owning design (U-1..U-7, O-1..O-6, items §8h.4, the verdict path §8h.6)
  - docs/DESIGN_Entity_Interactions.md — the interaction executors and the one interaction topic (R-221)
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — the Gunner seat and controlled mounts (32+k)
  - docs/blueprints/batches/HANDOFF_Use_Key_To_Behaviors.md — the behaviors half, which builds on this one
-->

# Handoff: the FPS use key, backend half (`CE-1151`, `CE-1152`, `CE-1153`, `CE-1156`)

**Dispatched at `2dfec7a37` (branch `ui`).** 🔒 User, `2026-10-16`: *"pls records into items and write handoff to the backend node and
behavior node to implement"*. **Your scope is FROZEN at `2dfec7a37`.** Documents that change after it are FYI only; if a later document
invalidates an item, stop and report it. ⛔ Do not adapt to it.

**First step:** merge `ui` at or after `2dfec7a37`. ⚠ `ui` does not contain `backend@636a08b48` yet. Then push a
`chore: started use-key at <sha>` commit (rule 1b).

## What it is (one paragraph; the design is the spec)

In FPS mode, `E` acts on whatever the crosshair rests on: a door, a man who is down, a vehicle, a standalone weapon. No new world action is
built. The possessed soldier's **brain** picks one of the interaction executors that already exist (door 4–8, pick up 9, put down 10,
embark 1, disembark 2), with their reach, timing and networked commands. Read [`DESIGN_Entity_Control.md`](../../DESIGN_Entity_Control.md)
§8h end to end, especially the class diagram, both sequences, the module graph, and **§8h.6** (how the brain's verdict reaches a HUD on
another node).

## Your items: build against §8h's diagrams, report any deviation, and fold it back into §8h (obligation ⑤)

| id | build | design | feature suite to extend first (T-1) |
|---|---|---|---|
| **`CE-1151`** | Three fields: `UseSeq` (byte, counter like `ShotSeq`), `UseVerb` (byte, 0 = primary) and `UseTarget` (long network id). Append them to `OperatorControlFrameEvent` and `OperatorInput` (`Possession.cs`), `ControlFrameInteractionPayload` (`InteractionMessages.cs`), `InteractionTransport` both directions (`:151`, `:161`), the copy in `PossessionSystem` (`Possession.cs:171`), and the Debug API body (`DebugApiService.Orders.cs:190`). | §8h U-2 | `InteractionTransportTests`, `EntityOrdersAcrossNodesTests` |
| **`CE-1152`** | `OperatorUseActions.For(world, actor, target) → UseAction[]` in `Fdp.Toolkits` Behavior. A `UseAction` carries `Verb`, `Label`, `ActionId`, `Available` and `UseReason`; your lane also chooses how the params are carried (a writer or a struct). Add `Embarkation.CanEmbark`: `TryEmbark`'s checks without seating anyone, and **`TryEmbark` calls it**. One rule, no copy. Verbs by target: see the CE-1152 tracker row. | §8h.2 class diagram, U-1, U-5…U-7, §8h.6 ① | `EmbarkationTests`, `PersonCarrierTests`, `EmbarkExecutorTests` |
| **`CE-1153`** | `OperatorUseStatus { UseSeq, ActionId, Outcome (Running / Done / Refused), Reason }`, Brain ownership group. Add descriptor `dtOperatorUseStatus = 141`, egress and ingress translators, the `NedOwnershipGroupBinding` mapping, and `HrotSharedComponentRegistry`. Mirror the `dtWeaponAmmo` (130) recipe end to end. | §8h.6 ② | `WeaponAmmoMirrorSystemTests` and the `dtWeaponAmmo` translator rails (mirror them) |
| **`CE-1156`** | `PossessionMode.Riding = 2`. `PossessionSystem` holds a Riding unit like any possessed one, so its task stays paused under origin Possession, but routes **no frames** to it. Fps ↔ Riding on the same station is a take-over (`Possession.cs:218` keeps the operator behaviour). `dtPossessedBy` already carries `Mode`. | §8h U-4, O-2, driving sequence | `EntityOrdersAcrossNodesTests`, `CgfLogicPackTests` |

## Key decisions, made with the user. Do not reopen them; report if a premise fails.

- ⭐ **`OperatorUseActions` reads ONLY replicated components.** The station runs it to draw the prompt and the brain runs it to decide, so
  the two must agree (§8h.6). Measured on `ui`: `SimTransform`, `DoorState`, `Health`, `PassengerBuffer`, `CarrierSeats`, `CarrierStations`,
  `WeaponSystemParams` and `IsEmbarkedTag` are all in `HrotSharedComponentRegistry`. `InteractionChannel` is **not** shared, so the rule must
  never read it.
- ⭐ **`UseReason` comes from the rule, not the executors.** Executors return only `Failure`. A failure after the action started is
  `Reason = Failed`.
- ⭐ **The verdict is STATE, not an event**, so a lost sample can't leave the HUD stale (a rejected alternative in §8h.6).

## Fences

- ⛔ **Descriptor 141 is reserved for this item.** It was free on `ui`, `behaviors` and `backend` at dispatch (`backend` tops out below
  140). If someone took it meanwhile, take the next free number and say so. ⛔ Never renumber an existing one.
- ⛔ **Append** the frame fields. Never reorder them: the wire struct and the DDS IDL follow the declaration order.
- ⚠ **It must reach the IG.** 📌 CE-1139 (`2026-10-16`) measured a shared component that never reached the IG world: the stance wire was
  host-wired, and "pure IG" ingests through a duplicate pack. Check `dtOperatorUseStatus` **live on an IG** with the cluster runbook
  ([`RUNBOOK_Cluster_Debugging_Over_Http.md`](../../RUNBOOK_Cluster_Debugging_Over_Http.md)). If it doesn't arrive, that is CE-1147 / CE-1148
  territory: report it, don't add a special case for the IG.
- ⭐ Cross-lane: `EntityOrders` and `PossessionStation` belong to the ui lane (CE-1158). Don't change them. If a signature you need is
  missing there, say so.

## Acceptance

- [ ] CE-1151: one frame with `UseSeq/UseVerb/UseTarget` goes station → `InteractionTransport` → `OperatorInput` on another node, unchanged
      (rail in `InteractionTransportTests`).
- [ ] CE-1152: rails for each target kind (door open/closed/locked, out of reach, man down / not down, vehicle seat free / full, standalone
      weapon, aboard). `TryEmbark` and `CanEmbark` agree on every existing `EmbarkationTests` case.
- [ ] CE-1153: `OperatorUseStatus` written on a Brain node arrives on a non-brain node (translator rail), and is seen live on an IG.
- [ ] CE-1156: Riding keeps the task paused, routes no frames, and Fps ↔ Riding keeps the operator behaviour running.
- [ ] Red-proof each new rail (revert the fix, watch it go red).
- [ ] Gate report per CLAUDE.md "THE GATE REPORT CONTRACT", including **row 8**: the cross-node suite
      (`EntityOrdersAcrossNodesTests`) is run, or why it can't gate is shown against the base sha.
- [ ] §8h folded with the as-built (class names, any deviation), prior state marked superseded. Tracker rows moved, ids stated.
- [ ] Pushed to `backend`. Tell the behaviors lane the sha. Their CE-1154 starts from it.
