<!--STATUS
state: ⛔ ON HOLD — NOT DISPATCHED (user, 2026-10-15: "do not start building yet"). Do NOT start; a later commit lifts the hold and stamps the dispatch sha.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/airborne/DESIGN_Airborne_Insertion.md — THE design (approved, R-1003): §1 diagrams, §3 decisions, §5 models, §7 items B-1…B-9
  - docs/blueprints/batches/HANDOFF_Ui_To_Behaviors_Airborne_Insertion.md — the twin handoff; behaviors builds on B-1…B-4, B-8, B-9
-->

# HANDOFF — backend: airborne insertion (fast-rope, parachute, the ejecting pilot) — the engine half

**For:** `backend`. **From:** `ui`. ⛔ **ON HOLD — not dispatched; do not start** (user: *"do not start building yet"*). When the hold is lifted, the dispatch sha is stamped here and the scope freezes there (later documents are FYI; a document that
invalidates an item ⇒ STOP that item and report it, R-106). Ids from your `CE-3xxx` block; the ui row tracking this is **CE-1117**.

🔒 **User, `2026-10-15`:** the design *"approved"* (R-1003) and three demos — a paratrooper squad jumps and regroups, an SF squad fast-ropes onto a
roof while the helicopter hovers above the building and leaves when all are safely down, and a pilot ejects.

## The items — build exactly [DESIGN_Airborne_Insertion](../../designs/airborne/DESIGN_Airborne_Insertion.md) §7 B-1…B-9

| # | item (design §) | the one thing not to miss |
|---|---|---|
| **B-1** | `Descent` component + `EntityDescent` descriptor (the `EntityWeaponPosture` egress / ingress pattern, APPENDED fields per R-169); `Locomotions.Of` reads it first (§1, D-1) | ⛔ do NOT mutate `Locomotion.Class` — it is not replicated (`Locomotion.cs:22-31`) |
| **B-2** | `LocomotionClass.Rope = 8 / Parachute = 9`, `RopeSlideModel`, `ParachuteModel` (§5), `MotionModelTable` rows; `CarKinematicsSystem.Without<Descent>()` (D-3) | the bank IS the body's roll in `SimTransform.Rotation` (D-5) — P3 carries it |
| **B-3** | `RouteHold.Rope / Drop`, `NavigationPhase.Roping / Dropping`; an `Unload` hover above 3 m refused | ⭐ a Rope hold's height is above the ROOF (highest surface), and it releases only when nobody is aboard AND nobody is on its ropes |
| **B-4** | `Embarkation.ExitIntoDescent` — leave at the door anchor / seat place and height, add `Descent{Exit}`; `DisembarkEntityCommand` gains the exit kind + anchor (appended) | ⚠ CE-3059 (the passenger's position written on the carrier's owner) applies — say how you resolve it |
| **B-5** | TKB `Gen.Parachute`, `RopeAnchors` on the UH-60s (400, 404), types Paratrooper + SF operator, the `Wind` singleton (scenario-set; absent = calm), the release-point helper | numbers from design §2 — every invented one a report line |
| **B-6** | `Locomotions.IsAircraft` used by `AmmoChoice` + `AirContacts` (D-4); fall damage (§5.3) | ★ a Stinger never selects a parachutist; a rifle does |
| **B-7…B-9** | slice 2: the Pilot type, `Descent{Ejection}` (the rocket arc), `EjectExecutor` + the pilot SPAWN, the auto-eject threshold | ⭐ the pilot is spawned BEFORE the jet is abandoned (R-2021: riders die with the aircraft) |

## Step 1 is yours
Fold the class / sequence detail you add into the design (§1, §5, §7 — diagram first), build, fold the as-built. ⭐ **Push B-1…B-4 first as
their own commit** — the behaviors lane's drills build on them.

## Report — the gate report contract (CLAUDE.md rule 8)
Rows per gate with `--no-build`, reds proven pre-existing at the base sha, the golden movement as a diff shape (the type catalogue grows), row 8:
the integration suite proving a descending body agrees across nodes. The sha of each item.
