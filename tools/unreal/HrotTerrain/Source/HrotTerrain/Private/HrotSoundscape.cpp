// CE-4651 (U-18n) — see HrotSoundscape.h
#include "HrotSoundscape.h"
#include "ProfilingDebugging/CsvProfiler.h"
#include "HrotAnimal.h"
#include "HrotFire.h"
#include "HrotShotRenderer.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Modules/ModuleManager.h"
#include "HrotVehicle.h"
#include "Components/AudioComponent.h"
#include "Components/DirectionalLightComponent.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Engine/DirectionalLight.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "GameFramework/Pawn.h"
#include "UObject/UObjectIterator.h"
#include "FoliageInstancedStaticMeshComponent.h"
#include "HAL/IConsoleManager.h"
#include "HAL/PlatformTime.h"
#include "Kismet/GameplayStatics.h"
#include "Sound/SoundAttenuation.h"
#include "Sound/SoundBase.h"

namespace
{
	float CRain = -1.f, CWind = -1.f, CNight = -2.f, CAmb = -1.f, CEng = -1.f;
	FAutoConsoleVariableRef VRain(TEXT("hrot.snd.Rain"), CRain, TEXT("rain 0..1 (-1 = leave the soundscape's own)"));
	FAutoConsoleVariableRef VWind(TEXT("hrot.snd.Wind"), CWind, TEXT("wind 0..1 (-1 = leave)"));
	FAutoConsoleVariableRef VNight(TEXT("hrot.snd.Night"), CNight, TEXT("-1 by the sun, 0 day, 1 night (-2 = leave)"));
	FAutoConsoleVariableRef VAmb(TEXT("hrot.snd.Ambience"), CAmb, TEXT("ambience volume (-1 = leave)"));
	FAutoConsoleVariableRef VEng(TEXT("hrot.snd.Engines"), CEng, TEXT("engine volume (-1 = leave)"));

	const TCHAR* const BedNames[] = {
		TEXT("amb/countryside_day"), TEXT("amb/meadow_wind"), TEXT("amb/forest_day"), TEXT("amb/trees_gusts"), TEXT("amb/trees_calm"),
		TEXT("amb/night_woods"), TEXT("amb/town_day"), TEXT("amb/traffic_city"), TEXT("amb/town_night"), TEXT("amb/rain_light"),
		TEXT("amb/rain_heavy"), TEXT("amb/town_rain_night"),
	};
	enum EBed { CountryDay, MeadowWind, ForestDay, TreesGusts, TreesCalm, NightWoods, TownDay, TrafficCity, TownNight, RainLight, RainHeavy, TownRainNight, BedCount };

	float Sat(float X) { return FMath::Clamp(X, 0.f, 1.f); }
	bool HasAny(const FString& S, std::initializer_list<const TCHAR*> Words)
	{
		for (const TCHAR* W : Words) if (S.Contains(W)) return true;
		return false;
	}
}

UHrotSoundscape* UHrotSoundscape::Get(const UObject* WorldContext)
{
	UWorld* W = WorldContext ? WorldContext->GetWorld() : nullptr;
	return W ? W->GetSubsystem<UHrotSoundscape>() : nullptr;
}

TStatId UHrotSoundscape::GetStatId() const { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotSoundscape, STATGROUP_Tickables); }

USoundBase* UHrotSoundscape::LoadAt(const TCHAR* Root, const FString& Name)
{
	const FString Key = FString(Root) + Name;
	if (TObjectPtr<USoundBase>* F = Cache.Find(Key)) return *F;
	if (Missing.Contains(Key)) return nullptr;
	FString Leaf; if (!Name.Split(TEXT("/"), nullptr, &Leaf, ESearchCase::IgnoreCase, ESearchDir::FromEnd)) Leaf = Name;
	USoundBase* S = LoadObject<USoundBase>(nullptr, *FString::Printf(TEXT("%s%s.%s"), Root, *Name, *Leaf), nullptr, LOAD_NoWarn | LOAD_Quiet);
	if (S) Cache.Add(Key, S); else Missing.Add(Key);
	return S;
}

const TCHAR* UHrotSoundscape::LibraryOf(const FString& Set)
{
	// the purchased pack (/Game/Hrot/AudioPro, never published) wins where it has the set; else the free library (/Game/Hrot/Audio)
	static const TCHAR* Pro = TEXT("/Game/Hrot/AudioPro/");
	static const TCHAR* Free = TEXT("/Game/Hrot/Audio/");
	return (LoadAt(Pro, Set) || LoadAt(Pro, Set + TEXT("_01"))) ? Pro : Free;
}

USoundBase* UHrotSoundscape::Load(const FString& Name, bool bCount)
{
	USoundBase* S = LoadAt(LibraryOf(Name), Name);
	if (!S && bCount && !Reported.Contains(Name)) { Reported.Add(Name); ++LibraryMissing; }
	return S;
}

bool UHrotSoundscape::Has(const FString& Set)
{
	const TCHAR* Lib = LibraryOf(Set);
	return LoadAt(Lib, Set + TEXT("_01")) || LoadAt(Lib, Set);
}

int32 UHrotSoundscape::PreloadAll()
{
	const double T0 = FPlatformTime::Seconds();
	FAssetRegistryModule& Mod = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
	int32 N = 0;
	for (const TCHAR* Root : { TEXT("/Game/Hrot/AudioPro/"), TEXT("/Game/Hrot/Audio/") })
	{
		TArray<FAssetData> Assets;
		Mod.Get().GetAssetsByPath(FName(*FString(Root).LeftChop(1)), Assets, true);
		for (const FAssetData& A : Assets)
		{
			const FString Pkg = A.PackageName.ToString();
			if (!Pkg.StartsWith(Root)) continue;
			const FString Name = Pkg.RightChop(FCString::Strlen(Root));   // "wpn/rifle_01": the key LoadAt builds (Root + Name)
			const FString Key = FString(Root) + Name;
			if (Cache.Contains(Key)) continue;
			if (USoundBase* S = Cast<USoundBase>(A.GetAsset())) { Cache.Add(Key, S); Missing.Remove(Key); ++N; }
		}
	}
	UE_LOG(LogTemp, Display, TEXT("HROT sound preload: %d sounds in %.2f s"), N, FPlatformTime::Seconds() - T0);
	return N;
}

USoundBase* UHrotSoundscape::Pick(const FString& Set)
{
	// the variants _01 .. _NN of ONE library (never a mix of the two), one at random; the single <Set> when there are none
	const TCHAR* Lib = LibraryOf(Set);
	int32 N = 0;
	while (N < 16 && LoadAt(Lib, FString::Printf(TEXT("%s_%02d"), *Set, N + 1))) ++N;
	if (N == 0) return Load(Set, true);
	return LoadAt(Lib, FString::Printf(TEXT("%s_%02d"), *Set, Rng.RandRange(1, N)));
}

void UHrotSoundscape::Step(const FVector& Foot, bool bRun, float Loud)
{
	// a soldier's footstep (U-18n): boots on the street in town, on dirt outside it
	UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld());
	if (!R || FVector::DistSquared(Foot, R->ViewLocation()) > FMath::Square(3000.0)) return;
	if (USoundBase* S = Pick(Town > 0.5f ? TEXT("foot/street") : TEXT("foot/dirt")))
	{
		UGameplayStatics::PlaySoundAtLocation(GetWorld(), S, Foot, FMath::Clamp(Loud * (bRun ? 1.f : 0.6f), 0.05f, 1.5f), Rng.FRandRange(0.94f, 1.06f), 0.f, Falloff(2.f, 30.f));
		++Steps;
	}
}

USoundBase* UHrotSoundscape::ImpactFor(const FString& Material)
{
	const FString M = Material.ToLower();
	if (M.IsEmpty()) return nullptr;
	if (HasAny(M, { TEXT("armo"), TEXT("tank"), TEXT("hull") })) return Pick(TEXT("hit/armor"));
	if (HasAny(M, { TEXT("metal"), TEXT("steel"), TEXT("iron"), TEXT("vehicle"), TEXT("car") })) return Pick(TEXT("hit/metal"));
	if (HasAny(M, { TEXT("glass"), TEXT("window") })) return Pick(TEXT("hit/glass"));
	if (HasAny(M, { TEXT("wood"), TEXT("timber"), TEXT("door"), TEXT("tree") })) return Pick(TEXT("hit/wood"));
	if (HasAny(M, { TEXT("flesh"), TEXT("body"), TEXT("person"), TEXT("human"), TEXT("animal") })) return Pick(TEXT("hit/body"));
	if (HasAny(M, { TEXT("stone"), TEXT("rock"), TEXT("concrete"), TEXT("brick"), TEXT("asphalt"), TEXT("plaster"), TEXT("masonry") }))
		return Pick(TEXT("hit/stone"));
	if (HasAny(M, { TEXT("earth"), TEXT("dirt"), TEXT("soil"), TEXT("sand"), TEXT("grass"), TEXT("mud"), TEXT("ground"), TEXT("gravel") })) return Pick(TEXT("hit/dirt"));
	return nullptr;   // water and the rest: the default (Lyra's) hit
}

USoundAttenuation* UHrotSoundscape::Falloff(float Inner, float Reach, float AtMaxDb, bool bNatural)
{
	const EAttenuationDistanceModel Model = bNatural ? EAttenuationDistanceModel::NaturalSound : EAttenuationDistanceModel::Custom;
	for (USoundAttenuation* A : Falloffs)
		if (FMath::IsNearlyEqual(A->Attenuation.AttenuationShapeExtents.X, Inner * 100.f) && FMath::IsNearlyEqual(A->Attenuation.FalloffDistance, (Reach - Inner) * 100.f)
			&& FMath::IsNearlyEqual(A->Attenuation.dBAttenuationAtMax, AtMaxDb) && A->Attenuation.DistanceAlgorithm == Model)
			return A;
	USoundAttenuation* A = NewObject<USoundAttenuation>(this);
	FSoundAttenuationSettings& S = A->Attenuation;
	S.bAttenuate = true;
	S.bSpatialize = bSpatialize;
	S.AttenuationShape = EAttenuationShape::Sphere;
	S.AttenuationShapeExtents = FVector(Inner * 100.f, 0, 0);
	S.FalloffDistance = (Reach - Inner) * 100.f;
	S.DistanceAlgorithm = Model;
	S.dBAttenuationAtMax = AtMaxDb;
	if (!bNatural)
	{
		// the custom curve's input is the distance past the inner sphere over FalloffDistance (0..1); keys log-spaced in distance so the
		// steep near part is sampled densely (the curve is linear between keys)
		FRichCurve& C = S.CustomAttenuationCurve.EditorCurveData;
		C.Reset();
		const int32 N = 24;
		for (int32 I = 0; I <= N; ++I)
		{
			const float D = Inner * FMath::Pow(Reach / Inner, float(I) / N);   // Inner .. Reach, geometric
			const float X = (D - Inner) / FMath::Max(1e-3f, Reach - Inner);
			C.AddKey(X, InverseDistanceGain(I == N ? Reach : D, Inner, Reach));
		}
	}
	S.bAttenuateWithLPF = true;
	S.LPFRadiusMin = Inner * 300.f;
	S.LPFRadiusMax = Reach * 100.f;
	Falloffs.Add(A);
	return A;
}

void UHrotSoundscape::SetSpatialize(bool bOn)
{
	bSpatialize = bOn;
	for (USoundAttenuation* A : Falloffs) A->Attenuation.bSpatialize = bOn;   // the shared ones already handed out too
}

void UHrotSoundscape::Drop(TWeakObjectPtr<UAudioComponent>& C)
{
	if (UAudioComponent* A = C.Get()) { A->Stop(); A->DestroyComponent(); Owned.Remove(A); }
	C.Reset();
}

void UHrotSoundscape::Deinitialize()
{
	for (FBed& B : Beds) Drop(B.C);
	for (FVoice& V : Voices) { Drop(V.Run); Drop(V.Idle); }
	for (auto& KV : Callers) Drop(KV.Value.Trot);
	for (auto& KV : Loops) Drop(KV.Value.C);
	Owned.Reset();
	Super::Deinitialize();
}

CSV_DECLARE_CATEGORY_EXTERN(HrotTerrain);

void UHrotSoundscape::Tick(float DeltaTime)
{
	CSV_SCOPED_TIMING_STAT(HrotTerrain, Soundscape);
	UWorld* W = GetWorld();
	if (!W || W->IsNetMode(NM_DedicatedServer)) return;
	// the soundscape plays in a running world only (the editor's own viewport world stays quiet)
	if (!(W->WorldType == EWorldType::PIE || W->WorldType == EWorldType::Game)) return;
	if (CRain >= 0) RainAmount = CRain;
	if (CWind >= 0) WindAmount = CWind;
	if (CNight > -2) NightOverride = CNight;
	if (CAmb >= 0) AmbienceVolume = CAmb;
	if (CEng >= 0) EngineVolume = CEng;
	UHrotShotRenderer* R = UHrotShotRenderer::Get(W);
	if (!bEnabled || !R) return;
	const FVector At = R->ViewLocation();
	const double Now = FPlatformTime::Seconds();
	const float Dt = FMath::Min(DeltaTime, 0.25f);
	if (Now >= NextProbe) { NextProbe = Now + 1.0; ProbeSurroundings(At); }
	MixBeds(Dt);
	UpdateEngines(At, Dt, Now);
	if (Now >= NextAnimals) { NextAnimals = Now + 0.5; UpdateAnimals(At, Now); UpdateFires(At); }
}

void UHrotSoundscape::ProbeSurroundings(const FVector& At)
{
	UWorld* W = GetWorld();
	// the height above the ground under the view (a helicopter's view hears the wind, not the crickets)
	FHitResult G;
	FCollisionQueryParams Q(SCENE_QUERY_STAT(HrotSoundGround), false);
	HeightAboveGround = W->LineTraceSingleByChannel(G, At, At - FVector(0, 0, 200000.0), ECC_Visibility, Q) ? float((At.Z - G.ImpactPoint.Z) / 100.0) : 0.f;
	// what stands around the ground point: buildings (static meshes taller than 3 m, wider than 4 m), trees (foliage / tree meshes)
	const FVector Ground = G.bBlockingHit ? G.ImpactPoint : At;
	if (FPlatformTime::Seconds() >= NextScan) { NextScan = FPlatformTime::Seconds() + 15.0; ScanStands(); }
	const float Radius = 8000.f;
	int32 Buildings = 0, Trees = 0;
	for (const FStand& St : Stands)
	{
		const UPrimitiveComponent* C = St.C.Get();
		if (!C) continue;
		if (St.bInstanced)
		{
			const UInstancedStaticMeshComponent* I = static_cast<const UInstancedStaticMeshComponent*>(C);
			if (!C->Bounds.GetSphere().Intersects(FSphere(Ground, St.bTree ? Radius * 0.5f : Radius))) continue;
			const int32 N = I->GetInstancesOverlappingSphere(Ground, St.bTree ? Radius * 0.5f : Radius, true).Num();
			(St.bTree ? Trees : Buildings) += N;
		}
		else if (FVector::DistSquared(C->Bounds.Origin, Ground) < FMath::Square(St.bTree ? Radius * 0.5f : Radius)) ++(St.bTree ? Trees : Buildings);
	}
	int32 Cars = 0;
	for (TActorIterator<AHrotVehicle> It(W); It; ++It)
		if (FVector::DistSquared(It->GetActorLocation(), Ground) < FMath::Square(15000.0)) ++Cars;
	BuildingsNear = Buildings; TreesNear = Trees; VehiclesNear = Cars;
	Town = Sat(Buildings / 8.f + Cars / 10.f);
	Forest = Sat(Trees / 25.f) * (1.f - 0.6f * Town);
	// night: the sun below the horizon (the level's atmosphere sun), faded over -6° .. +4°
	float N = 0.f;
	if (NightOverride >= 0) N = Sat(NightOverride);
	else
		for (TActorIterator<ADirectionalLight> It(W); It; ++It)
		{
			const UDirectionalLightComponent* L = Cast<UDirectionalLightComponent>(It->GetLightComponent());
			if (!L || !L->IsUsedAsAtmosphereSunLight()) continue;
			const float Elevation = FMath::RadiansToDegrees(FMath::Asin(FMath::Clamp(float(-It->GetActorForwardVector().Z), -1.f, 1.f)));
			N = Sat((4.f - Elevation) / 10.f);
			break;
		}
	Night = N;
	// the beds' wanted gains
	if (Beds.Num() == 0) for (const TCHAR* B : BedNames) Beds.Add(FBed{ B });
	const float Day = 1.f - Night, Open = Sat(1.f - Town - Forest), Rain = Sat(RainAmount), Dry = 1.f - 0.7f * Rain, Wind = Sat(WindAmount);
	const float High = Sat(1.f - (HeightAboveGround - 60.f) / 240.f);   // fades the ground's life above 60 m
	Beds[CountryDay].Want = Open * Day * Dry * High;
	Beds[MeadowWind].Want = (0.25f + 0.75f * Wind) * (Open + 0.4f * (1.f - High));
	Beds[ForestDay].Want = Forest * Day * Dry * High;
	Beds[TreesGusts].Want = Forest * Wind * High;
	Beds[TreesCalm].Want = Forest * (1.f - Wind) * 0.7f * High;
	Beds[NightWoods].Want = (Forest + Open) * Night * Dry * High;
	Beds[TownDay].Want = Town * Day * Dry * High;
	Beds[TrafficCity].Want = Town * (0.4f + 0.6f * Day) * Sat(Cars / 4.f) * High;
	Beds[TownNight].Want = Town * Night * Dry * High;
	Beds[RainLight].Want = Rain * (1.f - Rain) * 2.f;
	Beds[RainHeavy].Want = Rain * Rain;
	Beds[TownRainNight].Want = Town * Night * Rain;
}

void UHrotSoundscape::ScanStands()
{
	// every static primitive of a building's size, every tree: static meshes, instanced meshes (per instance) and generated meshes alike
	// (⚠ measured, U-18n: the demo city's houses are not static mesh components, and they answer no physics overlap)
	UWorld* W = GetWorld();
	Stands.Reset();
	TMap<FString, int32> Classes;
	for (TObjectIterator<UPrimitiveComponent> It; It; ++It)
	{
		UPrimitiveComponent* C = *It;
		if (C->GetWorld() != W || !C->IsRegistered() || C->IsEditorOnly() || !C->IsVisible()) continue;   // movable too (houses spawned at run time)
		const FString Cls = C->GetClass()->GetName();
		if (Cls.Contains(TEXT("Landscape")) || Cls.Contains(TEXT("Skeletal")) || Cls.Contains(TEXT("Particle")) || Cls.Contains(TEXT("Niagara")) || Cls.Contains(TEXT("Decal"))) continue;
		const AActor* O = C->GetOwner();
		if (O && (O->IsA<AHrotVehicle>() || O->IsA<AHrotAnimal>() || O->IsA<APawn>())) continue;
		const UStaticMeshComponent* Sm = Cast<UStaticMeshComponent>(C);
		const UStaticMesh* M = Sm ? Sm->GetStaticMesh() : nullptr;
		const FString Name = M ? M->GetName() : (O ? O->GetName() : FString());
		const bool bInst = C->IsA<UInstancedStaticMeshComponent>();
		const bool bFoliage = C->IsA<UFoliageInstancedStaticMeshComponent>() ||
			HasAny(Name, { TEXT("Tree"), TEXT("Beech"), TEXT("Pine"), TEXT("Oak"), TEXT("Birch"), TEXT("Spruce"), TEXT("Fir"), TEXT("Megaplant") });
		const FVector E = (bInst && M) ? M->GetBounds().BoxExtent * 2.0 * C->GetComponentScale() : C->Bounds.BoxExtent * 2.0;
		bool bTook = false;
		if (bFoliage) { if (E.Z > 400.0) { Stands.Add({ C, true, bInst }); bTook = true; } }
		else if (E.Z > 300.0 && E.Z < 20000.0 && FMath::Max(E.X, E.Y) > 400.0 && FMath::Max(E.X, E.Y) < 30000.0) { Stands.Add({ C, false, bInst }); bTook = true; }
		if (bTook) Classes.FindOrAdd(Cls)++;
	}
	StandClasses.Reset();
	for (const TPair<FString, int32>& KV : Classes) StandClasses += FString::Printf(TEXT("%s%s %d"), StandClasses.IsEmpty() ? TEXT("") : TEXT(", "), *KV.Key, KV.Value);
}

void UHrotSoundscape::MixBeds(float Dt)
{
	UWorld* W = GetWorld();
	for (FBed& B : Beds)
	{
		B.Have = FMath::FInterpConstantTo(B.Have, Sat(B.Want), Dt, 0.25f);   // a 4 s crossfade
		const float Vol = B.Have * AmbienceVolume;
		UAudioComponent* C = B.C.Get();
		if (Vol > 0.002f && !C)
		{
			if (USoundBase* S = One(B.Name))
			{
				C = Hold(UGameplayStatics::CreateSound2D(W, S, Vol, 1.f, Rng.FRandRange(0.f, 50.f), nullptr, false, false));
				if (C) { C->bIsUISound = false; C->Play(Rng.FRandRange(0.f, 50.f)); B.C = C; }
			}
		}
		else if (C && Vol <= 0.002f && B.Want <= 0.f) Drop(B.C);
		else if (C) C->SetVolumeMultiplier(FMath::Max(Vol, 0.001f));
	}
}

UHrotSoundscape::EEngine UHrotSoundscape::EngineOf(const AHrotVehicle* V)
{
	return EngineOfKit(V && V->Kit ? V->Kit->GetName() : FString(), V ? V->Dis : FString());
}

const TCHAR* UHrotSoundscape::EngineName(const FString& KitName, const FString& Dis)
{
	switch (EngineOfKit(KitName, Dis))
	{
	case EEngine::Tracked: return TEXT("Tracked"); case EEngine::Heavy: return TEXT("Heavy"); case EEngine::Jeep: return TEXT("Jeep"); case EEngine::Light: return TEXT("Light");
	case EEngine::Motorcycle: return TEXT("Motorcycle"); case EEngine::Helicopter: return TEXT("Helicopter"); case EEngine::Propeller: return TEXT("Propeller");
	case EEngine::Jet: return TEXT("Jet"); case EEngine::Dragon: return TEXT("Dragon"); default: return TEXT("None");
	}
}

UHrotSoundscape::EEngine UHrotSoundscape::EngineOfKit(const FString& K, const FString& Dis)
{
	// U-19h 21: the entity's own DIS decides first (a kit NAME is a guess: the F-16 kit stands in for the A-10 AND the F-16, a city car kit may be a van ...)
	//   1.2.*.4 C-130 -> prop; 1.2.*.20 / 1.2.*.21 helicopters (AH-64, UH-60) -> rotor; any other aircraft (1.2.*) -> jet;
	//   1.1.*.81..83 civilian road vehicles -> car; 1.1.*.6 HMMWV / the technicals' pickup -> jeep. Anything else: by the kit's name below.
	{
		TArray<FString> P; Dis.ParseIntoArray(P, TEXT("."));
		if (P.Num() >= 4 && P[0] == TEXT("1"))
		{
			const int32 Cat = FCString::Atoi(*P[3]);
			if (P[1] == TEXT("2")) return Cat == 4 ? EEngine::Propeller : (Cat == 20 || Cat == 21) ? EEngine::Helicopter : EEngine::Jet;
			if (P[1] == TEXT("1") && Cat >= 81 && Cat <= 83) return EEngine::Light;
			if (P[1] == TEXT("1") && Cat == 6) return EEngine::Jeep;
		}
	}
	if (K.Contains(TEXT("Dragon"))) return EEngine::Dragon;
	if (HasAny(K, { TEXT("Apache"), TEXT("OH1"), TEXT("UH60"), TEXT("Heli"), TEXT("Hind"), TEXT("Tiger"), TEXT("Puma") })) return EEngine::Helicopter;
	if (HasAny(K, { TEXT("AC130"), TEXT("Prop"), TEXT("Cessna") })) return EEngine::Propeller;
	if (HasAny(K, { TEXT("F16"), TEXT("F22"), TEXT("Typhoon"), TEXT("MiG") })) return EEngine::Jet;
	if (HasAny(K, { TEXT("Leclerc"), TEXT("Sherman"), TEXT("Tank"), TEXT("T72"), TEXT("M1A"), TEXT("BMP"), TEXT("AMX") })) return EEngine::Tracked;
	if (HasAny(K, { TEXT("Motorcycle"), TEXT("Bike"), TEXT("Moto") })) return EEngine::Motorcycle;
	if (HasAny(K, { TEXT("VBCI"), TEXT("VAB"), TEXT("Griffon"), TEXT("Jaguar"), TEXT("BTR"), TEXT("Truck"), TEXT("Bus"), TEXT("Armored") })) return EEngine::Heavy;
	if (HasAny(K, { TEXT("VBL"), TEXT("Serval"), TEXT("Panhard"), TEXT("HMMWV"), TEXT("Humvee"), TEXT("Jeep") })) return EEngine::Jeep;
	return EEngine::Light;   // civilian cars and vans
}

void UHrotSoundscape::UpdateEngines(const FVector& At, float Dt, double Now)
{
	UWorld* W = GetWorld();
	// every half second: the nearest MaxEngines vehicles within reach (aircraft 4x farther) get voices; the rest lose theirs
	if (Now >= NextPick)
	{
		NextPick = Now + 0.5;
		for (auto S = Stills.CreateIterator(); S; ++S) if (!S.Key().IsValid()) S.RemoveCurrent();
		TArray<TPair<double, AHrotVehicle*>> Near;
		for (TActorIterator<AHrotVehicle> It(W); It; ++It)
		{
			const EEngine E = EngineOf(*It);
			if (E == EEngine::None || It->Damage == EHrotVehicleDamage::Destroyed) continue;
			{   // U-19g: stood still for 2 s with nobody aboard = parked: no engine (aircraft and crewed vehicles unchanged)
				FStill& St = Stills.FindOrAdd(*It);
				const float Speed = St.bHave ? float(FVector::Dist(It->GetActorLocation(), St.Last) / 100.0 / 0.5) : 0.f;   // m/s over this pick interval
				St.For = !St.bHave ? 99.f : Speed < StillSpeed ? FMath::Min(St.For + 0.5f, 99.f) : 0.f;   // a new one is assumed still until seen moving
				St.Last = It->GetActorLocation(); St.bHave = true;
				const bool bGround = E != EEngine::Helicopter && E != EEngine::Propeller && E != EEngine::Jet && E != EEngine::Dragon;
				if (!EngineWanted(bGround, It->bCrewed, St.For)) continue;
			}
			const double Reach = EngineReach * 100.0 * (E == EEngine::Jet ? 10.0 : (E == EEngine::Helicopter || E == EEngine::Propeller) ? 4.0 : 1.0);
			const double D2 = FVector::DistSquared(It->GetActorLocation(), At);
			if (D2 < Reach * Reach) Near.Add({ D2 / (Reach * Reach), *It });
		}
		Near.Sort([](const TPair<double, AHrotVehicle*>& A, const TPair<double, AHrotVehicle*>& B) { return A.Key < B.Key; });
		if (Near.Num() > MaxEngines) Near.SetNum(MaxEngines);
		for (FVoice& V : Voices) V.bSeen = false;
		for (const TPair<double, AHrotVehicle*>& N : Near)
		{
			FVoice* Found = Voices.FindByPredicate([&](const FVoice& V) { return V.V.Get() == N.Value; });
			if (!Found) { FVoice& V = Voices.AddDefaulted_GetRef(); V.V = N.Value; V.Kind = EngineOf(N.Value); V.Last = N.Value->GetActorLocation(); Found = &V; }
			Found->bSeen = true;
		}
		for (int32 I = Voices.Num() - 1; I >= 0; --I)
		{
			FVoice& V = Voices[I];
			if (V.bSeen && V.V.IsValid()) continue;
			// a voiced vehicle that has just been destroyed goes off once (the wreck's flames crackle in UpdateFires)
			if (AHrotVehicle* A = V.V.Get())
				if (A->Damage == EHrotVehicleDamage::Destroyed && V.Kind != EEngine::Dragon)
					if (UHrotShotRenderer* R = UHrotShotRenderer::Get(W))
						R->PlaySound(Pick(TEXT("expl/car")), A->GetActorLocation(), Falloff(20.f, 3000.f));
			Drop(V.Run); Drop(V.Idle); Voices.RemoveAtSwap(I);
		}
	}
	EnginesPlaying = 0;
	for (FVoice& V : Voices)
	{
		AHrotVehicle* A = V.V.Get();
		if (!A) continue;
		const FVector P = A->GetActorLocation();
		const float Raw = Dt > 0 ? float(FVector::Dist(P, V.Last) / 100.0 / Dt) : 0.f;
		V.Last = P;
		V.Speed = FMath::FInterpTo(V.Speed, FMath::Min(Raw, 80.f), Dt, 3.f);   // m/s, smoothed (a teleport is clamped and eased)
		USceneComponent* Root = A->GetRootComponent();
		if (V.Kind == EEngine::Dragon)
		{
			// a whoosh on each downstroke of the wings (the kit's flap rate), louder the faster it flies
			const float Rate = A->Kit && A->Kit->FlapRate > 0 ? A->Kit->FlapRate : 0.8f;
			if (Now >= V.NextFlap)
			{
				V.NextFlap = Now + 1.0 / Rate;
				if (USoundBase* S = Pick(TEXT("scene/whoosh")))
					UGameplayStatics::SpawnSoundAttached(S, Root, NAME_None, FVector::ZeroVector, EAttachLocation::KeepRelativeOffset, false,
					                                     EngineVolume * (0.7f + 0.3f * Sat(V.Speed / 20.f)), 0.6f, 0.f, Falloff(20.f, 600.f));
			}
			++EnginesPlaying;
			continue;
		}
		if (V.Kind == EEngine::Jet)
		{
			// U-19h 20: the pass one-shot as before, and now a LOOP (veh/jet_idle) below - a jet used to have only the pass, every 15 s
			if (Now >= V.NextFlap && FVector::DistSquared(P, At) < FMath::Square(90000.0) && V.Speed > 30.f)
			{
				V.NextFlap = Now + 15.0;
				if (USoundBase* S = Pick(TEXT("veh/jet_pass")))
					UGameplayStatics::SpawnSoundAttached(S, Root, NAME_None, FVector::ZeroVector, EAttachLocation::KeepRelativeOffset, false,
					                                     EngineVolume, 1.f, 0.f, Falloff(150.f, 4000.f));
			}
		}
		const TCHAR* RunName = nullptr; const TCHAR* IdleName = nullptr;
		float Base = 1.f, Top = 20.f, Inner = 8.f, Reach = EngineReach;
		switch (V.Kind)
		{
		case EEngine::Tracked: RunName = TEXT("veh/tracked_run"); IdleName = TEXT("veh/tracked_idle"); Top = 15.f; Inner = 12.f; break;
		case EEngine::Heavy: RunName = TEXT("veh/truck_run"); IdleName = TEXT("veh/truck_idle"); Top = 20.f; Inner = 10.f; break;
		case EEngine::Jeep: RunName = TEXT("veh/jeep_run"); IdleName = TEXT("veh/jeep_idle"); Top = 25.f; Inner = 6.f; break;
		case EEngine::Light: RunName = TEXT("veh/car_run"); IdleName = TEXT("veh/car_idle"); Top = 25.f; Inner = 5.f; Reach = EngineReach * 0.6f; break;
		case EEngine::Motorcycle: RunName = TEXT("veh/moto_run"); IdleName = TEXT("veh/moto_idle"); Top = 25.f; Inner = 4.f; Reach = EngineReach * 0.6f; break;
		case EEngine::Helicopter: RunName = TEXT("veh/heli"); IdleName = TEXT("veh/heli_idle"); Top = 1.f; Inner = 40.f; Reach = EngineReach * 4.f; break;
		case EEngine::Propeller: RunName = TEXT("veh/plane_prop"); Top = 1.f; Inner = 60.f; Reach = EngineReach * 4.f; break;
		case EEngine::Jet: RunName = TEXT("veh/jet_idle"); Top = 1.f; Inner = 150.f; Reach = EngineReach * 6.f; break;   // audible ~2.4 km
		default: break;
		}
		const float Move = Sat((V.Speed - 0.5f) / 4.f);
		const bool bAir = V.Kind == EEngine::Helicopter || V.Kind == EEngine::Propeller || V.Kind == EEngine::Jet;
		// a helicopter: the flight loop as it moves, the idle loop on the ground or hovering slowly
		const float AirMove = Sat((V.Speed - 3.f) / 10.f);
		const bool bHeli = V.Kind == EEngine::Helicopter;
		const float RunVol = EngineVolume * (bHeli ? FMath::Max(AirMove, 0.35f) : bAir ? 1.f : (IdleName ? Move : 0.4f + 0.6f * Move));
		const float IdleVol = EngineVolume * (bHeli ? 1.f - 0.7f * AirMove : 1.f - 0.8f * Move);
		const float Pitch = Base * (bAir ? 1.f : 0.8f + 0.45f * Sat(V.Speed / Top));
		auto Keep = [&](TWeakObjectPtr<UAudioComponent>& C, const TCHAR* Name, float Vol, float P)
		{
			if (!Name) return;
			UAudioComponent* Ac = C.Get();
			if (!Ac)
			{
				USoundBase* S = One(Name);
				if (!S) return;
				Ac = Hold(UGameplayStatics::SpawnSoundAttached(S, Root, NAME_None, FVector::ZeroVector, EAttachLocation::KeepRelativeOffset, false,
				                                              FMath::Max(Vol, 0.001f), P, Rng.FRandRange(0.f, 5.f), Falloff(Inner, Reach), nullptr, false));
				C = Ac;
			}
			if (Ac) { Ac->SetVolumeMultiplier(FMath::Max(Vol, 0.001f)); Ac->SetPitchMultiplier(P); }
		};
		Keep(V.Run, RunName, RunVol, Pitch);
		Keep(V.Idle, IdleName, IdleVol, Base);
		++EnginesPlaying;
	}
}

void UHrotSoundscape::UpdateAnimals(const FVector& At, double Now)
{
	UWorld* W = GetWorld();
	const double Reach2 = FMath::Square(AnimalReach * 100.0);
	for (auto It = Callers.CreateIterator(); It; ++It)
		if (!It.Key().IsValid() || FVector::DistSquared(It.Key()->GetActorLocation(), At) > Reach2 * 1.5)
		{ Drop(It.Value().Trot); It.RemoveCurrent(); }
	for (TActorIterator<AHrotAnimal> It(W); It; ++It)
	{
		AHrotAnimal* A = *It;
		const FVector P = A->GetActorLocation();
		if (FVector::DistSquared(P, At) > Reach2) continue;
		const FString F = A->Family.ToString();
		FCaller& C = Callers.FindOrAdd(A);
		const bool bNew = C.NextAt == 0;
		const float Speed = bNew ? 0.f : float(FVector::Dist(P, C.Last) / 100.0 / 0.5);
		C.Last = P;
		const TCHAR* Set = nullptr; float Lo = 8, Hi = 25;
		if (F == TEXT("Horse")) { Set = Rng.FRand() < 0.5f ? TEXT("animal/horse_snort") : TEXT("animal/horse_snort_b"); Lo = 5; Hi = 15; }
		else if (F == TEXT("Pig")) { Set = TEXT("animal/pig"); Lo = 5; Hi = 18; }
		else if (F == TEXT("Poultry")) { Set = Rng.FRand() < 0.08f ? TEXT("animal/rooster") : Rng.FRand() < 0.3f ? (Rng.FRand() < 0.5f ? TEXT("animal/goose") : TEXT("animal/goose_flock")) : TEXT("animal/chicken"); Lo = 3; Hi = 12; }
		else if (F == TEXT("Bird")) { Set = TEXT("animal/raptor"); Lo = 25; Hi = 70; }
		else if (F == TEXT("Sheep") || F == TEXT("Goat")) { Set = TEXT("animal/sheep"); Lo = 6; Hi = 20; }
		// cattle, dogs, cats: the libraries have none yet
		if (bNew) { C.NextAt = Now + Rng.FRandRange(0.5f, Lo); continue; }   // the first call soon after it comes near
		if (Set && Now >= C.NextAt)
		{
			C.NextAt = Now + Rng.FRandRange(Lo, Hi);
			if (USoundBase* S = Pick(Set))
			{
				UGameplayStatics::PlaySoundAtLocation(W, S, P + FVector(0, 0, 120), AnimalVolume, Rng.FRandRange(0.92f, 1.08f), 0.f, Falloff(3.f, AnimalReach));
				++AnimalCalls;
			}
		}
		// a horse trotting or galloping is heard doing it
		if (F == TEXT("Horse"))
		{
			if (Speed > 0.6f && !C.Trot.IsValid())
				if (USoundBase* S = One(Speed > 2.5f ? TEXT("animal/horse_trot") : TEXT("animal/horse_walk")))
					C.Trot = Hold(UGameplayStatics::SpawnSoundAttached(S, A->GetRootComponent(), NAME_None, FVector::ZeroVector, EAttachLocation::KeepRelativeOffset,
					                                                  false, AnimalVolume * 0.8f, 1.f, Rng.FRandRange(0.f, 3.f), Falloff(3.f, AnimalReach), nullptr, false));
			if (UAudioComponent* T = C.Trot.Get())
			{
				if (Speed <= 0.4f) Drop(C.Trot);
				else T->SetPitchMultiplier(FMath::Clamp(Speed > 2.5f ? 0.85f + Speed / 12.f : 0.8f + Speed / 6.f, 0.8f, 1.5f));
			}
		}
	}
}

void UHrotSoundscape::UpdateFires(const FVector& At)
{
	UWorld* W = GetWorld();
	for (auto& KV : Loops) KV.Value.bSeen = false;
	auto Keep = [&](UObject* Key, USceneComponent* On, const TCHAR* Name, float Vol, float Inner, float Reach)
	{
		FLoop& L = Loops.FindOrAdd(Key);
		L.bSeen = true;
		UAudioComponent* C = L.C.Get();
		if (!C)
			if (USoundBase* S = One(Name))
				L.C = C = Hold(UGameplayStatics::SpawnSoundAttached(S, On, NAME_None, FVector::ZeroVector, EAttachLocation::KeepRelativeOffset, false,
				                                                   Vol, 1.f, Rng.FRandRange(0.f, 5.f), Falloff(Inner, Reach), nullptr, false));
		if (C) C->SetVolumeMultiplier(FMath::Max(Vol, 0.001f));
	};
	const double R2 = FMath::Square(8000.0);
	for (TActorIterator<AHrotGroundFire> It(W); It; ++It)
		if (FVector::DistSquared(It->GetActorLocation(), At) < R2) Keep(*It, It->GetRootComponent(), TEXT("scene/fire_crackle"), FMath::Clamp(It->Intensity, 0.3f, 1.5f), 3.f, 80.f);
	for (TActorIterator<AHrotVehicle> It(W); It; ++It)
		if (It->Damage == EHrotVehicleDamage::Destroyed && FVector::DistSquared(It->GetActorLocation(), At) < R2)
			Keep(*It, It->GetRootComponent(), TEXT("scene/fire_crackle"), 1.2f, 4.f, 80.f);
	for (TObjectIterator<UHrotFlamethrower> It; It; ++It)
		if (It->GetWorld() == W && It->bFiring && FVector::DistSquared(It->GetComponentLocation(), At) < FMath::Square(15000.0))
			Keep(*It, *It, TEXT("scene/flame_roar"), FMath::Clamp(It->Size, 0.6f, 1.5f), 4.f, 150.f);
	for (auto It = Loops.CreateIterator(); It; ++It)
		if (!It.Value().bSeen || !It.Key().IsValid()) { Drop(It.Value().C); It.RemoveCurrent(); }
}

FString UHrotSoundscape::Describe() const
{
	FString S = FString::Printf(TEXT("town %.2f forest %.2f night %.2f height %.0f m (buildings %d trees %d vehicles %d) engines %d calls %d steps %d missing %d |"),
	                            Town, Forest, Night, HeightAboveGround, BuildingsNear, TreesNear, VehiclesNear, EnginesPlaying, AnimalCalls, Steps, LibraryMissing);
	for (const FBed& B : Beds) if (B.Have > 0.01f) S += FString::Printf(TEXT(" %s %.2f"), B.Name + 4, B.Have);
	S += TEXT(" | stands: ") + StandClasses;
	if (Reported.Num()) S += TEXT(" | missing: ") + FString::Join(Reported.Array(), TEXT(", "));
	return S;
}
