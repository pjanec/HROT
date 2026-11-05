<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-05
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Entity_Control.md — OWNS the possession station and the FPS fire path (§8r.1 the fast-gun lock, CE-4143)
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — the weapon systems and WeaponSystems.CanFire gates
-->

# HANDOFF — coordinator → ui: possessed mounted guns (CE-2317, CE-1299, CE-1300)

**For:** the ui lane (`ui`). **From:** the coordinator lane. **Dispatched at `22b255f3b`**. ⭐ Scope FROZEN there;
later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒ a NOTE
to coordinator. Merge `coordinator` first.

## Why

🔒 User, `2026-11-05`, approved these as the next multiplayer gameplay work: three of the user's own live reports about possessing a
vehicle with a mounted gun. Each row in the [tracker](../Blueprint_Issues_Tracker.md) carries the user's words.

## The items

| # | row | what | design basis | done when |
|---|---|---|---|---|
| **①** | **CE-2317** — a possessed ZU-23 technical aims but never fires | 📐 not located. `WeaponSystems.CanFire` (`:64-100`) gates on `NoController`, `Destroyed`, the station arc; CE-1206's "a possessed host's station needs no gunner" fix exists, but this technical carries its own gunner. ⭐ The row's lean: **show `CanFire`'s `WeaponHold` reason on the HUD first** (the gate already says why not), then fix the one it names. Cross-lane into the fire rule if that is where it is — say it | weapon systems; Entity Control | a possessed `Technical_Zu23` fires; a refused shot shows its reason on the HUD; a rail on the gate that was blocking |
| **②** | **CE-1299** — a possessed technical's HMG hits people's feet; a mounted gun should fire along the aim like a rifle | ⚠ **check first whether CE-4143 already fixed it** (`2026-10-31`: a player's fast turret gun fires where he looks, `L` switches lock — Entity Control §8r.1, rails `CE4143_*`, "live lead-a-car check still to do"). If it did, close the row with the evidence; if not, the fire direction follows the crosshair ray as the rifle's sight mode does (CE-1078…1080) | Entity Control §8r.1 | rounds from a possessed HMG land where the crosshair points at a standing man (a rail, plus one live check) |
| **③** | **CE-1300** — in 3-D a mounted gun and its gunner are selectable apart from their vehicle (drag them and they jump back) | ⭐ the row's lean: the pick resolves an attached / seated entity to its CARRIER (the one pick adapter, CE-063); seat switching with G stays the way into the gun (CE-1210) | the pick adapter (CE-063) | clicking the gun or the gunner selects the vehicle; nothing attached can be dragged off; a rail in the pick adapter's suite |

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · a rail per item, red-proved · display rails under `xvfb-run -a`
for any drawing change (a skip is not a pass) · every red confirmed pre-existing against the base sha · `tracker-counts.py --check` · the ids
you allocate.

---
_coordinator lane, 2026-11-05_
