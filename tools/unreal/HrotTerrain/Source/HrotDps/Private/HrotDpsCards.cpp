// CE-4651 (U-18y) — the world's entity labels: which entities get a card, what it says, where it hangs; drawn by UHrotCardLayer.
// ⚠ STAND-IN SOURCE. HROT's projectors decide a card's content (Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/EntityCardGizmos.cs,
//   HealthBarGizmo.cs); the gateway is asked to carry their primitives (SimNetGateway NOTE-2026-11-21-Unreal-Labels-Via-Gizmos G-1). Until
//   then the cards are built HERE from Entity data in exactly their shape — row 0 the frame (side colour, ≤ 1 m/px), row 10 the health bar
//   (≤ 1 m/px; from the coarse EntityState / VehicleLook damage), row 20 "<name> #<id>" (≤ 2 m/px; an aircraft's at any distance, HROT
//   AircraftNameGizmo / R-143 rule A; R-144: the whole card of an aircraft is never cut). The modes are R-143's: 1 none, 2 commanders + every
//   aircraft, 3 every unit; a commander's card is a unit's (Entity.IsUnit, gateway NOTE-2026-11-22-Gateway-Entity-Status). The cutoffs are the
//   CLIENT's (R-144: DD-024 P-2 rejected) - HROT's numbers are used.
#include "HrotDpsWorld.h"
#include "HrotCardLayer.h"
#include "HrotDpsMotion.h"

#include "Engine/GameViewportClient.h"
#include "Engine/LocalPlayer.h"
#include "SceneView.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "Camera/PlayerCameraManager.h"
#include "UObject/SoftObjectPath.h"

void UHrotDpsWorld::TickCards()
{
	UWorld* World = GetWorld();
	if (!World) return;
	const bool bEntityCards = LabelMode >= 2;
	if (!bEntityCards && GizmoTexts.Num() == 0)   // U-18z: gizmo texts are cards too (text-only, depth-tested like a label)
	{
		if (Cards && Cards->Shown() > 0) Cards->Clear();
		return;
	}
	if (!Cards)
	{
		Cards = NewObject<UHrotCardLayer>(this);
		Cards->Material = Cast<UMaterialInterface>(FSoftObjectPath(TEXT("/Game/Hrot/Fx/Labels/M_HrotCard.M_HrotCard")).TryLoad());
		if (!Cards->Material) UE_LOG(LogTemp, Warning, TEXT("DPS labels: /Game/Hrot/Fx/Labels/M_HrotCard is missing (scripts/unreal/hrot_card_material.py) - no labels"));
	}
	APlayerController* PC = World->GetFirstPlayerController();
	if (!PC || !PC->PlayerCameraManager) return;
	FIntPoint Vp(1280, 720);
	float P00 = 0, P11 = 0;
	if (UGameViewportClient* G = World->GetGameViewport())
	{
		FVector2D S; G->GetViewportSize(S); if (S.X > 0 && S.Y > 0) Vp = FIntPoint(int32(S.X), int32(S.Y));
		FSceneViewProjectionData Pd;   // the real projection and view rect (aspect constraints, the camera's FOV)
		if (ULocalPlayer* LP = PC->GetLocalPlayer(); LP && G->Viewport && LP->GetProjectionData(G->Viewport, Pd))
		{
			const FMatrix M = Pd.ProjectionMatrix;
			P00 = float(M.M[0][0]); P11 = float(M.M[1][1]);
			const FIntRect R = Pd.GetConstrainedViewRect();
			if (R.Width() > 0 && R.Height() > 0) Vp = FIntPoint(R.Width(), R.Height());
		}
	}
	Cards->Begin(World, PC->PlayerCameraManager->GetCameraLocation(), PC->PlayerCameraManager->GetFOVAngle(), Vp, P00, P11);

	const int32 Air = DomainAir(), Person = DomainPerson();
	if (bWireCardsDirty) RegroupWireCards();
	// U-18z: the gizmo texts — a text-only card at the point (ids below 0: never an entity's)
	for (int32 I = 0; I < GizmoTexts.Num(); I++)
	{
		const FGizmoText& T = GizmoTexts[I];
		Cards->Show(-1 - I, T.Key, T.At, [&](HrotCards::FCard& C) { C.Text(1, T.Text, (uint32(T.Colour.R) << 24) | (uint32(T.Colour.G) << 16) | (uint32(T.Colour.B) << 8) | T.Colour.A); });
	}
	if (!bEntityCards) { Cards->End(); return; }
	if (WireCards.Num() > 0)   // DD-024: HROT's own cards — the stand-in below is not used
	{
		for (TPair<int32, FWireCard>& It : WireCards)
		{
			FEnt* E = Ents.Find(It.Key);
			if (!E || !E->bHasEntity) continue;   // the guide: a card whose Entity has not arrived is skipped
			FVector Anchor;
			if (AActor* A = E->Actor.Get())
			{
				FVector O, X; A->GetActorBounds(true, O, X);
				Anchor = FVector(A->GetActorLocation().X, A->GetActorLocation().Y, O.Z + X.Z);
			}
			else if (E->bHasState) Anchor = HrotDpsMotion::ToUnreal(E->Shown, OriginCm).GetLocation() + FVector(0, 0, 200);   // a commander: + 2 m
			else continue;
			const bool bAir = E->Domain == Air;
			FWireCard& W = It.Value;
			Cards->Show(It.Key, HashCombineFast(W.Key, bAir ? 1u : 0u), Anchor, [&](HrotCards::FCard& C)
			{
				for (const HrotCards::FGizmoCardPrim& P : W.Prims) HrotCards::AddGizmo(C, P, bAir);
			});
		}
		Cards->End();
		return;
	}
	for (TPair<int32, FEnt>& It : Ents)
	{
		FEnt& E = It.Value;
		AActor* A = E.Actor.Get();
		if (!E.bHasEntity || E.bRound || E.bAnimal || E.bRiding) continue;   // HROT Seeable.OnMap: no card for a round, an animal, a seated man
		const bool bAir = E.Domain == Air;
		if (LabelMode == 2 && !bAir && !E.bIsUnit) continue;   // mode 2 = the units (Entity.IsUnit, gateway DD-025 note) + every aircraft
		// the anchor: the drawn body's top (HROT EntityBodyLayer3D.cs:2165-2178); a unit has no body: its pose + 2 m
		FVector Anchor;
		if (A)
		{
			FVector Origin, Extent;
			A->GetActorBounds(true, Origin, Extent);
			Anchor = FVector(A->GetActorLocation().X, A->GetActorLocation().Y, Origin.Z + Extent.Z);
		}
		else if (E.bIsUnit && E.bHasState) Anchor = HrotDpsMotion::ToUnreal(E.Shown, OriginCm).GetLocation() + FVector(0, 0, 200);
		else continue;
		const uint8 Damage = E.Damage;
		const uint32 Key = HashCombineFast(HashCombineFast(GetTypeHash(E.Name), uint32(LabelMode) | (uint32(E.Side) << 4) | (uint32(Damage) << 12) | (bAir ? 1u << 20 : 0u) | (E.bIsUnit ? 1u << 21 : 0u)),
			E.StatusKey);   // (every status field is in StatusKey: DD-025)
		const int32 Id = It.Key;
		const int32 Mode = LabelMode;   // the lambda runs only on a key change; it reads E in place (no copy per frame, R-2084)
		Cards->Show(Id, Key, Anchor, [&](HrotCards::FCard& C)
		{
			const FString Text = E.Name.TrimStartAndEnd().IsEmpty() ? FString::Printf(TEXT("#%d"), Id) : FString::Printf(TEXT("%s #%d"), *E.Name.TrimStartAndEnd(), Id);
			HrotCards::BuildStatusCard(C, E.Status, Mode, bAir, E.bIsUnit, Text, E.Side, Damage);
		});
	}
	Cards->End();
}

FString UHrotDpsWorld::DescribeLabels() const
{
	static const TCHAR* Names[] = { TEXT("1 off"), TEXT("2 commanders"), TEXT("3 every unit") };
	return FString::Printf(TEXT("labels %s: %s"), Names[FMath::Clamp(LabelMode, 1, 3) - 1], Cards ? *Cards->Describe() : TEXT("none yet"));
}
