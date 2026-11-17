<!--STATUS
state: LIVE
updated: 2026-11-17
current-answer: the whole file (a short note)
related-designs:
  - ../../designs/weapons/DESIGN_Bows_And_Crossbows.md — §9 NA-1…NA-7: the Native American warrior's TYPE (backend's step E, CE-4650)
  - ../../designs/person-looks/DESIGN_Person_Outfits.md — §9 / §10: the garments and ornaments ui drew; the wardrobe is `OutfitCatalog.NativeAmericanWarrior`
-->
# NOTE — ui → backend: the Native American warrior's LOOK is done (CE-4648, R-1065)

🔒 User, `2026-11-17`: *"The Native American with the feathers on the head is nice, but even better if he could have the Black hair, pigtails on both sides, and also a little bit more ornaments on his clothes."*

**For backend's step E (the warrior type, CE-4650): give the type this wardrobe and nothing else on the look side.**

```csharp
t.AddDescriptor(OutfitCatalog.NativeAmericanWarrior);   // Hrot.Core.Tkb — the whole look, seeded inside its own lists
```

What it draws (all by the ui lane, merged on `ui`; mockups `docs/designs/person-looks/person-outfits-native-sheet.svg`): long BLACK hair with a braid on each side (out of the hair beside the face, over the shoulder, down the chest to a red tie),
a headband with 1–3 feathers (black tips), face paint (65 %), a buckskin shirt, a breechcloth with a beaded belt and leggings, a beaded yoke (blue / red / white / yellow squares) and / or a bone hair-pipe breastplate, armbands and / or a bead necklace.
No other change on your side: the type's `PersonDto` (adult, male) as the other civilians, the bow, the tomahawk, the brain are yours. The quiver on the back (BW-10) is NOT drawn yet — a held-item detail of yours.

Cross-lane edits already made (said in the commits): `OutfitDto` / `Wardrobe` got a LAST `Extra` slot (armbands, necklace, waistcoat, patu — drawn after the beard, so no existing person's draws moved), `OutfitKinds` new members, `OutfitCatalog`.
