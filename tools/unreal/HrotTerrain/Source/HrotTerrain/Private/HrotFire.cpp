// CE-4651 (U-17f) — see the header.
#include "HrotFire.h"

#include "HrotRuntimeLibrary.h"
#include "Components/DecalComponent.h"
#include "Engine/World.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInterface.h"
#include "Particles/ParticleSystem.h"
#include "Particles/ParticleSystemComponent.h"

// ⭐ measured in the fire lab (U-17f, scripts/unreal/hrot_vehiclelab.py "fx" entries): P_Fire_Big = a ~3 m fire + a tall black smoke
// column; P_Fire_Wall = a low ~1 m flame (a patch of burning ground); P_Fire_Small = scattered flickers only (too weak to read);
// P_flamethrower = a ~10–12 m stream along its +Z with a blue pilot flame at the nozzle
const TCHAR* FHrotFireLook::FireBig = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Fire/P_Fire_Big.P_Fire_Big");
const TCHAR* FHrotFireLook::FireSmall = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Fire/P_Fire_Wall.P_Fire_Wall");
const TCHAR* FHrotFireLook::Smoke = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Smoke/P_Smoke_A.P_Smoke_A");
const TCHAR* FHrotFireLook::Embers = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Sparks/P_Embers_A.P_Embers_A");
const TCHAR* FHrotFireLook::Flamethrower = TEXT("/Game/Realistic_Starter_VFX_Pack_Vol2/Particles/Fire/P_flamethrower.P_flamethrower");
const TCHAR* FHrotFireLook::Scorch = TEXT("/Game/Hrot/Fx/M_HrotScorch.M_HrotScorch");

namespace
{
	UParticleSystem* LoadPs(const TCHAR* Path)
	{
		return LoadObject<UParticleSystem>(nullptr, Path);
	}

	UParticleSystemComponent* SpawnAttached(const TCHAR* Path, USceneComponent* Parent, FName Socket, const FVector& Loc, const FRotator& Rot, float Scale)
	{
		UParticleSystem* PS = LoadPs(Path);
		if (!PS || !Parent) return nullptr;
		return UGameplayStatics::SpawnEmitterAttached(PS, Parent, Socket, Loc, Rot, FVector(Scale), EAttachLocation::KeepRelativeOffset, false);
	}
}

// ==== UHrotBurning ===================================================================================================================

UHrotBurning::UHrotBurning()
{
	PrimaryComponentTick.bCanEverTick = false;
}

UHrotBurning* UHrotBurning::SetBurning(AActor* Actor, float NewIntensity)
{
	if (!Actor) return nullptr;
	UHrotBurning* B = Actor->FindComponentByClass<UHrotBurning>();
	if (!B)
	{
		if (NewIntensity <= 0.f) return nullptr;
		B = NewObject<UHrotBurning>(Actor, NAME_None, RF_Transient);
		B->RegisterComponent();
	}
	B->Intensity = NewIntensity;
	B->Apply();
	return B;
}

UHrotBurning* UHrotBurning::SetBurningAt(AActor* Actor, FVector RelativeAnchor, float Scale, float NewIntensity)
{
	if (!Actor) return nullptr;
	UHrotBurning* B = Actor->FindComponentByClass<UHrotBurning>();
	if (!B)
	{
		if (NewIntensity <= 0.f) return nullptr;
		B = NewObject<UHrotBurning>(Actor, NAME_None, RF_Transient);
		B->RegisterComponent();
	}
	B->bAtAnchor = true; B->Anchor = RelativeAnchor; B->AnchorScale = Scale; B->Intensity = NewIntensity;
	B->Apply();
	return B;
}

void UHrotBurning::BeginPlay()
{
	Super::BeginPlay();
	if (Fx.Num() == 0) Apply();   // a play copy of an editor-placed burning thing arrives without its (transient) effects
}

void UHrotBurning::OnUnregister()
{
	Extinguish();
	Super::OnUnregister();
}

void UHrotBurning::Extinguish()
{
	for (UFXSystemComponent* C : Fx) if (C) C->DestroyComponent();
	Fx.Reset();
}

void UHrotBurning::SpawnOn(USceneComponent* Root, const TCHAR* Path, const FVector& RelLocation, float Scale)
{
	if (UParticleSystemComponent* C = SpawnAttached(Path, Root, NAME_None, RelLocation, FRotator::ZeroRotator, Scale)) Fx.Add(C);
}

void UHrotBurning::Apply()
{
	Extinguish();
	AActor* Owner = GetOwner();
	USceneComponent* Root = Owner ? Owner->GetRootComponent() : nullptr;
	if (!Root || Intensity <= 0.f) return;
	const float I = FMath::Clamp(Intensity, 0.f, 2.f);

	if (bAtAnchor)   // ONE fire at a point (a vehicle's engine deck): the wreck's look since U-17b
	{
		SpawnOn(Root, FHrotFireLook::FireBig, Anchor, AnchorScale * I);
		if (bSmoke) SpawnOn(Root, FHrotFireLook::Smoke, Anchor, AnchorScale * SmokeScale * I);
		return;
	}

	// ⭐ spread over the thing's size: its bounds (in its own space, so a turned house burns along its walls)
	FVector Origin, Extent;
	{
		const FBox Local = Owner->CalculateComponentsBoundingBoxInLocalSpace(true, false);
		if (!Local.IsValid) return;
		Origin = Local.GetCenter(); Extent = Local.GetExtent();
	}
	const float Span = 2.f * FMath::Max(Extent.X, Extent.Y);
	const FVector Scale3 = Root->GetComponentScale();
	const float Top = Origin.Z + Extent.Z * (Extent.Z > 150.f ? 0.6f : 0.f);   // a tall thing burns high on its body; a low one, all over
	if (Span < FHrotFireLook::SmallThing)
	{
		SpawnOn(Root, FHrotFireLook::FireSmall, FVector(Origin.X, Origin.Y, Top) , FMath::Max(0.5f, Span / 100.f) * I / Scale3.X);
		return;   // a small thing: a small fire, no column of smoke
	}
	const int32 Nx = FMath::Clamp(FMath::RoundToInt(2.f * Extent.X / FHrotFireLook::BigFireSpan), 1, FHrotFireLook::MaxFires);
	const int32 Ny = FMath::Clamp(FMath::RoundToInt(2.f * Extent.Y / FHrotFireLook::BigFireSpan), 1, FMath::Max(1, FHrotFireLook::MaxFires / Nx));
	const float Each = FMath::Clamp(FMath::Min(2.f * Extent.X / Nx, 2.f * Extent.Y / Ny) / FHrotFireLook::BigFireSpan, 0.4f, 1.5f) * I;
	for (int32 X = 0; X < Nx; ++X)
		for (int32 Y = 0; Y < Ny; ++Y)
		{
			const FVector At(Origin.X - Extent.X + (X + 0.5f) * 2.f * Extent.X / Nx, Origin.Y - Extent.Y + (Y + 0.5f) * 2.f * Extent.Y / Ny, Top);
			SpawnOn(Root, FHrotFireLook::FireBig, At, Each / Scale3.X);
		}
	if (bSmoke) SpawnOn(Root, FHrotFireLook::Smoke, FVector(Origin.X, Origin.Y, Origin.Z + Extent.Z),
	                    SmokeScale * FMath::Clamp(Span / 600.f, 0.5f, 3.f) * I / Scale3.X);
}

// ==== AHrotGroundFire ================================================================================================================

AHrotGroundFire::AHrotGroundFire()
{
	PrimaryActorTick.bCanEverTick = false;
	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent = Root;
	Scorch = CreateDefaultSubobject<UDecalComponent>(TEXT("Scorch"));
	Scorch->SetupAttachment(Root);
	Scorch->SetRelativeRotation(FRotator(-90.f, 0.f, 0.f));   // projected down onto the ground
}

void AHrotGroundFire::SetFire(float NewRadius, float NewIntensity)
{
	Radius = NewRadius; Intensity = NewIntensity;
	ApplyState();
}

void AHrotGroundFire::BeginPlay()
{
	Super::BeginPlay();
	ShownRadius = -1.f;   // the play copy arrives without the (transient) effects
	ApplyState();
}

void AHrotGroundFire::ApplyState()
{
	static UMaterialInterface* Mat = HrotLoadRooted<UMaterialInterface>(FHrotFireLook::Scorch);   // rooted (a plain static dangles)
	if (Scorch->GetDecalMaterial() != Mat) Scorch->SetDecalMaterial(Mat);
	Scorch->DecalSize = FVector(150.f, Radius * 1.15f, Radius * 1.15f);   // the burnt ground outlasts the flames (Intensity 0)
	Scorch->MarkRenderStateDirty();
	if (FMath::IsNearlyEqual(ShownRadius, Radius) && FMath::IsNearlyEqual(ShownIntensity, Intensity)) return;
	for (UFXSystemComponent* C : Fx) if (C) C->DestroyComponent();
	Fx.Reset();
	ShownRadius = Radius; ShownIntensity = Intensity;
	if (Intensity <= 0.f || !GetWorld()) return;
	const float I = FMath::Clamp(Intensity, 0.f, 2.f);
	auto Add = [this](const TCHAR* Path, const FVector& At, float Scale)
	{
		if (UParticleSystemComponent* C = SpawnAttached(Path, Root, NAME_None, At, FRotator::ZeroRotator, Scale)) Fx.Add(C);
	};
	// ⭐ low flames on a sunflower spiral over the disc (even cover, no grid lines), ~1 m flames 1.4 m apart, at most 24
	const float Spacing = 140.f;
	const int32 N = FMath::Clamp(FMath::RoundToInt(PI * Radius * Radius / (Spacing * Spacing)), 1, 24);
	const float Golden = PI * (3.f - FMath::Sqrt(5.f));
	for (int32 K = 0; K < N; ++K)
	{
		const float R = Radius * FMath::Sqrt((K + 0.5f) / N), A = K * Golden;
		// ⚠ measured (fire lab): at ×1 the low flames read as scattered flickers around the heart, not a burning patch — ×2.2
		Add(FHrotFireLook::FireSmall, FVector(R * FMath::Cos(A), R * FMath::Sin(A), 0.f), 2.2f * (0.9f + 0.4f * ((K * 7) % 5) / 4.f) * I);
	}
	// the heart + its smoke — ⚠ measured: at Radius/400 it dominated the patch (one big fire, flickers around): only for a wide patch, smaller
	if (Radius >= 400.f) Add(FHrotFireLook::FireBig, FVector::ZeroVector, FMath::Clamp(Radius / 700.f, 0.5f, 1.5f) * I);
	Add(FHrotFireLook::Embers, FVector(0.f, 0.f, 50.f), FMath::Clamp(Radius / 300.f, 0.5f, 3.f));
}

// ==== UHrotFlamethrower ==============================================================================================================

UHrotFlamethrower::UHrotFlamethrower()
{
	PrimaryComponentTick.bCanEverTick = true;
	PrimaryComponentTick.bStartWithTickEnabled = false;   // ⭐ ticks only while firing (its landing point follows the stream)
	EffectRotation = FRotator(-90.f, 0.f, 0.f);   // ⭐ measured: P_flamethrower streams along its +Z — pitched −90 it streams along X
}

UHrotFlamethrower* UHrotFlamethrower::Attach(USceneComponent* Parent, FName Socket, FTransform Offset, float NewSize)
{
	if (!Parent || !Parent->GetOwner()) return nullptr;
	UHrotFlamethrower* F = NewObject<UHrotFlamethrower>(Parent->GetOwner(), NAME_None, RF_Transient);
	F->Size = NewSize;
	F->SetUsingAbsoluteScale(true);   // the stream's size is Size alone — not the parent's scale (the dragon's mesh is scaled ×6)
	F->SetupAttachment(Parent, Socket);
	F->SetRelativeTransform(Offset);
	F->RegisterComponent();
	return F;
}

void UHrotFlamethrower::SetFiring(bool bNewFiring)
{
	bFiring = bNewFiring;
	ApplyFiring();
}

void UHrotFlamethrower::BeginPlay()
{
	Super::BeginPlay();
	bShown = false;
	ApplyFiring();
}

void UHrotFlamethrower::OnUnregister()
{
	if (Stream) Stream->DestroyComponent();
	if (LandingFire) LandingFire->DestroyComponent();
	Stream = nullptr; LandingFire = nullptr;
	Super::OnUnregister();
}

void UHrotFlamethrower::ApplyFiring()
{
	if (bFiring == bShown && (!bFiring || Stream)) return;
	bShown = bFiring;
	if (bFiring)
	{
		if (!Stream) Stream = SpawnAttached(FHrotFireLook::Flamethrower, this, NAME_None, FVector::ZeroVector, EffectRotation, Size);
		else Stream->Activate(true);
		SetComponentTickEnabled(true);
	}
	else
	{
		if (Stream) Stream->Deactivate();   // the stream already in the air finishes; no new fuel
		if (LandingFire) LandingFire->Deactivate();
		SetComponentTickEnabled(false);
	}
}

void UHrotFlamethrower::TickComponent(float DeltaTime, ELevelTick TickType, FActorComponentTickFunction* ThisTickFunction)
{
	Super::TickComponent(DeltaTime, TickType, ThisTickFunction);
	if (!bFiring || !GetWorld()) return;
	// where the stream lands: a trace along X up to its reach, else the ground under the reach's end
	const FVector From = GetComponentLocation(), Dir = GetForwardVector();
	const float Reach = Range * Size;
	FCollisionQueryParams Q(SCENE_QUERY_STAT(HrotFlame), false, GetOwner());
	FHitResult Hit;
	if (GetWorld()->LineTraceSingleByChannel(Hit, From, From + Dir * Reach, ECC_Visibility, Q)) Landing = Hit.ImpactPoint;
	else if (GetWorld()->LineTraceSingleByChannel(Hit, From + Dir * Reach, From + Dir * Reach - FVector(0, 0, 5000.f), ECC_Visibility, Q)) Landing = Hit.ImpactPoint;
	else { Landing = FVector::ZeroVector; return; }
	if (!LandingFire)
	{
		if (UParticleSystem* PS = LoadPs(FHrotFireLook::FireSmall))
			LandingFire = UGameplayStatics::SpawnEmitterAtLocation(this, PS, Landing, FRotator::ZeroRotator, FVector(1.5f * Size), false);
	}
	else
	{
		if (!LandingFire->IsActive()) LandingFire->Activate(true);
		LandingFire->SetWorldLocation(Landing);
	}
}
