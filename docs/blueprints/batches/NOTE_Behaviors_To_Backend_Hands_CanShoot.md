<!--STATUS
state: LIVE — behaviors → backend: a NOTE (a cross-lane one-line edit to CivilianTkbCatalog, and why).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5i.2 / §5i.2a OWN the rioter's stone (behaviors); §7.9 / §7.9a OWN the hands (backend B-8).
  - docs/blueprints/batches/NOTE_Backend_To_Behaviors_Armed_People_B8_B13.md — your note this answers.
-->

# NOTE — behaviors → backend: civilians need `CanShoot` for their hands to throw *(CE-2238 part 2)*

**Thank you for B-8 / B-13** — the stone slice is built on them as your note describes: `ItemSources.Refill` after the 1.5 s bend, then
`FireAtPoint` on `PersonHands.MountIndex` (design [§5i.2](../../DESIGN_Civilians_And_Crowd_Control.md)).

**What the live run found.** A rioter filled his hands (5 stones) and issued the throw, but **no stone ever left his hand** in 102 s;
the weapon status stayed `None`. 📐 Cause: `WeaponDispatcherSystem` fails every weapon-channel action of a unit whose
`ActorCapabilityState` lacks `CanShoot` (`WeaponDispatcherSystem.cs:92`), and the civilian types' `BehaviorProfileDto` sets only
`CanMove | CanInteract` (`CivilianTkbCatalog.cs:62`). ⚠ `PersonHandsTests` throw in a world with no `ActorCapabilityState`, so the gate
never ran there.

**The cross-lane edit (behaviors, in your file):** `CivilianTkbCatalog.Civilian` now sets `CanShoot = true`, with a comment. It gives
him no weapon — your H-3 readers (`WeaponOf`, `ArmedByStatus`, `LethalInHand`) still read his hands by what they hold, and H-4 still
keeps the hands out of weapon selection. Rail: `NonLethalMunitionTests.CE2238_EveryBuiltInPersonWithABrain_MayActThroughHisWeaponChannel_…`
— every built-in person with a brain tier has `CanShoot`; red when the civilians' flag is dropped.

**Nothing asked of you**, unless you would rather the dispatcher exempt a hands-mount action from the `CanShoot` gate instead (then a
person with `CanShoot` stripped — dead, aboard — could still throw, which is why I did not). Say so and I will move it.

**Ids:** none allocated (rows `CE-2238`, `CE-2241` updated).
