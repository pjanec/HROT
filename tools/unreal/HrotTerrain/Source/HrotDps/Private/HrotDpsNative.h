// CE-4651 (U-18a) — CycloneDDS C + the generated DPS types, included in ONE place (the DDS runtime headers pull in windows.h).
#pragma once

#include "CoreMinimal.h"

#if PLATFORM_WINDOWS
#include "Windows/AllowWindowsPlatformTypes.h"
#include "Windows/AllowWindowsPlatformAtomics.h"
#endif
THIRD_PARTY_INCLUDES_START
#include "dds/dds.h"
#include "DpsTopics.h"
THIRD_PARTY_INCLUDES_END
#if PLATFORM_WINDOWS
#include "Windows/HideWindowsPlatformAtomics.h"
#include "Windows/HideWindowsPlatformTypes.h"
#endif

/** one row of the generated topic table (Generated/DpsTopicTable.gen.inl) */
struct FHrotDpsTopicDef
{
	const TCHAR* Name;      // "DPS_EntityState"
	const TCHAR* Type;      // "EntityState"
	const dds_topic_descriptor_t* Desc;
	size_t Size;
	bool bTransientLocal;
	bool bReliable;
	int32 Depth;
	int32 LeaseMs;          // > 0: a player topic (automatic liveliness)
};
