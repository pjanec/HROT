using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-4648</c> (docs/designs/person-looks/DESIGN_Person_Outfits.md §3 OF-3, §5) — what people WEAR, as TKB data. ⭐ OF-3: the everyday
    /// civilian's, the child's, the elderly's, the police's and the soldier's palettes — once hard-coded in <c>Outfits.cs</c> — live here, with the
    /// SAME values in the SAME order (the draw is by index, so the existing frames do not move); the presentation flattens them once. ⭐ OF-1: the
    /// Middle-East wardrobes (§5) are <see cref="OutfitDto"/>s stamped on the types by <see cref="CivilianTkbCatalog"/> /
    /// <see cref="UrbanCombatTkbCatalog"/>. Colours are <c>#RRGGBB</c>. ⛔ A person's SEED varies him only inside these lists.
    /// </summary>
    public static class OutfitCatalog
    {
        /// <summary><c>#RRGGBB</c> of a colour.</summary>
        public static string C(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";

        // ── ⭐ OF-3 — today's palettes (same values, same order as the old Outfits.cs arrays) ─────────────────────────────────────────

        /// <summary>Five skin tones, palest first.</summary>
        public static readonly string[] SkinTones =
            [C(236, 200, 170), C(214, 172, 136), C(198, 150, 110), C(160, 112, 76), C(112, 76, 52)];

        /// <summary>Everyday hair: black, brown, light brown, blond, grey, red.</summary>
        public static readonly string[] HairColours =
            [C(30, 24, 20), C(70, 48, 30), C(140, 96, 56), C(200, 170, 110), C(150, 150, 150), C(130, 60, 30)];

        /// <summary>Everyday adult tops.</summary>
        public static readonly string[] Tops =
            [C(180, 40, 40), C(40, 90, 160), C(230, 230, 225), C(60, 120, 70), C(220, 180, 60), C(120, 60, 130), C(200, 110, 50), C(90, 90, 96), C(150, 190, 220), C(210, 150, 160), C(120, 90, 60), C(30, 30, 34)];

        /// <summary>A child's brighter tops (§6q O-3).</summary>
        public static readonly string[] ChildTops =
            [C(230, 60, 60), C(60, 140, 230), C(250, 210, 40), C(70, 190, 90), C(240, 120, 190), C(250, 150, 40)];

        /// <summary>Trousers, and a skirt's cloth.</summary>
        public static readonly string[] Bottoms =
            [C(52, 72, 110), C(150, 132, 96), C(110, 110, 112), C(36, 36, 38), C(92, 66, 44), C(40, 46, 70)];

        /// <summary>A suit's cloth.</summary>
        public static readonly string[] Suits =
            [C(52, 54, 58), C(34, 40, 62), C(28, 28, 30), C(96, 98, 102)];

        /// <summary>A suit's tie.</summary>
        public static readonly string[] Ties =
            [C(170, 30, 36), C(40, 70, 150), C(110, 30, 50), C(40, 100, 60)];

        /// <summary>A suit's white shirt.</summary>
        public static readonly string Shirt = C(238, 238, 236);

        /// <summary>An elderly person's hair (CE-1162).</summary>
        public static readonly string[] GreyHair =
            [C(200, 200, 198), C(170, 170, 168), C(232, 230, 224), C(140, 138, 134)];

        /// <summary>An elderly person's tops, when his type names no colour.</summary>
        public static readonly string[] MutedTops =
            [C(120, 100, 80), C(90, 96, 110), C(150, 140, 120), C(100, 110, 90), C(130, 90, 90), C(70, 72, 80)];

        /// <summary>The security-force uniform (navy).</summary>
        public static readonly string PoliceUniform = C(34, 46, 82);

        /// <summary>The police vest, darker than the uniform.</summary>
        public static readonly string PoliceVest = C(20, 24, 36);

        /// <summary>The vest's pouches.</summary>
        public static readonly string PolicePouch = C(30, 32, 40);

        /// <summary>The black belt.</summary>
        public static readonly string PoliceBelt = C(14, 14, 16);

        /// <summary>The police cap's white band.</summary>
        public static readonly string CapBand = C(236, 236, 232);

        /// <summary>The three tones of the friend's woodland pattern (light and dark mixed so it reads under the shading).</summary>
        public static readonly string[] Woodland =
            [C(122, 92, 56), C(26, 28, 22), C(128, 150, 84)];

        /// <summary>The three tones of the fleck pattern (sand-heavy).</summary>
        public static readonly string[] Fleck =
            [C(214, 194, 140), C(166, 156, 98), C(126, 72, 42)];

        /// <summary>The three tones of the hostile's desert pattern.</summary>
        public static readonly string[] Desert =
            [C(158, 126, 84), C(112, 84, 54), C(224, 208, 168)];

        /// <summary>A soldier's cloth by side, when neither he nor his type names one — a friend's: green-olive (R-1020).</summary>
        public static readonly string WoodlandCloth = C(96, 110, 72);

        /// <summary>A hostile's: desert khaki (R-1020).</summary>
        public static readonly string DesertCloth = C(190, 170, 125);

        /// <summary>Five eye colours.</summary>
        public static readonly string[] EyeColours =
            [C(60, 40, 28), C(96, 64, 36), C(70, 110, 160), C(80, 120, 80), C(110, 116, 120)];

        /// <summary>Four lip colours.</summary>
        public static readonly string[] LipColours =
            [C(132, 52, 48), C(150, 70, 66), C(118, 60, 56), C(170, 60, 70)];

        // ── ⭐ OF-1 / §5 — the Middle-East wardrobes (the exact RGB is a first estimate; ui tunes it against the mockup) ───────────────

        private static readonly string White = C(244, 243, 238), Cream = C(226, 212, 178), LightGrey = C(196, 196, 192), LightBlue = C(164, 190, 212),
            Brown = C(122, 94, 66), DarkBrown = C(74, 52, 38), Navy = C(30, 36, 64), DarkGreen = C(30, 56, 42), Black = C(22, 22, 24),
            DarkGrey = C(50, 50, 54), RedWhiteCheck = C(200, 120, 116),   // a red-and-white shemagh, averaged to one tone
            CapWhite = C(240, 240, 236), HijabBlue = C(36, 44, 84), HijabBrown = C(96, 70, 52), HijabGrey = C(110, 110, 112), LightPink = C(230, 190, 200),
            Khaki = C(160, 148, 110), Olive = C(96, 104, 72), FieldGrey = C(110, 110, 112), FieldBlack = C(30, 30, 34), FieldGreen = C(92, 100, 64),
            Drab = C(84, 80, 64), ClothGrey = C(140, 140, 142), ClothBlack = C(80, 80, 84), CapOlive = C(84, 90, 64);

        /// <summary>⭐ CE-4648 — the colour a Shemagh option uses to say "red-and-white check" (the presentation draws the check; any other colour is a plain cloth).</summary>
        public static string CheckShemagh => RedWhiteCheck;

        private static OutfitOption O(string kind, float weight, params string[] colours) => new() { Kind = kind, Weight = weight, Colours = [.. colours] };
        private static OutfitSlot S(params OutfitOption[] options) => new() { Options = [.. options] };

        // the Skin slot: the four middle tones of the five (light olive to brown) — ⛔ not the palest
        private static OutfitSlot MiddleEastSkin() => S(O("Skin", 1f, SkinTones[1], SkinTones[2], SkinTones[3], SkinTones[4]));
        private static OutfitOption Dark(string kind, float weight) => O(kind, weight, HairColours[0], HairColours[1]);

        /// <summary>1105 — a Middle-East man: a robe (70 %) or an everyday shirt, a shemagh, a skullcap or nothing on his head, short hair, mostly bearded.</summary>
        public static readonly OutfitDto MiddleEastMan = new()
        {
            Skin = MiddleEastSkin(),
            Top = S(O(nameof(OutfitTop.Robe), 50f, White), O(nameof(OutfitTop.Robe), 20f, Cream), O(nameof(OutfitTop.Robe), 15f, LightGrey),
                    O(nameof(OutfitTop.Robe), 10f, LightBlue), O(nameof(OutfitTop.Robe), 5f, Brown), O(nameof(OutfitTop.Shirt), 30f, Tops)),
            Legs = S(O(nameof(OutfitLegs.Trousers), 100f, Bottoms)),   // under a robe the legs are Hidden (OF-5)
            Head = S(O(nameof(OutfitHead.Shemagh), 24f, RedWhiteCheck), O(nameof(OutfitHead.Shemagh), 16f, CapWhite),
                     O(nameof(OutfitHead.Skullcap), 25f, CapWhite), O(nameof(OutfitHead.None), 35f)),
            Hair = S(Dark(nameof(OutfitHair.Short), 75f), Dark(nameof(OutfitHair.None), 25f)),
            Beard = S(Dark(nameof(OutfitBeard.None), 25f), Dark(nameof(OutfitBeard.Moustache), 25f), Dark(nameof(OutfitBeard.Beard), 50f)),
        };

        /// <summary>1106 — a Middle-East woman: an abaya (85 %) or a long-sleeved top and long skirt, a hijab (70 %) or a niqab (30 %).</summary>
        public static readonly OutfitDto MiddleEastWoman = new()
        {
            Skin = MiddleEastSkin(),
            Top = S(O(nameof(OutfitTop.Abaya), 63.75f, Black), O(nameof(OutfitTop.Abaya), 8.5f, DarkBrown), O(nameof(OutfitTop.Abaya), 8.5f, Navy),
                    O(nameof(OutfitTop.Abaya), 4.25f, DarkGreen), O(nameof(OutfitTop.Shirt), 15f, MutedTops)),
            Legs = S(O(nameof(OutfitLegs.LongSkirt), 100f, Bottoms)),
            Head = S(O(nameof(OutfitHead.Hijab), 17.5f, Black), O(nameof(OutfitHead.Hijab), 17.5f, HijabBlue), O(nameof(OutfitHead.Hijab), 17.5f, HijabBrown),
                     O(nameof(OutfitHead.Hijab), 17.5f, HijabGrey), O(nameof(OutfitHead.Niqab), 30f, Black)),
            Hair = S(Dark(nameof(OutfitHair.Long), 100f)),   // hidden under the head covering (OF-5)
            Beard = S(Dark(nameof(OutfitBeard.None), 100f)),
        };

        /// <summary>1107 — a Middle-East boy: a light robe (40 %) or a bright T-shirt, trousers or shorts, sometimes a skullcap.</summary>
        public static readonly OutfitDto MiddleEastBoy = new()
        {
            Skin = MiddleEastSkin(),
            Top = S(O(nameof(OutfitTop.Robe), 20f, White), O(nameof(OutfitTop.Robe), 10f, Cream), O(nameof(OutfitTop.Robe), 10f, LightBlue),
                    O(nameof(OutfitTop.TShirt), 60f, ChildTops)),
            Legs = S(O(nameof(OutfitLegs.Trousers), 70f, Bottoms), O(nameof(OutfitLegs.Shorts), 30f, Bottoms)),
            Head = S(O(nameof(OutfitHead.Skullcap), 20f, CapWhite), O(nameof(OutfitHead.None), 80f)),
            Hair = S(Dark(nameof(OutfitHair.Short), 100f)),
            Beard = S(Dark(nameof(OutfitBeard.None), 100f)),
        };

        /// <summary>1108 — a Middle-East girl: a bright top, a long skirt (70 %) or trousers, sometimes a light hijab, long hair.</summary>
        public static readonly OutfitDto MiddleEastGirl = new()
        {
            Skin = MiddleEastSkin(),
            Top = S(O(nameof(OutfitTop.Shirt), 50f, ChildTops), O(nameof(OutfitTop.TShirt), 50f, ChildTops)),
            Legs = S(O(nameof(OutfitLegs.LongSkirt), 70f, Bottoms), O(nameof(OutfitLegs.Trousers), 30f, Bottoms)),
            Head = S(O(nameof(OutfitHead.Hijab), 15f, CapWhite), O(nameof(OutfitHead.Hijab), 10f, LightPink), O(nameof(OutfitHead.Hijab), 10f, LightBlue),
                     O(nameof(OutfitHead.None), 65f)),
            Hair = S(Dark(nameof(OutfitHair.Long), 100f)),
            Beard = S(Dark(nameof(OutfitBeard.None), 100f)),
        };

        /// <summary>1109 — an elderly Middle-East man: a robe (85 %), a shemagh (60 %) or skullcap, grey hair, a grey beard (80 %).</summary>
        public static readonly OutfitDto MiddleEastElderlyMan = new()
        {
            Skin = MiddleEastSkin(),
            Top = S(O(nameof(OutfitTop.Robe), 40f, White), O(nameof(OutfitTop.Robe), 25f, LightGrey), O(nameof(OutfitTop.Robe), 20f, Brown),
                    O(nameof(OutfitTop.Shirt), 15f, MutedTops)),
            Legs = S(O(nameof(OutfitLegs.Trousers), 100f, Bottoms)),
            Head = S(O(nameof(OutfitHead.Shemagh), 30f, RedWhiteCheck), O(nameof(OutfitHead.Shemagh), 30f, CapWhite),
                     O(nameof(OutfitHead.Skullcap), 25f, CapWhite), O(nameof(OutfitHead.None), 15f)),
            Hair = S(O(nameof(OutfitHair.Short), 60f, GreyHair), O(nameof(OutfitHair.None), 40f, GreyHair)),
            Beard = S(O(nameof(OutfitBeard.Beard), 80f, GreyHair), O(nameof(OutfitBeard.None), 20f, GreyHair)),
        };

        /// <summary>1110 — an elderly Middle-East woman: an abaya (95 %), a hijab (75 %) or a niqab.</summary>
        public static readonly OutfitDto MiddleEastElderlyWoman = new()
        {
            Skin = MiddleEastSkin(),
            Top = S(O(nameof(OutfitTop.Abaya), 66.5f, Black), O(nameof(OutfitTop.Abaya), 14.25f, DarkGrey), O(nameof(OutfitTop.Abaya), 14.25f, DarkBrown),
                    O(nameof(OutfitTop.Shirt), 5f, MutedTops)),
            Legs = S(O(nameof(OutfitLegs.LongSkirt), 100f, Bottoms)),
            Head = S(O(nameof(OutfitHead.Hijab), 45f, Black), O(nameof(OutfitHead.Hijab), 30f, HijabBlue), O(nameof(OutfitHead.Niqab), 25f, Black)),
            Hair = S(O(nameof(OutfitHair.Long), 100f, GreyHair)),
            Beard = S(Dark(nameof(OutfitBeard.None), 100f)),
        };

        /// <summary>⭐ CE-4649 (bows BW-12) — an ARCHER / CROSSBOWMAN's everyday clothes: a shirt or a jacket in earth tones, trousers, a cap or nothing, short hair, often
        /// bearded. Not a uniform: no camouflage (OF-11).</summary>
        public static readonly OutfitDto Archer = new()
        {
            Skin = S(O("Skin", 1f, SkinTones)),
            Top = S(O(nameof(OutfitTop.Shirt), 50f, Khaki, Olive, Brown, FieldGrey), O(nameof(OutfitTop.Jacket), 50f, Olive, Khaki, DarkBrown)),
            Legs = S(O(nameof(OutfitLegs.Trousers), 100f, C(106, 100, 76), C(70, 72, 56), C(60, 52, 44))),
            Head = S(O(nameof(OutfitHead.Cap), 40f, CapOlive, Brown), O(nameof(OutfitHead.None), 60f)),
            Hair = S(O(nameof(OutfitHair.Short), 100f, HairColours)),
            Beard = S(O(nameof(OutfitBeard.None), 60f, HairColours[0]), O(nameof(OutfitBeard.Beard), 40f, HairColours[0])),
        };

        /// <summary>
        /// 2003 / 2010 / 2011 / 2012 — a Middle-East FIGHTER (OF-10, OF-12): drab shirt, military jacket or robe, often an ammunition chest rig, a
        /// shemagh (sometimes across the face), a cap or nothing, short hair, almost always bearded. ⛔ No camouflage: his top is no <c>Uniform</c>.
        /// </summary>
        public static readonly OutfitDto MiddleEastFighter = new()
        {
            Skin = MiddleEastSkin(),
            Top = S(O(nameof(OutfitTop.Shirt), 10f, Khaki), O(nameof(OutfitTop.Shirt), 10f, Olive), O(nameof(OutfitTop.Shirt), 10f, FieldGrey), O(nameof(OutfitTop.Shirt), 10f, FieldBlack),
                    O(nameof(OutfitTop.Jacket), 10f, Olive), O(nameof(OutfitTop.Jacket), 10f, Khaki), O(nameof(OutfitTop.Jacket), 10f, FieldGreen),
                    O(nameof(OutfitTop.Robe), 12f, White), O(nameof(OutfitTop.Robe), 10f, LightGrey), O(nameof(OutfitTop.Robe), 8f, Brown)),
            Over = S(O(nameof(OutfitOver.ChestRig), 75f, Drab), O(nameof(OutfitOver.None), 25f)),   // ⭐ CE-4648 polish: a rig in 75 % of fighters (was 60)
            Legs = S(O(nameof(OutfitLegs.Trousers), 100f, C(106, 100, 76), C(70, 72, 56), C(48, 48, 50), C(120, 112, 88))),
            // ⭐ CE-4648 polish (user review): head cover — a shemagh (40), a face wrap (32) or a skullcap (8) — in 80 % of fighters, a cap in 8, bareheaded in 12
            Head = S(O(nameof(OutfitHead.Shemagh), 20f, RedWhiteCheck), O(nameof(OutfitHead.Shemagh), 12f, CapWhite), O(nameof(OutfitHead.Shemagh), 8f, ClothBlack),
                     O(nameof(OutfitHead.ShemaghFaceWrap), 14f, Khaki), O(nameof(OutfitHead.ShemaghFaceWrap), 10f, Drab), O(nameof(OutfitHead.ShemaghFaceWrap), 8f, RedWhiteCheck),
                     O(nameof(OutfitHead.Skullcap), 8f, CapWhite),
                     O(nameof(OutfitHead.Cap), 4f, CapOlive), O(nameof(OutfitHead.Cap), 4f, FieldBlack), O(nameof(OutfitHead.None), 12f)),
            Hair = S(Dark(nameof(OutfitHair.Short), 100f)),
            Beard = S(Dark(nameof(OutfitBeard.Beard), 70f), Dark(nameof(OutfitBeard.Moustache), 15f), Dark(nameof(OutfitBeard.None), 15f)),
        };

        // ── ⭐ CE-4648 (R-1065) — the Native American warrior's look, one wardrobe (backend's type 2xxx gives it to the warrior: NOTE_Ui_To_Backend_Native_American_Look.md) ──

        private static readonly string BuckskinTan = C(201, 160, 106), BuckskinWarm = C(184, 140, 88), BuckskinPale = C(214, 180, 128), LeggingBrown = C(156, 118, 70), LeggingDark = C(130, 96, 58),
            BandRed = C(140, 42, 36), BandBlack = C(24, 24, 26), BandBlue = C(40, 70, 130), BlackHair = C(20, 18, 18);

        /// <summary>The Native American warrior: a buckskin shirt, breechcloth and leggings; ALWAYS long black hair with a braid on each side; a headband with feathers; face
        /// paint in two of three; a beaded yoke and / or a bone hair-pipe breastplate; armbands and / or a bead necklace — all seeded inside these lists.</summary>
        public static readonly OutfitDto NativeAmericanWarrior = new()
        {
            Skin = MiddleEastSkin(),
            Top = S(O(nameof(OutfitTop.Buckskin), 1f, BuckskinTan, BuckskinWarm, BuckskinPale)),
            Legs = S(O(nameof(OutfitLegs.Breechcloth), 1f, LeggingBrown, LeggingDark)),
            Over = S(O(nameof(OutfitOver.BeadedYoke), 40f), O(nameof(OutfitOver.HairPipe), 25f), O(nameof(OutfitOver.YokeHairPipe), 15f), O(nameof(OutfitOver.None), 20f)),
            Head = S(O(nameof(OutfitHead.Headband), 90f, BandRed, BandBlack, BandBlue), O(nameof(OutfitHead.None), 10f)),
            Hair = S(O(nameof(OutfitHair.Braids), 1f, BlackHair)),
            Beard = S(O(nameof(OutfitBeard.Paint), 65f), O(nameof(OutfitBeard.None), 35f)),
            Extra = S(O(nameof(OutfitExtra.Armbands), 25f), O(nameof(OutfitExtra.Necklace), 25f), O(nameof(OutfitExtra.ArmbandsNecklace), 30f), O(nameof(OutfitExtra.None), 20f)),
        };

        // ── ⭐ CE-4648 (R-1066) — Afghan dress: perahan tunban (earthy), waistcoat, pakol, lungee, patu, chadari ────────────────────────────

        private static readonly string AfWhite = C(240, 238, 230), AfGrey = C(150, 150, 148), AfBeige = C(200, 184, 150), AfBrown = C(122, 98, 70), AfPaleBlue = C(169, 184, 200), AfOlive = C(110, 112, 78),
            PakolBeige = C(185, 165, 127), PakolBrown = C(110, 86, 60), PakolGrey = C(120, 120, 118), VestBlack = C(30, 30, 32), VestGrey = C(72, 72, 76), VestBrown = C(80, 58, 40),
            PatuBrown = C(107, 91, 69), PatuGrey = C(120, 120, 118), PatuOlive = C(98, 100, 70), LungeeWhite = C(240, 238, 232), LungeeBlack = C(35, 35, 38), LungeeGrey = C(110, 110, 114),
            ChadariBlue = C(111, 160, 218), ChadariWhite = C(232, 232, 236), ChadariGrey = C(150, 152, 158), HeadscarfBlack = C(28, 28, 30), HeadscarfBrown = C(90, 66, 50), HeadscarfGreen = C(40, 70, 52);
        private static readonly string[] PerahanColours = [AfWhite, AfGrey, AfBeige, AfBrown, AfPaleBlue, AfOlive];

        private static OutfitSlot AfghanSkin() => S(O("Skin", 1f, SkinTones[1], SkinTones[2], SkinTones[3]));

        /// <summary>1111 — an Afghan man: perahan tunban (earth colours; the trousers follow the tunic), a pakol, a lungee or a skullcap, often a waistcoat or a patu, mostly bearded.</summary>
        public static readonly OutfitDto AfghanMan = new()
        {
            Skin = AfghanSkin(),
            Top = S(O(nameof(OutfitTop.Perahan), 1f, PerahanColours)),
            Legs = S(O(nameof(OutfitLegs.Tunban), 1f)),
            Head = S(O(nameof(OutfitHead.Pakol), 38f, PakolBeige, PakolBrown, PakolGrey), O(nameof(OutfitHead.Turban), 27f, LungeeWhite, LungeeBlack, LungeeGrey),
                     O(nameof(OutfitHead.Skullcap), 10f, CapWhite), O(nameof(OutfitHead.None), 25f)),
            Hair = S(Dark(nameof(OutfitHair.Short), 100f)),
            Beard = S(Dark(nameof(OutfitBeard.Beard), 55f), Dark(nameof(OutfitBeard.Moustache), 20f), Dark(nameof(OutfitBeard.None), 25f)),
            Extra = S(O(nameof(OutfitExtra.Waistcoat), 40f, VestBlack, VestGrey, VestBrown), O(nameof(OutfitExtra.Patu), 20f, PatuBrown, PatuGrey, PatuOlive),
                      O(nameof(OutfitExtra.WaistcoatPatu), 5f, PatuBrown, PatuGrey, PatuOlive), O(nameof(OutfitExtra.None), 35f)),
        };

        /// <summary>1115 — an Afghan elderly man: perahan tunban, mostly a large lungee, a patu round his shoulders, grey hair and a long beard.</summary>
        public static readonly OutfitDto AfghanElderlyMan = new()
        {
            Skin = AfghanSkin(),
            Top = S(O(nameof(OutfitTop.Perahan), 1f, AfWhite, AfGrey, AfBeige, AfBrown)),
            Legs = S(O(nameof(OutfitLegs.Tunban), 1f)),
            Head = S(O(nameof(OutfitHead.Turban), 70f, LungeeWhite, LungeeGrey, LungeeBlack), O(nameof(OutfitHead.Pakol), 15f, PakolBeige, PakolBrown, PakolGrey),
                     O(nameof(OutfitHead.Skullcap), 5f, CapWhite), O(nameof(OutfitHead.None), 10f)),
            Hair = S(O(nameof(OutfitHair.Short), 100f, GreyHair)),
            Beard = S(O(nameof(OutfitBeard.Beard), 92f, GreyHair), O(nameof(OutfitBeard.Moustache), 8f, GreyHair)),
            Extra = S(O(nameof(OutfitExtra.Patu), 70f, PatuBrown, PatuGrey, PatuOlive), O(nameof(OutfitExtra.Waistcoat), 10f, VestBlack, VestGrey, VestBrown), O(nameof(OutfitExtra.None), 20f)),
        };

        /// <summary>1112 — an Afghan woman: mostly the chadari (light blue, some white or grey), else a dark perahan tunban and a dark headscarf.</summary>
        public static readonly OutfitDto AfghanWoman = new()
        {
            Skin = AfghanSkin(),
            Top = S(O(nameof(OutfitTop.Chadari), 70f, ChadariBlue), O(nameof(OutfitTop.Chadari), 7f, ChadariWhite), O(nameof(OutfitTop.Chadari), 7f, ChadariGrey),
                    O(nameof(OutfitTop.Perahan), 16f, AfBrown, AfOlive, AfGrey, HeadscarfBlack)),
            Legs = S(O(nameof(OutfitLegs.Tunban), 1f)),
            Head = S(O(nameof(OutfitHead.Hijab), 1f, HeadscarfBlack, HeadscarfBrown, HeadscarfGreen)),
            Hair = S(Dark(nameof(OutfitHair.Long), 100f)),
            Beard = S(O(nameof(OutfitBeard.None), 1f)),
        };

        /// <summary>1116 — an Afghan elderly woman: the chadari (70 %) or a dark perahan tunban and headscarf.</summary>
        public static readonly OutfitDto AfghanElderlyWoman = new()
        {
            Skin = AfghanSkin(),
            Top = S(O(nameof(OutfitTop.Chadari), 55f, ChadariBlue), O(nameof(OutfitTop.Chadari), 8f, ChadariWhite), O(nameof(OutfitTop.Chadari), 7f, ChadariGrey),
                    O(nameof(OutfitTop.Perahan), 30f, AfBrown, AfGrey, HeadscarfBlack)),
            Legs = S(O(nameof(OutfitLegs.Tunban), 1f)),
            Head = S(O(nameof(OutfitHead.Hijab), 1f, HeadscarfBlack, HeadscarfBrown, HeadscarfGreen)),
            Hair = S(O(nameof(OutfitHair.Long), 1f, GreyHair)),
            Beard = S(O(nameof(OutfitBeard.None), 1f)),
        };

        /// <summary>1113 — an Afghan boy: a small perahan tunban, a pakol or a skullcap, sometimes a waistcoat.</summary>
        public static readonly OutfitDto AfghanBoy = new()
        {
            Skin = AfghanSkin(),
            Top = S(O(nameof(OutfitTop.Perahan), 1f, AfWhite, AfBeige, AfPaleBlue, AfGrey, AfOlive)),
            Legs = S(O(nameof(OutfitLegs.Tunban), 1f)),
            Head = S(O(nameof(OutfitHead.Pakol), 60f, PakolBeige, PakolBrown, PakolGrey), O(nameof(OutfitHead.Skullcap), 15f, CapWhite), O(nameof(OutfitHead.None), 25f)),
            Hair = S(Dark(nameof(OutfitHair.Short), 100f)),
            Beard = S(O(nameof(OutfitBeard.None), 1f)),
            Extra = S(O(nameof(OutfitExtra.Waistcoat), 25f, VestBlack, VestGrey, VestBrown), O(nameof(OutfitExtra.None), 75f)),
        };

        /// <summary>1114 — an Afghan girl: a bright dress over loose trousers and a coloured headscarf.</summary>
        public static readonly OutfitDto AfghanGirl = new()
        {
            Skin = AfghanSkin(),
            Top = S(O(nameof(OutfitTop.Perahan), 1f, ChildTops)),
            Legs = S(O(nameof(OutfitLegs.Tunban), 1f)),
            Head = S(O(nameof(OutfitHead.Hijab), 85f, C(62, 154, 143), C(230, 190, 60), C(214, 90, 130), C(110, 130, 200)), O(nameof(OutfitHead.None), 15f)),
            Hair = S(Dark(nameof(OutfitHair.Long), 100f)),
            Beard = S(O(nameof(OutfitBeard.None), 1f)),
        };

        /// <summary>2016–2018 — an Afghan FIGHTER: a perahan tunban, a lungee (black, dark grey or white), a pakol or a shemagh (head cover in 90 %), a chest rig in 75 %, often a waistcoat or a patu, almost always bearded. No camouflage.</summary>
        public static readonly OutfitDto AfghanFighter = new()
        {
            Skin = AfghanSkin(),
            Top = S(O(nameof(OutfitTop.Perahan), 1f, AfOlive, C(160, 148, 110), AfGrey, AfBrown, AfWhite, AfPaleBlue)),
            Legs = S(O(nameof(OutfitLegs.Tunban), 1f)),
            Over = S(O(nameof(OutfitOver.ChestRig), 75f, Drab), O(nameof(OutfitOver.None), 25f)),
            Head = S(O(nameof(OutfitHead.Turban), 22f, LungeeBlack), O(nameof(OutfitHead.Turban), 12f, LungeeGrey), O(nameof(OutfitHead.Turban), 11f, LungeeWhite),
                     O(nameof(OutfitHead.Pakol), 30f, PakolBeige, PakolBrown, PakolGrey), O(nameof(OutfitHead.Shemagh), 10f, RedWhiteCheck, CapWhite),
                     O(nameof(OutfitHead.Skullcap), 5f, CapWhite), O(nameof(OutfitHead.None), 10f)),
            Hair = S(Dark(nameof(OutfitHair.Short), 100f)),
            Beard = S(Dark(nameof(OutfitBeard.Beard), 75f), Dark(nameof(OutfitBeard.Moustache), 12f), Dark(nameof(OutfitBeard.None), 13f)),
            Extra = S(O(nameof(OutfitExtra.Waistcoat), 25f, VestBlack, VestGrey, VestBrown), O(nameof(OutfitExtra.Patu), 25f, PatuBrown, PatuGrey, PatuOlive),
                      O(nameof(OutfitExtra.WaistcoatPatu), 5f, PatuBrown, PatuGrey, PatuOlive), O(nameof(OutfitExtra.None), 45f)),
        };
    }
}
