// CE-4651 (U-18y) — entity labels ("cards") the way HROT draws them, as pure C++ (no rendering here; HrotCardLayer draws them).
//   * the NATO land-unit symbol: a port of HROT's SymbolCode + SymbolGeometry (FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Symbology/) —
//     the wire form is a 2525C SIDC (15 chars) with HROT's "|<mobility>|<mark>" tail, the drawing is the same vectors through a sink;
//   * a card = HROT's entity-card primitives (Fdp.Diagnostics.Contracts/EntityCards.cs): row 0 card-wide (frame, symbol cell), rows by
//     order (the health bar 10, the name 20); a Box on row 0 is in fractions of the card, a row's fraction box of the row's inner width;
//   * the layout = HROT's EntityCardRenderer.Layout (MinWidth 72, Padding 4, RowGap 2, Leader 20 px), the items over their
//     metres-per-pixel cutoff dropped first (HROT's projector MaxMetresPerPixel: frame / bar 1, name 2, an aircraft's name none).
// Source until the gateway carries HROT's card primitives (SimNetGateway NOTE-2026-11-21-Unreal-Labels-Via-Gizmos, G-1): a stand-in built
// from Entity data in UHrotDpsWorld::BuildCards.
#pragma once

#include "CoreMinimal.h"

namespace HrotCards
{
	enum class EAffiliation : uint8 { Unknown = 0, Friend = 1, Hostile = 2, Neutral = 3 };
	enum class ERole : uint8 { NoRole = 0, Infantry, Mechanized, Motorized, Armour, Reconnaissance, Artillery, Mortar, AntiArmour, Headquarters, Support };
	enum class EEchelon : uint8 { NoEchelon = 0, Team, Squad, Section, Platoon, Company, Battalion, Regiment, Brigade, Division };
	enum class EMobility : uint8 { NoMobility = 0, Wheeled, Tracked };

	/** a land unit's symbol as data (HROT SymbolCode) */
	struct HROTDPS_API FSymbol
	{
		EAffiliation Affiliation = EAffiliation::Unknown;
		ERole Role = ERole::NoRole;
		EEchelon Echelon = EEchelon::NoEchelon;
		EMobility Mobility = EMobility::NoMobility;
		ERole Secondary = ERole::NoRole;

		/** the wire form: "SFGPUCIZ---D---|T|AT" (at most 21 characters) */
		FString Format() const;
		/** a wire form or a plain 15-character SIDC; false when it is not a land-unit code this vocabulary draws */
		static bool Parse(const FString& Code, FSymbol& Out);
		uint32 Hash() const { return uint32(Affiliation) | (uint32(Role) << 4) | (uint32(Echelon) << 9) | (uint32(Mobility) << 14) | (uint32(Secondary) << 16); }
		bool operator==(const FSymbol& O) const { return Hash() == O.Hash(); }
	};

	/** where the geometry draws: pixels around a centre, x right, y DOWN; colours 0xRRGGBBAA (HROT ISymbolSink) */
	class HROTDPS_API ISymbolSink
	{
	public:
		virtual ~ISymbolSink() = default;
		/** a closed convex polygon, filled (alpha 0 = no fill) and outlined (Px 0 = no outline) */
		virtual void Polygon(TArrayView<const FVector2f> Points, uint32 Fill, uint32 Outline, float OutlinePx) = 0;
		virtual void Line(FVector2f A, FVector2f B, uint32 Colour, float Px) = 0;
		virtual void Ellipse(FVector2f Centre, float Rx, float Ry, uint32 Fill, uint32 Outline, float OutlinePx) = 0;
		/** a short text CENTRED at Centre */
		virtual void Text(FVector2f Centre, const FString& Text, float SizePx, uint32 Colour) = 0;
	};

	/** draws the symbol around C with a frame H pixels high (HROT SymbolGeometry.Build) */
	HROTDPS_API void BuildSymbol(const FSymbol& S, FVector2f C, float H, ISymbolSink& Sink);
	/** the symbol's pixel box at frame height H (clamped 6..256, rounded) and where its centre sits in it (HROT SymbolRenderer.Measure, pad 2) */
	HROTDPS_API void MeasureSymbol(const FSymbol& S, float H, float& OutW, float& OutH, float& OutCx, float& OutCy);

	// ── cards ──
	constexpr float MinWidth = 72.f, Padding = 4.f, RowGap = 2.f, Leader = 20.f, DefaultTextPx = 13.f, DefaultSymbolPx = 20.f;

	enum class EItem : uint8 { Box, Text, Line, Symbol, Tick };
	/** one card primitive (HROT DebugPrimitive in CoordinateSpace.EntityCard) */
	struct HROTDPS_API FItem
	{
		uint8 Row = 0;                 // ZIndex: 0 = card-wide
		EItem Kind = EItem::Box;
		// Box: centre A, half extent B (row 0 or bFraction: fractions; else pixels in the row). Text: A = (x, y) in the row. Line: A -> B. Symbol: -
		// Tick: a 1 px vertical mark, A.X a fraction of the row's inner width, A.Y its top in the row, Px its height, Colour its colour
		FVector2f A = FVector2f::ZeroVector, B = FVector2f::ZeroVector;
		uint32 Colour = 0xFFFFFFFF, Fill = 0;
		float Thickness = 0;           // outline / line px (0 = none)
		bool bFraction = false;        // a row's Box: x a fraction of the row's inner width, y pixels
		bool bAfterPrev = false;       // a Text: starts where the previous Text of its row ends (+ a small gap): a row of runs in several colours
		FString Text;                  // Text: the string; Symbol: the code
		float Px = DefaultTextPx;      // Text: the size; Symbol: the frame height
		float MaxMetresPerPixel = 0;   // shown only while the camera is finer than this (0 = at any distance)
	};

	struct HROTDPS_API FCard
	{
		int64 EntityId = 0;
		TArray<FItem> Items;
		void Frame(uint32 SideColour, uint8 BackgroundAlpha = 150, float MaxMpp = 0);
		void Text(uint8 Row, const FString& S, uint32 Colour, float SizePx = DefaultTextPx, float X = 0, float MaxMpp = 0, bool bAfterPrev = false);
		/** a bar: a back box and a fill of Fraction; YOffset = its top in the row (to centre it beside a taller tick) */
		void Bar(uint8 Row, float Fraction, uint32 Fill, uint32 Back, float HeightPx = 4, float MaxMpp = 0, float YOffset = 0);
		/** a thin vertical mark at Fraction of the row's width (HROT's strength / mind-break ticks) */
		void Tick(uint8 Row, float Fraction, uint32 Colour, float HeightPx, float MaxMpp = 0, float YOffset = 0);
		void Symbol(const FSymbol& S, float HeightPx = 22, float MaxMpp = 0);
	};

	/** one item placed: its row's top-left in card pixels */
	struct FPlaced
	{
		int32 Item = 0;
		FVector2f Origin = FVector2f::ZeroVector;
		float RowHeight = 0;
		bool bCardWide = false;
		float ExtraX = 0;      // a Text that follows another: where the previous one ended
	};
	struct HROTDPS_API FLayout
	{
		float W = 0, H = 0, CellW = 0;    // the card's size and its symbol cell's width
		uint32 LeaderColour = 0xAAAAAADC;
		TArray<FPlaced> Placed;            // card-wide items first, then the rows top-down
		uint64 VisibleMask = 0;            // which of the card's items (index < 64) survived the cutoff
	};
	using FMeasure = TFunctionRef<float(const FString& Text, float Px)>;
	/** HROT EntityCardRenderer.Layout for one card at the camera's metres per pixel; false = nothing to show */
	HROTDPS_API bool Layout(const FCard& Card, float MetresPerPixel, FMeasure Measure, FLayout& Out);

	/** the canvas a card is painted on (card pixels, the card's top-left at the origin given to Paint) */
	class HROTDPS_API ICardCanvas : public ISymbolSink
	{
	public:
		/** an axis-aligned rectangle, filled and / or outlined (inside the rectangle) */
		virtual void Rect(FVector2f Min, FVector2f Size, uint32 Fill, uint32 Outline, float OutlinePx) = 0;
		/** a text with its TOP-LEFT at At */
		virtual void TextAt(FVector2f At, const FString& Text, float SizePx, uint32 Colour) = 0;
	};
	/** paints a laid-out card with its top-left at TopLeft, and the leader line from the card's bottom to Anchor (HROT DrawCard) */
	HROTDPS_API void Paint(const FCard& Card, const FLayout& L, FVector2f TopLeft, FVector2f Anchor, ICardCanvas& Canvas);

	// ── SimNetGateway DD-024 (R-144): HROT's card gizmos over GizmoFrame — the appended GizmoPrimitive fields, as the adapter reads them ──
	enum class EGizmoShape : uint8 { LineStrip = 0, Arrow, Circle, Polygon, Box, Text, Icon };   // the contract's GizmoShape
	/** one card-space primitive (Space = EntityCard), as the client guide's "Entity cards" lays it out. Text: CardX/CardY px in the row, Width =
	 *  font px. Box: CardX/CardW centre/half-width fractions of the width, CardY/CardH HROT's centre/half-extent (row 0 fractions, rows px),
	 *  FillRgba the fill, ColourRgba an outline of Width px.
	 *  Icon: Text = the symbol code, CardH = height px (row 0 = the left cell) */
	struct FGizmoCardPrim
	{
		EGizmoShape Shape = EGizmoShape::Box;
		int32 AnchorId = 0;
		uint8 Row = 0;
		float CardX = 0, CardY = 0, CardW = 0, CardH = 0, Width = 0;
		uint32 ColourRgba = 0xFFFFFFFF, FillRgba = 0;
		FString Text;
	};
	/** the client's distance rule (R-144: each client chooses; HROT's numbers): frame / bar / symbol cut at FrameMpp, text at TextMpp; an
	 *  Air anchor's card is never cut (rule A) */
	struct FCutoffs { float FrameMpp = 1.f, TextMpp = 2.f; };
	/** adds one card-space primitive to its card (HROT EntityCards.cs helpers, inverted); false for a shape a card does not use */
	HROTDPS_API bool AddGizmo(FCard& Card, const FGizmoCardPrim& P, bool bAirAnchor, const FCutoffs& Cut = FCutoffs());

	// ── DD-025: the gateway's EntityStatus, as facts, and HROT's card content built from them (UnitCardGizmo / MindStateGizmo / HealthBarGizmo) ──
	enum class EMindBand : uint8 { None = 0, Calm, Nervous, Broken, Fear };
	/** one EntityStatus (none-values: HealthMax 0 = no health bar, StrengthAuthorized 0 = no strength, AmmoShare < 0 = no ammo bar, MindBand None =
	 *  no mind bar, MindBreak < 0 = no tick, "" = none). bHas false = no status has arrived: the coarse-damage fallback */
	struct HROTDPS_API FStatusFacts
	{
		bool bHas = false;
		FString Symbol, Behaviour, Task;
		float HealthCurrent = 0, HealthMax = 0, AmmoShare = -1, MindFill = 0, MindBreak = -1;
		int32 StrengthPresent = 0, StrengthAuthorized = 0;
		EMindBand MindBand = EMindBand::None;
		/** a hash of every field (for the card's change key, computed once when the status arrives) */
		uint32 Hash() const;
	};
	/** HROT's colour of a strength / ammo fraction: green >= Good, amber >= Fair, else red */
	HROTDPS_API uint32 LevelColour(float Fraction, float Good, float Fair);
	/** fills Card with an entity's card: the frame, the symbol of a unit, the bars (strength + ammo for a unit, health + mind for others), the name
	 *  and the info row. Mode: 2 units + aircraft, 3 every entity (both draw the full card; 1 = nothing). CoarseDamage (0..2) draws the health bar
	 *  when no status has arrived */
	HROTDPS_API void BuildStatusCard(FCard& Card, const FStatusFacts& F, int32 Mode, bool bAir, bool bUnit, const FString& NameText, uint8 Side, uint8 CoarseDamage);

	/** a line of PxWidth pixels at DistCm from the camera, in world centimetres (the line batcher's thickness): the viewport's width W covers
	 *  2 * d * tan(hfov / 2) at that distance. Floored at 1.5 px (a thinner line breaks up under temporal upsampling) */
	inline float PxToCm(float PxWidth, float DistCm, float TanHalfFov, float ViewportW)
	{
		return FMath::Max(PxWidth, 1.5f) * 2.f * FMath::Max(DistCm, 1.f) * TanHalfFov / FMath::Max(ViewportW, 1.f);
	}

	/** HROT's side colours (ResolvedStyleConstants tints): Side 1 friend blue, 2 hostile red, else neutral green */
	HROTDPS_API uint32 SideColour(uint8 Side);
	/** a hash of what the card shows (its items), for redraw-on-change */
	HROTDPS_API uint32 HashOf(const FCard& Card);
}
