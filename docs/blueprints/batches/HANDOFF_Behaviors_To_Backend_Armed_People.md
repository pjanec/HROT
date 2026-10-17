<!--STATUS
state: LIVE — behaviors → backend: a FRAME (you design the details as step 1, then build).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h OWNS this: the diagrams, AW-1…AW-14 and the user's words (R-2025). This frame only points at it.
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — W-A/W-C/W-F: built-in vs separable weapons, deploy state; B-9/B-10 extend it.
  - docs/DESIGN_Weapon_Layers.md — the weapon brain, WeaponChoice, W-13 posture; B-8's hands mount.
  - docs/DESIGN_Entity_Interactions.md — §7.4/§7.5 carry stations (R-2009, R-2017); B-11 extends them.
  - docs/DESIGN_Sensors_And_Doctrine.md — §7.8a "armed = 1"; B-7 replaces the test with one rule.
-->

# HANDOFF — behaviors → backend: armed people *(B-7…B-14)*

**Dispatched at:** the commit that adds this file, on `behaviors`. ⭐ Your scope is FROZEN at that commit; documents that change after it are
FYI only — if one invalidates an item, stop that item and say so (R-106), the others go on.

**What it is.** The user ruled how a person is armed, what he holds and what others can see ([Civilians §5h](../../DESIGN_Civilians_And_Crowd_Control.md),
**R-2025**, AW-1…AW-14). The behaviour half (draw, stow, conceal, pick up, throw, detonate, the riot, the ladder) is behaviors' `CE-2238`,
`CE-2239`, `CE-2241`; this is the engine and TKB half. ⭐ Build against §5h's class, state, sequence and module diagrams — do not redraw them
here; fold any deviation back into §5h (as-built rule).

**Fences.** ⛔ No entity per issued weapon (W-A; the user: *"we should not make it a separate entity, because there could be many soldiers
who just carry it but never use it"*). ⛔ No second firing path — everything thrown goes through `FireAtPoint` / `WeaponFireIntent`.
⛔ Posture never enters the general danger score (AW-10). ⛔ Nothing reads ground truth to call a Hidden station armed.

| # | item | design basis | ⭐ lean | acceptance |
|---|---|---|---|---|
| **B-7** | **`Armament.IsArmed(view, man)`** — the ONE rule, and the 27 `HasComponent<WeaponState>` readers that mean "armed" routed through it (the ones that mean "can I fire" stay) | AW-1, AW-2, AW-3 | beside `WeaponOf` / `DispenserOnly`; a non-lethal-only suite, a laid-down mount and a Hidden station arm nobody | a stone-only man, a laid-down soldier and a man with a Hidden pistol read Unarmed (0.3); a man with a pistol in Hands reads armed; `ThreatDanger` tests unchanged for soldiers |
| **B-8** | **the HANDS mount** on every person type (appended, non-lethal by default, an `AmmoLoadout`, empty at start) + **`Indirect`** on 6038 (stone) and 6037 (stun grenade) | AW-7 | one shared mount helper in the person templates | a man given 3 stones throws one on the arc and has 2 left; armed stays false |
| **B-9** | **the pistol** as a second built-in mount on the soldier types (and the planned Pilot, airborne B-7) | AW-4 | `WeaponChoice` already ranks mounts — no brain change expected | a soldier whose rifle is empty fires his pistol |
| **B-10** | **`LaidDown`** on built-in mounts (replicated; cannot fire; reads unarmed) | AW-5 | a flag on the mount's state, not a component per weapon | laid down ⇒ no round fires and `IsArmed` is false on every node |
| **B-11** | **carry stations per person: Hands, Back, Hidden** (`Concealed` on the seat) + the **pick-up rifle and pistol** weapon-system types (one man, fire while carried) + **one explosive-charge** type that bursts at its controller's position (worn Hidden / placed / a vehicle station) | AW-6, AW-12, AW-13 | `PersonCarrier()` grows two stations; the charge is a weapon system whose round bursts at the carrier | a pistol moved Hidden → Hands flips `IsArmed`; a worn charge detonated hurts by the warhead's numbers around him |
| **B-12** | **`HeldItem`** — a replicated byte {None, Weapon, Throwable, Phone, Binoculars} (the `Gesture` shape, B-6) + **pocket items** as type data (phone on civilians, binoculars on leaders/observers) | AW-14 | "Weapon" means `WeaponPosture.ActiveMount` — never a copy of it; binoculars: a vision multiplier while held (your call how the sensor reads it) | the byte reaches every node; holding binoculars lengthens the holder's vision |
| **B-13** | **`RoundPickUp`** on SimHost (a brain request → still lying there, not spent, in reach ⇒ remove it, put it in the picker's hands loadout; one winner) + **stone-source areas** (unlimited) + a **thrown stone stays 60 s** as a landed round | AW-8 | the request/grant pair on the existing wire family; the grant names the round's munition and what is left of its fuze/burn | two men reach one canister: one gets it; a stone source refills a man to 5 |
| **B-14** | **the cloud's SOURCE** — a gas canister is a landed round that vents; its cloud sits on it while it vents, drifts free when it is picked up, and a thrown canister starts a new cloud for the burn left | AW-9; §7.3a part 3 (your cloud) | an optional source entity on `AreaEffectCloud`, stepped to it by the owner each second | the canister thrown back raises a cloud where it lands; the old one drifts and fades |

**Order:** B-7 first (everything reads it), then B-8 and B-13 (stones are the riot's first need), then B-10, B-9, B-11, B-12, B-14.
**Ids:** allocate from your block (rule 3) and name them in the reply; behaviors' rows stay `CE-2238`/`CE-2239`/`CE-2241`.
**Gates:** the gate-report contract (CLAUDE.md rule 8), including the integration row for B-13 (a cross-node grant) and the in-process twin
`PostureScenarioTests.CE2233_CivNeutralGate_…` staying green (it reads danger).
