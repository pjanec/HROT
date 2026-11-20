// CE-4651 (U-18l) — see HrotDpsCameraPawn.h
#include "HrotDpsCameraPawn.h"
#include "HrotDpsWorld.h"
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
	if (APlayerController* PC = Cast<APlayerController>(GetController())) { PC->bShowMouseCursor = true; PC->bEnableClickEvents = true; }
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
	Mode = EHrotCameraMode::Free; FollowId = 0; FlyVelocity = FVector::ZeroVector;
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
	if (Mode == EHrotCameraMode::Free) return TEXT("free");
	const TCHAR* Band = FollowDistance < EyesBelow ? TEXT("eyes") : NearBlend(FollowDistance, EScheme::Person) >= 1.f ? TEXT("shoulder") : TEXT("orbit");
	return FString::Printf(TEXT("follow %d at %.2f m (%s), arm %.2f m"), FollowId, FollowDistance, Band, Arm);
}

void AHrotDpsCamera::Tick(float Dt)
{
	Super::Tick(Dt);
	TickInput(Dt);
	if (Mode == EHrotCameraMode::Follow) PlaceFollow(Dt);
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
	if (!W || !W->PoseNow(FollowId, PosM, Rot, Domain)) { Free(); return; }   // the unit is gone: back to free flight where we are
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
	const FVector3d Piv = Pivot(Centre, Eye, Fwd, FollowDistance, S, +1);
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
	UHrotDpsWorld* W = UHrotDpsWorld::Get(this);
	if (!PC || !W) return 0;
	FVector From, Dir;
	if (!PC->DeprojectMousePositionToWorld(From, Dir)) return 0;
	FCollisionQueryParams Q(SCENE_QUERY_STAT(HrotCamPick), true, this);
	TArray<FHitResult> Hits;
	GetWorld()->LineTraceMultiByChannel(Hits, From, From + Dir * 5e5, ECC_Visibility, Q);
	for (const FHitResult& H : Hits)
	{
		AActor* A = H.GetActor();
		for (AActor* X = A; X; X = X->GetAttachParentActor())   // a rider, a part: its own entity first, else what it rides
			if (const int32 Id = W->EntityOfActor(X)) return Id;
	}
	return 0;
}

void AHrotDpsCamera::TickInput(float Dt)
{
	APlayerController* PC = Cast<APlayerController>(GetController());
	if (!PC) return;   // driven by calls (Simulate, a test, a render node)
	float Mx = 0, My = 0; PC->GetInputMouseDelta(Mx, My);
	const float MouseScale = 10.f;   // ⚠ Unreal's mouse delta is not pixels: ~10 px a unit at the default sensitivity
	const bool bRmb = PC->IsInputKeyDown(EKeys::RightMouseButton);
	const float WheelIn = PC->WasInputKeyJustPressed(EKeys::MouseScrollUp) ? 1.f : PC->WasInputKeyJustPressed(EKeys::MouseScrollDown) ? -1.f : 0.f;
	const bool bShift = PC->IsInputKeyDown(EKeys::LeftShift) || PC->IsInputKeyDown(EKeys::RightShift);
	const bool bCtrl = PC->IsInputKeyDown(EKeys::LeftControl) || PC->IsInputKeyDown(EKeys::RightControl);
	const bool bW = PC->IsInputKeyDown(EKeys::W), bS = PC->IsInputKeyDown(EKeys::S), bA = PC->IsInputKeyDown(EKeys::A), bD = PC->IsInputKeyDown(EKeys::D);

	if (PC->WasInputKeyJustPressed(EKeys::LeftMouseButton) && !bRmb) Selected = PickUnderCursor();
	if (PC->WasInputKeyJustPressed(EKeys::F) && Selected) Follow(Selected);

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
