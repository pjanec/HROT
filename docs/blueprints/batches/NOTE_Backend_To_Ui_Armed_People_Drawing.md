<!--STATUS
state: LIVE — backend → ui: a NOTE (what B-9…B-12 put on the wire for the figure to draw).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.12a (pistol), §7.13a/§7.13b (carry stations, pick-up weapons, the charge), §7.14/§7.14a (the held item) OWN the data; §8 OWNS the ui designs.
  - docs/blueprints/batches/NOTE_Backend_To_Behaviors_Armed_People_B8_B13.md — the same landing, told to behaviors.
-->

# NOTE — backend → ui: what the armed-people items put on the wire for the figure *(CE-3231, CE-3233, CE-3234)*

Nothing is asked of you by date — this is the list of things the 3-D figure can now draw, and what each reads. All of it is already on
the wire and in recordings.

| what | read it from | draw |
|---|---|---|
| ⭐ **the pistol** (B-9, [CE-3231](../Blueprint_Issues_Tracker.md)) | the soldier's mount 1 (InfantrySoldier 2002) / 2 (Grenadier 2004); its `WeaponMountDto.Appearance.Look = ItemLook.Pistol`; `WeaponPosture.ActiveMount` says which mount is up | a pistol in his hand when `ActiveMount` is the pistol's mount |
| ⭐ **where a carried weapon sits** (B-11, [CE-3233](../Blueprint_Issues_Tracker.md)) | the carried thing's `IsEmbarkedTag.Seat` on the man (every node re-derives it from the replicated slot); `CarryStations.Hands / Back / Hidden(view, man)` give the seat numbers (1 / 2 / 3 on every person) | seat 1 HANDS: in his hands (today's drawing) · seat 2 BACK: **slung** on his back · seat 3 HIDDEN: **not drawn** (`CarryStations.IsConcealed`) |
| the pick-up rifle **616** and pistol **617** | weapon-system types (family WeaponSystem); `Appearance.Look` Rifle / Pistol on their mount | on the ground when nobody carries them; in his hands / on his back when carried |
| the **charge 618** | a weapon system, `Charges.IsCharge(view, e)`; worn it sits in HIDDEN (not drawn), in a car's boot (concealed, not drawn), placed it stands on the ground | a small box when placed; its burst is an ordinary `DetonationNotification` (the detonation gizmo already draws it) |
| ⭐ **what is in his hand** (B-12, [CE-3234](../Blueprint_Issues_Tracker.md)) | `WeaponPosture.HeldItem` (`HeldItemKind`: None · Weapon · Throwable · Phone · Binoculars), on `dtWeaponPosture` beside `ActiveMount`; `HeldItems.Of(view, man)` | Phone: a phone to his ear / in hand · Binoculars: raised to his eyes · Weapon / Throwable: today's weapon drawing for `ActiveMount` · None: empty hands |

**Ids:** none new for you (`dtWeaponPosture` gained the appended `HeldItem` byte).
