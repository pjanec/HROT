// CE-4651 (U-18z) — HROT's GIZMOS in the IG: the client's GizmoView (which layers) and the gateway's GizmoFrame (each layer's complete list of
// primitives, SimNetGateway DD-017 §3.7 / DPS-Client-Guide "Gizmos"), drawn every frame through the world's line batcher (DrawDebug*,
// compiled in Development; not Shipping). Shapes: LineStrip / Arrow / Circle (HROT a sphere: Width = radius) / Polygon (a filled triangle,
// z 0 on the wire: put on the ground here) / Box (centre, then extent x, extent y, angle deg) / Text. Icon is reserved (not sent).
// Rules (the guide): no view, no work; one view per seat; the view is VOLATILE ⇒ resent every 2 s (and so after a world boundary).
// ⚠ First cut, unverified live (the gateway says gizmos were never checked live either). Entity cards (DD-024) will be one more kind here.
#include "HrotDpsWorld.h"
#include "HrotDpsSubsystem.h"
#include "HrotDpsClient.h"
#include "DpsTopics.h"

#include "Components/LineBatchComponent.h"
#include "GameFramework/Actor.h"
#include "Engine/World.h"
#include "Engine/GameViewportClient.h"
#include "GameFramework/PlayerController.h"
#include "Camera/PlayerCameraManager.h"

void UHrotDpsWorld::OnGizmoFrame(const FHrotDpsSample& S)
{
	const SimNetGateway_Protocol_DdsPlayer_GizmoFrame& F = *static_cast<const SimNetGateway_Protocol_DdsPlayer_GizmoFrame*>(S.Data);
	const FString Layer = UTF8_TO_TCHAR(F.Layer);
	if (!S.bValid || S.bDisposed) { GizmoLayersShown.Remove(Layer); bGizmosDirty = true; if (GizmoCardPrims.Remove(Layer)) bWireCardsDirty = true; return; }   // a disposed layer = turned off
	TArray<FGizmoPrim>& Out = GizmoLayersShown.FindOrAdd(Layer);
	Out.Reset(F.Primitives._length);
	TArray<HrotCards::FGizmoCardPrim>& LayerCards = GizmoCardPrims.FindOrAdd(Layer);
	const bool bHadCards = LayerCards.Num() > 0;
	LayerCards.Reset();
	for (uint32 I = 0; I < F.Primitives._length; I++)
	{
		const SimNetGateway_Protocol_DdsPlayer_GizmoPrimitive& P = F.Primitives._buffer[I];
		if (P.Space == SimNetGateway_Protocol_DdsPlayer_GizmoSpaceEntityCard)   // U-18y: DD-024 — a card's primitive
		{
			HrotCards::FGizmoCardPrim& C = LayerCards.AddDefaulted_GetRef();
			C.Shape = HrotCards::EGizmoShape(uint8(P.Shape)); C.AnchorId = P.AnchorId; C.Row = P.Row;
			C.CardX = P.CardX; C.CardY = P.CardY; C.CardW = P.CardW; C.CardH = P.CardH; C.Width = P.Width;
			C.ColourRgba = P.ColourRgba; C.FillRgba = P.FillRgba;
			if (P.Text[0]) C.Text = UTF8_TO_TCHAR(P.Text);
			continue;
		}
		FGizmoPrim& G = Out.AddDefaulted_GetRef();
		G.Shape = uint8(P.Shape);
		G.Colour = FColor(uint8(P.ColourRgba >> 24), uint8(P.ColourRgba >> 16), uint8(P.ColourRgba >> 8), uint8(P.ColourRgba));
		G.Width = P.Width;
		if (P.Text[0]) G.Text = UTF8_TO_TCHAR(P.Text);
		G.Points.Reserve(P.Points._length);
		for (uint32 K = 0; K < P.Points._length; K++) G.Points.Add(FVector(P.Points._buffer[K].X, P.Points._buffer[K].Y, P.Points._buffer[K].Z));
	}
	if (LayerCards.Num() || bHadCards) bWireCardsDirty = true;
	GizmoFrames++;
	bGizmosDirty = true;
}

void UHrotDpsWorld::RegroupWireCards()
{
	bWireCardsDirty = false;
	WireCards.Reset();
	for (const TPair<FString, TArray<HrotCards::FGizmoCardPrim>>& L : GizmoCardPrims)
		for (const HrotCards::FGizmoCardPrim& P : L.Value) WireCards.FindOrAdd(P.AnchorId).Prims.Add(P);
	for (TPair<int32, FWireCard>& C : WireCards)   // the key: a card is rebuilt only when what it shows changed
	{
		uint32 K = 0;
		for (const HrotCards::FGizmoCardPrim& P : C.Value.Prims)
			K = HashCombineFast(K, HashCombineFast(GetTypeHash(P.Text), HashCombineFast(uint32(P.Shape) | (uint32(P.Row) << 8),
				HashCombineFast(P.ColourRgba ^ P.FillRgba, GetTypeHash(P.CardX) ^ GetTypeHash(P.CardY) ^ GetTypeHash(P.CardW) ^ GetTypeHash(P.CardH) ^ GetTypeHash(P.Width)))));
		C.Value.Key = K;
	}
}

void UHrotDpsWorld::SendGizmoView(double Real)
{
	if (LabelMode != SentLabelMode) NextGizmoView = 0;   // a mode change: say it now
	if (Real < NextGizmoView || !MyClientId) return;
	NextGizmoView = Real + 2.0;
	UHrotDpsSubsystem* Dps = GEngine ? GEngine->GetEngineSubsystem<UHrotDpsSubsystem>() : nullptr;
	if (!Dps || !Dps->IsRunning()) return;
	// the asked layers + (only with bWireCards) the label mode's HROT cards (R-143 / DD-024: 2 = UnitCards + UnitSymbols, 3 = also Labels);
	// without them the cards are the client's own, from entity data (TickCards)
	TArray<FString> All = GizmoLayers;
	if (bWireCards && LabelMode >= 2) { All.AddUnique(TEXT("UnitCards")); All.AddUnique(TEXT("UnitSymbols")); }
	if (bWireCards && LabelMode >= 3) All.AddUnique(TEXT("Labels"));
	if (!bWireCards && WireCards.Num() > 0) { WireCards.Reset(); for (auto& L : GizmoCardPrims) L.Value.Reset(); }   // switched off: back to our own
	SentLabelMode = LabelMode;
	if (All.Num() == 0 && !bGizmoViewSent) return;   // no view, no work (and nothing to withdraw)
	TArray<SimNetGateway_Protocol_DdsPlayer_NameItem> Names;
	for (const FString& L : All) { SimNetGateway_Protocol_DdsPlayer_NameItem& N = Names.AddZeroed_GetRef(); FCStringAnsi::Strncpy(N.Name, TCHAR_TO_UTF8(*L), sizeof(N.Name)); }
	SimNetGateway_Protocol_DdsPlayer_GizmoView V{};
	V.Layers._length = V.Layers._maximum = uint32(Names.Num()); V.Layers._buffer = Names.GetData(); V.Layers._release = false;
	V.RateHz = 10.f; V.ClientId = MyClientId;
	Dps->Client().Write(TEXT("DPS_GizmoView"), &V);
	bGizmoViewSent = All.Num() > 0;
}

void UHrotDpsWorld::DrawGizmos()
{
	UWorld* W = GetWorld();
	if (!W) return;
	// the camera: a line's px width is a world width in cm only at one distance and FOV (HROT's map draws px)
	FVector Cam = GizmoBuiltLoc; float TanHalf = GizmoBuiltTanHalfFov > 0 ? GizmoBuiltTanHalfFov : 1.f, ViewW = GizmoBuiltW > 0 ? GizmoBuiltW : 1280.f;
	if (APlayerController* PC = W->GetFirstPlayerController(); PC && PC->PlayerCameraManager)
	{
		Cam = PC->PlayerCameraManager->GetCameraLocation();
		TanHalf = FMath::Tan(FMath::DegreesToRadians(PC->PlayerCameraManager->GetFOVAngle()) * 0.5f);
		FVector2D S; if (UGameViewportClient* G = W->GetGameViewport()) { G->GetViewportSize(S); if (S.X > 0) ViewW = float(S.X); }
	}
	if (!bGizmosDirty)   // otherwise rebuilt only when a frame (or the asked layers) changed, and when the camera moved enough to change a width: 0 B per steady frame (R-2084)
	{
		if (GizmoLayersShown.Num() == 0 || !GizmoLines.IsValid() || GizmoBuiltTanHalfFov <= 0) return;
		const bool bMoved = FVector::DistSquared(Cam, GizmoBuiltLoc) > FMath::Square(0.1f * FMath::Max(GizmoNearestCm, 200.f));
		const bool bZoomed = FMath::Abs(TanHalf / GizmoBuiltTanHalfFov - 1.f) > 0.15f || FMath::Abs(ViewW / GizmoBuiltW - 1.f) > 0.15f;
		if (!bMoved && !bZoomed) return;
	}
	bGizmosDirty = false;
	GizmoBuiltLoc = Cam; GizmoBuiltTanHalfFov = TanHalf; GizmoBuiltW = ViewW; GizmoNearestCm = 1e9f;
	ULineBatchComponent* LB = GizmoLines.Get();
	if (!LB)
	{
		FActorSpawnParameters P; P.ObjectFlags |= RF_Transient; P.Name = MakeUniqueObjectName(W, AActor::StaticClass(), TEXT("HrotGizmos"));
		AActor* Holder = W->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity, P);
		if (!Holder) return;
		LB = NewObject<ULineBatchComponent>(Holder, TEXT("Lines"));
		Holder->SetRootComponent(LB); LB->RegisterComponent();
		GizmoLines = LB;
	}
	const float GizmoLiftCm = 20.f;   // a ground line above the ground, not in it
	LB->Flush();
	GizmoTexts.Reset();
	GizmosDrawn = 0;
	// one line of Px pixels, its cm width taken at its midpoint's distance (still SDPG_World: depth-tested)
	auto Line = [&](const FVector& A, const FVector& B, const FLinearColor& Col, float Px)
	{
		const float D = float(FVector::Dist((A + B) * 0.5, Cam));
		GizmoNearestCm = FMath::Min(GizmoNearestCm, D);
		LB->DrawLine(A, B, Col, SDPG_World, HrotCards::PxToCm(Px, D, TanHalf, ViewW), 0.f);
	};
	for (const TPair<FString, TArray<FGizmoPrim>>& L : GizmoLayersShown)
		for (const FGizmoPrim& G : L.Value)
		{
			if (G.Points.Num() == 0) continue;
			auto U = [&](int32 I) { return EnuMToUnreal(G.Points[I]) + FVector(0, 0, GizmoLiftCm); };
			GizmosDrawn++;
			const float Px = FMath::Max(G.Width, 1.5f);   // the wire's Width is pixels (HROT's map); Circle's is its radius, below
			const FLinearColor Col(G.Colour);
			switch (G.Shape)
			{
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeLineStrip:
				for (int32 I = 0; I + 1 < G.Points.Num(); I++) Line(U(I), U(I + 1), Col, Px);
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeArrow:
				if (G.Points.Num() >= 2)
				{
					const FVector A = U(0), B = U(1);
					const FVector Dir = (B - A).GetSafeNormal(), Lateral = FVector::CrossProduct(Dir, FVector::UpVector).GetSafeNormal();
					Line(A, B, Col, Px);
					Line(B, B - Dir * 60.f + Lateral * 25.f, Col, Px); Line(B, B - Dir * 60.f - Lateral * 25.f, Col, Px);   // the head
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeCircle:   // HROT draws a sphere: Width = the radius in metres; three great circles of 1.5 px lines
			{
				const FVector C = U(0); const float R = FMath::Max(10.f, G.Width * 100.f);
				constexpr int32 Seg = 24;
				for (int32 Axis = 0; Axis < 3; Axis++)
					for (int32 K = 0; K < Seg; K++)
					{
						auto At = [&](int32 I) { const float T = 2.f * PI * I / Seg, X = FMath::Cos(T) * R, Y = FMath::Sin(T) * R; return C + (Axis == 0 ? FVector(X, Y, 0) : Axis == 1 ? FVector(X, 0, Y) : FVector(0, X, Y)); };
						Line(At(K), At(K + 1), Col, 1.5f);
					}
				break;
			}
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapePolygon:
				if (G.Points.Num() >= 3)   // a filled triangle, z 0 on the wire: put on the ground (the origin's height)
				{
					TArray<FVector> V = { U(0), U(1), U(2) };
					for (FVector& X : V) X.Z = GizmoLiftCm + OriginCm.Z;
					LB->DrawMesh(V, { 0, 1, 2, 0, 2, 1 }, G.Colour, SDPG_World, 0.f);
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeBox:
				if (G.Points.Num() >= 2)   // centre, then (extent x, extent y, angle deg) in metres; the 12 edges
				{
					const FVector3d E = G.Points[1];
					const FVector C = U(0); const FQuat Q(FVector::UpVector, -FMath::DegreesToRadians(E.Z));
					const FVector Ext(E.X * 100.0, E.Y * 100.0, 10.0);
					auto Cn = [&](int32 I) { return C + Q.RotateVector(FVector((I & 1) ? Ext.X : -Ext.X, (I & 2) ? Ext.Y : -Ext.Y, (I & 4) ? Ext.Z : -Ext.Z)); };
					for (int32 I = 0; I < 8; I++)
						for (int32 Bit = 1; Bit < 8; Bit <<= 1)
							if (!(I & Bit)) Line(Cn(I), Cn(I | Bit), Col, Px);
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeText:
				if (!G.Text.IsEmpty())
				{
					FGizmoText& T = GizmoTexts.AddDefaulted_GetRef();
					T.At = U(0); T.Text = G.Text; T.Colour = G.Colour;
					T.Key = HashCombineFast(GetTypeHash(G.Text), G.Colour.ToPackedRGBA());
				}
				break;
			default: break;
			}
		}
}

void UHrotDpsWorld::SetGizmoLayers(const TArray<FString>& Layers)
{
	GizmoLayers = Layers;
	NextGizmoView = 0;   // say it now
	bGizmosDirty = true;
	static const TCHAR* CardLayers[] = { TEXT("UnitCards"), TEXT("UnitSymbols"), TEXT("Labels") };   // kept for the label mode
	for (auto It = GizmoLayersShown.CreateIterator(); It; ++It)
	{
		bool bCard = false; for (const TCHAR* C : CardLayers) bCard |= It.Key() == C;
		if (!bCard && !Layers.Contains(It.Key())) It.RemoveCurrent();
	}
}

FString UHrotDpsWorld::DescribeGizmos() const
{
	int32 N = 0;
	for (const TPair<FString, TArray<FGizmoPrim>>& L : GizmoLayersShown) N += L.Value.Num();
	return FString::Printf(TEXT("gizmos asked [%s], %d layers shown, %d primitives, %d frames, %d drawn"), *FString::Join(GizmoLayers, TEXT(",")), GizmoLayersShown.Num(), N, GizmoFrames, GizmosDrawn);
}
