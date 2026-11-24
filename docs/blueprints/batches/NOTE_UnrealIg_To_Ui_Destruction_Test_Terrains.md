# Note: building destruction in Unreal — we start on bt-range; please give us a Stechovice destruction test

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-24 |
| **For** | `ui` (HROT) |
| **Follows** | [NOTE_Ui_To_UnrealIg_Rounds_And_Building_Damage_Decisions.md](NOTE_Ui_To_UnrealIg_Rounds_And_Building_Damage_Decisions.md) (CE-11050..11052 — thank you, the panels tool is exactly what we asked for) |
| **User, verbatim** | *"let's work on some different terrain that supports destructible buildings from templates. We will apply to larger stechovice terrain later when the basics will be proven. Note that HROT can turn an OSM imported building into a destructible building almost on the fly (not sure how far we need to support that - maybe not necessary). Pls ask HROT to make some selected stechovice building enterable & destructible so we can test that later in stechovice as well."* |
| **Asks** | §2 a Stechovice destruction scenario · §3 the 723 plain houses |

## 1. Where we start

**`bt-range` + your `scenarios/demos/buildings-and-doors/bd-destruction`** — a passage blown by a charge, a hole from a tank's HE round, a
collapse from a Mk-82: every damage kind on three template houses, in a few minutes. Then `demo-city` (164 template houses, our look level
exists). Stechovice comes when the basics work.

## 2. Ask: a Stechovice destruction test

Measured in `assets/terrain/Stechovice`: **all 4,099 templates are enterable** (none `solid`; 94,686 openings; UTM design Q-U8) — so nothing
needs converting for a test. What we need is a **scenario**, like `bd-destruction`, on a few houses in the village where the battle is
(around the Village Rifleman, 1080, in `stechovice-battle`):

- a wall **passage** (a charge), a wall **hole** (a tank HE round), a **collapse** (a bomb or a heavy shell);
- **windows** shot through and broken by a blast nearby (cracked and broken panes);
- staggered so each act is seen on its own, with `checks.json` expect rows like `bd-destruction`'s;
- ideally a **playable** soldier, so the operator can walk in through the passage.

## 3. Question: the 723 plain houses

Stechovice also has **723 buildings without a template** (`height`, `floors`, `material` only, e.g. `w114708271`). HROT builds no panels for
them, so they can never be holed or collapsed — while Unreal draws them like any other house, so a tank firing into one shows nothing.
Is that intended? Our lean: give them templates the same way the importer generated the other 4,099 (the user notes HROT can do that almost
on the fly), so every house in the battle behaves alike. ⛔ Not urgent — it does not block §1 or §2.
