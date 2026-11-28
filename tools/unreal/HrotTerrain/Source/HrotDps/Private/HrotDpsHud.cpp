// CE-4651 (U-18m) — see HrotDpsHud.h
#include "HrotDpsHud.h"
#include "HrotDpsCameraPawn.h"
#include "HrotDpsStation.h"
#include "HrotDpsWorld.h"
#include "HrotDpsOverlay.h"
#include "HrotDpsCamera.h"
#include "CanvasItem.h"
#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "Engine/Font.h"
#include "GameFramework/PlayerController.h"
#include "HAL/PlatformTime.h"
#include "HrotSoundLog.h"

namespace
{
	const FLinearColor White(1, 1, 1, 1), Grey(0.6f, 0.6f, 0.6f, 1), Yellow(1, 0.85f, 0.2f, 1), Red(220 / 255.f, 30 / 255.f, 30 / 255.f, 1);
	const TCHAR* StanceName(uint8 S) { return S == 2 ? TEXT("CROUCHED") : S == 3 ? TEXT("PRONE") : TEXT("STANDING"); }
	using FLines = TArray<TPair<FString, FLinearColor>>;
	/** the UseReason word of a refused row (the contract's UseReason order) */
	FString UseReasonWord(uint8 R)
	{
		static const TCHAR* W[] = { TEXT("unknown"), TEXT("ok"), TEXT("out of reach"), TEXT("locked"), TEXT("door refuses"), TEXT("not down"), TEXT("seat taken"), TEXT("unfit"), TEXT("carrying"), TEXT("nothing to do"), TEXT("failed") };
		return R < UE_ARRAY_COUNT(W) ? FString(W[R]) : FString(TEXT("refused"));
	}

	struct FPainter
	{
		UCanvas* C;
		UFont* Font = GEngine->GetMediumFont();
		void Line(float X0, float Y0, float X1, float Y1, const FLinearColor& Col, float Thick)
		{
			FCanvasLineItem L(FVector2D(X0, Y0), FVector2D(X1, Y1));
			L.SetColor(Col); L.LineThickness = Thick; L.BlendMode = SE_BLEND_Translucent;
			C->DrawItem(L);
		}
		void Rect(float X, float Y, float W, float H, const FLinearColor& Col)
		{
			FCanvasTileItem T(FVector2D(X, Y), FVector2D(W, H), Col);
			T.BlendMode = SE_BLEND_Translucent;
			C->DrawItem(T);
		}
		void Text(const FString& S, float X, float Y, const FLinearColor& Col)
		{
			FCanvasTextItem T(FVector2D(X, Y), FText::FromString(S), Font, Col);
			T.EnableShadow(FLinearColor(0, 0, 0, 0.85f));   // readable over sky and sand
			C->DrawItem(T);
		}
		float Width(const FString& S) { float W = 0, H = 0; C->TextSize(Font, S, W, H); return W; }
		void Arc(float Cx, float Cy, float Radius, float CentreDeg, float HalfDeg, float Thick, const FLinearColor& Col)
		{
			const int32 N = 24;
			const float R = Radius + Thick * 0.5f;
			for (int32 I = 0; I < N; ++I)
			{
				const float A0 = FMath::DegreesToRadians(CentreDeg - HalfDeg + 2.f * HalfDeg * I / N);
				const float A1 = FMath::DegreesToRadians(CentreDeg - HalfDeg + 2.f * HalfDeg * (I + 1) / N);
				Line(Cx + R * FMath::Cos(A0), Cy + R * FMath::Sin(A0), Cx + R * FMath::Cos(A1), Cy + R * FMath::Sin(A1), Col, Thick);
			}
		}
		void Lines(const FLines& L, float X, float BottomY)
		{
			const float H = 20.f;
			float Y = BottomY - H * L.Num();
			for (const TPair<FString, FLinearColor>& Ln : L) { Text(Ln.Key, X, Y, Ln.Value); Y += H; }
		}
	};

	void DrawPlayer(FPainter& P, AHrotDpsCamera* Cam, UHrotDpsStation* St)
	{
		using namespace HrotPlayer;
		const float W = P.C->ClipX, H = P.C->ClipY, Cx = W * 0.5f, Cy = H * 0.5f;
		const double Now = FPlatformTime::Seconds();
		// the crosshair: four white ticks 3-10 px off the centre
		P.Line(Cx - 10, Cy, Cx - 3, Cy, White, 1.5f); P.Line(Cx + 3, Cy, Cx + 10, Cy, White, 1.5f);
		P.Line(Cx, Cy - 10, Cx, Cy - 3, White, 1.5f); P.Line(Cx, Cy + 3, Cx, Cy + 10, White, 1.5f);
		// the weapon's spread (OwnUnit.SpreadRad) as a circle through the zoomed view
		if (St->SpreadRad() > 0.f)
		{
			const float R = SpreadRadiusPx(St->SpreadRad(), Cam->VerticalFov(), H);
			if (R > 12.f && R < H * 0.45f) P.Arc(Cx, Cy, R, 0.f, 180.f, 1.f, FLinearColor(1, 1, 1, 0.35f));
		}
		// the hit marker: our round hit a unit (red: he went down)
		if (St->LastHitByUsAt() >= 0 && Now - St->LastHitByUsAt() < MarkerSeconds)
		{
			const FLinearColor Col = St->LastHitKilled() ? Red : White;
			for (int32 Sx = -1; Sx <= 1; Sx += 2) for (int32 Sy = -1; Sy <= 1; Sy += 2)
				P.Line(Cx + Sx * 7, Cy + Sy * 7, Cx + Sx * 14, Cy + Sy * 14, Col, 2.f);
		}
		// the damage arcs: toward where each hit came FROM, fading
		const float Radius = FMath::Min(W, H) * ArcRadiusShare;
		for (const UHrotDpsStation::FHit& Hit : St->HitsOnUs())
		{
			float Deg = 0;
			if (!ScreenAngle(Cam->ViewForwardEnu(), Hit.From, Deg)) continue;
			const float Fade = FMath::Clamp(1.f - float(Now - Hit.At) / ArcSeconds, 0.f, 1.f);
			P.Arc(Cx, Cy, Radius, Deg, ArcHalfWidthDeg, ArcThickness(Hit.Damage), FLinearColor(Red.R, Red.G, Red.B, 0.78f * Fade));
		}
		// U-19h: the hold-F list - HROT's offered verbs for the target, in its order; the highlighted row marked, refused rows grey with HROT's reason
		if (Cam->UseListOpen())
		{
			const TArray<FHrotDpsUseAction> Rows = St->Actions();
			float Y = Cy + 28;
			for (int32 I = 0; I < Rows.Num() && I < 8; ++I)
			{
				const FHrotDpsUseAction& A = Rows[I];
				FString Row = FString::Printf(TEXT("%s %d  %s"), I == Cam->UseListHighlight() ? TEXT(">") : TEXT(" "), I + 1, *A.Label);
				if (!A.bAvailable) Row += TEXT("  (") + UseReasonWord(A.Reason) + TEXT(")");
				P.Text(Row, Cx - 90.f, Y, !A.bAvailable ? Grey : I == Cam->UseListHighlight() ? Yellow : White);
				Y += 20.f;
			}
		}
		else
		// the use prompt under the crosshair
		if (!St->UsePrompt().IsEmpty())
		{
			const FString U = TEXT("F  ") + St->UsePrompt();
			P.Text(U, Cx - P.Width(U) * 0.5f, Cy + 28, Yellow);
		}
		// bottom-left: health, the weapons, the stance, the fire mode, the zoom
		FLines L;
		const float Hp = St->Health(), Max = St->MaxHealth();
		L.Add({ FString::Printf(TEXT("HEALTH %.0f / %.0f"), Hp, Max), Max > 0 && Hp < Max * 0.3f ? Red : White });
		const TArray<FHrotDpsAmmoLine> Ammo = St->Ammo();
		for (int32 I = 0; I < Ammo.Num(); ++I)
		{
			const FHrotDpsAmmoLine& A = Ammo[I];
			const bool bSel = A.Slot == St->ActiveSlot();
			FString Line = FString::Printf(TEXT("%s %d %s  %s  %d/%d"), bSel ? TEXT(">") : TEXT(" "), I + 1, *A.Weapon.ToUpper(), *A.AmmoType, A.Rounds, A.Max);
			if (A.Magazine >= 0) Line += FString::Printf(TEXT("  [%d]"), A.Magazine);
			if (A.bBusy) Line += TEXT("  LOADING");
			if (A.bOverheated) Line += TEXT("  OVERHEATED");
			else if (A.HeatPercent > 0) Line += FString::Printf(TEXT("  heat %d%%"), A.HeatPercent);
			L.Add({ Line, bSel ? Yellow : Grey });
		}
		if (Cam->PlayerScheme() == HrotCamera::EScheme::Person)
			L.Add({ FString::Printf(TEXT("STANCE %s%s"), StanceName(Cam->StanceWanted()), Cam->IsRunning() ? TEXT("  RUN") : TEXT("")), White });
		L.Add({ FString::Printf(TEXT("AIM %s%s   unit %d   Esc: let go"), *St->FireMode(), Cam->IsZooming() ? TEXT("  ZOOM") : TEXT(""), Cam->PlayedId()), Grey });
		P.Lines(L, 16.f, H - 16.f);
	}

	FLinearColor SoundColour(EHrotSndChannel C)
	{
		switch (C)
		{
		case EHrotSndChannel::Radio: return FLinearColor(0.35f, 0.85f, 1.f, 1.f);      // the earpiece: cyan
		case EHrotSndChannel::World: return FLinearColor(1.f, 0.85f, 0.2f, 1.f);        // a voice in the world: yellow
		case EHrotSndChannel::Weapon: return FLinearColor(1.f, 0.5f, 0.25f, 1.f);       // fire: orange
		case EHrotSndChannel::Ambience: return FLinearColor(0.55f, 0.9f, 0.5f, 1.f);    // beds and alarms: green
		case EHrotSndChannel::Event: return FLinearColor(0.95f, 0.45f, 0.8f, 1.f);      // events: magenta
		default: return FLinearColor(0.8f, 0.8f, 0.8f, 1.f);
		}
	}

	/** U-19f: the sound log (hrot.snd.Debug 1), lower-left above the status lines, newest at the bottom; the caption top-centre. Draws only what is live:
	 *  with nothing playing and no caption it costs two reads (the lines are formatted once, when a sound starts) */
	void DrawSoundLog(FPainter& P, float BottomY)
	{
		if (!FHrotSoundLog::IsOn()) return;
		const FHrotSoundLog& Log = FHrotSoundLog::Get();
		const double Now = FPlatformTime::Seconds();
		float CapAlpha = 0.f;
		const FString& Cap = Log.Caption(Now, CapAlpha);
		if (!Cap.IsEmpty())
		{
			UFont* Keep = P.Font; P.Font = GEngine->GetLargeFont();
			const float W = P.Width(Cap);
			P.Rect(P.C->ClipX * 0.5f - W * 0.5f - 12.f, 24.f, W + 24.f, 34.f, FLinearColor(0.f, 0.f, 0.f, 0.55f * CapAlpha));
			P.Text(Cap, P.C->ClipX * 0.5f - W * 0.5f, 28.f, FLinearColor(1.f, 1.f, 1.f, CapAlpha));
			P.Font = Keep;
		}
		int32 Live = 0;
		for (int32 I = 0; I < Log.Num(); ++I) if (Log.Alpha(I, Now) > 0.f) ++Live;
		const float H = 20.f;
		float Y = BottomY - H * Live;
		for (int32 I = 0; I < Log.Num(); ++I)
		{
			const float A = Log.Alpha(I, Now);
			if (A <= 0.f) continue;
			const FHrotSoundLogEntry& E = Log.At(I);
			FLinearColor Col = SoundColour(E.Channel); Col.A = A;
			P.Text(E.Text, 16.f, Y, Col);
			Y += H;
		}
	}

	/** U-19g: the selected entity is marked: four yellow corner brackets around its screen position (a click used to select with no visible sign) */
	void DrawSelection(FPainter& P, AHrotDpsCamera* Cam)
	{
		if (!Cam->SelectedId()) return;
		const FBox2D R = Cam->ScreenRectOf(Cam->SelectedId());   // the entity's own body, projected (not a fixed square at a point)
		if (!R.bIsValid) return;
		++Cam->SelectionMarksDrawn;
		const FVector2D C = R.GetCenter(), Half = FVector2D(FMath::Max(R.GetExtent().X, 14.f), FMath::Max(R.GetExtent().Y, 14.f));
		const float L = FMath::Min(9.f, FMath::Min(Half.X, Half.Y)), T = 2.f;
		for (int32 Sx = -1; Sx <= 1; Sx += 2) for (int32 Sy = -1; Sy <= 1; Sy += 2)
		{
			const float X = C.X + Sx * Half.X, Y = C.Y + Sy * Half.Y;
			P.Line(X, Y, X - Sx * L, Y, Yellow, T); P.Line(X, Y, X, Y - Sy * L, Yellow, T);
		}
	}

	void DrawMenu(FPainter& P, AHrotDpsCamera* Cam, APlayerController* PC)
	{
		const TArray<FHrotMenuRow> Rows = Cam->MenuRows();
		const FVector2D At = Cam->MenuOrigin();
		const float W = AHrotDpsCamera::MenuWidth, Rh = AHrotDpsCamera::MenuRowHeight, Hh = AHrotDpsCamera::MenuHeader;
		float Mx = -1, My = -1;
		if (PC) PC->GetMousePosition(Mx, My);
		float Wide = W;
		for (const FHrotMenuRow& R : Rows) if (!R.bEnabled) Wide = FMath::Max(Wide, P.Width(R.Label + TEXT("  - ") + R.Reason) + 48.f);
		P.Rect(At.X, At.Y, Wide, Hh + Rh * Rows.Num(), FLinearColor(0.06f, 0.07f, 0.08f, 0.92f));
		P.Text(FString::Printf(TEXT("Unit %d"), Cam->MenuEntityId()), At.X + 8, At.Y + 4, Grey);
		for (int32 I = 0; I < Rows.Num(); ++I)
		{
			const FHrotMenuRow& R = Rows[I];
			const float Y = At.Y + Hh + Rh * I;
			if (R.bEnabled && Mx >= At.X && Mx <= At.X + Wide && My >= Y && My < Y + Rh) P.Rect(At.X, Y, Wide, Rh, FLinearColor(0.2f, 0.35f, 0.6f, 0.9f));
			P.Text(R.bEnabled ? R.Label : R.Label + TEXT("  - ") + R.Reason, At.X + 8, Y + 3, R.bEnabled ? White : Grey);
			P.Text(R.Shortcut, At.X + Wide - P.Width(R.Shortcut) - 8, Y + 3, Grey);
		}
	}
}

static void DrawBody(UCanvas* Canvas, AHrotDpsCamera* Cam, APlayerController* PC)
{
	if (!Canvas || !Cam || !GEngine) return;
	FPainter P{ Canvas };
	UHrotDpsStation* St = UHrotDpsStation::Get(Cam);
	if (Cam->GetMode() == EHrotCameraMode::Player && St) { DrawPlayer(P, Cam, St); DrawSoundLog(P, Canvas->ClipY - 16.f - 20.f * 9); return; }
	DrawSelection(P, Cam);
	if (Cam->IsMenuOpen()) DrawMenu(P, Cam, PC);
	// the status line: what is selected, how to reach the menu, a possession in progress
	FLines L;
	if (Cam->PendingPossess() && St)
	{
		FString Line = FString::Printf(TEXT("Taking control of unit %d - %s"), Cam->PendingPossess(), *St->SlotState());
		if (!St->SlotReason().IsEmpty()) Line += TEXT(" (") + St->SlotReason() + TEXT(")");
		L.Add({ Line + TEXT("   Esc: cancel"), Yellow });
	}
	if (!Cam->NoticeLine().IsEmpty()) L.Add({ Cam->NoticeLine(), Yellow });
	if (St && !Cam->PendingPossess())   // the playable units HROT offers this station (PlayerSlot.Offers) - a number possesses one
	{
		const TArray<int32> Ids = St->OfferIds();
		if (Ids.Num() == 0) L.Add({ St->SlotState() == TEXT("(no slot)") ? TEXT("No player slot from the station yet") : TEXT("No playable units offered to this station"), Grey });
		else
		{
			L.Add({ TEXT("Playable units - press the number to possess:"), White });
			for (int32 K = 0; K < Ids.Num() && K < 9; ++K)
				L.Add({ FString::Printf(TEXT("  %d  %s  [unit %d]"), K + 1, *St->OfferName(Ids[K]), Ids[K]), White });
			if (Ids.Num() > 9) L.Add({ FString::Printf(TEXT("  ... %d playable in all: click any of them and press P"), Ids.Num()), Grey });
		}
	}
	if (Cam->SelectedId()) L.Add({ FString::Printf(TEXT("Selected unit %d   right-click: menu   F follow   P possess"), Cam->SelectedId()), White });
	else L.Add({ TEXT("Click a unit to select it, right-click it for its menu"), Grey });
	P.Lines(L, 16.f, Canvas->ClipY - 16.f);
	DrawSoundLog(P, Canvas->ClipY - 16.f - 20.f * L.Num() - 6.f);
}

void HrotDpsHud::Draw(UCanvas* Canvas, AHrotDpsCamera* Cam, APlayerController* PC)
{
	DrawBody(Canvas, Cam, PC);
	// U-19e (F-2): the director's subtitle and fade only while this client shows the station's suggested view; a free or followed camera ignores them
	if (!Canvas || !Cam || !GEngine || Cam->GetMode() != EHrotCameraMode::Director) return;
	UHrotDpsWorld* W = UHrotDpsWorld::Get(Cam);
	if (!W) return;
	const UHrotDpsWorld::FOverlayNow O = W->OverlayNow();
	if (!O.bHas) return;
	HrotDpsOverlay::FSpec Sp; Sp.Subtitle = O.Subtitle; Sp.Pos = O.Pos; Sp.Alpha = O.Alpha; Sp.Fade = O.Fade;
	const HrotDpsOverlay::FPainted Pd = HrotDpsOverlay::Paint(Canvas, FVector2D(Canvas->ClipX, Canvas->ClipY), Sp, GEngine->GetLargeFont());
	W->NoteOverlayDrawn(Pd.bSubtitle, Pd.bFade);
}

void AHrotDpsHud::DrawHUD()
{
	Super::DrawHUD();
	HrotDpsHud::Draw(Canvas, Cast<AHrotDpsCamera>(GetOwningPawn()), GetOwningPlayerController());
}
