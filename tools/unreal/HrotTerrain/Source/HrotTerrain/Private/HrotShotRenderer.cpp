// CE-4651 (U-16) — see the header.
#include "HrotShotRenderer.h"

#include "Components/InstancedStaticMeshComponent.h"
#include "Components/PointLightComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "GameFramework/PlayerController.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInterface.h"
#include "HrotPersonAnimInstance.h"
#include "HrotWeaponFx.h"
#if WITH_EDITOR
#include "Editor.h"
#include "LevelEditorViewport.h"
#endif

namespace
{
	const TCHAR* PlaneMesh = TEXT("/Engine/BasicShapes/Plane.Plane");             // 100 × 100 cm in XY, facing +Z
	const TCHAR* FlashMaterial = TEXT("/Game/Hrot/Fx/M_HrotFlash.M_HrotFlash");    // scripts/unreal/hrot_shotfx_assets.py
	const TCHAR* TracerMaterial = TEXT("/Game/Hrot/Fx/M_HrotTracer.M_HrotTracer");

	/** A plane instance spanning `Len` along X (= AxisX) and `Wid` along Y (= AxisY), centred at `Centre`. */
	FTransform Card(const FVector& Centre, const FVector& AxisX, const FVector& AxisY, float Len, float Wid)
	{
		const FMatrix M = FRotationMatrix::MakeFromXY(AxisX, AxisY);
		return FTransform(M.ToQuat(), Centre, FVector(Len / 100.0, Wid / 100.0, 1.0));
	}
}

UHrotShotRenderer* UHrotShotRenderer::Get(const UObject* WorldContext)
{
	UWorld* W = WorldContext ? WorldContext->GetWorld() : nullptr;
	return W ? W->GetSubsystem<UHrotShotRenderer>() : nullptr;
}

TStatId UHrotShotRenderer::GetStatId() const { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotShotRenderer, STATGROUP_Tickables); }

float UHrotShotRenderer::Now() const { return GetWorld() ? GetWorld()->GetRealTimeSeconds() : 0.f; }

bool UHrotShotRenderer::EnsureComponents()
{
	if (FlashIsm && TracerIsm && Holder && IsValid(Holder)) return true;
	UWorld* W = GetWorld();
	if (!W) return false;
	FActorSpawnParameters P; P.ObjectFlags |= RF_Transient; P.Name = MakeUniqueObjectName(W, AActor::StaticClass(), TEXT("HrotShots"));
	Holder = W->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity, P);
	if (!Holder) return false;
	USceneComponent* Root = NewObject<USceneComponent>(Holder, TEXT("Root"));
	Holder->SetRootComponent(Root); Root->RegisterComponent();
	UStaticMesh* Plane = LoadObject<UStaticMesh>(nullptr, PlaneMesh);
	auto Make = [&](const TCHAR* Name, const TCHAR* MatPath, int32 CustomFloats)
	{
		UInstancedStaticMeshComponent* C = NewObject<UInstancedStaticMeshComponent>(Holder, Name);
		C->SetStaticMesh(Plane);
		if (UMaterialInterface* M = LoadObject<UMaterialInterface>(nullptr, MatPath)) C->SetMaterial(0, M);
		C->NumCustomDataFloats = CustomFloats;
		C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
		C->SetCastShadow(false);
		C->SetMobility(EComponentMobility::Movable);
		C->SetupAttachment(Root);
		C->RegisterComponent();
		Holder->AddInstanceComponent(C);
		return C;
	};
	FlashIsm = Make(TEXT("Flashes"), FlashMaterial, 3);    // [0] age 0..1, [1] card (0 front, 1 side), [2] seed
	TracerIsm = Make(TEXT("Tracers"), TracerMaterial, 1);  // [0] intensity
	return true;
}

int32 UHrotShotRenderer::NextRound(const USceneComponent* Weapon)
{
	int32& N = Rounds.FindOrAdd(Weapon);
	return N++;
}

FVector UHrotShotRenderer::ViewLocation() const
{
	UWorld* W = GetWorld();
	if (W && W->IsGameWorld())
		if (APlayerController* PC = W->GetFirstPlayerController())
			if (PC->PlayerCameraManager) return PC->PlayerCameraManager->GetCameraLocation();
#if WITH_EDITOR
	if (GCurrentLevelEditingViewportClient) return GCurrentLevelEditingViewportClient->GetViewLocation();
#endif
	return FVector::ZeroVector;
}

bool UHrotShotRenderer::TakeBudget(EExtra Kind)
{
	float& T = Tokens[(int32)Kind];
	if (T < 1.f) return false;
	T -= 1.f; ++NearThisFrame;
	return true;
}

void UHrotShotRenderer::AddFlash(const FTransform& Muzzle, float Size)
{
	Flashes.Add({ Muzzle, Now(), FMath::FRand(), Size });
	if (FVector::Dist(Muzzle.GetLocation(), ViewLocation()) < NearRange) FlashLight(Muzzle.GetLocation());
}

void UHrotShotRenderer::AddTracer(const FVector& From, const FVector& To)
{
	const FVector D = To - From;
	const float Range = D.Size();
	if (Range > 1.f) Tracers.Add({ From, D / Range, Range, Now() });
}

void UHrotShotRenderer::FlashLight(const FVector& At) { Flash(At, 20000.f, 600.f, FlashLife); }

void UHrotShotRenderer::Flash(const FVector& At, float Intensity, float Radius, float Seconds)
{
	if (!EnsureComponents()) return;
	const float T = Now();
	FLight* Free = nullptr;
	for (FLight& L : Lights) if (L.Until <= T) { Free = &L; break; }
	if (!Free && Lights.Num() < MaxLights)
	{
		UPointLightComponent* C = NewObject<UPointLightComponent>(Holder);
		C->SetMobility(EComponentMobility::Movable);
		C->SetCastShadows(false);
		C->SetAttenuationRadius(600.f);
		C->SetLightColor(FLinearColor(1.f, 0.62f, 0.3f));
		C->SetupAttachment(Holder->GetRootComponent());
		C->RegisterComponent();
		Free = &Lights.Add_GetRef({ C, 0.f });
	}
	if (!Free) return;   // all lights busy: the flash still shows, unlit surroundings
	Free->C->SetWorldLocation(At);
	Free->C->SetIntensity(Intensity);
	Free->C->SetAttenuationRadius(Radius);
	Free->C->SetVisibility(true);
	Free->Until = T + Seconds;
}

void UHrotShotRenderer::StartTestFire(const TArray<UHrotPersonState*>& Shooters, float RoundsPerSecond, float BurstSeconds, float PauseSeconds)
{
	TestShooters.Reset(); for (UHrotPersonState* S : Shooters) if (S) TestShooters.Add(S);
	TestCarry.Init(0.f, TestShooters.Num());
	TestRate = RoundsPerSecond; TestBurst = FMath::Max(BurstSeconds, 0.01f); TestPause = FMath::Max(PauseSeconds, 0.f);
	TestRoundsFired = 0;
}

void UHrotShotRenderer::StopTestFire() { TestShooters.Reset(); TestCarry.Reset(); TestRate = 0.f; }

void UHrotShotRenderer::Tick(float DeltaTime)
{
	const double FireStart = FPlatformTime::Seconds();
	if (TestRate > 0.f && TestShooters.Num() > 0)
	{
		static FHrotShotFx Fx = UHrotWeaponFx::LyraRifleFx();
		const float T = Now(), Cycle = TestBurst + TestPause;
		for (int32 I = 0; I < TestShooters.Num(); ++I)
		{
			UHrotPersonState* S = TestShooters[I];
			if (!S || FMath::Fmod(T + I * 0.37f, Cycle) >= TestBurst) continue;   // out of step: each its own burst rhythm
			UMeshComponent* W = S->GetWeaponComponent();
			if (!W) continue;
			TestCarry[I] += TestRate * DeltaTime;
			while (TestCarry[I] >= 1.f)
			{
				TestCarry[I] -= 1.f;
				const FTransform M = UHrotWeaponFx::MuzzleTransform(W, Fx.MuzzleSocket);
				UHrotWeaponFx::FireShot(W, Fx, { M.GetLocation() + M.GetRotation().GetForwardVector() * 4000.0 }, {});
				++TestRoundsFired;
			}
		}
	}
	FireMsLastFrame = float((FPlatformTime::Seconds() - FireStart) * 1000.0);
	const double RebuildStart = FPlatformTime::Seconds();
	ON_SCOPE_EXIT { RebuildMsLastFrame = float((FPlatformTime::Seconds() - RebuildStart) * 1000.0); };
	NearSpawnedLastFrame = NearThisFrame; NearThisFrame = 0;
	const float Rates[4] = { SmokePerSecond, ShellPerSecond, ImpactPerSecond, BlastPerSecond };
	for (int32 K = 0; K < 4; ++K) Tokens[K] = FMath::Min(Tokens[K] + Rates[K] * DeltaTime, FMath::Max(Rates[K] * 0.1f, 1.f));   // ≤ 0.1 s of burst
	const float T = Now();
	Flashes.RemoveAll([&](const FFlash& F) { return T - F.Born > FlashLife; });
	Tracers.RemoveAll([&](const FTracer& R) { return (T - R.Born) * TracerSpeed - TracerLength > R.Range; });
	for (FLight& L : Lights) if (L.C && L.Until <= T && L.C->IsVisible()) L.C->SetVisibility(false);
	LiveFlashes = Flashes.Num(); LiveTracers = Tracers.Num();
	if (!FlashIsm && Flashes.Num() == 0 && Tracers.Num() == 0) return;
	if (!EnsureComponents()) return;

	// ⭐ rebuilt every frame from the live lists: a few hundred instances, one draw call per mesh
	TArray<FTransform> X; TArray<float> Data;
	X.Reserve(Flashes.Num() * 3); Data.Reserve(Flashes.Num() * 9);
	for (const FFlash& F : Flashes)
	{
		const FVector At = F.Muzzle.GetLocation();
		const FVector Fwd = F.Muzzle.GetRotation().GetForwardVector();
		const FVector Up = F.Muzzle.GetRotation().GetUpVector();
		const FVector Side = FVector::CrossProduct(Fwd, Up).GetSafeNormal();
		const float Age = FMath::Clamp((T - F.Born) / FlashLife, 0.f, 1.f);
		// the front card faces along the barrel (its plane ⟂ Fwd), spun by the seed so repeated flashes differ
		const FQuat Spin(Fwd, F.Seed * 2.f * PI);
		const float Len = FlashLength * F.Size, Wid = FlashWidth * F.Size;
		X.Add(Card(At + Fwd * 2.f, Spin.RotateVector(Side), Spin.RotateVector(Up), Wid * 1.2f, Wid * 1.2f));
		Data.Append({ Age, 0.f, F.Seed });
		// two side cards containing the barrel, crossed, extending forward from the muzzle
		X.Add(Card(At + Fwd * Len * 0.5f, Fwd, Spin.RotateVector(Up), Len, Wid));
		Data.Append({ Age, 1.f, F.Seed });
		X.Add(Card(At + Fwd * Len * 0.5f, Fwd, Spin.RotateVector(Side), Len, Wid));
		Data.Append({ Age, 1.f, F.Seed });
	}
	FlashIsm->ClearInstances();
	if (X.Num()) { FlashIsm->AddInstances(X, false, true); for (int32 I = 0; I < X.Num(); ++I) FlashIsm->SetCustomData(I, MakeArrayView(&Data[I * 3], 3), false); }
	FlashIsm->MarkRenderStateDirty();

	X.Reset(); Data.Reset();
	for (const FTracer& R : Tracers)
	{
		const float Head = FMath::Min((T - R.Born) * TracerSpeed, R.Range);
		const float Tail = FMath::Max(Head - TracerLength, 0.f);
		if (Head - Tail < 1.f) continue;
		const FVector Centre = R.From + R.Dir * ((Head + Tail) * 0.5f);
		const FVector A = FVector::CrossProduct(R.Dir, FVector::UpVector).GetSafeNormal(KINDA_SMALL_NUMBER, FVector::RightVector);
		const FVector B = FVector::CrossProduct(R.Dir, A);
		X.Add(Card(Centre, R.Dir, A, Head - Tail, TracerWidth)); Data.Add(1.f);
		X.Add(Card(Centre, R.Dir, B, Head - Tail, TracerWidth)); Data.Add(1.f);
	}
	TracerIsm->ClearInstances();
	if (X.Num()) { TracerIsm->AddInstances(X, false, true); for (int32 I = 0; I < X.Num(); ++I) TracerIsm->SetCustomData(I, MakeArrayView(&Data[I], 1), false); }
	TracerIsm->MarkRenderStateDirty();
}
