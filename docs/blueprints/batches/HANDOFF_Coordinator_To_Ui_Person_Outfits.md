<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-17
current-answer: the whole file
related-designs:
  - ../../designs/person-looks/DESIGN_Person_Outfits.md — ⭐ THE design (READY-TO-BUILD, R-4287 + R-4288): §3 OF-1…OF-12, §4 UML, §5 the content, §6 acceptance, §7 ids, §8 slices
  - ../../DESIGN_Map_3D_Mode.md — §6q / §6r: the block figure, its parts, details and faces you extend (update §6q to the as-built)
  - ../../DESIGN_Civilians_And_Crowd_Control.md — §7.6 F-4: `PersonDto` (age, sex) the outfits now read
  - ../../DESIGN_Add_Entity_Picker.md — §2j: the thumbnails the new types need
-->

# HANDOFF — coordinator → `ui`: PERSON OUTFITS FROM THE TKB + MIDDLE-EAST CIVILIANS AND FIGHTERS (CE-4648)

**For:** ui. **From:** coordinator. **Dispatched at `a85dbb4ee`**; ⛔ scope FROZEN there — later documents are FYI; one that invalidates a step ⇒
STOP that step, do the rest (R-106).

🔒 User, `2026-11-17`: *"I think the TKB should say what 3D model properties are, including the clothes"* · *"seeded variety within the lists"* ·
*"I would like to have also Arabian combatants, meaning that they can have weapons"* · *"approved, insurgent takes the new look, write the handoff"*.

## ⭐⭐⭐ UNATTENDED
Never ask, never wait. Decide by measurement and the claim table; record `R-1xxx` lane decisions for the user's review with what would change them.
Commit, push and keep `docs/blueprints/batches/STATUS_Ui_Person_Outfits.md` after every step. Ids from your own blocks; the type ids below are FIXED.
Cross-lane edits (`Fdp.Toolkits/Tkb/Domain` for the DTO, `Hrot.Core/Tkb` catalogues) are allowed and SAID in the commit. ⛔ Never change any person's
behaviour, weapons or brain — this is the LOOK only (the fighters reuse the insurgent template as it is).

## 0. Start
`git fetch origin coordinator`; merge it into `ui`; restore the test projects you run; push `chore: started person outfits at <sha>`. Read the design END
TO END, then §6q / §6r of Map 3D. Every existence claim through the graph AND grep.

## 1. Fixed ids
TKB types **1105–1110** (Middle-East man, woman, boy, girl, elderly man, elderly woman) · **2010–2012** (Middle-East fighter rifle, fighter RPG + rifle,
machine gunner — 2012 only if a man-portable machine-gun mount exists; else record it held). ⭐ Re-check each is free on every lane branch before declaring.

## 2. The slices — in order

| slice | build (design §) | done when |
|---|---|---|
| **S1 the data path** | OF-1 `OutfitDto` (TKB, beside `PersonDto`) · OF-2 sex and age from `PersonDto` (the female-type defect) · OF-3 today's wardrobes → `OutfitCatalog` data, a type without one gets the everyday set; the side camouflage stays a rule (`Uniform` + `BySide`) · OF-6 fixed draw order · OF-7 flattened once per type into `TypeLook` | ⭐ every EXISTING person frame pixel-identical (the frame rails that cover civilians, children, elderly, police, soldiers — under `xvfb-run -a`); the OF-2, OF-6, OF-7 (0 bytes, 500 people) rails |
| **S2 the garments** | ⛔ **mockups first** (R-4143) of robe, abaya, shemagh (+ agal), skullcap, hijab, niqab, chest rig, face wrap on the figure — house style over `scripts/mockup_kit.py`, committed beside the design; then OF-4 / OF-5 in `Outfits.Details` (robe hides legs, scarf hides hair, niqab and face wrap hide the lower face) | each garment's frame next to its mockup (R-4146), the side-by-side in your status |
| **S3 the civilians** | OF-8 + §5 content for 1105–1110 through the one `Civilian(...)` helper; thumbnails (`scripts/thumbnails.py --pack`); scenario `civ-middle-east-market` (40 people near and far, a preview image) | `scripts/scenario-regression.sh civ-middle-east-market` passes |
| **S4 the fighters** | OF-10 (2010–2012 from the insurgent template — one helper, no copied descriptors) · OF-11 the wardrobe wins over the class (rail) · OF-12 the `Insurgent` 2003 gets the fighter wardrobe; thumbnails; scenario `me-fighters-ambush` (fighters ambush a patrol in a Middle-East street, civilians around) | it passes (rifles and an RPG fire; the civilians flee); ⭐ the scenarios that place 2003 still pass (find them: `grep -rl` the type in `scenarios/`) |

## 3. Gates *(rule 8 — in your report)*
T-1 first: the outfit / figure frame rails and `Map3DTests` (⚠ three reds are pre-existing — `S1_TheIconFallback…`, `CE1309_TheMindStateBar…`,
`CE1086_TheFpvDrone…` — confirm against your base); the catalogue golden `EntityTypeCatalogTests` (it moves by exactly the new type lines — say so);
the civilians' and insurgent's own suites unchanged. Visual work: one key mutation per rail (proof proportionate to risk). Display-only rails run under
`xvfb-run -a` — a "skipped" is not a pass.

## 4. When you finish
Fold the as-built into the design and Map 3D §6q (prior state SUPERSEDED); `docs/CAPABILITIES.md` rows for TKB-driven dress and the Middle-East
people (by their scenarios); the status lists every id, every `R-1xxx` decision and each scenario's verdict.
