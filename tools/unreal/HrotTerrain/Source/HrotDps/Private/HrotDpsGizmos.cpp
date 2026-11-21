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
	// the asked layers + the label mode's (R-143 / DD-024: 2 = UnitCards + UnitSymbols, 3 = also Labels)
	TArray<FString> All = GizmoLayers;
	if (LabelMode >= 2) { All.AddUnique(TEXT("UnitCards")); All.AddUnique(TEXT("UnitSymbols")); }
	if (LabelMode >= 3) All.AddUnique(TEXT("Labels"));
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
	if (!W || !bGizmosDirty) return;   // rebuilt only when a frame (or the asked layers) changed — 0 B per steady frame (R-2084)
	bGizmosDirty = false;
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
	for (const TPair<FString, TArray<FGizmoPrim>>& L : GizmoLayersShown)
		for (const FGizmoPrim& G : L.Value)
		{
			if (G.Points.Num() == 0) continue;
			auto U = [&](int32 I) { return EnuMToUnreal(G.Points[I]) + FVector(0, 0, GizmoLiftCm); };
			GizmosDrawn++;
			const float Th = FMath::Clamp(G.Width, 1.f, 20.f);
			const FLinearColor Col(G.Colour);
			switch (G.Shape)
			{
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeLineStrip:
				for (int32 I = 0; I + 1 < G.Points.Num(); I++) LB->DrawLine(U(I), U(I + 1), Col, SDPG_World, Th, 0.f);
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeArrow:
				if (G.Points.Num() >= 2) LB->DrawDirectionalArrow(U(0), U(1), 60.f, Col, 0.f, SDPG_World, Th);
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeCircle:   // HROT draws a sphere: Width = the radius in metres
				LB->DrawSphere(U(0), FMath::Max(10.f, G.Width * 100.f), 16, Col, 0.f, SDPG_World, 1.f);
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapePolygon:
				if (G.Points.Num() >= 3)   // a filled triangle, z 0 on the wire: put on the ground (the origin's height)
				{
					TArray<FVector> V = { U(0), U(1), U(2) };
					for (FVector& X : V) X.Z = GizmoLiftCm + OriginCm.Z;
					LB->DrawMesh(V, { 0, 1, 2, 0, 2, 1 }, G.Colour, SDPG_World, 0.f);
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeBox:
				if (G.Points.Num() >= 2)   // centre, then (extent x, extent y, angle deg) in metres
				{
					const FVector3d E = G.Points[1];
					LB->DrawBox(U(0), FVector(E.X * 100.0, E.Y * 100.0, 10.0), FQuat(FVector::UpVector, -FMath::DegreesToRadians(E.Z)), Col, 0.f, SDPG_World, Th);
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
