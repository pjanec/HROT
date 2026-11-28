// CE-4651 (U-18m) — THE PLAYER STATION over DPS (the DPS client guide, "What the station sends back to HROT"): this IG as the station's
// one player. Reads PlayerSlot (the slot's state, the units offered) and OwnUnit (health, ammo per slot, the fire mode, the spread, the use
// verdict); a Detonation on our unit is a damage ARC, one by our unit a HIT MARKER (HROT's HitFeedback); writes JoinRequest (take a unit),
// ControlFrame (one per frame while Playing — HROT's FpsCameraDriver frame, filled by the camera pawn), Leave (Esc). One random ClientId.
// The camera pawn (AHrotDpsCamera) is the input and the view; the HUD (AHrotDpsHud) reads this.
#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "HrotDpsStation.generated.h"

/** one weapon line of OwnUnit.Ammo */
USTRUCT(BlueprintType)
struct FHrotDpsAmmoLine
{
	GENERATED_BODY()
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") int32 Slot = 0;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") FString Weapon;     // the HUD word: RIFLE, MG, CANNON, HANDS …
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") FString AmmoType;   // the round's name
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") int32 Rounds = 0;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") int32 Max = 0;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") int32 Magazine = -1;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") int32 LoadedRounds = -1;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") bool bBusy = false;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") bool bOverheated = false;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") int32 HeatPercent = 0;
};

/** one row of HROT's offered-verb list for the crosshair's target (OwnUnit.Actions, gateway DD-029), in HROT's order - the first is the primary */
USTRUCT(BlueprintType)
struct FHrotDpsUseAction
{
	GENERATED_BODY()
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") uint8 Verb = 0;        // the contract's UseVerb
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") FString Label;         // HROT's words
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") bool bAvailable = false;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") uint8 Reason = 0;      // UseReason when refused
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Player") bool bHoldOnly = false;
};

/** what the camera pawn sends this frame (HROT's OperatorControlFrame, the contract's ControlFrame) — positions in the contract's frame */
USTRUCT(BlueprintType)
struct FHrotDpsControl
{
	GENERATED_BODY()
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") float Forward = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") float Lateral = 0;   // + left (the body's frame)
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") float Steer = 0;     // vehicles, animals (A/D)
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") float Yaw = 0;       // the body heading wanted (rad, ENU, CCW)
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") float Vertical = 0;  // flyers
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") bool bRun = false;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 Stance = 0;    // 0 no request, 1 standing, 2 crouched, 3 prone
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") bool bTrigger = false;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 Mount = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 ShotSeq = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") int32 ShotTarget = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 CancelSeq = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 AmmoSeq = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 FirePref = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 UseSeq = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") int32 UseTarget = 0;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 Verb = 0;      // the contract's UseVerb this press names (0 = None = the primary)
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") uint8 Lean = 0;      // 0 none, 1 left, 2 right
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") bool bBinoculars = false;
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") FVector AimPoint = FVector::ZeroVector;   // m, ENU
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") FVector LookFrom = FVector::ZeroVector;   // m, ENU
	UPROPERTY(BlueprintReadWrite, Category = "HROT|Player") FVector LookDir = FVector::ForwardVector;
};

UCLASS()
class HROTDPS_API UHrotDpsStation : public UTickableWorldSubsystem
{
	GENERATED_BODY()

public:
	static UHrotDpsStation* Get(const UObject* WorldContext);

	/** subscribes the player topics (after UHrotDpsWorld::Start joined the station); idempotent */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") void Connect();
	/** the DPS client was restarted (UHrotDpsWorld::Start): subscribe again on the fresh one, the same ClientId */
	void Reconnect() { Subs.Reset(); Connect(); }
	/** asks to play unit UnitId (an offer's Id) — JoinRequest, Kind Unit */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") void Join(int32 UnitId);
	/** lets the unit go (Esc) — Leave; the unit returns to its AI */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") void Leave();
	/** one control frame (call every frame while Playing) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") void Send(const FHrotDpsControl& Frame);

	/** the slot's state: Waiting, Observing, Choosing, Requesting, Spawning, TakingControl, Playing, Dead (the contract's SlotState) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") FString SlotState() const;
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") bool IsPlaying() const { return bSlot && State == 6; }
	/** the unit this station plays (0 = none) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") int32 PlayedUnit() const { return bSlot ? SlotUnit : 0; }
	/** the unit we asked for, while the slot has not taken it yet (0 = none) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") int32 RequestedUnit() const { return Requested; }
	/** may this station play UnitId? false with the reason (not offered, taken by another station …) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") bool CanPossess(int32 UnitId, FString& Reason) const;
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") FString SlotReason() const { return Reason; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") int32 OffersCount() const { return Offers.Num(); }
	/** the units offered to this station (PlayerSlot.Offers): their ids, and "name (free|taken)" */
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") TArray<int32> OfferIds() const { TArray<int32> R; for (const FOffer& O : Offers) R.Add(O.Id); return R; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") FString OfferName(int32 Id) const
	{ for (const FOffer& O : Offers) if (O.Id == Id) return O.Name + (O.bFree ? TEXT(" (free)") : TEXT(" (taken)")); return FString(); }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player", meta = (WorldContext = "WorldContext")) static UHrotDpsStation* ForWorld(UObject* WorldContext) { return Get(WorldContext); }

	// OwnUnit — what the HUD shows
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") float Health() const { return OwnHealth; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") float MaxHealth() const { return OwnMaxHealth; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") int32 ActiveSlot() const { return OwnActiveSlot; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") TArray<FHrotDpsAmmoLine> Ammo() const { return OwnAmmo; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") TArray<FHrotDpsUseAction> Actions() const { return UseActions; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") FString FireMode() const { return OwnFireMode == 1 ? TEXT("LOCK") : TEXT("SIGHT"); }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") float SpreadRad() const { return OwnSpread; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") FString UsePrompt() const { return bCanUse ? UseLabel : FString(); }
	UFUNCTION(BlueprintCallable, Category = "HROT|Player") int32 FramesSent() const { return int32(Seq); }

	/** hit feedback (HROT's HitFeedback): a hit on us — where it came FROM (contract direction), when (real s), how much */
	struct FHit { FVector3d From = FVector3d::ZeroVector; double At = 0; float Damage = 0; };
	const TArray<FHit>& HitsOnUs() const { return Hits; }
	/** real seconds of our last hit on someone (the marker), and whether that one went down */
	double LastHitByUsAt() const { return MarkerAt; }
	bool LastHitKilled() const { return bMarkerKill; }

	virtual void Tick(float DeltaTime) override;
	virtual TStatId GetStatId() const override { RETURN_QUICK_DECLARE_CYCLE_STAT(UHrotDpsStation, STATGROUP_Tickables); }
	virtual bool DoesSupportWorldType(const EWorldType::Type T) const override { return T == EWorldType::Game || T == EWorldType::PIE; }
	virtual void Deinitialize() override;

private:
	struct FOffer { int32 Id = 0; FString Name; bool bFree = false; uint8 Kind = 0; };
	uint64 ClientId = 0;
	uint32 Seq = 0, RequestSeq = 0, LeaveSeq = 0;
	TArray<TPair<FString, int32>> Subs;
	bool bSlot = false;
	uint8 State = 0;
	int32 SlotUnit = 0, Requested = 0;
	FString Reason;
	TArray<FOffer> Offers;
	int32 LoggedOffers = -1;
	mutable TSet<int32> SaidRefused;   // CanPossess is const (the menu asks every frame) - each refused unit logged once
	float OwnHealth = 0, OwnMaxHealth = 0, OwnSpread = 0, LastHealth = -1;
	int32 OwnActiveSlot = 0;
	uint8 OwnFireMode = 0;
	bool bCanUse = false;
	FString UseLabel;
	TArray<FHrotDpsAmmoLine> OwnAmmo;
	TArray<FHrotDpsUseAction> UseActions;
	TArray<FHit> Hits;
	double MarkerAt = -1;
	bool bMarkerKill = false;
	int32 MarkerTarget = 0;
	float PendingDamage = 0;
	void OnSlot(const struct FHrotDpsSample& S);
	void OnOwnUnit(const struct FHrotDpsSample& S);
	void OnDetonation(const struct FHrotDpsSample& S);
	void OnManDowned(const struct FHrotDpsSample& S);
	FString LastUseLine;
};
