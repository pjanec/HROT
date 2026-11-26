// CE-4651 (U-18m) — see HrotDpsStation.h
#include "HrotDpsStation.h"
#include "ProfilingDebugging/CsvProfiler.h"
#include "HrotDpsNative.h"
#include "HrotDpsSubsystem.h"
#include "HrotDpsWorld.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "HAL/PlatformTime.h"
#include "Math/RandomStream.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotDpsStation, Log, All);

namespace
{
	using FSlotS = SimNetGateway_Protocol_DdsPlayer_PlayerSlot;
	using FOwnS = SimNetGateway_Protocol_DdsPlayer_OwnUnit;
	using FDetS = SimNetGateway_Protocol_DdsPlayer_Detonation;
	using FDownS = SimNetGateway_Protocol_DdsPlayer_ManDowned;
	double Now() { return FPlatformTime::Seconds(); }
	const TCHAR* StateName(uint8 S)
	{
		static const TCHAR* N[] = { TEXT("Waiting"), TEXT("Observing"), TEXT("Choosing"), TEXT("Requesting"), TEXT("Spawning"),
		                            TEXT("TakingControl"), TEXT("Playing"), TEXT("Dead") };
		return S < UE_ARRAY_COUNT(N) ? N[S] : TEXT("?");
	}
}

UHrotDpsStation* UHrotDpsStation::Get(const UObject* WorldContext)
{
	const UWorld* W = WorldContext ? WorldContext->GetWorld() : nullptr;
	return W ? W->GetSubsystem<UHrotDpsStation>() : nullptr;
}

void UHrotDpsStation::Connect()
{
	if (Subs.Num()) return;
	UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>();
	if (!Dps || !Dps->IsRunning()) return;
	FHrotDpsClient& C = Dps->Client();
	// a random nonzero ClientId (the guide): the gateway tells this station from another
	if (ClientId == 0) ClientId = Dps->GetClientId();   // U-18p: the process's one id (the clock probe and ClientWorld say it too)
	auto Sub = [this, &C](const TCHAR* Topic, void (UHrotDpsStation::*Fn)(const FHrotDpsSample&))
	{
		Subs.Add({ Topic, C.Subscribe(Topic, [this, Fn](const FHrotDpsSample& S) { (this->*Fn)(S); }) });
	};
	Sub(TEXT("DPS_PlayerSlot"), &UHrotDpsStation::OnSlot);
	Sub(TEXT("DPS_OwnUnit"), &UHrotDpsStation::OnOwnUnit);
	Sub(TEXT("DPS_Detonation"), &UHrotDpsStation::OnDetonation);
	Sub(TEXT("DPS_ManDowned"), &UHrotDpsStation::OnManDowned);
	UE_LOG(LogHrotDpsStation, Display, TEXT("player station: client %llx"), ClientId);
}

void UHrotDpsStation::Deinitialize()
{
	if (GEngine)
		if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>())
			for (const TPair<FString, int32>& S : Subs) Dps->Client().Unsubscribe(S.Key, S.Value);
	Subs.Reset();
	Super::Deinitialize();
}

void UHrotDpsStation::Join(int32 UnitId)
{
	Connect();
	SimNetGateway_Protocol_DdsPlayer_JoinRequest J = {};
	J.RequestSeq = ++RequestSeq;
	J.Kind = SimNetGateway_Protocol_DdsPlayer_JoinKindUnit;
	J.UnitId = UnitId;
	J.ClientId = ClientId;
	Requested = UnitId;
	if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()) Dps->Client().Write(TEXT("DPS_JoinRequest"), &J);
	UE_LOG(LogHrotDpsStation, Display, TEXT("join: unit %d (request %u)"), UnitId, RequestSeq);
}

void UHrotDpsStation::Leave()
{
	SimNetGateway_Protocol_DdsPlayer_Leave L = {};
	L.Seq = ++LeaveSeq;
	L.ClientId = ClientId;
	Requested = 0;
	if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()) Dps->Client().Write(TEXT("DPS_Leave"), &L);
	UE_LOG(LogHrotDpsStation, Display, TEXT("leave"));
}

void UHrotDpsStation::Send(const FHrotDpsControl& F)
{
	SimNetGateway_Protocol_DdsPlayer_ControlFrame C = {};
	C.Seq = ++Seq;
	C.Forward = F.Forward; C.Lateral = F.Lateral; C.Steer = F.Steer; C.Yaw = F.Yaw; C.Vertical = F.Vertical;
	// the old bytes and the new enums both (the guide: the old Gait byte's 0 is RUN; Stance byte = stance + 1)
	C.Gait = F.bRun ? 0 : 1;
	C.GaitMode = F.bRun ? SimNetGateway_Protocol_DdsPlayer_GaitRun : SimNetGateway_Protocol_DdsPlayer_GaitWalk;
	C.Stance = F.Stance;
	C.StanceWanted = F.Stance == 0 ? SimNetGateway_Protocol_DdsPlayer_StanceRequestNone
	               : F.Stance == 1 ? SimNetGateway_Protocol_DdsPlayer_StanceRequestStanding
	               : F.Stance == 2 ? SimNetGateway_Protocol_DdsPlayer_StanceRequestCrouched : SimNetGateway_Protocol_DdsPlayer_StanceRequestProne;
	C.Trigger = F.bTrigger ? 1 : 0;
	C.Mount = F.Mount;
	C.AimPoint = { F.AimPoint.X, F.AimPoint.Y, F.AimPoint.Z };
	C.ShotSeq = F.ShotSeq; C.ShotTarget = F.ShotTarget; C.CancelSeq = F.CancelSeq; C.AmmoSeq = F.AmmoSeq;
	C.UseSeq = F.UseSeq; C.UseVerb = 0; C.UseTarget = F.UseTarget; C.Verb = SimNetGateway_Protocol_DdsPlayer_UseVerb(F.Verb);
	C.Lean = F.Lean; C.Binoculars = F.bBinoculars ? 1 : 0; C.FirePref = F.FirePref;
	C.LookFrom = { F.LookFrom.X, F.LookFrom.Y, F.LookFrom.Z };
	C.LookDir = { float(F.LookDir.X), float(F.LookDir.Y), float(F.LookDir.Z) };
	C.ClientId = ClientId;
	if (UHrotDpsSubsystem* Dps = GEngine->GetEngineSubsystem<UHrotDpsSubsystem>()) Dps->Client().Write(TEXT("DPS_ControlFrame"), &C);
}

FString UHrotDpsStation::SlotState() const { return bSlot ? StateName(State) : TEXT("(no slot)"); }

bool UHrotDpsStation::CanPossess(int32 UnitId, FString& Why) const
{
	if (!bSlot) { Why = TEXT("no player slot on this station"); return false; }
	for (const FOffer& O : Offers)
		if (O.Id == UnitId) { if (O.bFree) return true; Why = TEXT("taken by another player"); return false; }
	Why = TEXT("not offered to this station");
	if (!SaidRefused.Contains(UnitId)) { SaidRefused.Add(UnitId); UE_LOG(LogHrotDpsStation, Display, TEXT("possess %d refused: not among the %d offers"), UnitId, Offers.Num()); }
	return false;
}

void UHrotDpsStation::OnSlot(const FHrotDpsSample& S)
{
	if (!S.bValid) { if (!S.bAlive) bSlot = false; return; }
	const FSlotS& D = *static_cast<const FSlotS*>(S.Data);
	const uint8 Was = State;
	bSlot = true; State = uint8(D.State); SlotUnit = D.UnitId; Reason = UTF8_TO_TCHAR(D.Reason);
	Offers.Reset();
	for (uint32 I = 0; I < D.Offers._length; ++I)
	{
		const auto& O = D.Offers._buffer[I];
		Offers.Add({ O.Id, UTF8_TO_TCHAR(O.Name), O.Free, uint8(O.Kind) });
	}
	if (State == 6 || State == 7 || State <= 2) Requested = 0;   // taken, dead, or back to choosing: the request is over
	if (Was != State) UE_LOG(LogHrotDpsStation, Display, TEXT("slot: %s unit %d (%d offers) %s"), StateName(State), SlotUnit, Offers.Num(), *Reason);
	if (Offers.Num() != LoggedOffers)   // live 2026-11-25: most soldiers' "Possess" was greyed - say what the station offers
	{
		LoggedOffers = Offers.Num();
		int32 Free = 0; FString Some;
		for (int32 I = 0; I < Offers.Num(); ++I) { if (Offers[I].bFree) ++Free; if (I < 12) Some += FString::Printf(TEXT(" %d '%s'%s"), Offers[I].Id, *Offers[I].Name, Offers[I].bFree ? TEXT("") : TEXT(" taken")); }
		UE_LOG(LogHrotDpsStation, Display, TEXT("offers: %d (%d free):%s"), Offers.Num(), Free, *Some);
	}
}

void UHrotDpsStation::OnOwnUnit(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FOwnS& D = *static_cast<const FOwnS*>(S.Data);
	if (LastHealth >= 0 && D.Health < LastHealth) PendingDamage += LastHealth - D.Health;   // the arc's thickness: what the hit took
	LastHealth = D.Health;
	OwnHealth = D.Health; OwnMaxHealth = D.MaxHealth; OwnActiveSlot = D.ActiveSlot; OwnFireMode = uint8(D.FireMode); OwnSpread = D.SpreadRad;
	{   // what HROT offers on the crosshair's target and how a use went - logged on change (the drive check, 2026-11-23)
		const FString Use = FString::Printf(TEXT("can %d target %d verb %d/%d '%s' seq %d outcome %d reason %d"), D.CanUse ? 1 : 0, D.UseTargetId, int32(D.Verb), int32(D.UseVerb),
			UTF8_TO_TCHAR(D.UseLabel), int32(D.UseSeq), int32(D.UseOutcome), int32(D.UseReason));
		// HROT's whole offered list for the target, in its order (gateway DD-029: OwnUnit.Actions) - the hold-F list's rows
		UseActions.Reset();
		FString List;
		for (uint32 I = 0; I < D.Actions._length; ++I)
		{
			const auto& A = D.Actions._buffer[I];
			UseActions.Add({ uint8(A.Verb), UTF8_TO_TCHAR(A.Label), A.Available, uint8(A.Reason), A.HoldOnly });
			List += FString::Printf(TEXT(" [%d '%s'%s%s]"), int32(A.Verb), UTF8_TO_TCHAR(A.Label), A.Available ? TEXT("") : *FString::Printf(TEXT(" refused %d"), int32(A.Reason)),
				A.HoldOnly ? TEXT(" hold") : TEXT(""));
		}
		const FString Now = Use + (List.IsEmpty() ? FString() : TEXT(" list") + List);
		if (Now != LastUseLine) { LastUseLine = Now; UE_LOG(LogHrotDpsStation, Display, TEXT("own unit %d use: %s"), D.UnitId, *Now); }
	}
	bCanUse = D.CanUse; UseLabel = UTF8_TO_TCHAR(D.UseLabel);
	OwnAmmo.Reset();
	for (uint32 I = 0; I < D.Ammo._length; ++I)
	{
		const auto& A = D.Ammo._buffer[I];
		FHrotDpsAmmoLine L;
		L.Slot = A.Slot; L.Weapon = UTF8_TO_TCHAR(A.Weapon); L.AmmoType = UTF8_TO_TCHAR(A.AmmoType); L.Rounds = A.Rounds; L.Max = A.Max;
		L.Magazine = A.Magazine; L.LoadedRounds = A.LoadedRounds; L.bBusy = A.Busy; L.bOverheated = A.Overheated; L.HeatPercent = A.HeatPercent;
		OwnAmmo.Add(L);
	}
	if (PendingDamage > 0 && Hits.Num()) { Hits.Last().Damage += PendingDamage; PendingDamage = 0; }
}

void UHrotDpsStation::OnDetonation(const FHrotDpsSample& S)
{
	if (!S.bValid || SlotUnit == 0) return;
	const FDetS& D = *static_cast<const FDetS*>(S.Data);
	if (D.TargetId == SlotUnit)
	{
		// HitFeedback.FromWhere: back along the round's travel, else toward the shooter
		FVector3d From(-D.Velocity.X, -D.Velocity.Y, -D.Velocity.Z);
		if (From.IsNearlyZero())
			if (UHrotDpsWorld* W = UHrotDpsWorld::Get(this))
			{
				FVector P; FQuat Q; int32 Dom;
				if (W->PoseNow(D.ShooterId, P, Q, Dom)) From = FVector3d(P) - FVector3d(D.Position.X, D.Position.Y, D.Position.Z);
			}
		if (!From.IsNearlyZero()) Hits.Add({ From, Now(), PendingDamage });
		PendingDamage = 0;
	}
	if (D.ShooterId == SlotUnit && D.TargetId != 0 && D.TargetId != SlotUnit) { MarkerAt = Now(); bMarkerKill = false; MarkerTarget = D.TargetId; }
}

void UHrotDpsStation::OnManDowned(const FHrotDpsSample& S)
{
	if (!S.bValid) return;
	const FDownS& D = *static_cast<const FDownS*>(S.Data);
	if (D.VictimId == MarkerTarget && MarkerAt >= 0 && Now() - MarkerAt < 1.0) bMarkerKill = true;   // the one we just hit went down: red
}

CSV_DECLARE_CATEGORY_EXTERN(HrotDps);

void UHrotDpsStation::Tick(float)
{
	CSV_SCOPED_TIMING_STAT(HrotDps, Station);
	const double T = Now();
	Hits.RemoveAll([T](const FHit& H) { return T - H.At > 2.0; });   // HitFeedback.ArcSeconds
}
