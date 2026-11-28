// CE-4651 (U-19h) - the pure part of "a shot SUMO car keeps its look": HROT takes a SUMO car over as an HROT entity with the SAME network id as the mover had,
// its DIS by kind (SISO-REF-010, HROT CE-11042; before it 1.1.0.81.0.0.0 for every kind), TKB TypeId by kind (UrbanCombatTkbCatalog.cs; TrafficPanic.cs). Such an entity is drawn from the mover's model family
// (car / van / bus) and, inside the family, by the SAME pick the mover used (id % count) - so the car does not change model when HROT takes it over.
#pragma once
#include "CoreMinimal.h"

namespace HrotDpsTrafficKit
{
	enum class EFamily : uint8 { None, Car, Van, Bus };

	/** the family of an HROT entity from its DIS (kind 1 land platform, domain 1): category 81 Car, 82 Bus, 83 Single Unit Cargo Truck (a van / box truck)
	 *  - HROT ui CE-11042 (R-11020, NOTE_Ui_To_UnrealIg_Traffic_Dis_Types.md) gives every traffic kind its SISO-REF-010 type: 1002 1.1.0.81.10, 1054 .81.43,
	 *  1055 .81.63, 1050 .81.52, 1056 .81.113, 1051 1.1.0.83.74, 1053 .83.101, 1052 1.1.0.82.1. Any country. ⚠ HROT builds before CE-11042 sent 1.1.0.81.0.0.0
	 *  for every kind: there the TKB TypeId still decides (1051 / 1053 van, 1052 bus). None = not a civilian road vehicle (a HMMWV is 1.1.225.6). */
	inline EFamily FamilyOf(const FString& Dis, int32 TypeId)
	{
		TArray<FString> P; Dis.ParseIntoArray(P, TEXT("."));
		if (P.Num() < 4 || P[0] != TEXT("1") || P[1] != TEXT("1")) return EFamily::None;
		const FString& Cat = P[3];
		if (Cat == TEXT("82")) return EFamily::Bus;
		if (Cat == TEXT("83")) return EFamily::Van;
		if (Cat != TEXT("81")) return EFamily::None;
		const bool bGeneric = P.Num() < 5 || P[4] == TEXT("0");   // the pre-CE-11042 form: one DIS for every kind
		if (bGeneric && TypeId == 1052) return EFamily::Bus;
		if (bGeneric && (TypeId == 1051 || TypeId == 1053)) return EFamily::Van;
		return EFamily::Car;
	}

	/** the index among a family's kits, the mover's own rule (key % count); -1 when the family has no kit */
	inline int32 Pick(int32 Id, int32 Count) { return Count > 0 ? int32(uint32(Id) % uint32(Count)) : -1; }
}
