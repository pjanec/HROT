// CE-4651 (U-19h) - the pure part of "a shot SUMO car keeps its look": HROT takes a SUMO car over as an HROT entity with the SAME network id as the mover had,
// DIS 1.1.0.81.0.0.0 for every car kind, TKB TypeId by kind (UrbanCombatTkbCatalog.cs; TrafficPanic.cs). Such an entity is drawn from the mover's model family
// (car / van / bus) and, inside the family, by the SAME pick the mover used (id % count) - so the car does not change model when HROT takes it over.
#pragma once
#include "CoreMinimal.h"

namespace HrotDpsTrafficKit
{
	enum class EFamily : uint8 { None, Car, Van, Bus };

	/** the family of an HROT entity: None when its DIS does not start 1.1.0.81 (not a traffic car); else by TypeId (1051 DeliveryVan, 1053 BoxTruck -> Van; 1052 CityBus -> Bus;
	 *  1002 CivilianCar, 1050 Hatchback, 1054 Sedan, 1055 Estate, 1056 Suv and any unknown id -> Car) */
	inline EFamily FamilyOf(const FString& Dis, int32 TypeId)
	{
		if (!(Dis == TEXT("1.1.0.81") || Dis.StartsWith(TEXT("1.1.0.81.")))) return EFamily::None;
		
		if (TypeId == 1052) return EFamily::Bus;
		if (TypeId == 1051 || TypeId == 1053) return EFamily::Van;
		return EFamily::Car;
	}

	/** the index among a family's kits, the mover's own rule (key % count); -1 when the family has no kit */
	inline int32 Pick(int32 Id, int32 Count) { return Count > 0 ? int32(uint32(Id) % uint32(Count)) : -1; }
}
