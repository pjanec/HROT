// CE-4651 (U-18y) — see HrotCards.h. A line-by-line port of HROT's SymbolCode / SymbolGeometry / SymbolRenderer.Measure and
// EntityCardRenderer.Layout / DrawCard (paths in the header); names kept so the two can be read side by side.
#include "HrotCards.h"

namespace HrotCards
{
	namespace
	{
		const TCHAR* FunctionOf(ERole R)
		{
			switch (R)
			{
			case ERole::Infantry: return TEXT("UCI---"); case ERole::Mechanized: return TEXT("UCIZ--"); case ERole::Motorized: return TEXT("UCIM--");
			case ERole::Armour: return TEXT("UCA---"); case ERole::Reconnaissance: return TEXT("UCR---"); case ERole::Artillery: return TEXT("UCF---");
			case ERole::Mortar: return TEXT("UCFM--"); case ERole::AntiArmour: return TEXT("UCAA--"); case ERole::Headquarters: return TEXT("UH----");
			case ERole::Support: return TEXT("US----"); default: return TEXT("U-----");
			}
		}
		const TCHAR* MarkOf(ERole R)
		{
			switch (R)
			{
			case ERole::AntiArmour: return TEXT("AT"); case ERole::Mortar: return TEXT("M"); case ERole::Artillery: return TEXT("A");
			case ERole::Reconnaissance: return TEXT("R"); case ERole::Armour: return TEXT("TK"); case ERole::Headquarters: return TEXT("HQ");
			case ERole::Support: return TEXT("S"); default: return TEXT("");
			}
		}
		const TCHAR EchelonChars[] = TEXT("-ABCDEFGHI");
		TCHAR AffiliationChar(EAffiliation A) { return A == EAffiliation::Friend ? 'F' : A == EAffiliation::Hostile ? 'H' : A == EAffiliation::Neutral ? 'N' : 'U'; }
		EAffiliation AffiliationOf(TCHAR C)
		{
			switch (C)
			{
			case 'F': case 'A': case 'D': case 'M': return EAffiliation::Friend;
			case 'H': case 'S': case 'J': case 'K': return EAffiliation::Hostile;
			case 'N': case 'L': return EAffiliation::Neutral;
			default: return EAffiliation::Unknown;
			}
		}

		constexpr uint32 FriendFill = 0x80E0FFFF, HostileFill = 0xFF8080FF, NeutralFill = 0xAAFFAAFF, UnknownFill = 0xFFFF80FF, Ink = 0x000000FF, White = 0xFFFFFFFF;
		uint32 FillOf(EAffiliation A) { return A == EAffiliation::Friend ? FriendFill : A == EAffiliation::Hostile ? HostileFill : A == EAffiliation::Neutral ? NeutralFill : UnknownFill; }

		// the frame's inner half box (where the icon lives) and its top / bottom, by affiliation
		void Frame(EAffiliation A, float H, float& Bx, float& By, float& Top, float& Bottom)
		{
			switch (A)
			{
			case EAffiliation::Friend: Bx = H * 0.75f; By = H * 0.5f; Top = -H * 0.5f; Bottom = H * 0.5f; break;
			case EAffiliation::Hostile: Bx = H * 0.4f; By = H * 0.4f; Top = -H * 0.8f; Bottom = H * 0.8f; break;
			case EAffiliation::Neutral: Bx = H * 0.5f + 2.f; By = H * 0.5f + 2.f; Top = -H * 0.5f - 2.f; Bottom = H * 0.5f + 2.f; break;
			default: Bx = H * 0.36f; By = H * 0.36f; Top = -H * 0.72f; Bottom = H * 0.72f; break;
			}
		}

		void Icon(ERole Role, FVector2f C, float Bx, float By, float W, ISymbolSink& S)
		{
			if (Role == ERole::Infantry || Role == ERole::Mechanized || Role == ERole::Motorized)
			{
				S.Line(C + FVector2f(-Bx, -By), C + FVector2f(Bx, By), Ink, W);
				S.Line(C + FVector2f(-Bx, By), C + FVector2f(Bx, -By), Ink, W);
			}
			switch (Role)
			{
			case ERole::Mechanized: case ERole::Armour: S.Ellipse(C, Bx * 0.62f, By * 0.55f, 0, Ink, W); break;
			case ERole::Motorized: S.Line(C + FVector2f(0, -By), C + FVector2f(0, By), Ink, W); break;
			case ERole::Reconnaissance: S.Line(C + FVector2f(-Bx, By), C + FVector2f(Bx, -By), Ink, W); break;
			case ERole::Artillery: S.Ellipse(C, By * 0.38f, By * 0.38f, Ink, 0, 0.f); break;
			case ERole::Mortar:
				S.Ellipse(C + FVector2f(0, By * 0.35f), By * 0.25f, By * 0.25f, 0, Ink, W);
				S.Line(C + FVector2f(0, By * 0.1f), C + FVector2f(0, -By * 0.7f), Ink, W);
				S.Line(C + FVector2f(0, -By * 0.7f), C + FVector2f(-By * 0.22f, -By * 0.4f), Ink, W);
				S.Line(C + FVector2f(0, -By * 0.7f), C + FVector2f(By * 0.22f, -By * 0.4f), Ink, W); break;
			case ERole::AntiArmour:
				S.Line(C + FVector2f(-Bx, By), C + FVector2f(0, -By), Ink, W);
				S.Line(C + FVector2f(0, -By), C + FVector2f(Bx, By), Ink, W); break;
			case ERole::Support: S.Line(C + FVector2f(-Bx, 0), C + FVector2f(Bx, 0), Ink, W); break;
			default: break;
			}
		}

		void Mobility(EMobility M, FVector2f C, float H, float Bottom, float W, ISymbolSink& S)
		{
			const float Y = C.Y + Bottom + H * 0.2f;
			if (M == EMobility::Tracked)
			{
				const float Hw = H * 0.6f, Hh = H * 0.12f;
				const FVector2f Band[4] = { { C.X - Hw, Y - Hh }, { C.X + Hw, Y - Hh }, { C.X + Hw, Y + Hh }, { C.X - Hw, Y + Hh } };
				S.Polygon(Band, 0, Ink, W);
			}
			else
			{
				const float R = H * 0.11f;
				for (int32 K = -1; K <= 1; K++) S.Ellipse(FVector2f(C.X + K * H * 0.4f, Y), R, R, 0, Ink, W);
			}
		}

		void Secondary(ERole R, FVector2f C, float Bx, float By, float H, ISymbolSink& S)
		{
			const FString Mark = MarkOf(R);
			if (Mark.IsEmpty()) return;
			const float Bw = H * (0.3f + 0.28f * Mark.Len()), Bh = H * 0.55f;
			const FVector2f O = C + FVector2f(Bx + H * 0.08f, By * 0.2f);
			const FVector2f Box[4] = { O, O + FVector2f(Bw, 0), O + FVector2f(Bw, Bh), O + FVector2f(0, Bh) };
			S.Polygon(Box, White, Ink, FMath::Max(1.f, H * 0.05f));
			S.Text(O + FVector2f(Bw * 0.5f, Bh * 0.5f), Mark, H * 0.45f, Ink);
		}

		void Echelon(EEchelon E, FVector2f C, float Top, float H, float W, ISymbolSink& S)
		{
			const float Y = C.Y + Top - H * 0.22f, R = FMath::Max(1.2f, H * 0.08f), Gap = H * 0.26f;
			const int32 Dots = E == EEchelon::Squad ? 1 : E == EEchelon::Section ? 2 : E == EEchelon::Platoon ? 3 : 0;
			if (Dots > 0)
			{
				for (int32 I = 0; I < Dots; I++) S.Ellipse(FVector2f(C.X + (I - (Dots - 1) * 0.5f) * Gap, Y), R, R, Ink, 0, 0.f);
				return;
			}
			const float BarH = H * 0.35f;
			const int32 Bars = E == EEchelon::Company ? 1 : E == EEchelon::Battalion ? 2 : E == EEchelon::Regiment ? 3 : 0;
			if (Bars > 0)
			{
				for (int32 I = 0; I < Bars; I++)
				{
					const float X = C.X + (I - (Bars - 1) * 0.5f) * Gap * 0.8f;
					S.Line(FVector2f(X, Y - BarH * 0.5f), FVector2f(X, Y + BarH * 0.5f), Ink, W * 1.2f);
				}
				return;
			}
			const int32 Crosses = E == EEchelon::Brigade ? 1 : E == EEchelon::Division ? 2 : 0;
			for (int32 I = 0; I < Crosses; I++)
			{
				const float X = C.X + (I - (Crosses - 1) * 0.5f) * BarH * 1.1f, Sz = BarH * 0.45f;
				S.Line(FVector2f(X - Sz, Y - Sz), FVector2f(X + Sz, Y + Sz), Ink, W * 1.2f);
				S.Line(FVector2f(X - Sz, Y + Sz), FVector2f(X + Sz, Y - Sz), Ink, W * 1.2f);
			}
			if (E == EEchelon::Team)
			{
				S.Ellipse(FVector2f(C.X, Y), BarH * 0.4f, BarH * 0.4f, 0, Ink, W);
				S.Line(FVector2f(C.X - BarH * 0.55f, Y + BarH * 0.55f), FVector2f(C.X + BarH * 0.55f, Y - BarH * 0.55f), Ink, W);
			}
		}

		/** HROT SymbolRenderer's BoundsSink: the box everything drawn touches */
		class FBoundsSink final : public ISymbolSink
		{
		public:
			FVector2f Min = FVector2f(FLT_MAX), Max = FVector2f(-FLT_MAX);
			void Add(FVector2f P, float R = 0) { Min = FVector2f(FMath::Min(Min.X, P.X - R), FMath::Min(Min.Y, P.Y - R)); Max = FVector2f(FMath::Max(Max.X, P.X + R), FMath::Max(Max.Y, P.Y + R)); }
			virtual void Polygon(TArrayView<const FVector2f> Points, uint32, uint32, float Px) override { for (const FVector2f& P : Points) Add(P, Px * 0.5f); }
			virtual void Line(FVector2f A, FVector2f B, uint32, float Px) override { Add(A, Px * 0.5f); Add(B, Px * 0.5f); }
			virtual void Ellipse(FVector2f C, float Rx, float Ry, uint32, uint32, float Px) override { Add(C - FVector2f(Rx, Ry), Px * 0.5f); Add(C + FVector2f(Rx, Ry), Px * 0.5f); }
			virtual void Text(FVector2f C, const FString& T, float Sz, uint32) override { const float Hw = Sz * 0.3f * T.Len(); Add(C - FVector2f(Hw, Sz * 0.5f)); Add(C + FVector2f(Hw, Sz * 0.5f)); }
		};

		float RowHeightOf(const FItem& I)
		{
			switch (I.Kind)
			{
			case EItem::Text: return I.Px > 0 ? I.Px : DefaultTextPx;
			case EItem::Box: return I.A.Y + I.B.Y;
			case EItem::Line: return FMath::Max(I.A.Y, I.B.Y) + 1.f;
				case EItem::Tick: return I.A.Y + I.Px;
			default: return 10.f;
			}
		}
		float RowWidthOf(const FItem& I, FMeasure Measure, float ExtraX)
		{
			switch (I.Kind)
			{
			case EItem::Text: return I.A.X + ExtraX + Measure(I.Text, RowHeightOf(I));
			case EItem::Box: return I.bFraction ? 0.f : I.A.X + I.B.X;
			case EItem::Line: return FMath::Max(I.A.X, I.B.X);
			default: return 0.f;
			}
		}
	}

	FString FSymbol::Format() const
	{
		FString S = TEXT("S");
		S.AppendChar(AffiliationChar(Affiliation)); S += TEXT("GP"); S += FunctionOf(Role);
		S.AppendChar(Role == ERole::Headquarters ? 'A' : '-');
		S.AppendChar(EchelonChars[FMath::Min<int32>(int32(Echelon), 9)]);
		S += TEXT("---");
		if (Mobility != EMobility::NoMobility || Secondary != ERole::NoRole)
		{
			S.AppendChar('|');
			S.AppendChar(Mobility == EMobility::Wheeled ? 'W' : Mobility == EMobility::Tracked ? 'T' : '-');
		}
		const FString Mark = MarkOf(Secondary);
		if (!Mark.IsEmpty()) { S.AppendChar('|'); S += Mark; }
		return S;
	}

	bool FSymbol::Parse(const FString& Code, FSymbol& Out)
	{
		Out = FSymbol();
		if (Code.Len() < 15 || Code[0] != 'S') return false;
		const FString Fn = Code.Mid(4, 6);
		ERole Role = ERole::NoRole;
		for (int32 R = int32(ERole::Infantry); R <= int32(ERole::Support); R++)
			if (Fn == FunctionOf(ERole(R))) { Role = ERole(R); break; }
		if (Role == ERole::NoRole && Fn[0] != 'U') return false;   // not a land unit
		EEchelon Ech = EEchelon::NoEchelon;
		for (int32 E = 1; E <= 9; E++) if (Code[11] == EchelonChars[E]) { Ech = EEchelon(E); break; }
		EMobility Mob = EMobility::NoMobility; ERole Sec = ERole::NoRole;
		FString Rest = Code.Mid(15);
		if (Rest.Len() >= 2 && Rest[0] == '|')
		{
			Mob = Rest[1] == 'W' ? EMobility::Wheeled : Rest[1] == 'T' ? EMobility::Tracked : EMobility::NoMobility;
			Rest = Rest.Mid(2);
			if (Rest.Len() >= 2 && Rest[0] == '|')
			{
				const FString Mark = Rest.Mid(1);
				for (int32 R = int32(ERole::Infantry); R <= int32(ERole::Support); R++)
					if (*MarkOf(ERole(R)) && Mark == MarkOf(ERole(R))) { Sec = ERole(R); break; }
			}
		}
		Out.Affiliation = AffiliationOf(Code[1]); Out.Role = Role; Out.Echelon = Ech; Out.Mobility = Mob; Out.Secondary = Sec;
		return true;
	}

	void BuildSymbol(const FSymbol& S, FVector2f C, float H, ISymbolSink& Sink)
	{
		const float W = FMath::Max(1.f, H * 0.07f);
		const uint32 Fill = FillOf(S.Affiliation);
		switch (S.Affiliation)
		{
		case EAffiliation::Friend:
		{
			const FVector2f P[4] = { C + FVector2f(-H * 0.75f, -H * 0.5f), C + FVector2f(H * 0.75f, -H * 0.5f), C + FVector2f(H * 0.75f, H * 0.5f), C + FVector2f(-H * 0.75f, H * 0.5f) };
			Sink.Polygon(P, Fill, Ink, W); break;
		}
		case EAffiliation::Hostile:
		{
			const float R = H * 0.8f;
			const FVector2f P[4] = { C + FVector2f(0, -R), C + FVector2f(R, 0), C + FVector2f(0, R), C + FVector2f(-R, 0) };
			Sink.Polygon(P, Fill, Ink, W); break;
		}
		case EAffiliation::Neutral:
		{
			const float Q = H * 0.5f + 2.f;
			const FVector2f P[4] = { C + FVector2f(-Q, -Q), C + FVector2f(Q, -Q), C + FVector2f(Q, Q), C + FVector2f(-Q, Q) };
			Sink.Polygon(P, Fill, Ink, W); break;
		}
		default:
		{
			const float U = H * 0.36f;
			Sink.Ellipse(C + FVector2f(0, -U), U, U, Fill, Ink, W); Sink.Ellipse(C + FVector2f(U, 0), U, U, Fill, Ink, W);
			Sink.Ellipse(C + FVector2f(0, U), U, U, Fill, Ink, W); Sink.Ellipse(C + FVector2f(-U, 0), U, U, Fill, Ink, W);
			Sink.Ellipse(C, U * 1.3f, U * 1.3f, Fill, 0, 0.f);
			break;
		}
		}
		float Bx, By, Top, Bottom; Frame(S.Affiliation, H, Bx, By, Top, Bottom);
		Icon(S.Role, C, Bx, By, W, Sink);
		if (S.Mobility != EMobility::NoMobility) Mobility(S.Mobility, C, H, Bottom, W, Sink);
		if (S.Secondary != ERole::NoRole) Secondary(S.Secondary, C, Bx, By, H, Sink);
		if (S.Echelon != EEchelon::NoEchelon && H >= 15.f) Echelon(S.Echelon, C, Top, H, W, Sink);
	}

	void MeasureSymbol(const FSymbol& S, float H, float& OutW, float& OutH, float& OutCx, float& OutCy)
	{
		const float Hh = float(FMath::Clamp(FMath::RoundToInt(H), 6, 256));
		FBoundsSink B;
		BuildSymbol(S, FVector2f::ZeroVector, Hh, B);
		constexpr float Pad = 2.f;
		const float MinX = FMath::FloorToFloat(B.Min.X - Pad), MinY = FMath::FloorToFloat(B.Min.Y - Pad);
		OutW = FMath::CeilToFloat(B.Max.X + Pad - MinX); OutH = FMath::CeilToFloat(B.Max.Y + Pad - MinY);
		OutCx = -MinX; OutCy = -MinY;
	}

	// ── cards (HROT EntityCards.cs) ──
	void FCard::Frame(uint32 Side, uint8 BackgroundAlpha, float MaxMpp)
	{
		FItem Back; Back.Kind = EItem::Box; Back.A = FVector2f(0.5f, 0.5f); Back.B = FVector2f(0.5f, 0.5f);
		Back.Colour = Back.Fill = 0x14161A00u | BackgroundAlpha; Back.MaxMetresPerPixel = MaxMpp;
		Items.Add(Back);
		FItem Outline = Back; Outline.Colour = Side; Outline.Fill = 0; Outline.Thickness = 1.f;
		Items.Add(Outline);
	}
	void FCard::Text(uint8 Row, const FString& S, uint32 Colour, float SizePx, float X, float MaxMpp, bool bAfter)
	{
		FItem I; I.Row = Row; I.Kind = EItem::Text; I.A = FVector2f(X, 0); I.Colour = Colour; I.Text = S.Left(31);
		I.Px = FMath::Clamp(SizePx, 6.f, 64.f); I.MaxMetresPerPixel = MaxMpp; I.bAfterPrev = bAfter;
		Items.Add(I);
	}
	void FCard::Bar(uint8 Row, float Fraction, uint32 FillC, uint32 Back, float HeightPx, float MaxMpp, float YOffset)
	{
		Fraction = FMath::Clamp(Fraction, 0.f, 1.f);
		auto Box = [&](float Centre, float Half, uint32 C)
		{
			FItem I; I.Row = Row; I.Kind = EItem::Box; I.bFraction = true; I.Colour = I.Fill = C;
			I.A = FVector2f(Centre, YOffset + HeightPx / 2); I.B = FVector2f(Half, HeightPx / 2); I.MaxMetresPerPixel = MaxMpp;
			Items.Add(I);
		};
		Box(0.5f, 0.5f, Back);
		if (Fraction > 0) Box(Fraction / 2, Fraction / 2, FillC);
	}
	void FCard::Tick(uint8 Row, float Fraction, uint32 Colour, float HeightPx, float MaxMpp, float YOffset)
	{
		FItem I; I.Row = Row; I.Kind = EItem::Tick; I.A = FVector2f(FMath::Clamp(Fraction, 0.f, 1.f), YOffset); I.Px = HeightPx; I.Colour = I.Fill = Colour; I.MaxMetresPerPixel = MaxMpp;
		Items.Add(I);
	}
	void FCard::Symbol(const FSymbol& S, float HeightPx, float MaxMpp)
	{
		FItem I; I.Kind = EItem::Symbol; I.Text = S.Format(); I.Px = FMath::Clamp(HeightPx, 6.f, 256.f); I.MaxMetresPerPixel = MaxMpp;
		Items.Add(I);
	}

	bool Layout(const FCard& Card, float Mpp, FMeasure Measure, FLayout& Out)
	{
		Out = FLayout();
		// the items this distance shows, ordered stable by row (row 0 first)
		TArray<int32, TInlineAllocator<32>> Order;
		for (int32 I = 0; I < Card.Items.Num(); I++)
		{
			const FItem& It = Card.Items[I];
			if (It.MaxMetresPerPixel > 0 && Mpp > It.MaxMetresPerPixel) continue;
			Order.Add(I);
			if (I < 64) Out.VisibleMask |= uint64(1) << I;
		}
		if (Order.Num() == 0) return false;
		Algo::StableSortBy(Order, [&](int32 I) { return Card.Items[I].Row; });
		TArray<float, TInlineAllocator<32>> Extra;   // per Order slot: where a following Text starts (the previous Text's end)
		Extra.Init(0.f, Order.Num());
		for (int32 K = 1; K < Order.Num(); K++)
		{
			const FItem& Prev = Card.Items[Order[K - 1]]; const FItem& It = Card.Items[Order[K]];
			if (It.bAfterPrev && It.Kind == EItem::Text && Prev.Kind == EItem::Text && Prev.Row == It.Row)
				Extra[K] = Prev.A.X + Extra[K - 1] + Measure(Prev.Text, RowHeightOf(Prev)) + 3.f;
		}
		int32 RowsStart = 0;
		while (RowsStart < Order.Num() && Card.Items[Order[RowsStart]].Row == 0) RowsStart++;
		float CellW = 0, CellH = 0;
		for (int32 K = 0; K < RowsStart; K++)
		{
			const FItem& It = Card.Items[Order[K]];
			FSymbol S;
			if (It.Kind == EItem::Symbol && FSymbol::Parse(It.Text, S))
			{
				float W, H, Cx, Cy; MeasureSymbol(S, It.Px, W, H, Cx, Cy);
				CellW = W + Padding; CellH = H;
			}
		}
		float Width = MinWidth - 2.f * Padding, Height = 0;
		TArray<float, TInlineAllocator<8>> RowH;
		for (int32 K = RowsStart; K < Order.Num();)
		{
			const uint8 Z = Card.Items[Order[K]].Row;
			float H = 0;
			for (; K < Order.Num() && Card.Items[Order[K]].Row == Z; K++)
			{
				H = FMath::Max(H, RowHeightOf(Card.Items[Order[K]]));
				Width = FMath::Max(Width, RowWidthOf(Card.Items[Order[K]], Measure, Extra[K]));
			}
			RowH.Add(H);
			Height += H;
		}
		Height += FMath::Max(0, RowH.Num() - 1) * RowGap;
		Out.W = Width + CellW + 2.f * Padding;
		Out.H = FMath::Max(FMath::Max(Height, CellH) + 2.f * Padding, 2.f * Padding + 4.f);
		Out.CellW = CellW;
		for (int32 K = 0; K < RowsStart; K++) Out.Placed.Add({ Order[K], FVector2f::ZeroVector, Out.H, true, 0.f });
		float Y = Padding + FMath::Max(0.f, (Out.H - 2.f * Padding - Height) * 0.5f);   // rows centred beside a taller symbol
		int32 Ri = 0;
		for (int32 K = RowsStart; K < Order.Num();)
		{
			const uint8 Z = Card.Items[Order[K]].Row;
			for (; K < Order.Num() && Card.Items[Order[K]].Row == Z; K++) Out.Placed.Add({ Order[K], FVector2f(Padding + CellW, Y), RowH[Ri], false, Extra[K] });
			Y += RowH[Ri] + RowGap;
			Ri++;
		}
		for (int32 K = 0; K < RowsStart; K++)
		{
			const FItem& It = Card.Items[Order[K]];
			if (It.Kind == EItem::Box && It.Thickness > 0 && (It.Colour & 0xFF)) Out.LeaderColour = It.Colour;
		}
		return true;
	}

	void Paint(const FCard& Card, const FLayout& L, FVector2f TL, FVector2f Anchor, ICardCanvas& C)
	{
		auto IsFill = [](const FItem& I) { return I.Kind == EItem::Box && (I.Fill & 0xFF); };   // (a Tick is a row item, drawn with its row)
		auto Draw = [&](const FPlaced& P)
		{
			const FItem& I = Card.Items[P.Item];
			const FVector2f O = TL + P.Origin;
			const float InnerW = L.W - 2.f * Padding - (P.bCardWide ? 0.f : L.CellW);   // a row's bars stop at the symbol cell
			switch (I.Kind)
			{
			case EItem::Text: C.TextAt(O + I.A + FVector2f(P.ExtraX, 0.f), I.Text, I.Px, I.Colour); break;
				case EItem::Tick: C.Rect(O + FVector2f(I.A.X * InnerW - 0.5f, I.A.Y), FVector2f(1.f, I.Px), I.Fill, 0, 0.f); break;
			case EItem::Box:
			{
				FVector2f Min, Size;
				if (P.bCardWide) { Min = TL + FVector2f((I.A.X - I.B.X) * L.W, (I.A.Y - I.B.Y) * L.H); Size = FVector2f(2.f * I.B.X * L.W, 2.f * I.B.Y * L.H); }
				else if (I.bFraction) { Min = O + FVector2f((I.A.X - I.B.X) * InnerW, I.A.Y - I.B.Y); Size = FVector2f(2.f * I.B.X * InnerW, 2.f * I.B.Y); }
				else { Min = O + I.A - I.B; Size = 2.f * I.B; }
				C.Rect(Min, Size, I.Fill, I.Thickness > 0 ? I.Colour : 0, FMath::Max(1.f, I.Thickness));
				break;
			}
			case EItem::Line: C.Line(O + I.A, O + I.B, I.Colour, 1.f); break;
			case EItem::Symbol:
			{
				FSymbol S;
				if (!FSymbol::Parse(I.Text, S)) break;
				float W, H, Cx, Cy; MeasureSymbol(S, I.Px, W, H, Cx, Cy);
				const FVector2f Box = P.bCardWide ? TL + FVector2f(Padding, (L.H - H) * 0.5f) : O;   // the left cell, centred in the card's height
				BuildSymbol(S, Box + FVector2f(Cx, Cy), float(FMath::Clamp(FMath::RoundToInt(I.Px), 6, 256)), C);
				break;
			}
			}
		};
		// row 0's backgrounds first, then the rows, then row 0's outlines (and the symbol): the frame on top, the background behind
		for (const FPlaced& P : L.Placed) if (P.bCardWide && IsFill(Card.Items[P.Item])) Draw(P);
		for (const FPlaced& P : L.Placed) if (!P.bCardWide) Draw(P);
		for (const FPlaced& P : L.Placed) if (P.bCardWide && !IsFill(Card.Items[P.Item])) Draw(P);
		C.Line(FVector2f(Anchor.X, TL.Y + L.H), Anchor, L.LeaderColour, 1.f);
	}

	bool AddGizmo(FCard& Card, const FGizmoCardPrim& P, bool bAir, const FCutoffs& Cut)
	{
		FItem I; I.Row = P.Row;
		const float FrameCut = bAir ? 0.f : Cut.FrameMpp, TextCut = bAir ? 0.f : Cut.TextMpp;
		switch (P.Shape)
		{
		case EGizmoShape::Text:
			I.Kind = EItem::Text; I.A = FVector2f(P.CardX, P.CardY); I.Colour = P.ColourRgba; I.Text = P.Text.Left(31);
			I.Px = P.Width > 0 ? FMath::Clamp(P.Width, 6.f, 64.f) : DefaultTextPx; I.MaxMetresPerPixel = TextCut;
			break;
		case EGizmoShape::Box:   // the guide: CardX / CardW centre and half-width as fractions of the width; CardY / CardH HROT's centre and
			// half-extent (fractions of the card on row 0, pixels in a row); FillRgba the fill (0 none); ColourRgba an outline of Width px (0 none)
			I.Kind = EItem::Box; I.MaxMetresPerPixel = FrameCut;
			I.A = FVector2f(P.CardX, P.CardY); I.B = FVector2f(P.CardW, P.CardH);
			I.bFraction = P.Row != 0;
			I.Fill = P.FillRgba;
			I.Thickness = (P.Width > 0 && (P.ColourRgba & 0xFF)) ? P.Width : 0.f;
			I.Colour = I.Thickness > 0 ? P.ColourRgba : P.FillRgba;
			break;
		case EGizmoShape::Icon:
			I.Kind = EItem::Symbol; I.Text = P.Text; I.Px = P.CardH > 0 ? FMath::Clamp(P.CardH, 6.f, 256.f) : DefaultSymbolPx; I.MaxMetresPerPixel = FrameCut;
			break;
		default: return false;
		}
		Card.EntityId = P.AnchorId;
		Card.Items.Add(I);
		return true;
	}

	uint32 FStatusFacts::Hash() const
	{
		uint32 H = bHas ? 1u : 0u;
		H = HashCombineFast(H, GetTypeHash(Symbol)); H = HashCombineFast(H, GetTypeHash(Behaviour)); H = HashCombineFast(H, GetTypeHash(Task));
		for (float V : { HealthCurrent, HealthMax, AmmoShare, MindFill, MindBreak }) H = HashCombineFast(H, ::GetTypeHash(V));
		H = HashCombineFast(H, uint32(StrengthPresent)); H = HashCombineFast(H, uint32(StrengthAuthorized));
		return HashCombineFast(H, uint32(MindBand));
	}

	uint32 LevelColour(float F, float Good, float Fair) { return F >= Good ? 0x5BD06AFFu : F >= Fair ? 0xF2C033FFu : 0xE65555FFu; }

	void BuildStatusCard(FCard& C, const FStatusFacts& F, int32 Mode, bool bAir, bool bUnit, const FString& Name, uint8 Side, uint8 CoarseDamage)
	{
		constexpr uint32 BarBack = 0x3A3A3ADCu, TickCol = 0xDCDCE1FFu, Teal = 0x3EC6B0FFu, MindBack = 0x282828DCu, NameCol = 0xF0F0F0FFu;
		const float Cut = bAir ? 0.f : 1.f, TextCut = bAir ? 0.f : 2.f;   // R-144 rule A: an aircraft's card is never cut
		if (Mode < 2) return;
		C.Frame(SideColour(Side), 150, Cut);
		if (bUnit)
		{
			FSymbol Sym;
			if (FSymbol::Parse(F.Symbol, Sym)) C.Symbol(Sym, 22, Cut);
			uint32 StrengthCol = 0;
			const bool bStrength = F.bHas && F.StrengthAuthorized > 0;
			if (bStrength)
			{
				const float Fr = FMath::Clamp(float(F.StrengthPresent) / float(F.StrengthAuthorized), 0.f, 1.f);
				StrengthCol = LevelColour(Fr, 0.9f, 0.7f);
				C.Bar(10, Fr, StrengthCol, BarBack, 5.f, Cut, 1.f);   // centred beside the 7 px ticks
				C.Tick(10, 0.7f, TickCol, 7.f, Cut); C.Tick(10, 0.9f, TickCol, 7.f, Cut);
			}
			if (F.bHas && F.AmmoShare >= 0.f) C.Bar(13, F.AmmoShare, LevelColour(F.AmmoShare, 0.5f, 0.25f), BarBack, 3.f, Cut);
			C.Text(20, Name, NameCol, DefaultTextPx, 0, TextCut);
			if (F.bHas)
			{
				const FString& What = F.Task.IsEmpty() ? F.Behaviour : F.Task;
				if (bStrength) C.Text(40, FString::Printf(TEXT("%d/%d"), F.StrengthPresent, F.StrengthAuthorized), StrengthCol, 12.f, 0, TextCut);
				if (!What.IsEmpty()) C.Text(40, TEXT("\u00B7 ") + What.Left(18), Teal, 12.f, 0, TextCut, bStrength);
			}
			return;
		}
		if (F.bHas)
		{
			if (F.HealthMax > 0.f)
			{
				const float Fr = FMath::Clamp(F.HealthCurrent / F.HealthMax, 0.f, 1.f);   // HROT HealthBarGizmo: green >= 0.66, yellow >= 0.33, red
				C.Bar(10, Fr, Fr >= 0.66f ? 0x00FF00FFu : Fr >= 0.33f ? 0xFFFF00FFu : 0xFF0000FFu, MindBack, 4.f, Cut);
			}
			if (F.MindBand != EMindBand::None)
			{
				const uint32 Col = F.MindBand == EMindBand::Calm ? 0x6E96C8FFu : F.MindBand == EMindBand::Nervous ? 0xF0AA28FFu : F.MindBand == EMindBand::Broken ? 0xE1322DFFu : 0xFA821EFFu;
				C.Bar(11, F.MindFill, Col, MindBack, 4.f, Cut, 1.f);
				if (F.MindBreak >= 0.f) C.Tick(11, F.MindBreak, 0xFFFFFFFFu, 6.f, Cut);
				if (F.MindBand == EMindBand::Broken) C.Text(12, TEXT("BROKEN"), Col, 12.f, 0, TextCut);
			}
		}
		else
		{
			const float Health = CoarseDamage >= 2 ? 0.f : CoarseDamage == 1 ? 0.5f : 1.f;   // no status yet: the coarse damage as a health bar
			C.Bar(10, Health, Health >= 0.66f ? 0x00FF00FFu : Health >= 0.33f ? 0xFFFF00FFu : 0xFF0000FFu, MindBack, 4.f, Cut);
		}
		C.Text(20, Name, NameCol, DefaultTextPx, 0, TextCut);
		if (F.bHas && !F.Behaviour.IsEmpty()) C.Text(40, TEXT("\u00B7 ") + F.Behaviour.Left(18), Teal, 12.f, 0, TextCut);
	}

	uint32 SideColour(uint8 Side) { return Side == 1 ? 0x0064FFFFu : Side == 2 ? 0xFF0000FFu : 0x00FF00FFu; }

	uint32 HashOf(const FCard& Card)
	{
		uint32 H = ::GetTypeHash(Card.EntityId);
		for (const FItem& I : Card.Items)
		{
			H = HashCombineFast(H, (uint32(I.Row) << 24) | (uint32(I.Kind) << 16) | (I.bFraction ? 1u : 0u));
			H = HashCombineFast(H, ::GetTypeHash(I.A.X)); H = HashCombineFast(H, ::GetTypeHash(I.A.Y));
			H = HashCombineFast(H, ::GetTypeHash(I.B.X)); H = HashCombineFast(H, ::GetTypeHash(I.B.Y));
			H = HashCombineFast(H, I.Colour); H = HashCombineFast(H, I.Fill);
			H = HashCombineFast(H, ::GetTypeHash(I.Thickness)); H = HashCombineFast(H, ::GetTypeHash(I.Px));
			H = HashCombineFast(H, GetTypeHash(I.Text)); H = HashCombineFast(H, I.bAfterPrev ? 1u : 0u);
		}
		return H;
	}
}
