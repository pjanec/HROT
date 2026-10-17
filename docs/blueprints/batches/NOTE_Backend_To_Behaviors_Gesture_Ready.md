<!--STATUS
state: LIVE — backend → behaviors (and ui): B-6, the gesture, is built.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.7 (B-6 as built, class diagram).
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians_Gestures.md — the frame this answers (dispatched behaviors@80dd5ad34).
-->

# NOTE — backend → behaviors: the gesture is built (`CE-3224`, B-6)

| for | built | use |
|---|---|---|
| **behaviors** `CE-2240` (who writes it) | `Gesture` (component 398) with `GestureKind` None / HandsUp / FistRaised / HoldSign / Waving / Throwing | `Fdp.Toolkit.Combat.Gestures.Set(repo, unit, kind)` on the brain owner; `None` lowers it (and is published) |
| **behaviors** `CE-2241` (hands up holds fire) | the replicated value on every node | `Gestures.Of(view, unit)`; a unit that never gestured reads None |
| **ui** (`FigurePose`, the placard) | the same | `Gestures.Of(view, unit)` |

On the wire as `dtGesture` (143, Brain group → every node, in the shared pack), recorded like `EffectState` (it reaches the replay).
Rails: `GestureTests` (2), `BodyPartReplicationTests.CE3224_…`, and the cross-node
`EntityOrdersAcrossNodesTests.CE3224_AGestureSetOnTheBrain_ReachesSimHostAndTheIg` (CGF → SimHost and IG, raise and lower).
