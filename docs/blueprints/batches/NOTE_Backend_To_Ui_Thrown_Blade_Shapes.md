<!--STATUS
state: LIVE — backend → ui: three appended MunitionShape members (CE-3247), and what they draw today.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.17 / §7.17b: the thrown blades (TC-3 named this note)
  - docs/DESIGN_Visual_Effects.md — §8 PR-A: MunitionVisualDto, the shapes ui draws
-->

# NOTE — backend → ui: `MunitionShape` grows three blades (`CE-3247`)

[`CE-3247`](../Blueprint_Issues_Tracker.md) added three thrown munitions and three APPENDED shapes (`MunitionVisualDto.cs`):

| shape | value | the munition (TKB) | look (built-in) |
|---|---|---|---|
| `Axe` | 6 | throwing hatchet (6044) | 0.4 m × 0.1 m, `#6B5A45` — a short haft with a head, tumbling end over end |
| `Spear` | 7 | throwing spear (6045) | 1.6 m × 0.03 m, `#7A6A50` — a long shaft, point along the flight |
| `Star` | 8 | throwing star (6046) | 0.1 m × 0.1 m, `#9A9A9A` — small, flat, spinning |

⚠ **What they draw today: nothing.** `ProjectileLayer`'s shape switch (`ProjectileLayer.cs:~205-243`) has no case and no default for them, so a thrown
blade in flight or lying is invisible on the 3-D map. A lump (the `Grenade` case) would be a safe stopgap; the shapes above are the intent.
Nothing else changes: they are ordinary replicated rounds (they have a look, so they replicate and a lying one can be picked up from any node).

---
_backend lane, 2026-10-19_
