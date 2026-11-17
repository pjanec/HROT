<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-17
current-answer: the whole file
related-designs:
  - ../../designs/person-looks/DESIGN_Person_Outfits.md — A: OF-1…OF-12 (R-4287, R-4288) — §3 decisions, §5 content, §6 acceptance, §8 S1 / S3 / S4
  - ../../designs/weapons/DESIGN_Bows_And_Crossbows.md — B and E: BW-1…BW-13 (R-4289), §9 NA-1…NA-7 (R-4290)
  - ../../designs/animals/DESIGN_Animals.md — C: §4b AN-20 (R-4289) · D: §4c AN-21 (R-4290)
  - ../../designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — `HitMarks` (HL-8), the arrow mark kind extends it
  - HANDOFF_Coordinator_To_Ui_Person_Outfits.md — ui's half (NARROWED to the drawing); it builds after you
-->

# HANDOFF — coordinator → `backend`: OUTFITS (data), BOWS, BIRDS, THE RABBIT HUNT, THE NATIVE AMERICAN WARRIOR — the non-UI half (CE-4648, CE-4649, CE-4650)

**For:** backend. **From:** coordinator. **Dispatched at `1bc4fb20e`**; ⛔ scope FROZEN there — later documents are FYI; one that invalidates a step
⇒ STOP that step, do the rest (R-106).

🔒 User, `2026-11-17`: *"the backend lane is idle. So I suggest giving the handoff to the backend lane. With whatever non-UI stuff we need"* ·
*"yes, write it and give me the prompt"* — R-4290. The decisions are all approved (R-4287…R-4290); the designs are the contract.

## ⭐⭐⭐ UNATTENDED
Never ask, never wait, never end a turn on a question. Every open choice: measure it, decide by the claim table (code + design), build it, record an
`R-3xxx` lane decision *"for the user's review"* with what would change it. A failed premise stops ONE step, never the batch (R-106). A defect is fixed
where it lives, with a rail in that feature's own suite (T-1). Commit, push and update **`docs/blueprints/batches/STATUS_Backend_Bows_Birds_Outfits.md`**
after every step. Cross-lane edits are ALLOWED and SAID in the commit and the status — this work touches `Hrot.Presentation` (outfits data path,
stand-in drawing), `Hrot.AI.Behaviors` (the animal brain plays, the circle raid, the hunter's rule) and `Hrot.Core` catalogues.

⛔ **Not yours — ui's, later**: mockups and the real DRAWING of every new garment, the bow / crossbow / quiver models and the draw pose, the arrow round
shape, the stuck arrow drawn at its angle, the arrow shaft drawn from a body mark, the rabbit and duck figures, thumbnails. ⭐ Where something must be
seen for a rail, use the NEAREST EXISTING look as a stand-in (a robe ⇒ the long skirt; a hijab ⇒ long hair in the cloth colour; an arrow ⇒ the spear shape
scaled; a duck ⇒ the bird; a rabbit ⇒ the cat scaled) and list every stand-in in the status for ui. Every scenario judges STATE, so it can pass without looks.

## 0. Start
`git fetch origin coordinator`; merge it into `backend`; `dotnet restore` the test projects you run; push `chore: started bows/birds/outfits at <sha>`.
Read RULINGS.md (R-4287…R-4290, R-4281 the fun pack, R-4269 riding), CLAUDE.md, then the three designs END TO END. First command:
`scripts/cbm.sh list_projects` in the background — every existence claim through the graph AND grep.

## 1. Ids
Fixed by the designs (⭐ re-check each is free on every lane branch before declaring): person types **1105–1110** (Middle-East civilians), **2010–2012**
(Middle-East fighters; 2012 only if a man-portable machine gun exists) · munitions **6066 arrow, 6067 crossbow bolt** · `WeaponCategory.Bow` **14** ·
`MunitionShape.Arrow` **9** · `HitMarkKind.Arrow` **5**, `.Bolt` **6**. ⭐ **Measured and taken by you** (say them in the status): Archer, Crossbowman,
the Native American warrior, the archery target prop, the duck (a lone bird + a flock), the rabbit, the hunter, the hunting-dog play's order id if any.
Tracker / rulings from your block (`CE-3xxx` / `R-3xxx`).

## 2. The work — in order

| # | build | done when |
|---|---|---|
| **A** outfits — data (CE-4648) | Outfits S1: OF-1 `OutfitDto` (TKB, beside `PersonDto`), OF-2 the type's sex and age (⭐ the "adult female drawn as a man half the time" defect), OF-3 today's wardrobes → `OutfitCatalog` data (one path, no palettes left in code; the side camouflage stays a rule), OF-6 the fixed draw order, OF-7 flattened once per type into `TypeLook`; then OF-11 (the wardrobe wins over the class) and the catalogue ROWS with their wardrobes: §5 types 1105–1110, 2010–2012, the Insurgent 2003's new wardrobe (OF-12), the Native American warrior (NA-2's wardrobe); new garment KINDS declared, drawn with stand-ins | ⭐ every existing person frame PIXEL-IDENTICAL (the frame rails covering civilians, children, elderly, police, soldiers — `xvfb-run -a`); the OF-2, OF-6, OF-7 (0 bytes, 500 people), OF-11 rails; `EntityTypeCatalogTests` golden moved by exactly the new type lines; the scenarios placing 2003 still pass |
| **B** bows (CE-4649) | S0 (the measurements in §8; ⚠ does the world rotation of a lying `LightweightRound` reach a viewer?), S1 (BW-1, -2, -6, -7, -9 SILENT, -12: the category, the arc, the two munitions with placeholder look rows, the Bow and Crossbow mounts, Archer, Crossbowman), S2 (BW-4 the stuck pose + ⭐ the lying-round cap 256 for EVERY lying round, BW-5 the `Arrow` / `Bolt` marks), BW-11 (the target prop + the ring scoring into the game score) | `bw-arrow-arc`, `bw-unheard`, `bw-crossbow`, `bw-stuck-wall`, `bw-stuck-body`, `bw-archery-contest` pass; the stuck-pose, cap and ring rails |
| **C** birds (AN-20) | S0: can a round hit a lone bird today, does a killed one fall? Then AN-20a (any round hits and kills a lone bird; it falls and lies), AN-20b (a flock hit kills one drawn bird: count −1, a dead bird spawned and falling, the rest flee), AN-20c the duck (a lone + a flock type) | `an-duck-hunt` (the shotgun) passes |
| **D** the rabbit hunt (AN-21) | AN-21a the rabbit species, AN-21b freeze first, AN-21c the ZIGZAG flee style (one parameter of the one animal brain, seeded), AN-21f the hunting-dog play (follows its handler, ranges ahead, flushes, never attacks people), AN-21g the hunter's standing rule "animals of the listed species, never people" | `an-rabbit-bolts` and `scenarios/fun/hunting/rabbit-hunt` pass |
| **E** the Native American warrior (CE-4650) | NA-1 the type (bow + the thrown hatchet as a tomahawk, insurgent brain, no armour) in a `FunTkbCatalog` file, NA-3 riding (the same type mounts a riding horse), NA-4 the CIRCLE RAID group play | `scenarios/fun/western/war-party` and `archer-ambush` pass |

## 3. Gates *(rule 8 — in the status)*
- T-1 first: each feature's own suites — outfits / figure frame rails, `Map3DTests` (⚠ three pre-existing reds: `S1_TheIconFallback…`, `CE1309_TheMindStateBar…`,
  `CE1086_TheFpvDrone…` — confirm at your base), `FireProcessingSystemTests`, `BallisticsSystemTests`, `RaycastSolverSystemTests`, `HitResolution*`,
  `ProjectileLayer*`, the animal suites (`AnimalHsmTests`, `AnimalFearTests`, `BirdFlightModelTests`), the civilians' and insurgent's suites.
- Every behaviour piece (the animal plays, the circle raid, the hunter rule) runs its rails with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + the world
  guard; `stepped-hash.py --threads 1,4,8` on `rabbit-hunt` and `war-party` (parallel safety — not the verdict).
- ⛔ No allocation and no LINQ on the hot paths you touch (the outfit pick, the stuck-round draw, the flock-hit test, the zigzag step) — 0-byte rails.
- Display-only rails under `xvfb-run -a`; a "skipped" is not a pass. Every red confirmed pre-existing against your base sha.

## 4. When you finish
Fold the as-built into the three designs (deviations argued, prior state SUPERSEDED); `docs/CAPABILITIES.md` rows by their scenarios; the status lists
every id, every `R-3xxx` decision, every scenario's verdict, ⭐ and EVERY stand-in look left for ui.
