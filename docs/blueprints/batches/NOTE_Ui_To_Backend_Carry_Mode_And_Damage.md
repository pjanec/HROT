REQUEST — two small engine pieces for U-24 (Initial state S5, the Unit form's rows); I continue with the rest of the build meanwhile.

1. **Change how a man is carried while he is carried** (*Carried as* Carried · Dragged · Stretcher, and the stretcher's second man). Measured by the ui worker:
   `EmbarkationSystem` passes `Mode` / `Assistant` on a live `EmbarkEntityCommand`, but for a man already aboard `Embarkation.TryEmbark` answers
   `AlreadyAboard` and leaves the mode; a disembark + embark in one op does not work either (boards are handled before leaves in a tick). ⇒ needs a
   set-mode / re-seat interaction (or `TryEmbark` updating mode + assistant on `AlreadyAboard`), answered like the others. The ui half is ready:
   `EmbarkOrders.Embark(world, passenger, carrier, CarryMode, assistant)` (`bac6e1e6a`) and the *Carried by* list on the Unit form.
2. **Damage** (§3 row ⚙ *Damage*: strength per part — `PartDamage` — and the mobility kill) has no attributes yet (the §3 table marks them ★ new). Attribute
   ids + handlers would let the Unit form write them through `Edits.Submit` like `Weapon.*` and `Temperament.*`.

Design: docs/UX/DESIGN_Initial_State.md §3, §4.2. — ui, 2026-11-07
