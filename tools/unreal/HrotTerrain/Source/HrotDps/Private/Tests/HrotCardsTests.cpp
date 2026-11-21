// CE-4651 (U-18y) — rails for HrotCards: HROT's symbol code and geometry, and its card layout, ported (numbers worked by hand from HROT's
// SymbolCode / SymbolGeometry / SymbolRenderer.Measure / EntityCardRenderer.Layout).
// Run: UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests Hrot.Dps.Cards; Quit" -nullrhi -unattended
#include "Misc/AutomationTest.h"
#include "HrotCards.h"

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

	TestTrue(TEXT("the hash moves with the content"), HashOf(C) != HashOf(U));
	FCard C2 = C; C2.Items.Last().Text = TEXT("T-72 #1044");
	TestTrue(TEXT("  a renamed card redraws"), HashOf(C) != HashOf(C2));
	return true;
}

#endif
