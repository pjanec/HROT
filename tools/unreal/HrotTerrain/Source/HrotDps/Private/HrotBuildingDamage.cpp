// U-20: building destruction - see HrotBuildingDamage.h and DRAFT_Unreal_Image_Generator.md "U-20 Building destruction in Unreal".
#include "HrotBuildingDamage.h"

#include "Components/DynamicMeshComponent.h"
#include "Dom/JsonObject.h"
#include "DynamicMesh/DynamicMesh3.h"
#include "DynamicMesh/DynamicMeshAttributeSet.h"
#include "Engine/StaticMesh.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "GeometryScript/MeshBooleanFunctions.h"
#include "GeometryScript/MeshPrimitiveFunctions.h"
#include "GeometryScript/MeshNormalsFunctions.h"
#include "GeometryScript/MeshUVFunctions.h"
#include "HrotExplosionFx.h"
#include "HrotSoundscape.h"
#include "Sound/SoundBase.h"
#include "HrotRuntimeLibrary.h"
#include "Kismet/GameplayStatics.h"
#include "Components/DecalComponent.h"
#include "Materials/MaterialInterface.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Engine/Texture.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "StaticMeshResources.h"
#include "UDynamicMesh.h"

DEFINE_LOG_CATEGORY_STATIC(LogHrotBuildings, Log, All);

using namespace UE::Geometry;

namespace
{
	constexpr int32 SlotGlass = 5;   // scripts/terrain_look.py SLOT_GLASS
	FVector Ue(double X, double Y, double Z) { return FVector(X * 100.0, -Y * 100.0, Z * 100.0); }   // HROT local m -> Unreal cm
	int32 HouseOf(const FVector2f& Uv1) { return FMath::RoundToInt(Uv1.X); }

	/** the tile's LOD0 render data (CPU access, U-20 BuildMesh) as a dynamic mesh: triangles whose house passes Keep, with UV0, UV1 (the ids),
	 *  normals and material ids. Returns false when the mesh has no CPU data or no id channel. */
	bool ToDynamic(const UStaticMesh* SM, TFunctionRef<bool(int32)> Keep, FDynamicMesh3& Out, int32* MostMaterial = nullptr)
	{
		if (!SM || !SM->GetRenderData() || SM->GetRenderData()->LODResources.Num() == 0) return false;
		const FStaticMeshLODResources& L = SM->GetRenderData()->LODResources[0];
		const FPositionVertexBuffer& PB = L.VertexBuffers.PositionVertexBuffer;
		const FStaticMeshVertexBuffer& VB = L.VertexBuffers.StaticMeshVertexBuffer;
		if (VB.GetNumTexCoords() < 2 || PB.GetNumVertices() == 0) return false;
		TArray<uint32> Idx; L.IndexBuffer.GetCopy(Idx);
		if (Idx.Num() == 0) return false;
		Out.Clear();
		Out.EnableAttributes(); Out.Attributes()->SetNumUVLayers(2); Out.Attributes()->EnableMaterialID();
		FDynamicMeshUVOverlay* U0 = Out.Attributes()->GetUVLayer(0);
		FDynamicMeshUVOverlay* U1 = Out.Attributes()->GetUVLayer(1);
		FDynamicMeshNormalOverlay* N = Out.Attributes()->PrimaryNormals();
		FDynamicMeshMaterialAttribute* Mat = Out.Attributes()->GetMaterialID();
		TMap<uint32, int32> VMap;
		TMap<int32, int32> MatCount;
		for (const FStaticMeshSection& S : L.Sections)
			for (uint32 T = 0; T < S.NumTriangles; ++T)
			{
				const uint32 I[3] = { Idx[S.FirstIndex + T * 3], Idx[S.FirstIndex + T * 3 + 1], Idx[S.FirstIndex + T * 3 + 2] };
				if (!Keep(HouseOf(VB.GetVertexUV(I[0], 1)))) continue;
				int32 V[3];
				for (int32 K = 0; K < 3; ++K)
				{
					if (const int32* Found = VMap.Find(I[K])) { V[K] = *Found; continue; }
					V[K] = Out.AppendVertex(FVector3d(PB.VertexPosition(I[K]))); VMap.Add(I[K], V[K]);
				}
				int32 Tid = Out.AppendTriangle(V[0], V[1], V[2]);
				if (Tid < 0)   // a non-manifold edge (overlapping panels): this triangle gets its own vertices
				{
					for (int32 K = 0; K < 3; ++K) V[K] = Out.AppendVertex(FVector3d(PB.VertexPosition(I[K])));
					Tid = Out.AppendTriangle(V[0], V[1], V[2]);
					if (Tid < 0) continue;
				}
				int32 E0[3], E1[3], En[3];
				for (int32 K = 0; K < 3; ++K)
				{
					E0[K] = U0->AppendElement(VB.GetVertexUV(I[K], 0)); E1[K] = U1->AppendElement(VB.GetVertexUV(I[K], 1));
					En[K] = N->AppendElement(FVector3f(VB.VertexTangentZ(I[K])));
				}
				U0->SetTriangle(Tid, FIndex3i(E0[0], E0[1], E0[2])); U1->SetTriangle(Tid, FIndex3i(E1[0], E1[1], E1[2]));
				N->SetTriangle(Tid, FIndex3i(En[0], En[1], En[2]));
				Mat->SetValue(Tid, S.MaterialIndex);
				if (S.MaterialIndex != SlotGlass) MatCount.FindOrAdd(S.MaterialIndex)++;
			}
		if (MostMaterial) { int32 Best = -1; for (const TPair<int32, int32>& P : MatCount) if (P.Value > Best) { Best = P.Value; *MostMaterial = P.Key; } }
		return true;
	}

	AActor* SpawnDynamic(UWorld* World, const FTransform& At, FDynamicMesh3&& Mesh, const UStaticMesh* Materials, const TCHAR* Name)
	{
		FActorSpawnParameters P; P.ObjectFlags |= RF_Transient;
		AActor* A = World->SpawnActor<AActor>(AActor::StaticClass(), At, P);
		if (!A) return nullptr;
		UDynamicMeshComponent* C = NewObject<UDynamicMeshComponent>(A, Name);
		A->SetRootComponent(C);
		C->SetMobility(EComponentMobility::Movable);
		C->RegisterComponent();
		C->SetWorldTransform(At);
		C->SetMesh(MoveTemp(Mesh));
		if (Materials)
		{
			const TArray<FStaticMaterial>& M = Materials->GetStaticMaterials();
			for (int32 I = 0; I < M.Num(); ++I) C->SetMaterial(I, M[I].MaterialInterface);
		}
		C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
		return A;
	}

	UDynamicMeshComponent* DynOf(AActor* A) { return A ? Cast<UDynamicMeshComponent>(A->GetRootComponent()) : nullptr; }
}

namespace
{
	// U-20 glass sounds (the user: "the sound of glass shattering or the bullet coming through the glass ... and please preload them"):
	// a round through a pane = HROT's own glass hit (hit/glass, preloaded with the library); a pane shattering = Lyra's falling-glass set
	USoundBase* GlassShatter(int32 I)
	{
		static USoundBase* S[7] = {};
		const int32 K = FMath::Abs(I) % 7;
		if (!S[K]) S[K] = HrotLoadRooted<USoundBase>(*FString::Printf(TEXT("/Game/Audio/Sounds/Impacts/Lyra_ImpactGlassDebris_%02d.Lyra_ImpactGlassDebris_%02d"), K + 1, K + 1));
		return S[K];
	}
	void PlayGlass(UWorld* World, USoundBase* S, const FVector& At, float Volume)
	{
		if (!S || !World) return;
		UHrotSoundscape* Snd = UHrotSoundscape::Get(World);
		UGameplayStatics::PlaySoundAtLocation(World, S, At, Volume, FMath::FRandRange(0.94f, 1.06f), 0.f, Snd ? Snd->Falloff(4.f, 160.f) : nullptr);
	}
}

int32 UHrotBuildingDamage::Preload()
{
	int32 N = 0;
	for (int32 I = 0; I < 7; ++I) N += GlassShatter(I) ? 1 : 0;
	for (int32 I = 0; I < 4; ++I) N += HrotLoadRooted<UTexture>(*FString::Printf(TEXT("/Game/Hrot/Fx/Cracks/T_HrotGlassCrack_%d.T_HrotGlassCrack_%d"), I, I)) ? 1 : 0;
	for (const TCHAR* P : { TEXT("/Game/Hrot/Fx/M_HrotGlassCrack.M_HrotGlassCrack"), TEXT("/Game/Hrot/Fx/M_HrotWallScorch.M_HrotWallScorch") })
		N += HrotLoadRooted<UMaterialInterface>(P) ? 1 : 0;
	for (const TCHAR* P : { TEXT("/Engine/BasicShapes/Plane.Plane"), TEXT("/Engine/BasicShapes/Cube.Cube") }) N += HrotLoadRooted<UStaticMesh>(P) ? 1 : 0;
	UE_LOG(LogHrotBuildings, Display, TEXT("buildings: %d damage assets preloaded"), N);
	return N;
}

void UHrotBuildingDamage::UseTerrain(UObject* WorldContext, const FString& Terrain)
{
	LastWorld = WorldContext ? WorldContext->GetWorld() : nullptr;
	LastTerrain = Terrain;
}

bool UHrotBuildingDamage::LoadIndex(const FString& Terrain)
{
	if (IndexTerrain == Terrain) return Index.Num() > 0;
	IndexTerrain = Terrain; Index.Reset(); Tiles.Reset(); Houses.Reset();
	const FString Path = FPaths::Combine(FPaths::ProjectDir(), TEXT("Data"), Terrain, Terrain + TEXT(".buildings-index.json"));
	FString Text;
	if (!FFileHelper::LoadFileToString(Text, *Path)) { UE_LOG(LogHrotBuildings, Warning, TEXT("buildings: no index %s - damage is not drawn"), *Path); return false; }
	TSharedPtr<FJsonObject> Root;
	if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Root) || !Root.IsValid()) return false;
	const TSharedPtr<FJsonObject>* B = nullptr;
	if (!Root->TryGetObjectField(TEXT("buildings"), B)) return false;
	for (const TPair<FString, TSharedPtr<FJsonValue>>& It : (*B)->Values)
	{
		const TSharedPtr<FJsonObject> O = It.Value->AsObject();
		FIndexed H;
		const TArray<TSharedPtr<FJsonValue>>& T = O->GetArrayField(TEXT("tile"));
		H.Tile = FIntPoint(int32(T[0]->AsNumber()), int32(T[1]->AsNumber()));
		H.Index = int32(O->GetNumberField(TEXT("index")));
		H.bPanels = O->GetBoolField(TEXT("panels"));
		H.Base = float(O->GetNumberField(TEXT("base"))); H.Top = float(O->GetNumberField(TEXT("top")));
		for (const TSharedPtr<FJsonValue>& P : O->GetArrayField(TEXT("footprint")))
		{
			const TArray<TSharedPtr<FJsonValue>>& Q = P->AsArray(); H.Footprint.Add(FVector2D(Q[0]->AsNumber(), Q[1]->AsNumber()));
		}
		const TArray<TSharedPtr<FJsonValue>>* Walls = nullptr;
		if (O->TryGetArrayField(TEXT("walls"), Walls))
			for (const TSharedPtr<FJsonValue>& W : *Walls)
			{
				const TArray<TSharedPtr<FJsonValue>>& Q = W->AsArray();
				FWall Wl; Wl.A = FVector2D(Q[0]->AsNumber(), Q[1]->AsNumber()); Wl.B = FVector2D(Q[2]->AsNumber(), Q[3]->AsNumber()); Wl.Thickness = float(Q[4]->AsNumber());
				if (Q.Num() >= 8)
				{
					Wl.Base = float(Q[5]->AsNumber()); Wl.Top = float(Q[6]->AsNumber());
					for (const TSharedPtr<FJsonValue>& Ov : Q[7]->AsArray())
					{
						const TArray<TSharedPtr<FJsonValue>>& Oa = Ov->AsArray();
						Wl.Openings.Add({ float(Oa[0]->AsNumber()), float(Oa[1]->AsNumber()), float(Oa[2]->AsNumber()), float(Oa[3]->AsNumber()), Oa[4]->AsNumber() > 0 });
					}
				}
				H.Walls.Add(MoveTemp(Wl));
			}
		Index.Add(It.Key, MoveTemp(H));
	}
	UE_LOG(LogHrotBuildings, Display, TEXT("buildings: %d houses indexed for %s"), Index.Num(), *Terrain);
	return Index.Num() > 0;
}

UHrotBuildingDamage::FTile* UHrotBuildingDamage::TileOf(UWorld* World, const FString& Terrain, FIntPoint T)
{
	FTile& Tile = Tiles.FindOrAdd(T);
	if (Tile.Static.IsValid()) return &Tile;
	const FString Want = FString::Printf(TEXT("SM_%s_%d_%d"), *Terrain.Replace(TEXT("-"), TEXT("_")), T.X, T.Y);
	for (TActorIterator<AStaticMeshActor> It(World); It; ++It)
		if (const UStaticMesh* SM = It->GetStaticMeshComponent()->GetStaticMesh(); SM && SM->GetName() == Want) { Tile.Static = *It; return &Tile; }
	UE_LOG(LogHrotBuildings, Warning, TEXT("buildings: no tile mesh %s in the level"), *Want);
	return nullptr;
}

void UHrotBuildingDamage::RebuildTile(UWorld* World, FTile& Tile)
{
	AStaticMeshActor* S = Cast<AStaticMeshActor>(Tile.Static.Get());
	if (!S) return;
	const UStaticMesh* SM = S->GetStaticMeshComponent()->GetStaticMesh();
	FDynamicMesh3 M;
	const TSet<int32>& Out = Tile.Out;
	if (!ToDynamic(SM, [&](int32 H) { return !Out.Contains(H); }, M)) { UE_LOG(LogHrotBuildings, Warning, TEXT("buildings: %s has no CPU data / ids"), *SM->GetName()); return; }
	if (AActor* Old = Tile.Dynamic.Get()) { if (UDynamicMeshComponent* C = DynOf(Old)) { C->SetMesh(MoveTemp(M)); return; } }
	Tile.Dynamic = SpawnDynamic(World, S->GetActorTransform(), MoveTemp(M), SM, TEXT("HrotTileRest"));
	S->SetActorHiddenInGame(true);
	S->GetStaticMeshComponent()->SetVisibility(false);
}

UHrotBuildingDamage::FHouse* UHrotBuildingDamage::TakeOut(UWorld* World, const FString& Terrain, const FString& Key, const FIndexed& H)
{
	if (FHouse* Have = Houses.Find(Key)) return Have;
	FTile* Tile = TileOf(World, Terrain, H.Tile);
	AStaticMeshActor* S = Tile ? Cast<AStaticMeshActor>(Tile->Static.Get()) : nullptr;
	if (!S) return nullptr;
	const UStaticMesh* SM = S->GetStaticMeshComponent()->GetStaticMesh();
	FDynamicMesh3 M; int32 Wall = 0;
	if (!ToDynamic(SM, [&](int32 X) { return X == H.Index; }, M, &Wall) || M.TriangleCount() == 0) return nullptr;
	FHouse& House = Houses.Add(Key);
	House.WallMaterial = Wall;
	House.WallMat = SM->GetMaterial(Wall);
	House.GlassMat = SM->GetMaterial(SlotGlass);
	House.TileOrigin = S->GetActorLocation();
	House.Actor = SpawnDynamic(World, S->GetActorTransform(), MoveTemp(M), SM, TEXT("HrotHouse"));
	Tile->Out.Add(H.Index);
	RebuildTile(World, *Tile);
	UE_LOG(LogHrotBuildings, Display, TEXT("buildings: %s taken out of tile %d,%d (house %d)"), *Key, H.Tile.X, H.Tile.Y, H.Index);
	return &House;
}

void UHrotBuildingDamage::Cut(FHouse& House, const FIndexed& H, const FHrotBreachIn& B, bool bLive, UWorld* World)
{
	UDynamicMeshComponent* C = DynOf(House.Actor.Get());
	if (!C || !H.Walls.IsValidIndex(B.Panel)) return;
	const FWall& W = H.Walls[B.Panel];
	const FVector A = Ue(W.A.X, W.A.Y, 0), Bp = Ue(W.B.X, W.B.Y, 0);
	const FVector Dir = (Bp - A).GetSafeNormal2D();
	const float Mid = (B.From + B.To) * 0.5f;
	const FVector Centre = A + Dir * (Mid * 100.0) + FVector(0, 0, (B.Bottom + B.Top) * 50.0) - House.TileOrigin;   // tile-local cm
	// a RAGGED outline (the breach's rectangle, its edge jittered - seeded by the breach, so every client cuts the same), extruded through the wall
	UDynamicMesh* Tool = NewObject<UDynamicMesh>(this);
	const float HalfW = FMath::Max(B.To - B.From, 0.1f) * 50.f, HalfH = FMath::Max(B.Top - B.Bottom, 0.1f) * 50.f;
	const bool bFloor = B.Bottom <= H.Base + 0.05f;   // a passage from the floor keeps a straight bottom
	FRandomStream Rand(GetTypeHash(FString::Printf(TEXT("%d:%.2f:%.2f"), B.Panel, B.From, B.Bottom)));
	TArray<FVector2D> Poly;
	const int32 Steps = 6;
	const float J = FMath::Min(HalfW, HalfH) * 0.18f;
	auto Edge = [&](FVector2D P0, FVector2D P1, bool bStraight)
	{
		for (int32 K = 0; K < Steps; ++K)
		{
			const FVector2D P = FMath::Lerp(P0, P1, float(K) / Steps);
			const FVector2D Nrm = FVector2D(P1.Y - P0.Y, P0.X - P1.X).GetSafeNormal();   // outward for a counter-clockwise outline
			Poly.Add(bStraight || K == 0 ? P : P + Nrm * Rand.FRandRange(-J, J));
		}
	};
	Edge(FVector2D(-HalfW, -HalfH), FVector2D(HalfW, -HalfH), bFloor);
	Edge(FVector2D(HalfW, -HalfH), FVector2D(HalfW, HalfH), false);
	Edge(FVector2D(HalfW, HalfH), FVector2D(-HalfW, HalfH), false);
	Edge(FVector2D(-HalfW, HalfH), FVector2D(-HalfW, -HalfH), false);
	const FVector Normal = FVector::CrossProduct(Dir, FVector::UpVector).GetSafeNormal();
	const FRotator Rot = FRotationMatrix::MakeFromXZ(Dir, Normal).Rotator();   // local X along the wall, Z through it, Y up
	const float Depth = (W.Thickness + 0.4f) * 100.f;
	UGeometryScriptLibrary_MeshPrimitiveFunctions::AppendSimpleExtrudePolygon(Tool, FGeometryScriptPrimitiveOptions(),
		FTransform(Rot, Centre - Normal * (Depth * 0.5f)), Poly, Depth, 0, true, EGeometryScriptPrimitiveOriginMode::Base);
	// TrimInside: our walls are open shells (two faces per panel), so a Subtract's inside / outside is not defined - trim removes what the hole covers
	UGeometryScriptLibrary_MeshBooleanFunctions::ApplyMeshBoolean(C->GetDynamicMesh(), FTransform::Identity, Tool, FTransform::Identity,
		EGeometryScriptBooleanOperation::TrimInside, FGeometryScriptMeshBooleanOptions());
	Tool->MarkAsGarbage();
	// the user: "scorched sides if the hole comes from explosion (and not from bulldozer)" - one decal through the wall (both faces), kept with the
	// house. ⏳ HROT does not yet say what made a breach (a ram / blade reports the hull's centre as its Blast) - asked; until then every hole is scorched
	static UMaterialInterface* Scorch = HrotLoadRooted<UMaterialInterface>(TEXT("/Game/Hrot/Fx/M_HrotWallScorch.M_HrotWallScorch"));
	if (Scorch && B.bExplosion)   // M_HrotWallScorch is darkest inside ~0.45 of its radius: sized so that band lies on the hole's edge
	{
		const float R = FMath::Max(HalfW, HalfH) * 2.2f;
		UGameplayStatics::SpawnDecalAtLocation(World, Scorch, FVector((W.Thickness * 100.f) * 0.5f + 25.f, R, R), Centre + House.TileOrigin,
			FRotator((-Normal).Rotation().Pitch, (-Normal).Rotation().Yaw, Rand.FRandRange(0.f, 360.f)), 0.f);
	}
	if (bLive)
	{
		// away from the blast: the side of the wall the blast was on (Blast in HROT's frame; zero = unknown -> the wall's +normal)
		FVector Away = Normal;
		if (!B.Blast.IsNearlyZero())
		{
			const FVector BlastW = Ue(B.Blast.X, B.Blast.Y, B.Blast.Z);
			if (FVector::DotProduct(BlastW - (Centre + House.TileOrigin), Normal) > 0) Away = -Normal;
		}
		const float Area = (HalfW * HalfH * 4.f) / 10000.f;
		SpawnDebris(World, Centre + House.TileOrigin, Away, FMath::Clamp(int32(Area * 14.f), 10, 40), 6.f, 22.f, 900.f, House.WallMat.Get(), HalfW);
	}
}

bool UHrotBuildingDamage::Pane(const FIndexed& H, const FHouse& House, int32 Panel, int32 Opening, FVector& Corner, FVector& Along, FVector& Up, FVector& Normal, float& Wd, float& Ht) const
{
	if (!H.Walls.IsValidIndex(Panel) || !H.Walls[Panel].Openings.IsValidIndex(Opening)) return false;
	const FWall& W = H.Walls[Panel]; const FOpening& O = W.Openings[Opening];
	const FVector A = Ue(W.A.X, W.A.Y, 0), B = Ue(W.B.X, W.B.Y, 0);
	Along = (B - A).GetSafeNormal2D(); Up = FVector::UpVector; Normal = FVector::CrossProduct(Along, Up).GetSafeNormal();
	const float F = 6.f;   // the frame (terrain_look.py build_panels: 0.06 m)
	Corner = A + Along * (O.At * 100.f + F) + FVector(0, 0, O.Sill * 100.f + F);
	Wd = O.Width * 100.f - 2 * F; Ht = (O.Head - O.Sill) * 100.f - 2 * F;
	return Wd > 1.f && Ht > 1.f;
}

void UHrotBuildingDamage::Crack(FHouse& House, const FIndexed& H, const FHrotWindowIn& Wd, int32 Which, UWorld* World, bool bLive)
{
	static UMaterialInterface* CrackMat = HrotLoadRooted<UMaterialInterface>(TEXT("/Game/Hrot/Fx/M_HrotGlassCrack.M_HrotGlassCrack"));
	static UStaticMesh* Plane = HrotLoadRooted<UStaticMesh>(TEXT("/Engine/BasicShapes/Plane.Plane"));
	FVector Corner, Along, Up, Normal; float W, Ht;
	if (!CrackMat || !Plane || !Pane(H, House, Wd.Panel, Wd.Opening, Corner, Along, Up, Normal, W, Ht)) return;
	const float U = (Which == 0 ? Wd.U0 : Wd.U1) / 255.f, V = (Which == 0 ? Wd.V0 : Wd.V1) / 255.f;
	// a bullet hole (hrot_glass_crack_tex.py): one of four variants, turned at random; its cracks reach the quad's edge - sized to the nearer frame
	const float Pu = U * W, Pv = V * Ht;
	const float Size = FMath::Clamp(2.f * FMath::Min(FMath::Min(Pu, W - Pu), FMath::Min(Pv, Ht - Pv)) + 8.f, 14.f, 60.f) / 100.f;
	FRandomStream Pick(int32(Wd.Panel * 7919 + Wd.Opening * 131 + Which * 17 + H.Index));
	static UTexture* Tex[4] = { nullptr, nullptr, nullptr, nullptr };
	for (int32 I = 0; I < 4; ++I) if (!Tex[I]) Tex[I] = HrotLoadRooted<UTexture>(*FString::Printf(TEXT("/Game/Hrot/Fx/Cracks/T_HrotGlassCrack_%d.T_HrotGlassCrack_%d"), I, I));
	UMaterialInstanceDynamic* Mi = UMaterialInstanceDynamic::Create(CrackMat, this);
	if (UTexture* T = Tex[Pick.RandRange(0, 3)]) Mi->SetTextureParameterValue(TEXT("Crack"), T);
	const FVector Spin = Along.RotateAngleAxis(Pick.FRandRange(0.f, 360.f), Normal);
	if (bLive)   // the round through the pane
		if (UHrotSoundscape* Snd = UHrotSoundscape::Get(World)) PlayGlass(World, Snd->Pick(TEXT("hit/glass")), Corner + Along * Pu + Up * Pv, 0.9f);
	// measured: one quad on the inner face sat BEHIND the translucent pane and only its tips showed - one on each face, drawn after the glass
	for (const float Side : { 1.f, -1.f })
	{
		const FVector At = Corner + Along * (U * W) + Up * (V * Ht) + Normal * (0.8f * Side);
		FActorSpawnParameters SP; SP.ObjectFlags |= RF_Transient;
		AStaticMeshActor* A = World->SpawnActor<AStaticMeshActor>(AStaticMeshActor::StaticClass(), FTransform(FRotationMatrix::MakeFromZX(Normal * Side, Spin).Rotator(), At, FVector(Size)), SP);
		if (!A) continue;
		UStaticMeshComponent* C = A->GetStaticMeshComponent();
		C->SetMobility(EComponentMobility::Movable);
		C->SetStaticMesh(Plane);
		C->SetMaterial(0, Mi);
		C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
		C->SetCastShadow(false);
		C->SetTranslucentSortPriority(10);
		House.Cracks.Add(int64(Wd.Panel) * 100000 + Wd.Opening, A);
	}
}

void UHrotBuildingDamage::BreakGlass(FHouse& House, const FIndexed& H, int32 Panel, int32 Opening, bool bLive, UWorld* World)
{
	UDynamicMeshComponent* C = DynOf(House.Actor.Get());
	if (!C) return;
	const int32 Part = Panel * 256 + Opening + 1;
	int32 Removed = 0;
	C->GetDynamicMesh()->EditMesh([&](FDynamicMesh3& M)
	{
		const FDynamicMeshUVOverlay* U1 = M.Attributes()->GetUVLayer(1);
		const FDynamicMeshMaterialAttribute* Mat = M.Attributes()->GetMaterialID();
		TArray<int32> Doomed;
		for (int32 Tid : M.TriangleIndicesItr())
		{
			if (Mat->GetValue(Tid) != SlotGlass || !U1->IsSetTriangle(Tid)) continue;
			if (FMath::RoundToInt(U1->GetElement(U1->GetTriangle(Tid).A).Y) == Part) Doomed.Add(Tid);
		}
		for (int32 Tid : Doomed) if (M.RemoveTriangle(Tid) == EMeshResult::Ok) ++Removed;
	});
	UE_LOG(LogHrotBuildings, Display, TEXT("buildings: window %d/%d broken (%d glass triangles)"), Panel, Opening, Removed);
	const int64 Id = int64(Panel) * 100000 + Opening;
	TArray<TWeakObjectPtr<AActor>> Old; House.Cracks.MultiFind(Id, Old); for (const TWeakObjectPtr<AActor>& A : Old) if (A.IsValid()) A->Destroy();
	House.Cracks.Remove(Id);
	FVector Corner, Along, Up, Normal; float W, Ht;
	if (!Pane(H, House, Panel, Opening, Corner, Along, Up, Normal, W, Ht)) return;
	// shards left in the frame (HROT's own look: an open hole with shards left in the frame): jagged triangles from each edge inward
	FDynamicMesh3 M; M.EnableAttributes(); M.Attributes()->EnableMaterialID();
	FRandomStream R(int32(Id) * 31 + H.Index);
	auto L = [&](float U, float V) { return FVector3d(Corner + Along * U + Up * V - House.TileOrigin); };
	const FVector2f Edges[4][2] = { { {0, 0}, {1, 0} }, { {1, 0}, {1, 1} }, { {1, 1}, {0, 1} }, { {0, 1}, {0, 0} } };
	for (const auto& E : Edges)
	{
		const int32 N = R.RandRange(1, 3);
		for (int32 K = 0; K < N; ++K)
		{
			const float S0 = R.FRandRange(0.f, 0.7f), S1 = FMath::Min(S0 + R.FRandRange(0.15f, 0.35f), 1.f), Sm = (S0 + S1) * 0.5f;
			const FVector2f P0 = FMath::Lerp(E[0], E[1], S0), P1 = FMath::Lerp(E[0], E[1], S1), Pm = FMath::Lerp(E[0], E[1], Sm);
			const FVector2f In = FVector2f(0.5f, 0.5f) - Pm;
			const FVector2f Tip = Pm + In * R.FRandRange(0.25f, 0.7f);
			const int32 Va = M.AppendVertex(L(P0.X * W, P0.Y * Ht)), Vb = M.AppendVertex(L(P1.X * W, P1.Y * Ht)), Vc = M.AppendVertex(L(Tip.X * W, Tip.Y * Ht));
			const int32 Tid = M.AppendTriangle(Va, Vb, Vc);
			if (Tid >= 0) M.Attributes()->GetMaterialID()->SetValue(Tid, 0);
		}
	}
	if (AActor* Sh = SpawnDynamic(World, FTransform(House.TileOrigin), MoveTemp(M), nullptr, TEXT("HrotShards")))
		if (UDynamicMeshComponent* Sc = DynOf(Sh))
		{
			UGeometryScriptLibrary_MeshNormalsFunctions::RecomputeNormals(Sc->GetDynamicMesh(), FGeometryScriptCalculateNormalsOptions());
			Sc->SetMaterial(0, House.GlassMat.Get());
		}
	if (bLive)   // the pane shattering: its glass falling out
	{
		const FVector Mid = Corner + Along * (W * 0.5f) + Up * (Ht * 0.5f);
		PlayGlass(World, GlassShatter(R.RandRange(0, 6)), Mid, 1.0f);
		if (UHrotSoundscape* Snd = UHrotSoundscape::Get(World)) PlayGlass(World, Snd->Pick(TEXT("hit/glass")), Mid, 0.8f);
	}
	if (bLive)   // the burst: glass flies out of the frame and fades like the wall debris
		SpawnDebris(World, Corner + Along * (W * 0.5f) + Up * (Ht * 0.5f), Normal * (R.FRand() < 0.5f ? 1.f : -1.f), 18, 3.f, 9.f, 450.f, House.GlassMat.Get(),
			FMath::Min(W, Ht) * 0.4f, true);
}

void UHrotBuildingDamage::Collapse(UWorld* World, FHouse& House, const FIndexed& H, bool bLive)
{
	House.bCollapsed = true;
	const UStaticMesh* SM = nullptr;
	if (FTile* T = Tiles.Find(H.Tile)) if (AStaticMeshActor* S = Cast<AStaticMeshActor>(T->Static.Get())) SM = S->GetStaticMeshComponent()->GetStaticMesh();
	if (AActor* A = House.Actor.Get()) A->Destroy();
	if (H.Footprint.Num() < 3) return;
	// HROT's own rubble rule (RubbleMound): a heap whose top is 55 % of the footprint, blocks on it
	FVector2D C2(0, 0); for (const FVector2D& P : H.Footprint) C2 += P; C2 /= H.Footprint.Num();
	const float Height = FMath::Clamp((H.Top - H.Base) * 0.4f + 0.6f, 1.2f, 5.0f);
	auto L = [&](double X, double Y, double Z) { return FVector3d(Ue(X, Y, Z) - House.TileOrigin); };
	const int32 N = H.Footprint.Num();
	// the heap's height at a point: rising over a 2.5 m ramp from the footprint's edge (signed distance, inside > 0) to Height, rough
	auto Inside = [&](const FVector2D& P)
	{
		bool In = false;
		for (int32 I = 0, J = N - 1; I < N; J = I++)
			if (((H.Footprint[I].Y > P.Y) != (H.Footprint[J].Y > P.Y)) &&
				(P.X < (H.Footprint[J].X - H.Footprint[I].X) * (P.Y - H.Footprint[I].Y) / (H.Footprint[J].Y - H.Footprint[I].Y) + H.Footprint[I].X)) In = !In;
		return In;
	};
	auto EdgeDist = [&](const FVector2D& P)
	{
		double Best = TNumericLimits<double>::Max();
		for (int32 I = 0; I < N; ++I)
		{
			const FVector2D A = H.Footprint[I], B2 = H.Footprint[(I + 1) % N];
			const FVector2D AB = B2 - A; const double T = FMath::Clamp(FVector2D::DotProduct(P - A, AB) / FMath::Max(AB.SizeSquared(), 1e-6), 0.0, 1.0);
			Best = FMath::Min(Best, FVector2D::Distance(P, A + AB * T));
		}
		return Inside(P) ? Best : -Best;
	};
	auto HeapZ = [&](const FVector2D& P)
	{
		const double D = EdgeDist(P);
		if (D < -1.2) return double(H.Base) - 0.05;
		const double Ramp = FMath::Clamp((D + 1.2) / 3.7, 0.0, 1.0);
		const double Noise = 0.75 + 0.35 * FMath::PerlinNoise2D(FVector2D(P.X * 0.45, P.Y * 0.45)) + 0.15 * FMath::PerlinNoise2D(FVector2D(P.X * 1.7, P.Y * 1.7));
		return double(H.Base) + Height * FMath::Sqrt(Ramp) * Noise;
	};
	FBox2D Bb(H.Footprint); Bb = Bb.ExpandBy(1.5);
	const double Cell = 0.5;
	const int32 Nx = FMath::Clamp(int32(Bb.GetSize().X / Cell) + 1, 2, 200), Ny = FMath::Clamp(int32(Bb.GetSize().Y / Cell) + 1, 2, 200);
	FDynamicMesh3 M; M.EnableAttributes(); M.Attributes()->EnableMaterialID();
	TArray<int32> G; G.SetNum(Nx * Ny);
	for (int32 Y = 0; Y < Ny; ++Y) for (int32 X = 0; X < Nx; ++X)
	{
		const FVector2D P = Bb.Min + FVector2D(X * Bb.GetSize().X / (Nx - 1), Y * Bb.GetSize().Y / (Ny - 1));
		G[Y * Nx + X] = M.AppendVertex(L(P.X, P.Y, HeapZ(P)));
	}
	for (int32 Y = 0; Y + 1 < Ny; ++Y) for (int32 X = 0; X + 1 < Nx; ++X)
	{
		const int32 A = G[Y * Nx + X], B2 = G[Y * Nx + X + 1], C = G[(Y + 1) * Nx + X + 1], D = G[(Y + 1) * Nx + X];
		for (const FIndex3i& T : { FIndex3i(A, B2, C), FIndex3i(A, C, D) })   // up-facing (measured: the other winding drew the heap black from above)
		{
			const int32 Tid = M.AppendTriangle(T);
			if (Tid >= 0) M.Attributes()->GetMaterialID()->SetValue(Tid, House.WallMaterial);
		}
	}
	FTransform At(House.TileOrigin);
	AActor* R = SpawnDynamic(World, At, MoveTemp(M), SM, TEXT("HrotRubble"));
	if (UDynamicMeshComponent* RC = DynOf(R))
	{
		FRandomStream Rand(GetTypeHash(H.Index) ^ GetTypeHash(H.Tile));
		FGeometryScriptPrimitiveOptions O;
		const int32 Blocks = FMath::Clamp(int32(H.Footprint.Num() * 6), 30, 60);
		for (int32 K = 0; K < Blocks; ++K)   // blocks and broken wall slabs, lying on the heap (lower ones larger, the outer ones on the slope)
		{
			const float Rr = FMath::Sqrt(Rand.FRand());
			const FVector2D P = C2 + (H.Footprint[Rand.RandRange(0, N - 1)] - C2) * (Rr * 0.95f);
			const float Z = float(HeapZ(P));
			const bool bSlab = Rand.FRand() < 0.3f;
			const float S = bSlab ? Rand.FRandRange(120.f, 260.f) : Rand.FRandRange(30.f, 90.f);
			UGeometryScriptLibrary_MeshPrimitiveFunctions::AppendBox(RC->GetDynamicMesh(), O,
				FTransform(FRotator(Rand.FRandRange(-40, 40), Rand.FRandRange(0, 360), Rand.FRandRange(-40, 40)), L(P.X, P.Y, Z)),
				S, bSlab ? S * Rand.FRandRange(0.5f, 0.9f) : S * Rand.FRandRange(0.6f, 1.4f), bSlab ? 25.f : S * 0.7f, 0, 0, 0, EGeometryScriptPrimitiveOriginMode::Center);
		}

		RC->GetDynamicMesh()->EditMesh([&](FDynamicMesh3& D) { if (D.HasAttributes() && D.Attributes()->HasMaterialID()) for (int32 Tid : D.TriangleIndicesItr()) D.Attributes()->GetMaterialID()->SetValue(Tid, House.WallMaterial); });
		UGeometryScriptLibrary_MeshUVFunctions::SetMeshUVsFromBoxProjection(RC->GetDynamicMesh(), 0, FTransform(FQuat::Identity, FVector(L(C2.X, C2.Y, H.Base)), FVector(3.0)),
			FGeometryScriptMeshSelection());
		UGeometryScriptLibrary_MeshNormalsFunctions::RecomputeNormals(RC->GetDynamicMesh(), FGeometryScriptCalculateNormalsOptions());
	}
	House.Rubble = R;
	if (bLive)
	{
		UHrotExplosionFx::Explode(World, Ue(C2.X, C2.Y, H.Base + 1.0), FVector::UpVector, EHrotBlast::TankHE);   // the dust cloud
		SpawnDebris(World, Ue(C2.X, C2.Y, H.Base + Height), FVector::UpVector, 50, 15.f, 55.f, 700.f, House.WallMat.Get(), 300.f);
	}
	UE_LOG(LogHrotBuildings, Display, TEXT("buildings: house %d collapsed to rubble%s"), H.Index, bLive ? TEXT(" (live)") : TEXT(""));
}

void UHrotBuildingDamage::SpawnDebris(UWorld* World, const FVector& At, const FVector& Away, int32 Count, float MinCm, float MaxCm, float Speed, UMaterialInterface* Mat, float Spread, bool bFlat)
{
	static UStaticMesh* Cube = HrotLoadRooted<UStaticMesh>(TEXT("/Engine/BasicShapes/Cube.Cube"));
	if (!Cube || !World) return;
	FRandomStream R(int32(At.X * 7 + At.Y * 13 + At.Z));
	for (int32 I = 0; I < Count; ++I)
	{
		const FVector P = At + FVector(R.FRandRange(-Spread, Spread), R.FRandRange(-Spread, Spread), R.FRandRange(-Spread * 0.5f, Spread * 0.5f)) * 0.6f;
		FActorSpawnParameters SP; SP.ObjectFlags |= RF_Transient; SP.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
		AStaticMeshActor* A = World->SpawnActor<AStaticMeshActor>(AStaticMeshActor::StaticClass(), FTransform(FRotator(R.FRandRange(0, 360), R.FRandRange(0, 360), R.FRandRange(0, 360)), P), SP);
		if (!A) continue;
		UStaticMeshComponent* C = A->GetStaticMeshComponent();
		C->SetMobility(EComponentMobility::Movable);
		C->SetStaticMesh(Cube);
		if (Mat) C->SetMaterial(0, Mat);
		const float S = R.FRandRange(MinCm, MaxCm) / 100.f;   // the cube is 1 m
		const FVector Scale(S, S * R.FRandRange(0.5f, 1.3f), bFlat ? 0.006f : S * R.FRandRange(0.4f, 1.0f));
		A->SetActorScale3D(Scale);
		C->SetCollisionProfileName(TEXT("PhysicsActor"));
		C->SetCastShadow(S > 0.25f);
		C->SetSimulatePhysics(true);
		const FVector V = (Away + FVector(R.FRandRange(-0.6f, 0.6f), R.FRandRange(-0.6f, 0.6f), R.FRandRange(0.1f, 0.9f))).GetSafeNormal() * Speed * R.FRandRange(0.4f, 1.0f);
		C->SetPhysicsLinearVelocity(V);
		C->SetPhysicsAngularVelocityInDegrees(FVector(R.FRandRange(-720, 720), R.FRandRange(-720, 720), R.FRandRange(-720, 720)));
		Debris.Add({ A, World->GetTimeSeconds(), R.FRandRange(5.0, 8.0), Scale });
	}
}

void UHrotBuildingDamage::TickDebris(UWorld* World)
{
	if (!World) return;
	const double Now = World->GetTimeSeconds();
	for (int32 I = Debris.Num() - 1; I >= 0; --I)
	{
		FDebris& D = Debris[I];
		AActor* A = D.A.Get();
		const double Age = Now - D.Born;
		if (!A || Age > D.Life + 1.5) { if (A) A->Destroy(); Debris.RemoveAtSwap(I); continue; }
		if (Age > D.Life) A->SetActorScale3D(D.Scale * float(1.0 - (Age - D.Life) / 1.5));   // shrink away, then gone
	}
}

void UHrotBuildingDamage::Apply(UWorld* World, const FString& Terrain, const FHrotBuildingDamageIn& In, double SimNow)
{
	if (!In.bAlive) return;   // damage gone (a reload): the house stays as it is drawn - ⏳ restoring it is later work
	if (!LoadIndex(Terrain)) return;
	const FIndexed* H = Index.Find(In.Key);
	if (!H) { UE_LOG(LogHrotBuildings, Warning, TEXT("buildings: %s is not in the index"), *In.Key); return; }
	FHouse* House = TakeOut(World, Terrain, In.Key, *H);
	if (!House || House->bCollapsed) return;
	auto Live = [&](double At) { return At > 0 && SimNow - At < 3.0; };   // the gateway's rule: 0 = from a save; an old stamp = state at join
	for (const FHrotBreachIn& B : In.Breaches)
	{
		const FString Id = FString::Printf(TEXT("%d:%.2f:%.2f:%.2f:%.2f"), B.Panel, B.From, B.To, B.Bottom, B.Top);
		if (House->Cuts.Contains(Id)) continue;
		House->Cuts.Add(Id);
		Cut(*House, *H, B, Live(B.MadeAt), World);
	}
	for (const FHrotWindowIn& Wd : In.Windows)
	{
		const int64 Id = int64(Wd.Panel) * 100000 + Wd.Opening;
		if (House->Broken.Contains(Id)) continue;
		int32& Had = House->Hits.FindOrAdd(Id);
		if (Wd.Hits >= 3) { House->Broken.Add(Id); BreakGlass(*House, *H, Wd.Panel, Wd.Opening, Live(Wd.HitAt), World); continue; }
		for (int32 K = Had; K < FMath::Min<int32>(Wd.Hits, 2); ++K) Crack(*House, *H, Wd, K, World, Live(Wd.HitAt));   // the first hit cracks at (U0, V0), the second at (U1, V1)
		Had = FMath::Max<int32>(Had, Wd.Hits);
	}
	if (In.bCollapsed) Collapse(World, *House, *H, Live(In.CollapsedAt));
}

void UHrotBuildingDamage::Tick(UWorld* World, const FString& Terrain, double SimNow)
{
	if (World && !Terrain.IsEmpty()) { LastWorld = World; LastTerrain = Terrain; LastSimNow = SimNow; }   // no station yet: the Test* terrain stays
	if (Debris.Num()) TickDebris(World ? World : LastWorld.Get());
	if (Pending.Num() == 0 || !World || Terrain.IsEmpty()) return;
	TArray<FHrotBuildingDamageIn> Now = MoveTemp(Pending); Pending.Reset();
	for (const FHrotBuildingDamageIn& In : Now) Apply(World, Terrain, In, SimNow);
}

bool UHrotBuildingDamage::TestDamage(const FString& Key, int32 Panel, float From, float To, float Bottom, float Top, bool bCollapse, bool bLive)
{
	UWorld* W = LastWorld.Get();
	if (!W || !LoadIndex(LastTerrain) || !Index.Contains(Key)) return false;
	FHrotBuildingDamageIn In; In.Key = Key; In.bAlive = true;
	const double Now = 1000.0;
	if (Panel >= 0) In.Breaches.Add({ Panel, From, To, Bottom, Top, bLive ? Now : 0.0, FVector::ZeroVector });
	In.bCollapsed = bCollapse; In.CollapsedAt = bLive ? Now : 0.0;
	Apply(W, LastTerrain, In, Now);
	return Houses.Contains(Key);
}

bool UHrotBuildingDamage::TestHitWindow(const FString& Key, int32 Panel, int32 Opening, int32 Hits, int32 U0, int32 V0, int32 U1, int32 V1, bool bLive)
{
	UWorld* W = LastWorld.Get();
	if (!W || !LoadIndex(LastTerrain) || !Index.Contains(Key)) return false;
	FHrotBuildingDamageIn In; In.Key = Key; In.bAlive = true;
	FHrotWindowIn Wd; Wd.Panel = Panel; Wd.Opening = Opening; Wd.Hits = uint8(Hits); Wd.HitAt = bLive ? 1000.0 : 0.0;
	Wd.U0 = uint8(U0); Wd.V0 = uint8(V0); Wd.U1 = uint8(U1); Wd.V1 = uint8(V1);
	In.Windows.Add(Wd);
	Apply(W, LastTerrain, In, 1000.0);
	return Houses.Contains(Key);
}

bool UHrotBuildingDamage::TestBreakWindow(const FString& Key, int32 Panel, int32 Opening, bool bLive)
{
	UWorld* W = LastWorld.Get();
	if (!W || !LoadIndex(LastTerrain) || !Index.Contains(Key)) return false;
	FHrotBuildingDamageIn In; In.Key = Key; In.bAlive = true;
	FHrotWindowIn Wd; Wd.Panel = Panel; Wd.Opening = Opening; Wd.Hits = 3; Wd.HitAt = bLive ? 1000.0 : 0.0; In.Windows.Add(Wd);
	Apply(W, LastTerrain, In, 1000.0);
	return Houses.Contains(Key);
}
