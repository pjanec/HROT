// CE-4651 (U-18y) — rails for HrotCards: HROT's symbol code and geometry, and its card layout, ported (numbers worked by hand from HROT's
// SymbolCode / SymbolGeometry / SymbolRenderer.Measure / EntityCardRenderer.Layout).
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Cards; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotCards.h"
#include "HrotCardLayer.h"

#if WITH_DEV_AUTOMATION_TESTS

using namespace HrotCards;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotCardsSymbolTest, "Hrot.Dps.Cards.Symbol",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotCardsSymbolTest::RunTest(const FString&)
{
	FSymbol S; S.Affiliation = EAffiliation::Friend; S.Role = ERole::Mechanized; S.Echelon = EEchelon::Platoon; S.Mobility = EMobility::Tracked; S.Secondary = ERole::AntiArmour;
	TestEqual(TEXT("the wire form, HROT's own example"), S.Format(), FString(TEXT("SFGPUCIZ---D---|T|AT")));
	FSymbol Back;
	TestTrue(TEXT("parses"), FSymbol::Parse(S.Format(), Back));
	TestTrue(TEXT("round trip"), Back == S);

	FSymbol H; H.Affiliation = EAffiliation::Hostile; H.Role = ERole::Infantry; H.Echelon = EEchelon::Squad;
	TestEqual(TEXT("a hostile infantry squad"), H.Format(), FString(TEXT("SHGPUCI----B---")));
	FSymbol Hq; Hq.Affiliation = EAffiliation::Neutral; Hq.Role = ERole::Headquarters; Hq.Echelon = EEchelon::Company;
	TestEqual(TEXT("a headquarters marks position 11"), Hq.Format(), FString(TEXT("SNGPUH----AE---")));

	FSymbol P;
	TestTrue(TEXT("a plain 2525C SIDC"), FSymbol::Parse(TEXT("SFGPUCI--------"), P));
	TestTrue(TEXT("  friend infantry, no echelon"), P.Affiliation == EAffiliation::Friend && P.Role == ERole::Infantry && P.Echelon == EEchelon::NoEchelon);
	TestTrue(TEXT("suspect counts as hostile"), FSymbol::Parse(TEXT("SSGPUCA----E---"), P) && P.Affiliation == EAffiliation::Hostile && P.Role == ERole::Armour);
	TestTrue(TEXT("an unknown land function: the frame only"), FSymbol::Parse(TEXT("SFGPUXYZ-------"), P) && P.Role == ERole::NoRole);
	TestFalse(TEXT("not a land unit (an air track)"), FSymbol::Parse(TEXT("SFAPMF---------"), P));
	TestFalse(TEXT("too short"), FSymbol::Parse(TEXT("SFGPUCI"), P));

	// SymbolRenderer.Measure for a friend infantry squad at 22 px (by hand): frame +-16.5 x +-11, outline 1.54 -> +-17.27 / 11.77;
	// the squad dot at y -15.84, r 1.76 -> top -17.6; pad 2, floor/ceil -> 40 x 34, the centre at (20, 20)
	FSymbol Sq; Sq.Affiliation = EAffiliation::Friend; Sq.Role = ERole::Infantry; Sq.Echelon = EEchelon::Squad;
	float W, Hh, Cx, Cy; MeasureSymbol(Sq, 22, W, Hh, Cx, Cy);
	TestEqual(TEXT("measure width"), W, 40.f); TestEqual(TEXT("measure height"), Hh, 34.f);
	TestEqual(TEXT("centre x"), Cx, 20.f); TestEqual(TEXT("centre y"), Cy, 20.f);
	MeasureSymbol(Sq, 12, W, Hh, Cx, Cy);
	TestTrue(TEXT("no echelon marks under 15 px: the box shrinks to the frame"), Hh < 20.f && Cy < 10.f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotCardsLayoutTest, "Hrot.Dps.Cards.Layout",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotCardsLayoutTest::RunTest(const FString&)
{
	auto Measure = [](const FString& T, float) { return 7.f * T.Len(); };   // 7 px a character
	FCard C; C.EntityId = 1043;
	C.Frame(SideColour(2), 150, 1.f);
	C.Bar(10, 0.5f, 0x00FF00FF, 0x282828DC, 4, 1.f);
	C.Text(20, TEXT("T-72 #1043"), 0xFFFFFFFF, 13, 0, 2.f);

	FLayout L;
	TestTrue(TEXT("close: a card"), Layout(C, 0.5f, Measure, L));
	// the width: max(72 - 8, 70) + 8 = 78; the rows: bar 4 + gap 2 + name 13 = 19, + padding 8 = 27
	TestEqual(TEXT("width"), L.W, 78.f); TestEqual(TEXT("height"), L.H, 27.f);
	TestEqual(TEXT("leader in the frame's side colour"), L.LeaderColour, SideColour(2));
	int32 Rows = 0; float NameY = -1, BarY = -1;
	for (const FPlaced& P : L.Placed)
		if (!P.bCardWide) { Rows++; if (C.Items[P.Item].Kind == EItem::Text) NameY = P.Origin.Y; else BarY = P.Origin.Y; }
	TestEqual(TEXT("bar back + fill + name"), Rows, 3);
	TestEqual(TEXT("the bar's row on top (row 10)"), BarY, 4.f);
	TestEqual(TEXT("the name's row under it (row 20)"), NameY, 10.f);

	TestTrue(TEXT("1.5 m/px: the frame and bar are cut, the name stays"), Layout(C, 1.5f, Measure, L));
	TestEqual(TEXT("  a bare name: 78 x 21"), FVector2f(L.W, L.H), FVector2f(78, 21));
	TestEqual(TEXT("  only the name is visible"), L.VisibleMask, uint64(1) << 4);
	TestFalse(TEXT("3 m/px: nothing"), Layout(C, 3.f, Measure, L));

	// a unit: the symbol is the left cell (40 + 4 wide at 22 px), the rows move right of it and the card is as tall as the symbol
	FCard U; U.EntityId = 7;
	U.Frame(SideColour(1));
	FSymbol Sq; Sq.Affiliation = EAffiliation::Friend; Sq.Role = ERole::Infantry; Sq.Echelon = EEchelon::Squad;
	U.Symbol(Sq, 22);
	U.Text(20, TEXT("1st sqd"), 0xFFFFFFFF);
	TestTrue(TEXT("a unit card"), Layout(U, 0.f, Measure, L));
	TestEqual(TEXT("  cell"), L.CellW, 44.f);
	TestEqual(TEXT("  width = 64 + 44 + 8"), L.W, 116.f);
	TestEqual(TEXT("  height = the symbol 34 + 8"), L.H, 42.f);
	for (const FPlaced& P : L.Placed)
		if (!P.bCardWide) { TestEqual(TEXT("  the name right of the cell"), P.Origin.X, 48.f); TestEqual(TEXT("  centred beside the symbol"), P.Origin.Y, 4.f + (34.f - 13.f) * 0.5f); }

	// HROT EntityCardRenderer.Layout (checked line by line): the content height = max(the symbol's, the rows'), the rows centred only when shorter.
	// A unit with FOUR rows taller than its symbol: strength bar + ticks (7), ammo (3), name (13), info (12) + 3 gaps = 41 > the symbol's 34
	FCard T4; T4.EntityId = 9;
	T4.Frame(SideColour(1)); T4.Symbol(Sq, 22);
	T4.Bar(10, 0.8f, 0xF2C033FF, 0x3A3A3ADC, 5.f, 1.f, 1.f); T4.Tick(10, 0.7f, 0xDCDCE1FF, 7.f, 1.f); T4.Tick(10, 0.9f, 0xDCDCE1FF, 7.f, 1.f);
	T4.Bar(13, 0.3f, 0xF2C033FF, 0x3A3A3ADC, 3.f, 1.f);
	T4.Text(20, TEXT("Alpha #9"), 0xFFFFFFFF, 13, 0, 2.f);
	T4.Text(40, TEXT("7/9"), 0xF2C033FF, 12, 0, 2.f); T4.Text(40, TEXT("· Assault"), 0x3EC6B0FF, 12, 0, 2.f, true);
	TestTrue(TEXT("a symbol card with four rows"), Layout(T4, 0.f, Measure, L));
	TestEqual(TEXT("  H = the rows' 41 + padding 8 (not the symbol's 42)"), L.H, 49.f);
	float RowsTop = 1e9f, RowsBottom = -1e9f;
	for (const FPlaced& P : L.Placed)
		if (!P.bCardWide) { RowsTop = FMath::Min(RowsTop, P.Origin.Y); RowsBottom = FMath::Max(RowsBottom, P.Origin.Y + P.RowHeight); TestTrue(TEXT("  every row inside the card, a padding from its edge"), P.Origin.Y >= Padding && P.Origin.Y + P.RowHeight <= L.H - Padding); }
	TestTrue(TEXT("  the rows span exactly the content (41)"), FMath::IsNearlyEqual(RowsBottom - RowsTop, 41.f));
	TestTrue(TEXT("  the info run follows the strength text"), L.Placed.ContainsByPredicate([&](const FPlaced& P) { return T4.Items[P.Item].bAfterPrev && P.ExtraX > 0; }));
	TestTrue(TEXT("the hash moves with the content"), HashOf(C) != HashOf(U));
	FCard C2 = C; C2.Items.Last().Text = TEXT("T-72 #1044");
	TestTrue(TEXT("  a renamed card redraws"), HashOf(C) != HashOf(C2));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotCardsGizmoTest, "Hrot.Dps.Cards.Gizmo",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotCardsGizmoTest::RunTest(const FString&)
{
	// DD-024: HROT's Frame (a background box + an outline box on row 0), Bar (row 10, two fraction boxes), Text (row 20) -> the same card the
	// stand-in builds; the client's cutoffs (R-144) applied by kind, none for an Air anchor
	// (X, W) fractions of the width; (Y, H) HROT's centre / half-extent; Px = Width (a Text's font px, a Box's outline px)
	auto Prim = [](EGizmoShape Sh, uint8 Row, float X, float W, float Y, float H, uint32 Col, uint32 Fill, const TCHAR* T = TEXT(""), float Px = 0)
	{ FGizmoCardPrim P; P.Shape = Sh; P.AnchorId = 42; P.Row = Row; P.CardX = X; P.CardW = W; P.CardY = Y; P.CardH = H; P.ColourRgba = Col; P.FillRgba = Fill; P.Text = T; P.Width = Px; return P; };
	FCard C;
	TestTrue(TEXT("frame background"), AddGizmo(C, Prim(EGizmoShape::Box, 0, 0.5f, 0.5f, 0.5f, 0.5f, 0x14161A96, 0x14161A96), false));
	TestTrue(TEXT("frame outline"), AddGizmo(C, Prim(EGizmoShape::Box, 0, 0.5f, 0.5f, 0.5f, 0.5f, 0xFF0000FF, 0, TEXT(""), 1), false));
	TestTrue(TEXT("bar back"), AddGizmo(C, Prim(EGizmoShape::Box, 10, 0.5f, 0.5f, 2, 2, 0x282828DC, 0x282828DC), false));
	TestTrue(TEXT("name"), AddGizmo(C, Prim(EGizmoShape::Text, 20, 0, 0, 0, 0, 0xF0F0F0FF, 0, TEXT("T-72 #42"), 13), false));
	TestFalse(TEXT("a world-space shape is not a card's"), AddGizmo(C, Prim(EGizmoShape::Arrow, 0, 0, 0, 0, 0, 0, 0), false));
	TestEqual(TEXT("the card's anchor"), C.EntityId, int64(42));
	TestEqual(TEXT("four items"), C.Items.Num(), 4);
	TestTrue(TEXT("the outline is an outline"), C.Items[1].Thickness > 0 && C.Items[1].Fill == 0);
	TestTrue(TEXT("the bar is a fraction of the row"), C.Items[2].bFraction && C.Items[2].A.Y == 2.f);
	auto Measure = [](const FString& T, float) { return 7.f * T.Len(); };
	FLayout L;
	TestTrue(TEXT("lays out"), Layout(C, 0.5f, Measure, L));
	TestEqual(TEXT("  like the stand-in: 72 x 27"), FVector2f(L.W, L.H), FVector2f(72, 27));
	TestTrue(TEXT("1.5 m/px: the name only"), Layout(C, 1.5f, Measure, L) && L.VisibleMask == (uint64(1) << 3));
	FCard Air;
	AddGizmo(Air, Prim(EGizmoShape::Box, 0, 0.5f, 0.5f, 0.5f, 0.5f, 0x0064FFFF, 0, TEXT(""), 1), true);
	AddGizmo(Air, Prim(EGizmoShape::Text, 20, 0, 0, 0, 0, 0xF0F0F0FF, 0, TEXT("Eagle 1"), 13), true);
	TestTrue(TEXT("rule A: an aircraft's card at 50 m/px"), Layout(Air, 50.f, Measure, L) && L.VisibleMask == 3);
	FCard U;
	TestTrue(TEXT("a symbol cell"), AddGizmo(U, Prim(EGizmoShape::Icon, 0, 0, 0, 0, 22, 0xFFFFFFFF, 0, TEXT("SFGPUCI----B---")), false));
	TestTrue(TEXT("  as a symbol of 22 px"), U.Items[0].Kind == EItem::Symbol && U.Items[0].Px == 22.f);
	return true;
}

// a gizmo line's pixel width in world cm: the viewport's width covers 2 d tan(hfov/2) at distance d
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotCardsGizmoPxTest, "Hrot.Dps.Cards.GizmoPx",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotCardsGizmoPxTest::RunTest(const FString&)
{
	// 90 deg: tan 1; 1000 px wide, 3000 cm away: the view spans 6000 cm, 6 cm a pixel
	TestEqual(TEXT("2 px at 30 m = 12 cm"), PxToCm(2.f, 3000.f, 1.f, 1000.f), 12.f);
	TestEqual(TEXT("below 1.5 px is 1.5 px (0 = the default)"), PxToCm(0.f, 3000.f, 1.f, 1000.f), 9.f);
	TestEqual(TEXT("3x farther, 3x wider"), PxToCm(2.f, 9000.f, 1.f, 1000.f), 36.f);
	TestTrue(TEXT("a narrower FOV: thinner"), PxToCm(2.f, 3000.f, 0.5f, 1000.f) < 12.f);
	return true;
}

// DD-025: the card content HROT's gizmos give, built from the gateway's EntityStatus (UnitCardGizmo / MindStateGizmo / HealthBarGizmo)
namespace
{
	/** the items of a row, in card order */
	TArray<const FItem*> RowItems(const FCard& C, uint8 Row) { TArray<const FItem*> R; for (const FItem& I : C.Items) if (I.Row == Row) R.Add(&I); return R; }
	FString InfoRow(const FCard& C) { FString S; for (const FItem* I : RowItems(C, 40)) { if (!S.IsEmpty()) S += TEXT(" "); S += I->Text; } return S; }
	const FItem* FillOf(const FCard& C, uint8 Row) { const TArray<const FItem*> R = RowItems(C, Row); return R.Num() >= 2 && R[1]->Kind == EItem::Box ? R[1] : nullptr; }   // a bar: back, then the fill
	int32 TicksOf(const FCard& C, uint8 Row) { int32 N = 0; for (const FItem* I : RowItems(C, Row)) if (I->Kind == EItem::Tick) N++; return N; }
	FStatusFacts Unit(int32 Present, int32 Auth, float Ammo, const TCHAR* Task = TEXT(""), const TCHAR* Behaviour = TEXT(""))
	{
		FStatusFacts F; F.bHas = true; F.Symbol = TEXT("SFGPUCI----B---"); F.StrengthPresent = Present; F.StrengthAuthorized = Auth; F.AmmoShare = Ammo; F.Task = Task; F.Behaviour = Behaviour;
		return F;
	}
	FStatusFacts Man(float Cur, float Max, EMindBand Band, float Fill, float Break, const TCHAR* Behaviour = TEXT(""))
	{
		FStatusFacts F; F.bHas = true; F.HealthCurrent = Cur; F.HealthMax = Max; F.MindBand = Band; F.MindFill = Fill; F.MindBreak = Break; F.Behaviour = Behaviour;
		return F;
	}
	FCard Build(const FStatusFacts& F, bool bUnit, int32 Mode = 3, bool bAir = false)
	{
		FCard C; BuildStatusCard(C, F, Mode, bAir, bUnit, TEXT("1st #7"), 1, 0); return C;
	}
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotCardsStatusTest, "Hrot.Dps.Cards.Status",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotCardsStatusTest::RunTest(const FString&)
{
	// a unit: the strength bar green >= 0.9, amber >= 0.7, else red; ticks at 0.7 and 0.9; "P/A" in the bar's colour
	const FItem* F95 = FillOf(Build(Unit(19, 20, -1), true), 10);
	TestTrue(TEXT("strength 0.95: green"), F95 && F95->Fill == 0x5BD06AFFu);
	const FItem* F80 = FillOf(Build(Unit(16, 20, -1), true), 10);
	TestTrue(TEXT("strength 0.8: amber"), F80 && F80->Fill == 0xF2C033FFu);
	const FItem* F50 = FillOf(Build(Unit(10, 20, -1), true), 10);
	TestTrue(TEXT("strength 0.5: red"), F50 && F50->Fill == 0xE65555FFu);
	const FCard U = Build(Unit(7, 9, 0.6f, TEXT("Assault"), TEXT("Patrol")), true);
	TestTrue(TEXT("strength bar fills 7/9"), FillOf(U, 10) && FMath::IsNearlyEqual(FillOf(U, 10)->B.X * 2.f, 7.f / 9.f));
	TestEqual(TEXT("two strength ticks"), TicksOf(U, 10), 2);
	TestEqual(TEXT("info row: strength, then the task"), InfoRow(U), FString(TEXT("7/9 · Assault")));
	TestTrue(TEXT("  the strength text takes the bar's colour, the task is teal"),
		RowItems(U, 40).Num() == 2 && RowItems(U, 40)[0]->Colour == 0xF2C033FFu && RowItems(U, 40)[1]->Colour == 0x3EC6B0FFu && RowItems(U, 40)[1]->bAfterPrev);
	TestEqual(TEXT("info row: the behaviour when the task is empty"), InfoRow(Build(Unit(7, 9, -1, TEXT(""), TEXT("Patrol")), true)), FString(TEXT("7/9 · Patrol")));
	TestTrue(TEXT("the symbol cell"), Build(Unit(7, 9, -1), true).Items.ContainsByPredicate([](const FItem& I) { return I.Kind == EItem::Symbol; }));
	TestEqual(TEXT("ammo 0.6: the bar is green"), FillOf(U, 13) ? FillOf(U, 13)->Fill : 0u, 0x5BD06AFFu);
	TestEqual(TEXT("ammo 0.3: amber"), FillOf(Build(Unit(7, 9, 0.3f), true), 13) ? FillOf(Build(Unit(7, 9, 0.3f), true), 13)->Fill : 0u, 0xF2C033FFu);
	TestEqual(TEXT("ammo 0.1: red"), FillOf(Build(Unit(7, 9, 0.1f), true), 13) ? FillOf(Build(Unit(7, 9, 0.1f), true), 13)->Fill : 0u, 0xE65555FFu);
	TestEqual(TEXT("ammo -1: no ammo bar"), RowItems(Build(Unit(7, 9, -1), true), 13).Num(), 0);
	const FCard NoStr = Build(Unit(0, 0, -1, TEXT("Hold")), true);
	TestEqual(TEXT("StrengthAuthorized 0: no strength bar"), RowItems(NoStr, 10).Num(), 0);
	TestEqual(TEXT("  nor its ticks"), TicksOf(NoStr, 10), 0);
	TestEqual(TEXT("  the info row is the task alone"), InfoRow(NoStr), FString(TEXT("· Hold")));

	// a man / vehicle: health, mind, BROKEN, the behaviour
	const FCard M = Build(Man(30, 100, EMindBand::Broken, 0.8f, 0.6f, TEXT("Flee")), false);
	const FItem* H = FillOf(M, 10);
	TestTrue(TEXT("health 0.3: red, 30 % wide"), H && H->Fill == 0xFF0000FFu && FMath::IsNearlyEqual(H->B.X * 2.f, 0.3f));
	TestTrue(TEXT("mind bar in the Broken colour"), FillOf(M, 11) && FillOf(M, 11)->Fill == 0xE1322DFFu);
	TestEqual(TEXT("the mind-break tick"), TicksOf(M, 11), 1);
	const TArray<const FItem*> Br = RowItems(M, 12);
	TestTrue(TEXT("BROKEN row"), Br.Num() == 1 && Br[0]->Text == TEXT("BROKEN") && Br[0]->Colour == 0xE1322DFFu);
	TestEqual(TEXT("the behaviour"), InfoRow(M), FString(TEXT("· Flee")));
	TestEqual(TEXT("HealthMax 0: no health bar"), RowItems(Build(Man(0, 0, EMindBand::None, 0, -1), false), 10).Num(), 0);
	const FCard Calm = Build(Man(100, 100, EMindBand::Calm, 0.2f, -1), false);
	TestEqual(TEXT("mind None: no mind bar"), RowItems(Build(Man(100, 100, EMindBand::None, 0, -1), false), 11).Num(), 0);
	TestTrue(TEXT("a calm mind: a bar, no tick (MindBreak -1), no BROKEN"), FillOf(Calm, 11) && FillOf(Calm, 11)->Fill == 0x6E96C8FFu && TicksOf(Calm, 11) == 0 && RowItems(Calm, 12).Num() == 0);
	TestTrue(TEXT("nervous / fear colours"), FillOf(Build(Man(1, 1, EMindBand::Nervous, 0.5f, -1), false), 11)->Fill == 0xF0AA28FFu && FillOf(Build(Man(1, 1, EMindBand::Fear, 0.5f, -1), false), 11)->Fill == 0xFA821EFFu);
	TestEqual(TEXT("no behaviour, no info row"), RowItems(Calm, 40).Num(), 0);

	// no status yet: the coarse damage bar, as before
	FCard Old; BuildStatusCard(Old, FStatusFacts(), 3, false, false, TEXT("x"), 1, 1);
	TestTrue(TEXT("no status: the damage-1 bar is half, yellow"), FillOf(Old, 10) && FillOf(Old, 10)->Fill == 0xFFFF00FFu);
	// the cutoffs: bars, ticks and the frame go at 1 m/px, texts at 2; an aircraft's card never
	auto Measure = [](const FString& T, float) { return 6.f * T.Len(); };
	FLayout L;
	TestTrue(TEXT("1.5 m/px: only the texts remain (a unit)"), Layout(U, 1.5f, Measure, L));
	for (const FPlaced& P : L.Placed) TestTrue(TEXT("  a text"), U.Items[P.Item].Kind == EItem::Text);
	TestTrue(TEXT("an aircraft is never cut"), Layout(Build(Man(1, 1, EMindBand::None, 0, -1), false, 3, true), 50.f, Measure, L));
	TestTrue(TEXT("mode 1 builds nothing"), Build(Unit(7, 9, -1), true, 1).Items.Num() == 0);

	// the hash moves with every status field
	const uint32 Base = HashOf(Build(Unit(7, 9, 0.6f, TEXT("Assault")), true));
	TestTrue(TEXT("hash: present"), HashOf(Build(Unit(6, 9, 0.6f, TEXT("Assault")), true)) != Base);
	TestTrue(TEXT("hash: ammo"), HashOf(Build(Unit(7, 9, 0.2f, TEXT("Assault")), true)) != Base);
	TestTrue(TEXT("hash: task"), HashOf(Build(Unit(7, 9, 0.6f, TEXT("Defend")), true)) != Base);
	const FStatusFacts B0 = Man(60, 100, EMindBand::Nervous, 0.4f, 0.7f, TEXT("Flee"));
	const uint32 FactsHash = B0.Hash(), CardHash = HashOf(Build(B0, false));
	auto Changed = [&](auto Mutate)
	{
		FStatusFacts X = B0; Mutate(X);
		return X.Hash() != FactsHash && HashOf(Build(X, false)) != CardHash;
	};
	TestTrue(TEXT("hash: health"), Changed([](FStatusFacts& X) { X.HealthCurrent = 20; }));
	TestTrue(TEXT("hash: mind fill"), Changed([](FStatusFacts& X) { X.MindFill = 0.9f; }));
	TestTrue(TEXT("hash: mind break"), Changed([](FStatusFacts& X) { X.MindBreak = 0.3f; }));
	TestTrue(TEXT("hash: mind band"), Changed([](FStatusFacts& X) { X.MindBand = EMindBand::Fear; }));
	TestTrue(TEXT("hash: behaviour"), Changed([](FStatusFacts& X) { X.Behaviour = TEXT("Hide"); }));
	FStatusFacts Sy = B0; Sy.Symbol = TEXT("SFGPUCI----B---");
	TestTrue(TEXT("facts hash: symbol, strength"), Sy.Hash() != FactsHash);
	FStatusFacts St = B0; St.StrengthPresent = 3; FStatusFacts Au = B0; Au.StrengthAuthorized = 3; FStatusFacts Am = B0; Am.AmmoShare = 0.5f;
	TestTrue(TEXT("facts hash: present / authorized / ammo"), St.Hash() != FactsHash && Au.Hash() != FactsHash && Am.Hash() != FactsHash);
	return true;
}

// U-19h 20: the billboard's size gives the texture 1:1 on screen at any aspect (the engine's own formulas, MaterialBillboardComponent.cpp:166-171)
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FHrotCardBillboardSizeTest, "Hrot.Dps.Cards.BillboardSize", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FHrotCardBillboardSizeTest::RunTest(const FString&)
{
	struct FView { int32 W, H; float P00, P11; };
	for (const FView V : { FView{ 1160, 605, 0.9272f, 1.7778f }, FView{ 1920, 1080, 1.f, 1.7778f }, FView{ 1280, 720, 0.75f, 1.3333f } })
		for (const FIntPoint Tex : { FIntPoint(74, 96), FIntPoint(123, 140), FIntPoint(104, 84) })
		{
			const FVector2f S = UHrotCardLayer::BillboardSize(Tex, V.P00, V.P11, FIntPoint(V.W, V.H));
			const float Vertical = S.X * V.P11 * V.H;                              // BaseSizeX * W along view up
			const float Horizontal = S.Y * (V.P00 / V.P11) * V.P00 * V.W;          // BaseSizeY * AspectRatio * W along view right
			TestEqual(*FString::Printf(TEXT("%dx%d view, %dx%d card: shown as wide as the texture"), V.W, V.H, Tex.X, Tex.Y), Horizontal, float(Tex.X), 0.01f);
			TestEqual(*FString::Printf(TEXT("%dx%d view, %dx%d card: shown as tall as the texture"), V.W, V.H, Tex.X, Tex.Y), Vertical, float(Tex.Y), 0.01f);
		}
	return true;
}
#endif
