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
#include "ProceduralMeshComponent.h"
#include "Materials/MaterialInterface.h"
#include "UObject/SoftObjectPath.h"
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
	TArray<FGizmoPrim> Old = MoveTemp(Out);   // the same shape resent on the same terrain keeps its draped points
	const uint32 Stamp = HashCombineFast(HashCombineFast(GetTypeHash(ShownTerrain), GetTypeHash(ShownBase)), GetTypeHash(ShownOverlay));
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
		if (Old.IsValidIndex(Out.Num() - 1) && Old[Out.Num() - 1].bDraped && Old[Out.Num() - 1].DrapeStamp == Stamp && Old[Out.Num() - 1].Shape == G.Shape && Old[Out.Num() - 1].Points == G.Points)
		{
			FGizmoPrim& O = Old[Out.Num() - 1];
			G.bDraped = true; G.bGround = O.bGround; G.DrapeStamp = Stamp; G.Drape = MoveTemp(O.Drape);
		}
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
	FVector Cam = GizmoBuiltLoc, Fwd = GizmoBuiltFwd; float TanHalf = GizmoBuiltTanHalfFov > 0 ? GizmoBuiltTanHalfFov : 1.f, ViewW = GizmoBuiltW > 0 ? GizmoBuiltW : 1280.f;
	if (APlayerController* PC = W->GetFirstPlayerController(); PC && PC->PlayerCameraManager)
	{
		Cam = PC->PlayerCameraManager->GetCameraLocation(); Fwd = PC->PlayerCameraManager->GetCameraRotation().Vector();
		TanHalf = FMath::Tan(FMath::DegreesToRadians(PC->PlayerCameraManager->GetFOVAngle()) * 0.5f);
		FVector2D S; if (UGameViewportClient* G = W->GetGameViewport()) { G->GetViewportSize(S); if (S.X > 0) ViewW = float(S.X); }
	}
	if (GizmoLineMaterial != GizBuiltMaterial || GizmoLinePxFloor != GizBuiltFloor) bGizmosDirty = true;   // the look changed
	if (!bGizmosDirty)   // otherwise rebuilt only when a frame (or the asked layers) changed, and when the camera moved enough to change a width: 0 B per steady frame (R-2084)
	{
		if (GizmoLayersShown.Num() == 0 || !GizmoLines.IsValid() || GizmoBuiltTanHalfFov <= 0) return;
		const bool bMoved = FVector::DistSquared(Cam, GizmoBuiltLoc) > FMath::Square(0.1f * FMath::Max(GizmoNearestCm, 200.f));
		const bool bZoomed = FMath::Abs(TanHalf / GizmoBuiltTanHalfFov - 1.f) > 0.15f || FMath::Abs(ViewW / GizmoBuiltW - 1.f) > 0.15f;
		const bool bTurned = FVector::DotProduct(Fwd, GizmoBuiltFwd) < 0.99939f;   // cos 2 deg
		if (!bMoved && !bZoomed && !bTurned) return;
	}
	bGizmosDirty = false;
	GizmoBuiltLoc = Cam; GizmoBuiltFwd = Fwd; GizmoBuiltTanHalfFov = TanHalf; GizmoBuiltW = ViewW; GizmoNearestCm = 1e9f;
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
	UProceduralMeshComponent* Mesh = GizmoMesh.Get();
	if (!Mesh)
	{
		Mesh = NewObject<UProceduralMeshComponent>(LB->GetOwner(), TEXT("Ribbons"));
		Mesh->SetupAttachment(LB);
		Mesh->bUseComplexAsSimpleCollision = false;
		Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
		Mesh->SetGenerateOverlapEvents(false); Mesh->SetCanEverAffectNavigation(false);
		Mesh->SetCastShadow(false); Mesh->bCastDynamicShadow = false; Mesh->bAffectDistanceFieldLighting = false; Mesh->bAffectDynamicIndirectLighting = false;
		Mesh->bVisibleInRayTracing = false; Mesh->bVisibleInReflectionCaptures = false; Mesh->bVisibleInRealTimeSkyCaptures = false; Mesh->bReceivesDecals = false;
		Mesh->RegisterComponent();
		GizmoMesh = Mesh; GizBuiltMaterial = TEXT("?");
	}
	if (GizBuiltMaterial != GizmoLineMaterial)
	{
		const FString Name = TEXT("M_HrotGizmoLine") + GizmoLineMaterial;
		Mesh->SetMaterial(0, Cast<UMaterialInterface>(FSoftObjectPath(FString::Printf(TEXT("/Game/Hrot/Fx/Labels/%s.%s"), *Name, *Name)).TryLoad()));
		GizBuiltMaterial = GizmoLineMaterial;
	}
	GizBuiltFloor = GizmoLinePxFloor;
	LB->Flush();
	GizmoTexts.Reset();
	GizmosDrawn = 0;
	// one line of Px pixels = a camera-facing ribbon quad: half-width (Px / 2 + 1 px of anti-aliasing fringe) in cm AT EACH END'S distance, extruded
	// along normalize(cross(segment, view)); UV = (Px, -1 / +1 across the width) so the material knows where the solid core ends (M_HrotGizmoLine)
	GizVerts.Reset(); GizNormals.Reset(); GizTris.Reset(); GizUv.Reset(); GizCols.Reset();
	auto Line = [&](const FVector& A, const FVector& B, const FColor& Col, float Px)
	{
		const FVector Dir = B - A;
		const FVector Mid = (A + B) * 0.5;
		const FVector Across = FVector::CrossProduct(Dir, Mid - Cam).GetSafeNormal();
		if (Across.IsNearlyZero()) return;   // a point, or a line pointing at the camera
		const float Da = float(FVector::Dist(A, Cam)), Db = float(FVector::Dist(B, Cam));
		GizmoNearestCm = FMath::Min(GizmoNearestCm, float(FVector::Dist(Mid, Cam)));
		const float Ha = HrotCards::PxToCm(Px * 0.5f + 1.f, Da, TanHalf, ViewW), Hb = HrotCards::PxToCm(Px * 0.5f + 1.f, Db, TanHalf, ViewW);
		const int32 V0 = GizVerts.Num();
		GizVerts.Add(A + Across * Ha); GizVerts.Add(A - Across * Ha); GizVerts.Add(B + Across * Hb); GizVerts.Add(B - Across * Hb);
		const FColor C = Col;   // the gizmo's RGBA as it is (the material reads vertex colours as they are)
		for (int32 K = 0; K < 4; K++) { GizNormals.Add(FVector::UpVector); GizCols.Add(C); GizUv.Add(FVector2D(Px, (K & 1) ? -1.0 : 1.0)); }
		GizTris.Append({ V0, V0 + 1, V0 + 2, V0 + 2, V0 + 1, V0 + 3 });
	};
	// ── draping (HROT GizmoRenderer3D M18): the ground's height under a point (a downward trace against static geometry only: the landscape and
	// buildings, never a vehicle or a man), lifted by max(6 cm, 0.5 px at that distance) so the line does not fight the terrain's depth ──
	const uint32 Stamp = HashCombineFast(HashCombineFast(GetTypeHash(ShownTerrain), GetTypeHash(ShownBase)), GetTypeHash(ShownOverlay));
	auto GroundAt = [&](const FVector& Ue)
	{
		FHitResult Hit; float Z = float(OriginCm.Z);
		const FCollisionObjectQueryParams Obj(ECC_WorldStatic);
		const FCollisionQueryParams Q(SCENE_QUERY_STAT(HrotGizmoDrape), false);
		if (W->LineTraceSingleByObjectType(Hit, FVector(Ue.X, Ue.Y, OriginCm.Z + 50000.0), FVector(Ue.X, Ue.Y, OriginCm.Z - 50000.0), Obj, Q)) Z = float(Hit.ImpactPoint.Z);
		const float D = float(FVector::Dist(FVector(Ue.X, Ue.Y, Z), Cam));
		return FVector(Ue.X, Ue.Y, Z + FMath::Max(6.f, 0.5f * 2.f * D * TanHalf / ViewW));
	};
	constexpr double DrapeStepM = 3.0; constexpr int32 MaxPieces = 160;   // HROT: DrapeStep, MaxSegments
	auto DrapePoly = [&](TArray<FVector>& Out, const TArray<FVector>& Enu)   // a z = 0 polyline (ENU m), every edge cut into <= 3 m pieces
	{
		Out.Reset();
		for (int32 I = 0; I < Enu.Num(); I++)
		{
			Out.Add(GroundAt(EnuMToUnreal(Enu[I])));
			if (I + 1 >= Enu.Num()) break;
			const int32 N = FMath::Clamp(FMath::CeilToInt(FVector::Dist2D(Enu[I], Enu[I + 1]) / DrapeStepM), 1, MaxPieces);
			for (int32 K = 1; K < N; K++) Out.Add(GroundAt(EnuMToUnreal(FMath::Lerp(Enu[I], Enu[I + 1], double(K) / N))));
		}
	};
	for (TPair<FString, TArray<FGizmoPrim>>& L : GizmoLayersShown)
		for (FGizmoPrim& G : L.Value)
		{
			if (G.Points.Num() == 0) continue;
			if (!G.bDraped)
			{
				G.bDraped = true; G.DrapeStamp = Stamp; G.Drape.Reset();
				const bool bPoint = G.Shape == SimNetGateway_Protocol_DdsPlayer_GizmoShapeCircle || G.Shape == SimNetGateway_Protocol_DdsPlayer_GizmoShapeBox || G.Shape == SimNetGateway_Protocol_DdsPlayer_GizmoShapeText;
				G.bGround = true;   // all the points z = 0 (a Circle's / Box's / Text's: the centre; a Box's second point is extents + an angle)
				for (int32 I = 0; I < (bPoint ? 1 : G.Points.Num()); I++) G.bGround &= FMath::IsNearlyZero(G.Points[I].Z, 1e-3);
				if (G.bGround)
				{
					TArray<FVector> Enu;
					switch (G.Shape)
					{
					case SimNetGateway_Protocol_DdsPlayer_GizmoShapeLineStrip: Enu = G.Points; DrapePoly(G.Drape, Enu); break;
					case SimNetGateway_Protocol_DdsPlayer_GizmoShapeArrow: if (G.Points.Num() >= 2) { Enu = { G.Points[0], G.Points[1] }; DrapePoly(G.Drape, Enu); } break;
					case SimNetGateway_Protocol_DdsPlayer_GizmoShapePolygon: for (int32 I = 0; I < FMath::Min(3, G.Points.Num()); I++) G.Drape.Add(GroundAt(EnuMToUnreal(G.Points[I]))); break;
					case SimNetGateway_Protocol_DdsPlayer_GizmoShapeText: G.Drape.Add(GroundAt(EnuMToUnreal(G.Points[0]))); break;
					case SimNetGateway_Protocol_DdsPlayer_GizmoShapeCircle:   // a ground circle is a horizontal ring (a selection ring), Width = its radius
					{
						const double R = FMath::Max(0.1, double(G.Width)); const int32 N = FMath::Clamp(FMath::CeilToInt(2.0 * PI * R / DrapeStepM), 24, MaxPieces);
						for (int32 K = 0; K <= N; K++) Enu.Add(G.Points[0] + FVector(FMath::Cos(2.0 * PI * K / N) * R, FMath::Sin(2.0 * PI * K / N) * R, 0));
						DrapePoly(G.Drape, Enu);
						break;
					}
					case SimNetGateway_Protocol_DdsPlayer_GizmoShapeBox:   // a ground box is its rectangle: centre, then (extent x, extent y, angle deg CCW)
						if (G.Points.Num() >= 2)
						{
							const double Ex = G.Points[1].X, Ey = G.Points[1].Y, A = FMath::DegreesToRadians(G.Points[1].Z), Cs = FMath::Cos(A), Sn = FMath::Sin(A);
							const double Cx[5] = { -Ex, Ex, Ex, -Ex, -Ex }, Cy[5] = { -Ey, -Ey, Ey, Ey, -Ey };
							for (int32 K = 0; K < 5; K++) Enu.Add(G.Points[0] + FVector(Cx[K] * Cs - Cy[K] * Sn, Cx[K] * Sn + Cy[K] * Cs, 0));
							DrapePoly(G.Drape, Enu);
						}
						break;
					default: break;
					}
				}
			}
			auto U = [&](int32 I) { return EnuMToUnreal(G.Points[I]); };   // a shape with height: drawn as given
			GizmosDrawn++;
			const float Px = FMath::Max(G.Width, GizmoLinePxFloor);   // the wire's Width is pixels (HROT's map); a Circle's is its radius, below
			const FColor Col = G.Colour;
			auto Polyline = [&](bool bHead)
			{
				for (int32 I = 0; I + 1 < G.Drape.Num(); I++) Line(G.Drape[I], G.Drape[I + 1], Col, Px);
				if (bHead && G.Drape.Num() >= 2)
				{
					const FVector B = G.Drape.Last(), Dir = (B - G.Drape[G.Drape.Num() - 2]).GetSafeNormal(), Lateral = FVector::CrossProduct(Dir, FVector::UpVector).GetSafeNormal();
					Line(B, B - Dir * 60.f + Lateral * 25.f, Col, Px); Line(B, B - Dir * 60.f - Lateral * 25.f, Col, Px);
				}
			};
			switch (G.Shape)
			{
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeLineStrip:
				if (G.bGround) Polyline(false);
				else for (int32 I = 0; I + 1 < G.Points.Num(); I++) Line(U(I), U(I + 1), Col, Px);
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeArrow:
				if (G.bGround) Polyline(true);
				else if (G.Points.Num() >= 2)
				{
					const FVector A = U(0), B = U(1);
					const FVector Dir = (B - A).GetSafeNormal(), Lateral = FVector::CrossProduct(Dir, FVector::UpVector).GetSafeNormal();
					Line(A, B, Col, Px);
					Line(B, B - Dir * 60.f + Lateral * 25.f, Col, Px); Line(B, B - Dir * 60.f - Lateral * 25.f, Col, Px);   // the head
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeCircle:
				if (G.bGround) { Polyline(false); break; }
				{   // HROT draws a sphere with height: Width = the radius in metres; three great circles of 1.5 px lines
					const FVector C = U(0); const float R = FMath::Max(10.f, G.Width * 100.f);
					constexpr int32 Seg = 24;
					for (int32 Axis = 0; Axis < 3; Axis++)
						for (int32 K = 0; K < Seg; K++)
						{
							auto At = [&](int32 I) { const float T = 2.f * PI * I / Seg, X = FMath::Cos(T) * R, Y = FMath::Sin(T) * R; return C + (Axis == 0 ? FVector(X, Y, 0) : Axis == 1 ? FVector(X, 0, Y) : FVector(0, X, Y)); };
							Line(At(K), At(K + 1), Col, 1.5f);
						}
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapePolygon:
				if (G.Points.Num() >= 3)   // a filled triangle: on the ground when z = 0, else as given
				{
					TArray<FVector> V = G.bGround && G.Drape.Num() == 3 ? G.Drape : TArray<FVector>{ U(0), U(1), U(2) };
					LB->DrawMesh(V, { 0, 1, 2, 0, 2, 1 }, G.Colour, SDPG_World, 0.f);
				}
				break;
			case SimNetGateway_Protocol_DdsPlayer_GizmoShapeBox:
				if (G.bGround) { Polyline(false); break; }
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
					T.At = G.bGround && G.Drape.Num() ? G.Drape[0] : U(0); T.Text = G.Text; T.Colour = G.Colour;
					T.Key = HashCombineFast(GetTypeHash(G.Text), G.Colour.ToPackedRGBA());
				}
				break;
			default: break;
			}
		}
	// the ribbons: the same vertex count = an in-place update (a steady frame allocates nothing), else the section is made again
	if (GizVerts.Num() == 0) { Mesh->ClearMeshSection(0); GizSectionVerts = -1; }
	else if (GizVerts.Num() == GizSectionVerts) Mesh->UpdateMeshSection(0, GizVerts, GizNormals, GizUv, GizCols, TArray<FProcMeshTangent>());
	else { Mesh->CreateMeshSection(0, GizVerts, GizTris, GizNormals, GizUv, GizCols, TArray<FProcMeshTangent>(), false); GizSectionVerts = GizVerts.Num(); }
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
