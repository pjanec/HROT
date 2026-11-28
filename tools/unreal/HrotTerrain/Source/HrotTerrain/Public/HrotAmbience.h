// CE-4651 (U-19d) — HROT's AMBIENCE LAYERS and WEATHER in the Unreal IG (docs/designs/IG/DESIGN_IG_Squad_Radio.md §6, gateway DD-028).
//   FHrotAmbienceMix   PURE: the rows (DPS_Ambience: a dotted kind, an intensity, optionally a place and a radius) → which recordings loop / play now
//   FHrotWeatherPick   PURE: the rows (DPS_Weather: global, or a region with an outline) → rain 0..1 and wind 0..1 at the camera
//   UHrotAmbiencePlayer  the loops, the events and the 4 s crossfades; clips from UHrotSoundscape::Pick (the private pack, /Game/Hrot/AudioPro/amb)
// Weather needs no player: it drives the two numbers UHrotSoundscape's rain and wind beds already read (RainAmount, WindAmount).
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotAmbience.generated.h"

class UAudioComponent;

/** the pure mix of the ambience rows */
struct HROTTERRAIN_API FHrotAmbienceMix
{
	enum class ECategory : uint8 { None, Bed, Event, Alarm };
	struct FRow { int32 Id = 0; FString Kind; float Intensity = 1.f; bool bHasPosition = false; FVector PositionCm = FVector::ZeroVector; float RadiusCm = 0.f; };
	struct FLoop { int32 Id = 0; FString Set; ECategory Cat = ECategory::None; float Gain = 0.f, Intensity = 1.f; bool bPositioned = false; FVector PositionCm = FVector::ZeroVector; float RadiusCm = 0.f; };
	struct FPlan { TArray<FLoop> Loops; bool bEvent = false; FLoop Event; };

	static constexpr int32 MaxBeds = 2;
	static constexpr int32 MaxAlarms = 3;
	static constexpr double ShotWindow = 10.0;   // A-5: the beds have shots baked in; mixed lower while simulated shots were heard this recently

	/** a kind's set ("amb/war_city"), the last dotted part dropped until one is known (A-1); empty = an unknown family */
	static FString KindToSet(const FString& Kind);
	static ECategory CategoryOf(const FString& Set);
	/** A-2: a positionless row = Intensity; a positioned one: full within half the radius, linearly to silent at the radius */
	static float GainOf(const FRow& R, const FVector& ListenerCm);
	/** A-5: 0.5 (-6 dB) for a battle bed while shots were heard in the last 10 s, else 1 */
	static float ShotDuck(double Now, double LastShotAt) { return (LastShotAt >= 0.0 && Now - LastShotAt < ShotWindow) ? 0.5f : 1.f; }

	void Set(const FRow& R);
	/** the row ended (disposed): it is forgotten and listed for TakeDisposed */
	void Remove(int32 Id);
	TArray<int32> TakeDisposed() { TArray<int32> D = MoveTemp(Disposed); Disposed.Reset(); return D; }
	int32 Num() const { return Rows.Num(); }
	/** U-19f: the row by id (the sound log names its kind), null = none */
	const FRow* RowOf(int32 Id) const { return Rows.Find(Id); }
	/** A-3: the two loudest battle / naval beds, up to three alarms, and the next event (once per row) */
	FPlan Plan(const FVector& ListenerCm) const;
	/** an event row was started (an event plays once per row) */
	void MarkPlayed(int32 Id) { Played.Add(Id); }
	/** kinds nothing knew, once each (the caller logs them) */
	TArray<FString> TakeUnknown() { TArray<FString> U = MoveTemp(Unknown); Unknown.Reset(); return U; }

private:
	TMap<int32, FRow> Rows;
	TSet<int32> Played;
	TArray<int32> Disposed;
	mutable TSet<FString> Warned;
	mutable TArray<FString> Unknown;
};

/** the pure weather at a point */
struct HROTTERRAIN_API FHrotWeatherPick
{
	struct FRow
	{
		int32 Id = 0;
		TArray<FVector2D> Outline;   // metres, the gateway's ground plane; empty = the global row
		float FeatherM = 0.f, WindSpeedMps = 0.f, PrecipMmPerHour = 0.f;
		bool bRain = false;          // the precipitation falls as rain-like (drizzle, rain, sleet, hail) - snow does not
	};
	struct FOut { float Rain = 0.f, Wind = 0.f; bool bAny = false; };

	void Set(const FRow& R) { Rows.Add(R.Id, R); }
	void Remove(int32 Id) { Rows.Remove(Id); }
	int32 Num() const { return Rows.Num(); }
	/** A-6: the region whose outline holds the point (its feather blending toward the global row), else the global row; bAny false = no rows at all */
	FOut At(const FVector2D& PointM) const;
	static float RainOf(const FRow& R) { return R.bRain ? FMath::Clamp(R.PrecipMmPerHour / 8.f, 0.f, 1.f) : 0.f; }
	static float WindOf(const FRow& R) { return FMath::Clamp(R.WindSpeedMps / 15.f, 0.f, 1.f); }
	static bool Inside(const TArray<FVector2D>& Poly, const FVector2D& P);
	static float DistanceToEdge(const TArray<FVector2D>& Poly, const FVector2D& P);

private:
	TMap<int32, FRow> Rows;
};

UCLASS()
class HROTTERRAIN_API UHrotAmbiencePlayer : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	static UHrotAmbiencePlayer* Get(const UObject* WorldContext);
	UFUNCTION(BlueprintCallable, Category = "HROT|Sound", meta = (WorldContext = "WorldContext"))
	static UHrotAmbiencePlayer* ForWorld(UObject* WorldContext) { return Get(WorldContext); }

	void SetRow(const FHrotAmbienceMix::FRow& R) { Mix.Set(R); NextPlan = 0; }
	void RemoveRow(int32 Id) { Mix.Remove(Id); NextPlan = 0; }
	/** a simulated shot was heard (OnFire): the battle beds go 6 dB lower for 10 s */
	void NoteShot();

	/** the war layers' own volume (the free beds' AmbienceVolume is held back by the user): console hrot.snd.War */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Sound") float WarVolume = 1.f;
	/** ⭐ probes */
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 LoopsStarted = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 EventsPlayed = 0;
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 Fades = 0;       // loops told to fade out (a disposed row, a bed that fell out of the top two)
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Sound") int32 LoopsPlaying = 0;
	UFUNCTION(BlueprintCallable, Category = "HROT|Sound") FString Describe() const;
	/** the sets looping now, "amb/war_city 0.80, amb/al_airraid 1.00" */
	UFUNCTION(BlueprintCallable, Category = "HROT|Sound") FString LoopsText() const;

	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override;
	virtual bool IsTickableInEditor() const override { return true; }
	virtual void Deinitialize() override;

private:
	struct FPlaying { FString Set; TWeakObjectPtr<UAudioComponent> C; float Have = 0.f, Want = 0.f, Rate = 0.25f; bool bEnding = false, bHadComp = false; };
	FHrotAmbienceMix Mix;
	TMap<int32, FPlaying> Loops;
	double NextPlan = 0, LastShot = -1, EventUntil = 0;
	TWeakObjectPtr<UAudioComponent> EventC;
	UPROPERTY(Transient) TArray<TObjectPtr<UAudioComponent>> Owned;
	void Apply(double Now);
	/** the clip of the loop found (false = the library lacks it, counted by the soundscape); OutC = its component (null on a device-less run: -NoSound) */
	bool Start(const FHrotAmbienceMix::FLoop& L, class UHrotSoundscape* Snd, UAudioComponent*& OutC, float& OutSeconds);
};
