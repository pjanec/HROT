// CE-4651 (U-16) — see the header.
#include "HrotShotRenderer.h"
#include "ProfilingDebugging/CsvProfiler.h"
#include "Misc/App.h"
#if WITH_EDITOR
#include "Settings/LevelEditorMiscSettings.h"
#endif

#include "Components/InstancedStaticMeshComponent.h"
#include "Components/PointLightComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "GameFramework/PlayerController.h"
#include "Kismet/GameplayStatics.h"
#include "Components/AudioComponent.h"
#include "Materials/MaterialInterface.h"
#include "AudioDevice.h"
#include "AudioDeviceManager.h"
#include "Engine/Engine.h"
#include "Sound/SoundAttenuation.h"
#include "Sound/SoundBase.h"
#include "Sound/SoundConcurrency.h"
#include "HrotPersonAnimInstance.h"
#include "HrotWeaponFx.h"
#include "HrotSoundLog.h"
#if WITH_EDITOR
#include "Editor.h"
#include "LevelEditorViewport.h"
#endif

namespace
{
	const TCHAR* PlaneMesh = TEXT("/Engine/BasicShapes/Plane.Plane");             // 100 × 100 cm in XY, facing +Z
	const TCHAR* FlashMaterial = TEXT("/Game/Hrot/Fx/M_HrotFlash.M_HrotFlash");    // scripts/unreal/hrot_shotfx_assets.py
	const TCHAR* TracerMaterial = TEXT("/Game/Hrot/Fx/M_HrotTracer.M_HrotTracer");
	const TCHAR* PuffMaterial = TEXT("/Game/Hrot/Fx/M_HrotTrailSmoke.M_HrotTrailSmoke");

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

float UHrotShotRenderer::Now() const
{
	if (TimeSource) return float(TimeSource());   // U-18q: a render job's sim clock
	return GetWorld() ? GetWorld()->GetRealTimeSeconds() : 0.f;
}

bool UHrotShotRenderer::EnsureComponents()
{
	if (FlashIsm && TracerIsm && PuffIsm && Holder && IsValid(Holder)) return true;
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
	PuffIsm = Make(TEXT("Puffs"), PuffMaterial, 2);        // [0] opacity, [1] brightness
	PuffIsm->SetTranslucentSortPriority(-1);               // smoke behind the flashes and tracers that fly through it
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

void UHrotShotRenderer::AddFlash(const FTransform& Muzzle, float Size, float Life)
{
	Flashes.Add({ Muzzle, Now(), FMath::FRand(), Size, Life > 0.f ? Life : FlashLife });
	if (FVector::Dist(Muzzle.GetLocation(), ViewLocation()) < NearRange) FlashLight(Muzzle.GetLocation());
}

void UHrotShotRenderer::AddPuff(const FVector& At, float Size0, float Size1, float Life, float Brightness)
{
	if (Puffs.Num() >= MaxPuffs) Puffs.RemoveAt(0, Puffs.Num() - MaxPuffs + 1, EAllowShrinking::No);
	Puffs.Add({ At, Now(), FMath::Max(Life, 0.1f), Size0, Size1, FMath::FRand(), Brightness });
}

void UHrotShotRenderer::PlaySound(USoundBase* Sound, const FVector& At, USoundAttenuation* Attenuation, USoundConcurrency* Concurrency, float Volume, float Pitch)
{
	if (!Sound || !GetWorld()) return;
	const float Dist = FVector::Dist(At, ViewLocation());
	if (Dist > SoundRange) return;
	FPendingSound S{ Sound, Attenuation, Concurrency, At, Now() + (SpeedOfSound > 0.f ? Dist / SpeedOfSound : 0.f), Volume, Pitch, Now() };
	if (S.When <= Now() + 0.01f) PlayNow(S); else PendingSounds.Add(S);
}

int32 UHrotShotRenderer::PlaySoundHeld(USoundBase* Sound, const FVector& At, USoundAttenuation* Attenuation, USoundConcurrency* Concurrency, float Volume, float Pitch)
{
	if (!Sound || !GetWorld()) return 0;
	const float Dist = FVector::Dist(At, ViewLocation());
	if (Dist > SoundRange) return 0;
	const float Delay = SpeedOfSound > 0.f ? Dist / SpeedOfSound : 0.f;
	const int32 Handle = NextHeld++;
	FHeld& H = Held.Add(Handle);
	H.Delay = Delay; H.Born = Now();
	FPendingSound S{ Sound, Attenuation, Concurrency, At, Now() + Delay, Volume, Pitch, Now(), Handle };
	if (S.When <= Now() + 0.01f) PlayNow(S); else PendingSounds.Add(S);
	return Handle;
}

void UHrotShotRenderer::FadeHeld(int32 Handle, float Seconds)
{
	if (FHeld* H = Held.Find(Handle)) { H->FadeAt = Now() + H->Delay; H->FadeSeconds = Seconds; }
}

bool UHrotShotRenderer::ActivateWorldAudio()
{
	UWorld* W = GetWorld();
	FAudioDeviceManager* M = GEngine ? GEngine->GetAudioDeviceManager() : nullptr;
	if (!W || !M) return false;
	FAudioDeviceHandle H = W->GetAudioDevice();
	if (!H.IsValid()) return false;
	// ⚠ measured: SetActiveDevice did not hold (the editor picks the active device again from window focus) — SOLO wins over that
	M->SetSoloDevice(H.GetDeviceID());
	// ⚠ measured (U-18n): a hidden, unfocused editor still mixes to exact digital silence — the unfocused volume multiplier is 0 and
	// background audio is off; Movie Render Queue lifts both the same way to record audio (MovieGraphDefaultAudioRenderer.cpp)
	FApp::SetUnfocusedVolumeMultiplier(1.f);
#if WITH_EDITOR
	if (ULevelEditorMiscSettings* Misc = GetMutableDefault<ULevelEditorMiscSettings>()) Misc->bAllowBackgroundAudio = true;
#endif
	return true;
}

void UHrotShotRenderer::PlayNow(const FPendingSound& S)
{
	if (!S.Sound.IsValid() || !GetWorld()) return;
	if (S.Handle)   // a held sound keeps its component, to be faded later
	{
		UAudioComponent* C = UGameplayStatics::SpawnSoundAtLocation(GetWorld(), S.Sound.Get(), S.At, FRotator::ZeroRotator, S.Volume, S.Pitch, 0.f, S.Att.Get(), S.Con.Get(), true);
		if (FHeld* H = Held.Find(S.Handle)) H->C = C;
		++HeldStarted;
	}
	else UGameplayStatics::PlaySoundAtLocation(GetWorld(), S.Sound.Get(), S.At, FRotator::ZeroRotator, S.Volume, S.Pitch, 0.f, S.Att.Get(), S.Con.Get());
	++SoundsPlayed;
	FHrotSoundLog::Get().Add(EHrotSndChannel::Weapon, S.Sound->GetName(), FString(), FVector::Dist(S.At, ViewLocation()) / 100.f);   // U-19f: the sound log, at the moment it really starts
	if (bLogSounds)
	{
		FAudioDeviceHandle H = GetWorld()->GetAudioDevice();
		UE_LOG(LogTemp, Warning, TEXT("[HROT-SOUND] t=%.3f %s at %.0f m: asked %.3f, %.3f s later; device %d active sounds %d"),
			GetWorld()->GetTimeSeconds(), *S.Sound->GetName(), FVector::Dist(S.At, ViewLocation()) / 100.f, S.Asked, Now() - S.Asked,
			H.IsValid() ? (int32)H.GetDeviceID() : -1, H.IsValid() ? H->GetActiveSounds().Num() : -1);
	}
}

void UHrotShotRenderer::AddTracer(const FVector& From, const FVector& To, float Length, float Width)
{
	const FVector D = To - From;
	const float Range = D.Size();
	if (Range > 1.f) Tracers.Add({ From, D / Range, Range, Now(), Length > 0.f ? Length : TracerLength, Width > 0.f ? Width : TracerWidth });
}

void UHrotShotRenderer::AddDash(const FVector& From, const FVector& To, float Strength)
{
	if (FVector::DistSquared(From, To) > 1.f) Dashes.Add({ From, To, FMath::Clamp(Strength, 0.f, 1.f) });
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

CSV_DECLARE_CATEGORY_EXTERN(HrotTerrain);

void UHrotShotRenderer::Tick(float DeltaTime)
{
	CSV_SCOPED_TIMING_STAT(HrotTerrain, ShotRenderer);
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
	Flashes.RemoveAll([&](const FFlash& F) { return T - F.Born > F.Life; });
	Tracers.RemoveAll([&](const FTracer& R) { return (T - R.Born) * TracerSpeed - R.Length > R.Range; });
	for (FLight& L : Lights) if (L.C && L.Until <= T && L.C->IsVisible()) L.C->SetVisibility(false);
	Puffs.RemoveAll([&](const FPuff& P) { return T - P.Born > P.Life; });
	for (int32 I = PendingSounds.Num() - 1; I >= 0; --I)   // the bangs whose sound has now reached the view
		if (PendingSounds[I].When <= T) { PlayNow(PendingSounds[I]); PendingSounds.RemoveAtSwap(I); }
	for (auto It = Held.CreateIterator(); It; ++It)   // U-19c: the cuts of held sounds whose delay has passed; the finished ones are forgotten
	{
		FHeld& H = It.Value();
		if (H.FadeAt >= 0.f && H.FadeAt <= T && H.C.IsValid()) { H.C->FadeOut(FMath::Max(H.FadeSeconds, 0.01f), 0.f); H.FadeAt = -1.f; ++HeldFaded; }
		if (T - H.Born > 30.f || (H.Born + H.Delay + 0.1f < T && !H.C.IsValid())) It.RemoveCurrent();
	}
	LiveFlashes = Flashes.Num(); LiveTracers = Tracers.Num(); LivePuffs = Puffs.Num(); LiveDashes = Dashes.Num();
	if (!FlashIsm && Flashes.Num() == 0 && Tracers.Num() == 0 && Puffs.Num() == 0 && Dashes.Num() == 0) return;
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
		const float Age = FMath::Clamp((T - F.Born) / F.Life, 0.f, 1.f);
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
	const FVector View = ViewLocation();
	for (const FTracer& R : Tracers)
	{
		const float Head = FMath::Min((T - R.Born) * TracerSpeed, R.Range);
		const float Tail = FMath::Max(Head - R.Length, 0.f);
		if (Head - Tail < 1.f) continue;
		const FVector Centre = R.From + R.Dir * ((Head + Tail) * 0.5f);
		const FVector A = FVector::CrossProduct(R.Dir, FVector::UpVector).GetSafeNormal(KINDA_SMALL_NUMBER, FVector::RightVector);
		const FVector B = FVector::CrossProduct(R.Dir, A);
		const float Wd = FMath::Max(R.Width, float(FVector::Dist(Centre, View)) * TracerMinAngle);
		X.Add(Card(Centre, R.Dir, A, Head - Tail, Wd)); Data.Add(1.f);
		X.Add(Card(Centre, R.Dir, B, Head - Tail, Wd)); Data.Add(1.f);
	}
	for (const FDash& Ds : Dashes)   // U-18f: the dashes added for this frame (then cleared: their owner adds them again next frame)
	{
		const FVector D = Ds.To - Ds.From; const float Len = D.Size();
		const FVector Dir = D / Len, Centre = (Ds.From + Ds.To) * 0.5f;
		const FVector A = FVector::CrossProduct(Dir, FVector::UpVector).GetSafeNormal(KINDA_SMALL_NUMBER, FVector::RightVector);
		const FVector B = FVector::CrossProduct(Dir, A);
		const float Wd = FMath::Max(TracerWidth, float(FVector::Dist(Centre, View)) * TracerMinAngle);
		X.Add(Card(Centre, Dir, A, Len, Wd)); Data.Add(Ds.Strength);
		X.Add(Card(Centre, Dir, B, Len, Wd)); Data.Add(Ds.Strength);
	}
	Dashes.Reset();
	TracerIsm->ClearInstances();
	if (X.Num()) { TracerIsm->AddInstances(X, false, true); for (int32 I = 0; I < X.Num(); ++I) TracerIsm->SetCustomData(I, MakeArrayView(&Data[I], 1), false); }
	TracerIsm->MarkRenderStateDirty();

	// ⭐ trail smoke: each puff 3 crossed soft cards (a blob from any side, 6 triangles), growing and fading; it rises a little
	X.Reset(); Data.Reset();
	X.Reserve(Puffs.Num() * 3); Data.Reserve(Puffs.Num() * 6);
	for (const FPuff& P : Puffs)
	{
		const float Age = T - P.Born, A = Age / P.Life;
		const float Size = FMath::Lerp(P.Size0, P.Size1, FMath::Sqrt(A));
		const float Opacity = FMath::Min(1.f, Age / 0.15f) * FMath::Pow(1.f - A, 1.5f);
		const FVector At = P.At + FVector(0.f, 0.f, 25.f * Age);
		const FQuat Spin = FQuat(FVector(P.Seed, 1.f - P.Seed, 0.5f).GetSafeNormal(), P.Seed * 2.f * PI);
		const FVector Ax = Spin.RotateVector(FVector::XAxisVector), Ay = Spin.RotateVector(FVector::YAxisVector), Az = Spin.RotateVector(FVector::ZAxisVector);
		X.Add(Card(At, Ax, Ay, Size, Size)); X.Add(Card(At, Ay, Az, Size, Size)); X.Add(Card(At, Az, Ax, Size, Size));
		for (int32 K = 0; K < 3; ++K) Data.Append({ Opacity, P.Bright });
	}
	PuffIsm->ClearInstances();
	if (X.Num())
	{
		PuffIsm->AddInstances(X, false, true);
		PuffIsm->PerInstanceSMCustomData = Data;   // all at once (a SetCustomData per instance costs thousands of calls)
	}
	PuffIsm->MarkRenderStateDirty();
}
