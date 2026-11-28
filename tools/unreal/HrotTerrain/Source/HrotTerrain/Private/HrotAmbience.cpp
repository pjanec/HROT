// CE-4651 (U-19d) — see HrotAmbience.h
#include "HrotAmbience.h"
#include "ProfilingDebugging/CsvProfiler.h"
#include "HrotSoundscape.h"
#include "HrotShotRenderer.h"
#include "HrotSoundLog.h"
#include "Components/AudioComponent.h"
#include "Engine/World.h"
#include "HAL/IConsoleManager.h"
#include "HAL/PlatformTime.h"
#include "Kismet/GameplayStatics.h"
#include "Sound/SoundBase.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotAmbience, Log, All);

namespace
{
	float CWar = -1.f;
	FAutoConsoleVariableRef VWar(TEXT("hrot.snd.War"), CWar, TEXT("the war ambience layers' volume (-1 = leave the player's own, default 1)"));
}

// ---- the pure mix ------------------------------------------------------------------------------------------------------------------
FString FHrotAmbienceMix::KindToSet(const FString& KindIn)
{
	static const TMap<FString, FString> Table = {
		{ TEXT("battle.distant"), TEXT("amb/war_distant") }, { TEXT("battle.city"), TEXT("amb/war_city") }, { TEXT("battle.night"), TEXT("amb/war_night") },
		{ TEXT("battle.heavy"), TEXT("amb/war_heavy") }, { TEXT("battle.sniper"), TEXT("amb/war_sniper") }, { TEXT("battle.ambush"), TEXT("amb/war_ambush") },
		{ TEXT("battle.aftermath"), TEXT("amb/war_aftermath") },
		{ TEXT("event.airstrike"), TEXT("amb/ev_airstrike") }, { TEXT("event.drone"), TEXT("amb/ev_drone") }, { TEXT("event.rocket"), TEXT("amb/ev_rocket") },
		{ TEXT("event.convoy"), TEXT("amb/ev_convoy") }, { TEXT("event.evacuation"), TEXT("amb/ev_evacuation") },
		{ TEXT("event.evacuation.city"), TEXT("amb/ev_evacuation_city") }, { TEXT("event.nuclear"), TEXT("amb/ev_nuclear") },
		{ TEXT("alarm.airraid"), TEXT("amb/al_airraid") }, { TEXT("alarm.airraid.preliminary"), TEXT("amb/al_airraid_pre") }, { TEXT("alarm.bell"), TEXT("amb/al_bell") },
		{ TEXT("alarm.intruder"), TEXT("amb/al_intruder") }, { TEXT("alarm.emergency"), TEXT("amb/al_emergency") },
		{ TEXT("naval.ship"), TEXT("amb/nv_ship") }, { TEXT("naval.warship"), TEXT("amb/nv_warship") }, { TEXT("naval.underwater"), TEXT("amb/nv_underwater") },
		// a bare family: its everyday recording (the event family has none: an event is always a named one)
		{ TEXT("battle"), TEXT("amb/war_distant") }, { TEXT("alarm"), TEXT("amb/al_emergency") }, { TEXT("naval"), TEXT("amb/nv_ship") },
	};
	FString K = KindIn.ToLower();
	while (!K.IsEmpty())
	{
		if (const FString* S = Table.Find(K)) return *S;
		int32 Dot;
		if (!K.FindLastChar('.', Dot)) break;
		K.LeftInline(Dot);
	}
	return FString();
}

FHrotAmbienceMix::ECategory FHrotAmbienceMix::CategoryOf(const FString& Set)
{
	if (Set.StartsWith(TEXT("amb/war_")) || Set.StartsWith(TEXT("amb/nv_"))) return ECategory::Bed;
	if (Set.StartsWith(TEXT("amb/ev_"))) return ECategory::Event;
	if (Set.StartsWith(TEXT("amb/al_"))) return ECategory::Alarm;
	return ECategory::None;
}

float FHrotAmbienceMix::GainOf(const FRow& R, const FVector& L)
{
	const float I = FMath::Clamp(R.Intensity, 0.f, 1.f);
	if (!R.bHasPosition || R.RadiusCm <= 0.f) return I;
	const float D = FVector::Dist(R.PositionCm, L), Half = R.RadiusCm * 0.5f;
	if (D <= Half) return I;
	if (D >= R.RadiusCm) return 0.f;
	return I * (R.RadiusCm - D) / Half;
}

void FHrotAmbienceMix::Set(const FRow& R)
{
	if (!Rows.Contains(R.Id)) Played.Remove(R.Id);
	Rows.Add(R.Id, R);
	if (KindToSet(R.Kind).IsEmpty() && !Warned.Contains(R.Kind)) { Warned.Add(R.Kind); Unknown.Add(R.Kind); }
}

void FHrotAmbienceMix::Remove(int32 Id)
{
	if (Rows.Remove(Id)) Disposed.Add(Id);
	Played.Remove(Id);
}

FHrotAmbienceMix::FPlan FHrotAmbienceMix::Plan(const FVector& L) const
{
	FPlan P;
	TArray<int32> Ids; Rows.GetKeys(Ids); Ids.Sort();
	TArray<FLoop> Beds, Alarms;
	for (int32 Id : Ids)
	{
		const FRow& R = Rows[Id];
		FLoop X; X.Id = Id; X.Set = KindToSet(R.Kind); X.Cat = CategoryOf(X.Set);
		if (X.Cat == ECategory::None) continue;
		X.Gain = GainOf(R, L); X.Intensity = FMath::Clamp(R.Intensity, 0.f, 1.f);
		X.bPositioned = R.bHasPosition && R.RadiusCm > 0.f; X.PositionCm = R.PositionCm; X.RadiusCm = R.RadiusCm;
		if (X.Gain <= 0.001f) continue;
		if (X.Cat == ECategory::Bed) Beds.Add(X);
		else if (X.Cat == ECategory::Alarm) Alarms.Add(X);
		else if (!P.bEvent && !Played.Contains(Id)) { P.bEvent = true; P.Event = X; }   // the lowest-id event not yet played
	}
	const auto Loudest = [](const FLoop& A, const FLoop& B) { return A.Gain > B.Gain || (A.Gain == B.Gain && A.Id < B.Id); };
	Beds.StableSort(Loudest); Alarms.StableSort(Loudest);
	for (int32 I = 0; I < Beds.Num() && I < MaxBeds; ++I) P.Loops.Add(Beds[I]);
	for (int32 I = 0; I < Alarms.Num() && I < MaxAlarms; ++I) P.Loops.Add(Alarms[I]);
	return P;
}

// ---- the pure weather --------------------------------------------------------------------------------------------------------------
bool FHrotWeatherPick::Inside(const TArray<FVector2D>& P, const FVector2D& Q)
{
	bool In = false;
	for (int32 I = 0, J = P.Num() - 1; I < P.Num(); J = I++)
		if (((P[I].Y > Q.Y) != (P[J].Y > Q.Y)) && (Q.X < (P[J].X - P[I].X) * (Q.Y - P[I].Y) / (P[J].Y - P[I].Y) + P[I].X)) In = !In;
	return In;
}

float FHrotWeatherPick::DistanceToEdge(const TArray<FVector2D>& P, const FVector2D& Q)
{
	float Best = TNumericLimits<float>::Max();
	for (int32 I = 0, J = P.Num() - 1; I < P.Num(); J = I++)
	{
		const FVector2D A = P[J], B = P[I], AB = B - A;
		const float L2 = AB.SizeSquared();
		const float T = L2 > 0.f ? FMath::Clamp(FVector2D::DotProduct(Q - A, AB) / L2, 0.f, 1.f) : 0.f;
		Best = FMath::Min(Best, FVector2D::Distance(Q, A + AB * T));
	}
	return Best;
}

FHrotWeatherPick::FOut FHrotWeatherPick::At(const FVector2D& Pt) const
{
	FOut O;
	if (Rows.Num() == 0) return O;
	O.bAny = true;
	const FRow* Global = nullptr; const FRow* Region = nullptr; int32 RegionId = MAX_int32;
	for (const TPair<int32, FRow>& KV : Rows)
	{
		const FRow& R = KV.Value;
		if (R.Outline.Num() < 3) { if (!Global || KV.Key < Global->Id) Global = &R; continue; }
		if (Inside(R.Outline, Pt) && KV.Key < RegionId) { Region = &R; RegionId = KV.Key; }
	}
	const float GR = Global ? RainOf(*Global) : 0.f, GW = Global ? WindOf(*Global) : 0.f;
	if (!Region) { O.Rain = GR; O.Wind = GW; return O; }
	// the feather: full strength FeatherM inside the edge, blending toward the global row (calm when there is none) at the edge
	const float W = Region->FeatherM > 0.f ? FMath::Clamp(DistanceToEdge(Region->Outline, Pt) / Region->FeatherM, 0.f, 1.f) : 1.f;
	O.Rain = FMath::Lerp(GR, RainOf(*Region), W); O.Wind = FMath::Lerp(GW, WindOf(*Region), W);
	return O;
}

// ---- the player --------------------------------------------------------------------------------------------------------------------
UHrotAmbiencePlayer* UHrotAmbiencePlayer::Get(const UObject* WorldContext)
{
	UWorld* W = WorldContext ? WorldContext->GetWorld() : nullptr;
	return W ? W->GetSubsystem<UHrotAmbiencePlayer>() : nullptr;
}

TStatId UHrotAmbiencePlayer::GetStatId() const { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotAmbiencePlayer, STATGROUP_Tickables); }

void UHrotAmbiencePlayer::NoteShot() { LastShot = FPlatformTime::Seconds(); NextPlan = 0; }

FString UHrotAmbiencePlayer::Describe() const
{
	return FString::Printf(TEXT("ambience: %d rows, %d loops started (%d playing: %s), %d events, %d fades"), Mix.Num(), LoopsStarted, LoopsPlaying, *LoopsText(), EventsPlayed, Fades);
}

FString UHrotAmbiencePlayer::LoopsText() const
{
	TArray<FString> T;
	for (const TPair<int32, FPlaying>& KV : Loops) if (!KV.Value.bEnding) T.Add(FString::Printf(TEXT("%s %.2f"), *KV.Value.Set, KV.Value.Have));
	return FString::Join(T, TEXT(", "));
}

void UHrotAmbiencePlayer::Deinitialize()
{
	for (TPair<int32, FPlaying>& KV : Loops) if (UAudioComponent* C = KV.Value.C.Get()) C->Stop();
	Loops.Reset(); Owned.Reset();
	Super::Deinitialize();
}

bool UHrotAmbiencePlayer::Start(const FHrotAmbienceMix::FLoop& L, UHrotSoundscape* Snd, UAudioComponent*& OutC, float& OutSeconds)
{
	OutSeconds = 0.f; OutC = nullptr;
	USoundBase* S = Snd->Pick(L.Set);   // a miss is counted by the soundscape (LibraryMissing)
	if (!S) return false;
	OutSeconds = S->GetDuration();
	UAudioComponent* C = nullptr;
	if (L.bPositioned) C = UGameplayStatics::SpawnSoundAtLocation(GetWorld(), S, L.PositionCm, FRotator::ZeroRotator, 0.f, 1.f, 0.f, Snd->Falloff(L.RadiusCm * 0.005f, L.RadiusCm * 0.01f), nullptr, false);
	else C = UGameplayStatics::SpawnSound2D(GetWorld(), S, 0.f, 1.f, 0.f, nullptr, false, false);
	if (C) Owned.Add(C);
	OutC = C;
	{   // U-19f: the sound log - beds and alarms loop (AMBIENCE), a named event plays once (EVENT)
		UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld());
		const float DistM = L.bPositioned && R ? FVector::Dist(L.PositionCm, R->ViewLocation()) / 100.f : -1.f;
		const FHrotAmbienceMix::FRow* Row = Mix.RowOf(L.Id);
		FHrotSoundLog::Get().Add(L.Cat == FHrotAmbienceMix::ECategory::Event ? EHrotSndChannel::Event : EHrotSndChannel::Ambience, S->GetName(),
			FString::Printf(TEXT("%s %.1f"), Row ? *Row->Kind : *L.Set, L.Intensity), DistM);
	}
	return true;
}

void UHrotAmbiencePlayer::Apply(double Now)
{
	UHrotSoundscape* Snd = UHrotSoundscape::Get(this);
	UHrotShotRenderer* R = UHrotShotRenderer::Get(GetWorld());
	if (!Snd) return;
	for (const FString& U : Mix.TakeUnknown()) UE_LOG(LogHrotAmbience, Warning, TEXT("ambience: no recording for the kind '%s' (its family is unknown)"), *U);
	// a disposed row fades out over 2 s (A-4)
	for (int32 Id : Mix.TakeDisposed())
		if (FPlaying* P = Loops.Find(Id)) if (!P->bEnding) { P->bEnding = true; P->Want = 0.f; P->Rate = 0.5f; ++Fades; }
	const FVector Listener = R ? R->ViewLocation() : FVector::ZeroVector;
	const FHrotAmbienceMix::FPlan Plan = Mix.Plan(Listener);
	const bool bOn = Snd->bEnabled && WarVolume > 0.f;
	TSet<int32> Wanted;
	for (const FHrotAmbienceMix::FLoop& L : Plan.Loops)
	{
		const float Duck = L.Cat == FHrotAmbienceMix::ECategory::Bed && L.Set.StartsWith(TEXT("amb/war_")) ? FHrotAmbienceMix::ShotDuck(Now, LastShot) : 1.f;
		const float Target = bOn ? (L.bPositioned ? L.Intensity : L.Gain) * WarVolume * Duck : 0.f;
		Wanted.Add(L.Id);
		if (FPlaying* P = Loops.Find(L.Id))
		{
			if (!P->bEnding) P->Want = Target;
			continue;
		}
		float Secs; UAudioComponent* C = nullptr;
		if (!Start(L, Snd, C, Secs)) continue;
		FPlaying P; P.Set = L.Set; P.C = C; P.bHadComp = C != nullptr; P.Want = Target; P.Rate = 0.25f;   // 4 s crossfades
		Loops.Add(L.Id, P);
		++LoopsStarted;
		UE_LOG(LogHrotAmbience, Display, TEXT("ambience: loop %s for row %d (%s)"), *L.Set, L.Id, L.bPositioned ? TEXT("positioned") : TEXT("2-D"));
	}
	for (TPair<int32, FPlaying>& KV : Loops)   // a bed that fell out of the loudest two (or a row no longer audible): 4 s out
		if (!KV.Value.bEnding && !Wanted.Contains(KV.Key)) { KV.Value.bEnding = true; KV.Value.Want = 0.f; KV.Value.Rate = 0.25f; ++Fades; }
	// events: one at a time, each row once
	if (Plan.bEvent && Now >= EventUntil && bOn)
	{
		float Secs; UAudioComponent* C = nullptr;
		if (Start(Plan.Event, Snd, C, Secs))
		{
			if (C) { C->SetVolumeMultiplier(Plan.Event.Intensity * WarVolume); C->Play(); }
			EventC = C; EventUntil = Now + FMath::Max(Secs, 1.f);
			Mix.MarkPlayed(Plan.Event.Id);
			++EventsPlayed;
			UE_LOG(LogHrotAmbience, Display, TEXT("ambience: event %s for row %d"), *Plan.Event.Set, Plan.Event.Id);
		}
	}
}

CSV_DEFINE_CATEGORY(HrotTerrain, true);

void UHrotAmbiencePlayer::Tick(float Dt)
{
	CSV_SCOPED_TIMING_STAT(HrotTerrain, Ambience);
	UWorld* W = GetWorld();
	if (!W || W->IsNetMode(NM_DedicatedServer)) return;
	if (!(W->WorldType == EWorldType::PIE || W->WorldType == EWorldType::Game)) return;
	if (CWar >= 0.f) WarVolume = CWar;
	const double Now = FPlatformTime::Seconds();
	if (Now >= NextPlan) { NextPlan = Now + 1.0; Apply(Now); }
	const float Step = FMath::Min(Dt, 0.25f);
	int32 Playing = 0;
	for (auto It = Loops.CreateIterator(); It; ++It)
	{
		FPlaying& P = It.Value();
		P.Have = P.Have < P.Want ? FMath::Min(P.Have + P.Rate * Step, P.Want) : FMath::Max(P.Have - P.Rate * Step, P.Want);
		UAudioComponent* C = P.C.Get();
		if (C) { C->SetVolumeMultiplier(FMath::Max(P.Have, 0.0001f)); if (!C->IsPlaying()) C->Play(); }
		if ((P.bHadComp && !C) || (P.bEnding && P.Have <= 0.001f)) { if (C) { C->Stop(); Owned.Remove(C); } It.RemoveCurrent(); continue; }
		if (!P.bEnding) ++Playing;
	}
	LoopsPlaying = Playing;
	if (UAudioComponent* E = EventC.Get()) if (Now > EventUntil + 1.0 && !E->IsPlaying()) { Owned.Remove(E); EventC.Reset(); }
}
