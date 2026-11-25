// CE-4651 (U-18l) — see HrotDpsCameraPawn.h
#include "HrotDpsCameraPawn.h"
#include "HrotDpsPick.h"
#include "HrotDpsWorld.h"
#include "HrotDpsNative.h"   // U-18p: the View enums
#include "HrotDpsStation.h"
#include "HrotDpsHud.h"
#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "HAL/PlatformTime.h"
#include "Debug/DebugDrawService.h"
#include "Camera/CameraComponent.h"
#include "GameFramework/PlayerController.h"
#include "Engine/World.h"
#include "Components/SkeletalMeshComponent.h"
#include "InputCoreTypes.h"

namespace
{
	using namespace HrotCamera;
	/** ENU direction → Unreal (y flipped) */
	FVector UeDir(const FVector3d& D) { return FVector(D.X, -D.Y, D.Z); }
	FVector3d EnuDir(const FVector& D) { return FVector3d(D.X, -D.Y, D.Z); }
	FRotator RotOf(const FVector3d& EnuForward) { return UeDir(EnuForward).Rotation(); }
}

AHrotDpsCamera::AHrotDpsCamera()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.TickGroup = TG_PostUpdateWork;   // after the units moved this frame
	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent = Root;
	Cam = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	Cam->SetupAttachment(Root);
	AutoPossessPlayer = EAutoReceiveInput::Player0;
}

AHrotDpsCamera* AHrotDpsCamera::SpawnIn(UObject* WorldContext, FVector At, FRotator Rotation)
{
	UWorld* W = WorldContext ? WorldContext->GetWorld() : nullptr;
	if (!W) return nullptr;
	FActorSpawnParameters P; P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	return W->SpawnActor<AHrotDpsCamera>(AHrotDpsCamera::StaticClass(), At, Rotation, P);
}

void AHrotDpsCamera::BeginPlay()
{
	Super::BeginPlay();
	if (APlayerController* PC = Cast<APlayerController>(GetController()))
	{
		PC->bShowMouseCursor = true; PC->bEnableClickEvents = true;
		PC->ClientSetHUD(AHrotDpsHud::StaticClass());   // U-18m — the menu, the crosshair, the player's lines
	}
	FreeFov = Cam->FieldOfView;
	// no controller (Simulate, a test, a render node): the same HUD through the debug-draw service, onto the viewport looking through us
	if (!Cast<APlayerController>(GetController()))
		DebugHud = UDebugDrawService::Register(TEXT("Game"), FDebugDrawDelegate::CreateUObject(this, &AHrotDpsCamera::DrawDebugHud));
}

void AHrotDpsCamera::EndPlay(const EEndPlayReason::Type Why)
{
	if (DebugHud.IsValid()) { UDebugDrawService::Unregister(DebugHud); DebugHud.Reset(); }
	Super::EndPlay(Why);
}

void AHrotDpsCamera::DrawDebugHud(UCanvas* Canvas, APlayerController* PC)
{
	if (bDrawHud) HrotDpsHud::Draw(Canvas, this, PC);
}

void AHrotDpsCamera::HideBody(AActor* A, bool bHide)
{
	if (!A) return;
	if (bHide)
	{
		// only what is visible now — and only that comes back (a kit hides parts of its own, e.g. a body's built-in gun)
		TArray<UPrimitiveComponent*> Parts; A->GetComponents(Parts);
		HiddenParts.Reset();
		for (UPrimitiveComponent* C : Parts) if (C->IsVisible()) { C->SetVisibility(false, false); HiddenParts.Add(C); }
		HiddenBody = A;
	}
	else
	{
		for (TWeakObjectPtr<UPrimitiveComponent>& C : HiddenParts) if (C.IsValid()) C->SetVisibility(true, false);
		HiddenParts.Reset();
		HiddenBody = nullptr;
	}
}

void AHrotDpsCamera::Follow(int32 EntityId, float Distance)
{
	if (AActor* H = HiddenBody.Get()) HideBody(H, false);
	FollowId = EntityId; FollowDistance = Distance; Mode = EHrotCameraMode::Follow;
	bPlaced = false; Arm = 0; LookYawOffset = LookPitchOffset = 0;
}

void AHrotDpsCamera::Free()
{
	// back to free flight at the SAME spot (HROT: the operator takes the camera where it is)
	if (AActor* H = HiddenBody.Get()) HideBody(H, false);
	if (Mode == EHrotCameraMode::Director) { Cam->SetFieldOfView(FreeFov); bDirectorBound = false; FollowSide = 1; }   // U-18p
	Mode = EHrotCameraMode::Free; FollowId = 0; FlyVelocity = FVector::ZeroVector;
}

void AHrotDpsCamera::PlaceAt(const FVector& At, const FQuat& Rotation, float HFovDeg)
{
	if (Mode != EHrotCameraMode::Free) Free();
	Cam->SetWorldLocationAndRotation(At, Rotation);
	Cam->SetFieldOfView(HFovDeg);
}

void AHrotDpsCamera::Director()
{
	if (AActor* H = HiddenBody.Get()) HideBody(H, false);
	Mode = EHrotCameraMode::Director; FollowId = 0; FlyVelocity = FVector::ZeroVector;
	DirectorCut = 0; bDirectorBound = false; bPlaced = false;
}

void AHrotDpsCamera::PlaceDirector(float Dt)
{
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	if (!W) return;
	const UHrotDpsWorld::FViewNow V = W->ViewNow();
	if (!V.bHas) return;   // no suggestion now: hold where we are
	const bool bCut = V.CutSeq != DirectorCut;
	DirectorCut = V.CutSeq;
	// the vertical field of view (HROT's) -> Unreal's horizontal one
	FVector2D Vp(16, 9); if (GEngine && GEngine->GameViewport) GEngine->GameViewport->GetViewportSize(Vp);
	const double Aspect = Vp.Y > 0 ? Vp.X / Vp.Y : 16.0 / 9.0;
	Cam->SetFieldOfView(float(FMath::RadiansToDegrees(2.0 * FMath::Atan(FMath::Tan(FMath::DegreesToRadians(double(V.FovDeg)) * 0.5) * Aspect))));
	const bool bBindable = V.TargetId != 0 && (V.Mode == SimNetGateway_Protocol_DdsPlayer_ViewModeOrbit || V.Mode == SimNetGateway_Protocol_DdsPlayer_ViewModeEyes ||
		V.Mode == SimNetGateway_Protocol_DdsPlayer_ViewModeShoulder || V.Mode == SimNetGateway_Protocol_DdsPlayer_ViewModeChase);
	FVector PosM; FQuat Rot; int32 Domain = 0;
	if (bBindable && W->PoseNow(V.TargetId, PosM, Rot, Domain))
	{
		// bound: OUR follow rig on the station's binding — the look direction from the station's view, the distance and the shoulder its own
		if (bCut || !bDirectorBound || FollowId != V.TargetId) { Arm = 0; LookYawOffset = LookPitchOffset = 0; }
		bDirectorBound = true;
		FollowId = V.TargetId;
		FollowDistance = V.Mode == SimNetGateway_Protocol_DdsPlayer_ViewModeEyes ? 0.f : V.Distance;
		FollowSide = V.Side < 0 ? -1 : 1;
		const FVector3d Fwd = V.Q.RotateVector(FVector3d(1, 0, 0));
		Yaw = FMath::Atan2(Fwd.Y, Fwd.X);
		Pitch = FMath::Clamp(FMath::Asin(FMath::Clamp(Fwd.Z, -1.0, 1.0)), -1.4, 1.4);
		bPlaced = true;
		PlaceFollow(Dt);
		if (Mode != EHrotCameraMode::Director) Director();   // PlaceFollow lets go when the unit is gone: stay with the station
		return;
	}
	if (bDirectorBound) { if (AActor* H = HiddenBody.Get()) HideBody(H, false); bDirectorBound = false; FollowId = 0; }
	FollowSide = 1;
	// unbound: the station's own pose, dead-reckoned with its rates (W blends within one cut, jumps on a new one)
	Cam->SetWorldLocationAndRotation(W->EnuMToUnreal(FVector(V.P)), HrotDpsMotion::ToUnreal(V.Q));
}

void AHrotDpsCamera::Orbit(float YawDeg, float PitchDeg)
{
	if (FollowDistance < EyesBelow) { LookYawOffset += FMath::DegreesToRadians(YawDeg); LookPitchOffset = FMath::Clamp(LookPitchOffset + FMath::DegreesToRadians(PitchDeg), -1.2, 1.2); }
	else { Yaw += FMath::DegreesToRadians(YawDeg); Pitch = FMath::Clamp(Pitch + FMath::DegreesToRadians(PitchDeg), -1.4, 1.4); }
}

void AHrotDpsCamera::Wheel(float Notches) { FollowDistance = Step(FollowDistance, Notches); }

FTransform AHrotDpsCamera::CameraTransform() const { return Cam->GetComponentTransform(); }

FString AHrotDpsCamera::Describe() const
{
	if (Mode == EHrotCameraMode::Director)
	{
		const UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
		return FString::Printf(TEXT("director %s: %s"), bDirectorBound ? *FString::Printf(TEXT("bound to %d at %.2f m, arm %.2f m"), FollowId, FollowDistance, Arm) : TEXT("the view's pose"),
			W ? *W->DescribeView() : TEXT("-"));
	}
	if (Mode == EHrotCameraMode::Free) return PossessWanted ? FString::Printf(TEXT("free, possessing %d"), PossessWanted) : TEXT("free");
	if (Mode == EHrotCameraMode::Player)
	{
		const UHrotDpsStation* St = UHrotDpsStation::Get(this);
		return FString::Printf(TEXT("player %d (%s) at %.2f m, look %.0f/%.0f deg, body %.0f deg, aim %d, health %.0f/%.0f, slot %d, %d weapons, %d frames sent, %d hits on us"),
			PlayerUnit, St ? *St->SlotState() : TEXT("-"), PlayerDistance, FMath::RadiansToDegrees(PlayerYaw), FMath::RadiansToDegrees(PlayerPitch),
			FMath::RadiansToDegrees(BodyYaw), AimTarget, St ? St->Health() : 0.f, St ? St->MaxHealth() : 0.f, St ? St->ActiveSlot() : -1,
			St ? St->Ammo().Num() : 0, St ? St->FramesSent() : 0, St ? St->HitsOnUs().Num() : 0);
	}
	const TCHAR* Band = FollowDistance < EyesBelow ? TEXT("eyes") : NearBlend(FollowDistance, EScheme::Person) >= 1.f ? TEXT("shoulder") : TEXT("orbit");
	return FString::Printf(TEXT("follow %d at %.2f m (%s), arm %.2f m"), FollowId, FollowDistance, Band, Arm);
}

void AHrotDpsCamera::Tick(float Dt)
{
	Super::Tick(Dt);
	// U-18m — possession follows the SLOT (HROT: the view follows PossessedBy): Playing our unit -> his view; lost -> free flight
	if (UHrotDpsStation* St = UHrotDpsStation::Get(this))
	{
		if (PossessWanted && St->IsPlaying() && St->PlayedUnit() == PossessWanted) EnterPlayer();
		else if (Mode == EHrotCameraMode::Player && St->IsPlaying() && St->PlayedUnit() != 0 && St->PlayedUnit() != PlayerUnit)
		{
			// U-19g: HROT moved our seat to another unit (its drive hand-over: the soldier rides, the vehicle is ours; get out: back to him) -
			// the client only follows what the slot reports (CE-11041 / gateway DD-029)
			UE_LOG(LogTemp, Display, TEXT("HROT camera: the slot moved us from unit %d to %d"), PlayerUnit, St->PlayedUnit());
			PossessWanted = St->PlayedUnit();
			EnterPlayer();
		}
		else if (Mode == EHrotCameraMode::Player && (!St->IsPlaying() || St->PlayedUnit() != PlayerUnit))
		{
			UE_LOG(LogTemp, Display, TEXT("HROT camera: the slot let unit %d go (%s) - free flight"), PlayerUnit, *St->SlotState());
			LeavePlayerView();
		}
	}
	if (Mode == EHrotCameraMode::Player) { TickPlayer(Dt, Cast<APlayerController>(GetController())); return; }
	TickInput(Dt);
	if (Mode == EHrotCameraMode::Follow) PlaceFollow(Dt);
	else if (Mode == EHrotCameraMode::Director) PlaceDirector(Dt);
}

float AHrotDpsCamera::HeightAboveGround(const FVector& At) const
{
	FHitResult Hit;
	FCollisionQueryParams Q(SCENE_QUERY_STAT(HrotCamGround), false, this);
	if (GetWorld()->LineTraceSingleByChannel(Hit, At, At - FVector(0, 0, 1e6), ECC_Visibility, Q)) return float((At.Z - Hit.ImpactPoint.Z) / 100.0);
	return float(At.Z / 100.0);
}

double AHrotDpsCamera::FirstHit(const FVector& From, const FVector& To) const
{
	FCollisionQueryParams Q(SCENE_QUERY_STAT(HrotCamArm), false, this);
	if (UHrotDpsWorld* W = UHrotDpsWorld::Get(this))
		if (AActor* A = W->ActorOf(FollowId))
		{
			Q.AddIgnoredActor(A);
			TArray<AActor*> Attached; A->GetAttachedActors(Attached, true, true);   // riders, a gun on its pintle
			Q.AddIgnoredActors(Attached);
		}
	FHitResult Hit;
	return GetWorld()->LineTraceSingleByChannel(Hit, From, To, ECC_Visibility, Q) ? double(Hit.Time) : -1.0;
}

void AHrotDpsCamera::PlaceFollow(float Dt)
{
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	FVector PosM; FQuat Rot; int32 Domain = 0;
	if (!W || !W->PoseNow(FollowId, PosM, Rot, Domain)) { UE_LOG(LogTemp, Display, TEXT("HROT camera: entity %d has no pose - free flight"), FollowId); Free(); return; }   // the unit is gone: back to free flight where we are
	const FVector3d P(PosM);
	const EScheme S = Domain == W->DomainAir() ? EScheme::Flyer : (Domain == W->DomainPerson() || Domain == W->DomainAnimal()) ? EScheme::Person : EScheme::Vehicle;
	// the eye: a person's drawn head (the guide: the character's head socket), else HROT's fallback above the position
	FVector3d Eye = P + FVector3d(0, 0, S == EScheme::Person ? 1.6 : 2.2);
	if (AActor* A = W->ActorOf(FollowId); A && S == EScheme::Person)
	{
		TArray<USkeletalMeshComponent*> Meshes; A->GetComponents(Meshes);
		for (USkeletalMeshComponent* M : Meshes)
			if (M->IsVisible() && M->DoesSocketExist(TEXT("head"))) { Eye = FVector3d(W->UnrealToEnuM(M->GetSocketLocation(TEXT("head")))); break; }
	}
	const FVector3d Centre(Eye.X, Eye.Y, (P.Z + Eye.Z) * 0.5);
	const FVector3d BodyFwd = FVector3d(FQuat4d(Rot).RotateVector(FVector3d(1, 0, 0)));
	if (!bPlaced) { Yaw = FMath::Atan2(BodyFwd.Y, BodyFwd.X); Pitch = -0.35; bPlaced = true; }
	AActor* Body = W->ActorOf(FollowId);
	if ((FollowDistance < EyesBelow) != HiddenBody.IsValid()) HideBody(Body, FollowDistance < EyesBelow);
	{   // U-19g: crossing the eyes stage keeps the view direction (a jump to "some fixed direction" was reported live)
		const bool bEyes = FollowDistance < EyesBelow;
		if (Mode == EHrotCameraMode::Follow && bEyes != bWasEyes)
		{
			const double BYaw = FMath::Atan2(BodyFwd.Y, BodyFwd.X), BPitch = FMath::Asin(FMath::Clamp(BodyFwd.Z, -1.0, 1.0));
			if (bEyes) HrotCamera::EyesEntry(Yaw, Pitch, BYaw, BPitch, LookYawOffset, LookPitchOffset);
			else HrotCamera::EyesLook(BYaw, BPitch, LookYawOffset, LookPitchOffset, Yaw, Pitch);   // out: the orbit continues from the gaze
		}
		bWasEyes = bEyes;
	}
	if (FollowDistance < EyesBelow)
	{
		// at his eyes: where HE looks (his heading — ⏳ the gaze from PersonAim's look target), the held offset springing back
		const double K = FMath::Exp(-LookSpringRate * Dt);
		LookYawOffset *= K; LookPitchOffset *= K;
		const double GYaw = FMath::Atan2(BodyFwd.Y, BodyFwd.X) + LookYawOffset;
		const double GPitch = FMath::Clamp(FMath::Asin(FMath::Clamp(BodyFwd.Z, -1.0, 1.0)) + LookPitchOffset, -1.4, 1.4);
		Cam->SetWorldLocationAndRotation(W->EnuMToUnreal(FVector(Eye)), RotOf(Forward(GYaw, GPitch)));
		Arm = 0;
		return;
	}
	const FVector3d Fwd = Forward(Yaw, Pitch);
	const FVector3d Piv = Pivot(Centre, Eye, Fwd, FollowDistance, S, FollowSide);
	const FVector3d Wanted = Camera(Piv, Fwd, FollowDistance, true);
	const double T = FirstHit(W->EnuMToUnreal(FVector(Piv)), W->EnuMToUnreal(FVector(Wanted)));
	const FVector3d FreeAt = SpringArm(Piv, Wanted, T);
	const float Want = float((FreeAt - Piv).Size());
	Arm = EaseArm(Arm == 0.f ? Want : Arm, Want, Dt, ScaleOf(S));
	FVector3d At = Piv + (Wanted - Piv).GetSafeNormal() * Arm;
	At = AboveHeadWhenSqueezed(At, Eye, Arm, float((Wanted - Piv).Size()));
	FVector Ue = W->EnuMToUnreal(FVector(At));
	const float H = HeightAboveGround(Ue + FVector(0, 0, 200));
	if (H - 2.f < AboveGround) Ue.Z += (AboveGround - (H - 2.f)) * 100.0;   // never under the ground
	Cam->SetWorldLocationAndRotation(Ue, RotOf(Fwd));
}

void AHrotDpsCamera::FreeLook(float DYaw, float DPitch)
{
	FRotator R = Cam->GetComponentRotation();
	R.Yaw += DYaw; R.Pitch = FMath::Clamp(R.Pitch + DPitch, FMath::RadiansToDegrees(MinPitch), FMath::RadiansToDegrees(MaxPitch)); R.Roll = 0;
	Cam->SetWorldRotation(R);
}

int32 AHrotDpsCamera::PickUnderCursor() const
{
	APlayerController* PC = Cast<APlayerController>(GetController());
	if (!PC) return 0;
	float Mx = 0, My = 0;
	if (!PC->GetMousePosition(Mx, My)) return 0;
	return PickAt(FVector2D(Mx, My));
}

int32 AHrotDpsCamera::PickAt(const FVector2D& ScreenPx) const
{
	APlayerController* PC = Cast<APlayerController>(GetController());
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	if (!PC || !W) return 0;
	// (1) what the trace hits: vehicles, buildings
	int32 HitId = 0; double HitDepth = 0;
	FVector From, Dir;
	if (PC->DeprojectScreenPositionToWorld(ScreenPx.X, ScreenPx.Y, From, Dir))
	{
		FCollisionQueryParams Q(SCENE_QUERY_STAT(HrotCamPick), true, this);
		TArray<FHitResult> Hits;
		GetWorld()->LineTraceMultiByChannel(Hits, From, From + Dir * 5e5, ECC_Visibility, Q);
		for (const FHitResult& H : Hits)
		{
			for (AActor* X = H.GetActor(); X && !HitId; X = X->GetAttachParentActor())   // a rider, a part: its own entity first, else what it rides
				HitId = W->EntityOfActor(X);
			if (HitId) { HitDepth = H.Distance; break; }
		}
	}
	// (2) every drawn entity on the screen (U-19g: a skeletal soldier does not block the trace)
	const FVector Eye = Cam->GetComponentLocation();
	TArray<HrotDpsPick::FCand> Cands;
	for (const int32 Id : W->EntityIds())
	{
		FBox2D R; FVector2D A, B; bool bMan; double Depth;
		if (!ProjectBody(Id, R, A, B, bMan, Depth)) continue;
		Cands.Add({ Id, A, Depth, B, bMan });
	}
	const int32 Picked = HrotDpsPick::Choose(Cands, ScreenPx, HitId, HitDepth);
	// a user action, not per frame: the nearest candidate and the viewport, so a miss can be told from a scale mismatch (DPI) after a live run
	const HrotDpsPick::FCand* Nearest = nullptr; float NearD = 1e9f;
	for (const HrotDpsPick::FCand& C : Cands) { const float D = C.bSeg ? HrotDpsPick::DistToSegment(ScreenPx, C.Screen, C.B) : float(FVector2D::Distance(C.Screen, ScreenPx)); if (D < NearD) { NearD = D; Nearest = &C; } }
	int32 VX = 0, VY = 0; PC->GetViewportSize(VX, VY);
	int32 Men = 0; for (const HrotDpsPick::FCand& C : Cands) Men += C.bSeg ? 1 : 0;
	UE_LOG(LogTemp, Display, TEXT("HROT pick at (%.0f, %.0f) viewport %dx%d: %d candidates (%d men), nearest %d at (%.0f, %.0f) %.0f px, trace hit %d, picked %d"),
		ScreenPx.X, ScreenPx.Y, VX, VY, Cands.Num(), Men, Nearest ? Nearest->Id : 0, Nearest ? Nearest->Screen.X : -1.0, Nearest ? Nearest->Screen.Y : -1.0, Nearest ? NearD : -1.f, HitId, Picked);
	return Picked;
}

FVector2D AHrotDpsCamera::ScreenOf(int32 EntityId) const
{
	APlayerController* PC = Cast<APlayerController>(GetController());
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	FBox2D R; FVector2D A, B; bool bMan; double Depth;
	if (!PC || !W || !ProjectBody(EntityId, R, A, B, bMan, Depth)) return FVector2D(-1, -1);
	return (A + B) * 0.5;   // a man: mid-body; else the box centre
}

bool AHrotDpsCamera::ProjectBody(int32 EntityId, FBox2D& Rect, FVector2D& A, FVector2D& B, bool& bMan, double& Depth) const
{
	APlayerController* PC = Cast<APlayerController>(GetController());
	const UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	FBox Box;
	if (!PC || !W || !W->BodyBox(EntityId, Box, bMan)) return false;
	Rect = FBox2D(ForceInit);
	for (int32 I = 0; I < 8; ++I)
	{
		const FVector P((I & 1) ? Box.Max.X : Box.Min.X, (I & 2) ? Box.Max.Y : Box.Min.Y, (I & 4) ? Box.Max.Z : Box.Min.Z);
		FVector2D S;
		if (PC->ProjectWorldLocationToScreen(P, S, false)) Rect += S;
	}
	if (!Rect.bIsValid) return false;
	const float Margin = bMan ? 6.f : 4.f;
	Rect = FBox2D(Rect.Min - FVector2D(Margin, Margin), Rect.Max + FVector2D(Margin, Margin));
	const FVector C = Box.GetCenter();
	if (bMan)
	{
		const FVector Foot(C.X, C.Y, Box.Min.Z), Head(C.X, C.Y, Box.Max.Z);
		if (!PC->ProjectWorldLocationToScreen(Foot, A, false) || !PC->ProjectWorldLocationToScreen(Head, B, false)) return false;
	}
	else if (!PC->ProjectWorldLocationToScreen(C, A, false)) return false;
	else B = A;
	Depth = FVector::Dist(C, Cam->GetComponentLocation());
	return true;
}

FRotator AHrotDpsCamera::ViewRotation() const { return Cam->GetComponentRotation(); }

FBox2D AHrotDpsCamera::ScreenRectOf(int32 EntityId) const
{
	FBox2D R; FVector2D A, B; bool bMan; double Depth;
	return ProjectBody(EntityId, R, A, B, bMan, Depth) ? R : FBox2D(ForceInit);
}

int32 AHrotDpsCamera::TestClick(FVector2D ScreenPx, bool bRight)
{
	const int32 Id = PickAt(ScreenPx);
	if (bRight) { if (Id) { Selected = Id; OpenMenu(Id, ScreenPx); } }
	else Selected = Id;
	return Id;
}

void AHrotDpsCamera::TickInput(float Dt)
{
	APlayerController* PC = Cast<APlayerController>(GetController());
	if (!PC) return;   // driven by calls (Simulate, a test, a render node)
	float Mx = 0, My = 0; PC->GetInputMouseDelta(Mx, My);
	const bool bRmb = PC->IsInputKeyDown(EKeys::RightMouseButton);
	const float WheelIn = PC->WasInputKeyJustPressed(EKeys::MouseScrollUp) ? 1.f : PC->WasInputKeyJustPressed(EKeys::MouseScrollDown) ? -1.f : 0.f;
	const bool bShift = PC->IsInputKeyDown(EKeys::LeftShift) || PC->IsInputKeyDown(EKeys::RightShift);
	const bool bCtrl = PC->IsInputKeyDown(EKeys::LeftControl) || PC->IsInputKeyDown(EKeys::RightControl);
	const bool bW = PC->IsInputKeyDown(EKeys::W), bS = PC->IsInputKeyDown(EKeys::S), bA = PC->IsInputKeyDown(EKeys::A), bD = PC->IsInputKeyDown(EKeys::D);

	// U-18m — the unit menu: a right CLICK (short, no drag) opens it; a click on a row runs it, elsewhere closes it
	float MouseX = 0, MouseY = 0; PC->GetMousePosition(MouseX, MouseY);
	if (PC->WasInputKeyJustPressed(EKeys::RightMouseButton)) { RmbDownAt = FPlatformTime::Seconds(); RmbTravel = 0; RmbPressAt = FVector2D(MouseX, MouseY); }
	if (bRmb) RmbTravel += (FMath::Abs(Mx) + FMath::Abs(My)) * MouseScale;
	// U-19g: RMB held for LOOK (past a click's time or travel) captures the mouse - hidden, relative, never stopped by the screen edge - and the cursor
	// comes back where the press began; a short click is untouched (it opens the menu at the click point)
	if (bRmb && !bLookCaptured && RmbDownAt >= 0 && !MenuEntity && (RmbTravel >= MenuClickTravel || FPlatformTime::Seconds() - RmbDownAt >= MenuClickSeconds))
	{
		bLookCaptured = true; PC->SetShowMouseCursor(false); PC->SetInputMode(FInputModeGameOnly());
	}
	if (bLookCaptured && !bRmb)
	{
		bLookCaptured = false;
		FInputModeGameAndUI In; In.SetHideCursorDuringCapture(false); PC->SetInputMode(In);
		PC->SetShowMouseCursor(true);
		PC->SetMouseLocation(FMath::RoundToInt(RmbPressAt.X), FMath::RoundToInt(RmbPressAt.Y));
	}
	if (PC->WasInputKeyJustReleased(EKeys::RightMouseButton) && RmbDownAt >= 0)
	{
		if (FPlatformTime::Seconds() - RmbDownAt < MenuClickSeconds && RmbTravel < MenuClickTravel)
			if (const int32 Id = PickUnderCursor()) { Selected = Id; OpenMenu(Id, FVector2D(MouseX, MouseY)); }
		RmbDownAt = -1;
	}
	if (MenuEntity)
	{
		if (PC->WasInputKeyJustPressed(EKeys::Escape)) { CloseMenu(); return; }
		if (PC->WasInputKeyJustPressed(EKeys::F)) { const int32 Id = MenuEntity; CloseMenu(); Follow(Id); return; }
		if (PC->WasInputKeyJustPressed(EKeys::P)) { const int32 Id = MenuEntity; CloseMenu(); Possess(Id); return; }
		if (PC->WasInputKeyJustPressed(EKeys::LeftMouseButton))
		{
			const int32 Row = FMath::FloorToInt32((MouseY - MenuAt.Y - MenuHeader) / MenuRowHeight);
			const bool bIn = MouseX >= MenuAt.X && MouseX <= MenuAt.X + MenuWidth && MouseY >= MenuAt.Y + MenuHeader && Row < MenuRows().Num();
			if (bIn) MenuChoose(Row); else CloseMenu();
			return;
		}
	}
	else if (PC->WasInputKeyJustPressed(EKeys::LeftMouseButton) && !bRmb) Selected = PickUnderCursor();
	if (!MenuEntity && PC->WasInputKeyJustPressed(EKeys::F) && Selected) Follow(Selected);
	if (!MenuEntity && PC->WasInputKeyJustPressed(EKeys::P) && Selected) TryPossess(Selected);   // live 2026-11-25: P skipped the menu's check and asked HROT for an un-offered man (NotPlayable)
	if (!MenuEntity && Mode != EHrotCameraMode::Player)   // the playable units HROT offers this station, by number (the HUD lists them)
		if (UHrotDpsStation* OSt = UHrotDpsStation::Get(this))
		{
			static const FKey OfferKeys[9] = { EKeys::One, EKeys::Two, EKeys::Three, EKeys::Four, EKeys::Five, EKeys::Six, EKeys::Seven, EKeys::Eight, EKeys::Nine };
			const TArray<int32> Ids = OSt->OfferIds();
			for (int32 K = 0; K < 9 && K < Ids.Num(); ++K)
				if (PC->WasInputKeyJustPressed(OfferKeys[K])) { Selected = Ids[K]; TryPossess(Ids[K]); break; }
		}
	if (PossessWanted && PC->WasInputKeyJustPressed(EKeys::Escape)) { LetGo(); return; }   // a request not taken yet: withdrawn

	if (!MenuEntity && PC->WasInputKeyJustPressed(EKeys::V)) { if (Mode == EHrotCameraMode::Director) Free(); else Director(); return; }   // U-18p
	if (!MenuEntity && Mode != EHrotCameraMode::Player && PC->WasInputKeyJustPressed(EKeys::L))   // U-18y: labels off / commanders / every unit (L is lock while playing)
		if (UHrotDpsWorld* LW = UHrotDpsWorld::Get(this)) { LW->CycleLabels(); UE_LOG(LogTemp, Display, TEXT("HROT IG: %s"), *LW->DescribeLabels()); }
	if (!MenuEntity && Mode != EHrotCameraMode::Player && PC->WasInputKeyJustPressed(EKeys::G))   // U-18z: HROT's gizmos on / off (the reference client's G set)
		if (UHrotDpsWorld* GW = UHrotDpsWorld::Get(this))
		{
			GW->SetGizmoLayers(GW->GizmoLayers.Num() ? TArray<FString>() : TArray<FString>{ TEXT("Paths"), TEXT("Contacts"), TEXT("FireTraces"), TEXT("Routes") });
			UE_LOG(LogTemp, Display, TEXT("HROT IG: %s"), *GW->DescribeGizmos());
		}
	if (Mode == EHrotCameraMode::Director)
	{
		if (PC->WasInputKeyJustPressed(EKeys::Escape) || bW || bS || bA || bD) Free();   // free flight where it is
		return;
	}
	if (Mode == EHrotCameraMode::Follow)
	{
		if (PC->WasInputKeyJustPressed(EKeys::Escape) || bW || bS || bA || bD) { Free(); return; }   // the operator takes the camera back
		if (WheelIn != 0.f) Wheel(WheelIn);
		if (bRmb) Orbit(-Mx * MouseScale * FMath::RadiansToDegrees(OrbitPerPixel), My * MouseScale * FMath::RadiansToDegrees(OrbitPerPixel));
		return;
	}
	// ── free flight (MapCamera3D) ──
	const FVector Eye = Cam->GetComponentLocation();
	const float H = HeightAboveGround(Eye);
	if (WheelIn != 0.f)
	{
		WheelStreak = (WheelIdle <= WheelStreakSeconds && FMath::Sign(WheelIn) == WheelSign) ? WheelStreak + 1 : 0;
		WheelIdle = 0; WheelSign = FMath::Sign(WheelIn);
		WheelTravel += WheelStep(H, WheelStreak) * WheelIn * 100.f;
	}
	WheelIdle += Dt;
	if (WheelTravel != 0.f)   // glided out
	{
		const float K = 1.f - FMath::Exp(-Dt / WheelGlideSeconds);
		const float Dd = FMath::Abs(WheelTravel) < 0.2f ? WheelTravel : WheelTravel * K;
		WheelTravel -= Dd;
		Cam->SetWorldLocation(Cam->GetComponentLocation() + Cam->GetForwardVector() * Dd);
	}
	if (bRmb)
	{
		FreeLook(Mx * MouseScale * FMath::RadiansToDegrees(LookSpeed), My * MouseScale * FMath::RadiansToDegrees(LookSpeed));
		FVector Move = FVector::ZeroVector;
		if (bW) Move += Cam->GetForwardVector();
		if (bS) Move -= Cam->GetForwardVector();
		if (bD) Move += Cam->GetRightVector();
		if (bA) Move -= Cam->GetRightVector();
		if (PC->IsInputKeyDown(EKeys::E)) Move += FVector::UpVector;
		if (PC->IsInputKeyDown(EKeys::Q)) Move -= FVector::UpVector;
		FlyHeld = Move.IsZero() ? 0.f : FlyHeld + Dt;
		const float Speed = FlySpeed(H, FlyHeld, bShift, bCtrl) * 100.f;
		const FVector Want = Move.IsZero() ? FVector::ZeroVector : Move.GetSafeNormal() * Speed;
		FlyVelocity += (Want - FlyVelocity) * (1.f - FMath::Exp(-Dt / FlyEaseSeconds));
		if (Want.IsZero() && FlyVelocity.SizeSquared() < 1e-2) FlyVelocity = FVector::ZeroVector;
		Cam->SetWorldLocation(Cam->GetComponentLocation() + FlyVelocity * Dt);
	}
	else { FlyVelocity = FVector::ZeroVector; FlyHeld = 0; }
}

// ───────────────────────── U-18m: the unit menu ─────────────────────────

TArray<FHrotMenuRow> AHrotDpsCamera::MenuRows() const
{
	TArray<FHrotMenuRow> Rows;
	if (!MenuEntity) return Rows;
	// HROT's map menu (MapMenuActions): "Follow with the camera" (View) and "Possess (FPS)" (Orders, P; greyed with the reason)
	FHrotMenuRow F; F.Label = TEXT("Follow with the camera"); F.Shortcut = TEXT("F"); F.Action = 1;
	Rows.Add(F);
	FHrotMenuRow P; P.Label = TEXT("Possess (FPS)"); P.Shortcut = TEXT("P"); P.Action = 2;
	if (UHrotDpsStation* St = UHrotDpsStation::Get(this)) P.bEnabled = St->CanPossess(MenuEntity, P.Reason);
	else { P.bEnabled = false; P.Reason = TEXT("no player station"); }
	Rows.Add(P);
	return Rows;
}

void AHrotDpsCamera::OpenMenu(int32 EntityId, FVector2D ScreenAt)
{
	if (UHrotDpsStation* St = UHrotDpsStation::Get(this)) St->Connect();
	MenuEntity = EntityId; MenuAt = ScreenAt;
}

void AHrotDpsCamera::MenuChoose(int32 Row)
{
	const TArray<FHrotMenuRow> Rows = MenuRows();
	const int32 Id = MenuEntity;
	if (!Rows.IsValidIndex(Row) || !Rows[Row].bEnabled) return;   // a greyed row does nothing (its line says why)
	CloseMenu();
	if (Rows[Row].Action == 1) Follow(Id);
	else if (Rows[Row].Action == 2) Possess(Id);
}

// ───────────────────────── U-18m: possession ─────────────────────────

void AHrotDpsCamera::TryPossess(int32 EntityId)
{
	FString Why;
	UHrotDpsStation* St = UHrotDpsStation::Get(this);
	if (St) St->Connect();
	if (St && St->CanPossess(EntityId, Why)) { Possess(EntityId); return; }
	Notice = FString::Printf(TEXT("Cannot possess unit %d: %s"), EntityId, St ? *Why : TEXT("no player station"));
	NoticeAt = FPlatformTime::Seconds();
	UE_LOG(LogTemp, Display, TEXT("HROT IG: %s"), *Notice);
}

void AHrotDpsCamera::Possess(int32 EntityId)
{
	UHrotDpsStation* St = UHrotDpsStation::Get(this);
	if (!St || EntityId == 0) return;
	St->Join(EntityId);
	PossessWanted = EntityId;
	if (Mode != EHrotCameraMode::Player) Follow(EntityId, HrotCamera::PersonDefault * 3.f);   // watch him while the slot takes him
}

void AHrotDpsCamera::LetGo()
{
	if (UHrotDpsStation* St = UHrotDpsStation::Get(this)) St->Leave();
	LeavePlayerView();
}

void AHrotDpsCamera::LeavePlayerView()
{
	PossessWanted = 0;
	if (AActor* H = HiddenBody.Get()) HideBody(H, false);
	if (Mode == EHrotCameraMode::Player)
	{
		Cam->SetFieldOfView(FreeFov);
		if (APlayerController* PC = Cast<APlayerController>(GetController()))
		{
			PC->SetShowMouseCursor(true);
			FInputModeGameAndUI In; In.SetHideCursorDuringCapture(false); PC->SetInputMode(In);
		}
	}
	PlayerUnit = 0;
	Free();   // the camera flies on from where it is
}

void AHrotDpsCamera::EnterPlayer()
{
	PlayerUnit = PossessWanted; PossessWanted = 0;
	if (AActor* H = HiddenBody.Get()) HideBody(H, false);
	Mode = EHrotCameraMode::Player; FollowId = PlayerUnit;
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	FVector PosM; FQuat Rot; int32 Domain = 0;
	bAnimal = false;
	if (W && W->PoseNow(PlayerUnit, PosM, Rot, Domain))
	{
		const FVector3d Fwd = FQuat4d(Rot).RotateVector(FVector3d(1, 0, 0));
		PlayerYaw = BodyYaw = FMath::Atan2(Fwd.Y, Fwd.X);
		PScheme = Domain == W->DomainAir() ? EScheme::Flyer : (Domain == W->DomainPerson() || Domain == W->DomainAnimal()) ? EScheme::Person : EScheme::Vehicle;
		bAnimal = Domain == W->DomainAnimal();
	}
	PlayerPitch = 0; PlayerDistance = HrotCamera::DefaultDistance(PScheme); PlayerArm = 0; LookIdle = 0;
	Stance = 0; Mount = 0; ShoulderSide = 1; HeldHeading.Reset();
	if (APlayerController* PC = Cast<APlayerController>(GetController()))
	{
		PC->SetShowMouseCursor(false);
		PC->SetInputMode(FInputModeGameOnly());   // the mouse is the look now
	}
	UE_LOG(LogTemp, Display, TEXT("HROT camera: playing unit %d"), PlayerUnit);
}

// ───────────────────────── U-18m: the player (HROT's FpsCameraDriver.HandleInput) ─────────────────────────

void AHrotDpsCamera::TickPlayer(float Dt, APlayerController* PC)
{
	using namespace HrotPlayer;
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	UHrotDpsStation* St = UHrotDpsStation::Get(this);
	if (!W || !St) return;
	auto Down = [PC](const FKey& K) { return PC && PC->IsInputKeyDown(K); };
	auto Pressed = [PC](const FKey& K) { return PC && PC->WasInputKeyJustPressed(K); };
	// U-19h: the hold-F list; while it is open Esc / right mouse cancel it (not let go) and the wheel / 1-8 belong to it
	TArray<HrotDpsUseList::FRow> UseRows;
	for (const FHrotDpsUseAction& A : St->Actions()) UseRows.Add({ A.Verb, A.bAvailable });
	const bool bListWasOpen = UseList.bOpen;
	if (!bListWasOpen && Pressed(EKeys::Escape)) { LetGo(); return; }   // Esc: let him go, fly free

	// ── the look: the mouse turns the view (pixels × the zoomed sensitivity, FpsZoom.SensitivityFor) ──
	bZooming = Down(EKeys::RightMouseButton) || TestZoom;
	const float Sens = SensitivityFor(VerticalFov());
	float Mx = 0, My = 0; if (PC) PC->GetInputMouseDelta(Mx, My);
	const double DYaw = -Mx * MouseScale * Sens + FMath::DegreesToRadians(TestLookYaw);
	const double DPitch = My * MouseScale * Sens + FMath::DegreesToRadians(TestLookPitch);
	TestLookYaw = TestLookPitch = 0;
	PlayerYaw = Wrap(PlayerYaw + DYaw);
	PlayerPitch = FMath::Clamp(PlayerPitch + DPitch, -1.4, 1.4);
	LookIdle = (DYaw != 0 || DPitch != 0) ? 0.f : LookIdle + Dt;

	FVector PosM; FQuat Rot; int32 Domain = 0;
	if (!W->PoseNow(PlayerUnit, PosM, Rot, Domain)) return;   // not drawn yet; the slot decides when he is gone
	const FVector3d P(PosM);
	const FVector3d HullFwd = FQuat4d(Rot).RotateVector(FVector3d(1, 0, 0));
	const double HullYaw = FMath::Atan2(HullFwd.Y, HullFwd.X);
	const bool bVehicle = PScheme == EScheme::Vehicle, bFlyer = PScheme == EScheme::Flyer, bPerson = PScheme == EScheme::Person && !bAnimal;

	// a vehicle's look left alone eases back behind the hull (RecentreAfter, RecentreRate)
	if (bVehicle && LookIdle > RecentreAfter) PlayerYaw = TurnToward(PlayerYaw, HullYaw, FMath::DegreesToRadians(RecentreRateDeg) * Dt);

	// ── the keys (HROT's EmbodiedKeys): W/S forward, A/D side (A = +1, left) ──
	float Forward = FMath::Clamp((Down(EKeys::W) ? 1.f : 0.f) - (Down(EKeys::S) ? 1.f : 0.f) + TestForward, -1.f, 1.f);
	float SideIn = FMath::Clamp((Down(EKeys::A) ? 1.f : 0.f) - (Down(EKeys::D) ? 1.f : 0.f) + TestSide, -1.f, 1.f);
	if (bPerson && PlayerDistance > 0.f)
	{
		// over the shoulder: the body lags the look; W/A/S/D relative to the CAMERA, turned into the body's frame
		BodyYaw = ShoulderBodyYaw(BodyYaw, PlayerYaw, Forward != 0.f || SideIn != 0.f, Dt);
		CameraRelative(Forward, SideIn, Wrap(PlayerYaw - BodyYaw));
	}
	else BodyYaw = PlayerYaw;
	const float Climb = (Down(EKeys::Q) || Down(EKeys::SpaceBar) ? 1.f : 0.f) - (Down(EKeys::Z) || Down(EKeys::C) ? 1.f : 0.f);
	double FlyerYaw;
	if (SideIn != 0.f) { FlyerYaw = FlyerHeading(HullYaw, SideIn); HeldHeading.Reset(); }
	else { if (!HeldHeading.IsSet()) HeldHeading = HullYaw; FlyerYaw = HeldHeading.GetValue(); }   // held, never re-read (CE-1354)
	bRunning = Down(EKeys::LeftShift) || Down(EKeys::RightShift) || TestRun;

	// the wheel: the distance continuum (ahead / eyes / shoulder); O: the eyes <-> over the shoulder
	const float WheelIn = Pressed(EKeys::MouseScrollUp) ? 1.f : Pressed(EKeys::MouseScrollDown) ? -1.f : 0.f;
	if (WheelIn != 0.f && !bListWasOpen) PlayerDistance = WheelDistance(PlayerDistance, WheelIn, PScheme);
	if (Pressed(EKeys::O)) PlayerDistance = PlayerDistance > 0.f ? 0.f : HrotCamera::DefaultDistance(PScheme);
	// 1-4 a weapon from the unit's slots (OwnUnit.Ammo), R its next round, L sight / lock, X drops a locked shot
	const TArray<FHrotDpsAmmoLine> Slots = St->Ammo();
	const FKey Num[4] = { EKeys::One, EKeys::Two, EKeys::Three, EKeys::Four };
	for (int32 K = 0; K < 4; ++K) if (!bListWasOpen && Pressed(Num[K]) && K < Slots.Num()) Mount = uint8(Slots[K].Slot);
	if (Pressed(EKeys::R)) ++AmmoSeq;
	if (Pressed(EKeys::L)) FirePref = St->FireMode() == TEXT("LOCK") ? 1 : 2;
	if (Pressed(EKeys::X)) ++CancelSeq;
	// lean (Q / E held; a person) — it also puts the camera over that shoulder (and it stays there)
	const bool bL = Down(EKeys::Q), bR = Down(EKeys::E);
	const uint8 Lean = !bPerson || bL == bR ? 0 : bL ? 1 : 2;
	if (Lean == 1) ShoulderSide = -1; else if (Lean == 2) ShoulderSide = 1;
	const bool bBinoculars = bPerson && Down(EKeys::B);
	// stance: C toggles crouched, Z prone, Ctrl held crouches (a person only; on a flyer C / Z descend)
	if (bPerson)
	{
		const uint8 Now = Stance == 0 ? 1 : Stance;
		if (Pressed(EKeys::C)) Stance = Now == 2 ? 1 : 2;
		if (Pressed(EKeys::Z)) Stance = Now == 3 ? 1 : 3;
		bCrouchHeld = Down(EKeys::LeftControl) || Down(EKeys::RightControl);
	}

	PlacePlayer(Dt, P);

	// the aim: the ray through the crosshair (the screen centre) against terrain and units, else AimReach along the view
	const FVector From = Cam->GetComponentLocation(), Dir = Cam->GetForwardVector();
	FCollisionQueryParams Q(SCENE_QUERY_STAT(HrotAim), true, this);
	if (AActor* Me = W->ActorOf(PlayerUnit)) { Q.AddIgnoredActor(Me); TArray<AActor*> Att; Me->GetAttachedActors(Att, true, true); Q.AddIgnoredActors(Att); }
	FHitResult Hit;
	AimTarget = 0;
	FVector AimUe = From + Dir * AimReach * 100.0;
	if (GetWorld()->LineTraceSingleByChannel(Hit, From, AimUe, ECC_Visibility, Q))
	{
		AimUe = Hit.ImpactPoint;
		for (AActor* X = Hit.GetActor(); X && !AimTarget; X = X->GetAttachParentActor()) AimTarget = W->EntityOfActor(X);
	}
	AimPoint = FVector3d(W->UnrealToEnuM(AimUe));
	const bool bFirePress = Pressed(EKeys::LeftMouseButton) || TestFirePress;
	TestFirePress = false;
	if (bFirePress && !bBinoculars) { ++ShotSeq; ShotTarget = AimTarget; }
	{
		static const FKey Keys8[HrotDpsUseList::MaxRows] = { EKeys::One, EKeys::Two, EKeys::Three, EKeys::Four, EKeys::Five, EKeys::Six, EKeys::Seven, EKeys::Eight };
		int32 Pick = -1; for (int32 K = 0; K < HrotDpsUseList::MaxRows; ++K) if (Pressed(Keys8[K])) Pick = K;
		const int32 Wheel = Pressed(EKeys::MouseScrollDown) ? 1 : Pressed(EKeys::MouseScrollUp) ? -1 : 0;
		const HrotDpsUseList::FOut Use = HrotDpsUseList::Step(UseList, Down(EKeys::F), FPlatformTime::Seconds(), UseRows, Wheel, Pick, Pressed(EKeys::Escape) || Pressed(EKeys::RightMouseButton));
		if (Use.bPress) { ++UseSeq; UseVerb = Use.Verb; }
		else if (TestUsePress) { ++UseSeq; UseVerb = TestUseVerb; }
		if (Use.bPress || TestUsePress) UE_LOG(LogTemp, Display, TEXT("HROT player: use press %d verb %d on %d"), UseSeq, int32(UseVerb), AimTarget);
		TestUsePress = false; TestUseVerb = 0;
	}

	FHrotDpsControl C;
	C.Forward = Forward;
	C.Lateral = bVehicle || bAnimal || bFlyer ? 0.f : SideIn;
	C.Steer = bVehicle || bAnimal ? SideIn : 0.f;
	C.Yaw = float(bFlyer ? FlyerYaw : BodyYaw);
	C.Vertical = bFlyer ? Climb : 0.f;
	C.bRun = bRunning;
	C.Stance = bPerson ? (bCrouchHeld ? 2 : Stance) : 0;
	C.bTrigger = !bBinoculars && (Down(EKeys::LeftMouseButton) || TestTrigger);
	C.Mount = Mount;
	C.ShotSeq = ShotSeq; C.ShotTarget = ShotTarget; C.CancelSeq = CancelSeq; C.AmmoSeq = AmmoSeq; C.FirePref = FirePref;
	C.UseSeq = UseSeq; C.UseTarget = AimTarget; C.Verb = UseVerb;
	C.Lean = Lean; C.bBinoculars = bBinoculars;
	C.AimPoint = FVector(AimPoint);
	C.LookFrom = W->UnrealToEnuM(From);
	C.LookDir = FVector(EnuDir(Dir));
	St->Send(C);
}

void AHrotDpsCamera::PlacePlayer(float Dt, const FVector3d& P)
{
	using namespace HrotPlayer;
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	// the eye: a person's drawn head, else HROT's eye height for the kind
	FVector3d Eye = P + FVector3d(0, 0, PScheme == EScheme::Person ? PersonEyeHeight : PScheme == EScheme::Flyer ? RotorcraftEyeHeight : VehicleEyeHeight);
	AActor* Body = W->ActorOf(PlayerUnit);
	if (Body && PScheme == EScheme::Person)
	{
		TArray<USkeletalMeshComponent*> Meshes; Body->GetComponents(Meshes);
		for (USkeletalMeshComponent* M : Meshes)
			if ((M->IsVisible() || HiddenParts.Contains(M)) && M->DoesSocketExist(TEXT("head"))) { Eye = FVector3d(W->UnrealToEnuM(M->GetSocketLocation(TEXT("head")))); break; }
	}
	const FVector3d Fwd = HrotCamera::Forward(PlayerYaw, PlayerPitch);
	const bool bEyes = PlayerDistance <= 0.f;
	if (bEyes != HiddenBody.IsValid()) HideBody(Body, bEyes);
	// the zoom: HROT's VERTICAL field of view -> Unreal's horizontal one
	FVector2D Vp(16, 9); if (GEngine && GEngine->GameViewport) GEngine->GameViewport->GetViewportSize(Vp);
	const double Aspect = Vp.Y > 0 ? Vp.X / Vp.Y : 16.0 / 9.0;
	Cam->SetFieldOfView(float(FMath::RadiansToDegrees(2.0 * FMath::Atan(FMath::Tan(FMath::DegreesToRadians(VerticalFov()) * 0.5) * Aspect))));
	if (bEyes)
	{
		// his eyes, or ahead of them (< 0) along the level look
		const FVector3d Level = FVector3d(Fwd.X, Fwd.Y, 0).GetSafeNormal();
		Cam->SetWorldLocationAndRotation(W->EnuMToUnreal(FVector(Eye + Level * double(-PlayerDistance))), RotOf(Fwd));
		PlayerArm = 0;
		return;
	}
	// over the shoulder (the lean picks the side) / behind a vehicle: the rig's pivot, the spring arm against walls
	const FVector3d Centre(Eye.X, Eye.Y, (P.Z + Eye.Z) * 0.5);
	const FVector3d Piv = HrotCamera::Pivot(Centre, Eye, Fwd, PlayerDistance, PScheme, ShoulderSide);
	const FVector3d Wanted = HrotCamera::Camera(Piv, Fwd, PlayerDistance, true);
	const double T = FirstHit(W->EnuMToUnreal(FVector(Piv)), W->EnuMToUnreal(FVector(Wanted)));
	const FVector3d FreeAt = HrotCamera::SpringArm(Piv, Wanted, T);
	const float Want = float((FreeAt - Piv).Size());
	PlayerArm = HrotCamera::EaseArm(PlayerArm == 0.f ? Want : PlayerArm, Want, Dt, HrotCamera::ScaleOf(PScheme));
	FVector3d At = Piv + (Wanted - Piv).GetSafeNormal() * PlayerArm;
	At = HrotCamera::AboveHeadWhenSqueezed(At, Eye, PlayerArm, float((Wanted - Piv).Size()));
	FVector Ue = W->EnuMToUnreal(FVector(At));
	const float H = HeightAboveGround(Ue + FVector(0, 0, 200));
	if (H - 2.f < CameraAboveGround) Ue.Z += (CameraAboveGround - (H - 2.f)) * 100.0;
	Cam->SetWorldLocationAndRotation(Ue, RotOf(Fwd));
}
