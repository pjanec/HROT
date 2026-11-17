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

        private static readonly string White = C(236, 234, 226), Cream = C(226, 212, 178), LightGrey = C(196, 196, 192), LightBlue = C(164, 190, 212),
            Brown = C(122, 94, 66), DarkBrown = C(74, 52, 38), Navy = C(30, 36, 64), DarkGreen = C(30, 56, 42), Black = C(22, 22, 24),
            DarkGrey = C(50, 50, 54), RedWhiteCheck = C(200, 120, 116),   // a red-and-white shemagh, averaged to one tone
            CapWhite = C(240, 240, 236), HijabBlue = C(36, 44, 84), HijabBrown = C(96, 70, 52), HijabGrey = C(110, 110, 112), LightPink = C(230, 190, 200),
            Khaki = C(160, 148, 110), Olive = C(96, 104, 72), FieldGrey = C(110, 110, 112), FieldBlack = C(30, 30, 34), FieldGreen = C(92, 100, 64),
            Drab = C(84, 80, 64), ClothGrey = C(140, 140, 142), ClothBlack = C(80, 80, 84), CapOlive = C(84, 90, 64);

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
        /// ⭐ <c>CE-4650</c> NA-2 (DESIGN_Bows_And_Crossbows.md §9) — the NATIVE AMERICAN WARRIOR: a buckskin shirt with fringe or a bare chest (skin tone), leggings and a breechcloth (buckskin, dark), a headband with feathers / a single
        /// feather / nothing, black hair in two braids or loose, stripes of face paint on a third of them. Drawn with stand-ins (the presentation lists them): buckskin = a shirt in tan, bare chest = a shirt in his skin tone, leggings = trousers,
        /// breechcloth = the short-skirt box, headband = the police-cap band in its colour, feather and face paint = not drawn, braids = the long-hair box.
        /// </summary>
        public static readonly OutfitDto NativeWarrior = new()
        {
            Skin = S(O("Skin", 1f, C(198, 150, 110), C(176, 126, 88), C(160, 112, 76))),
            Top = S(O(nameof(OutfitTop.Buckskin), 50f, C(190, 150, 100), C(176, 136, 90), C(160, 124, 84)), O(nameof(OutfitTop.Shirt), 50f, C(198, 150, 110), C(176, 126, 88), C(160, 112, 76))),
            Legs = S(O(nameof(OutfitLegs.Trousers), 100f, C(170, 134, 90), C(150, 116, 76), C(132, 100, 64))),
            Over = S(O(nameof(OutfitOver.Breechcloth), 100f, C(96, 70, 46), C(70, 52, 36), C(52, 40, 30))),
            Head = S(O(nameof(OutfitHead.Headband), 60f, C(170, 30, 30), C(30, 30, 34), C(230, 228, 220)), O(nameof(OutfitHead.Feather), 25f, C(236, 232, 224), C(60, 44, 30)), O(nameof(OutfitHead.None), 15f)),
            Hair = S(O(nameof(OutfitHair.Braids), 60f, HairColours[0]), O(nameof(OutfitHair.Long), 40f, HairColours[0])),
            FacePaint = S(O(nameof(OutfitFacePaint.Stripes), 30f, C(170, 30, 30), C(236, 232, 224), C(24, 24, 26)), O(nameof(OutfitFacePaint.None), 70f)),
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
            Over = S(O(nameof(OutfitOver.ChestRig), 60f, Drab), O(nameof(OutfitOver.None), 40f)),
            Legs = S(O(nameof(OutfitLegs.Trousers), 100f, C(106, 100, 76), C(70, 72, 56), C(48, 48, 50), C(120, 112, 88))),
            Head = S(O(nameof(OutfitHead.Shemagh), 20f, RedWhiteCheck), O(nameof(OutfitHead.Shemagh), 12f, CapWhite), O(nameof(OutfitHead.Shemagh), 8f, ClothBlack),
                     O(nameof(OutfitHead.ShemaghFaceWrap), 10f, CapWhite), O(nameof(OutfitHead.ShemaghFaceWrap), 8f, ClothGrey), O(nameof(OutfitHead.ShemaghFaceWrap), 7f, FieldBlack),
                     O(nameof(OutfitHead.Cap), 8f, CapOlive), O(nameof(OutfitHead.Cap), 7f, FieldBlack), O(nameof(OutfitHead.None), 20f)),
            Hair = S(Dark(nameof(OutfitHair.Short), 100f)),
            Beard = S(Dark(nameof(OutfitBeard.Beard), 70f), Dark(nameof(OutfitBeard.Moustache), 15f), Dark(nameof(OutfitBeard.None), 15f)),
        };
    }
}
