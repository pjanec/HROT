// CE-4651 (U-18l) — THE IG's CAMERA: free flight and FOLLOW (watch any unit) as HROT's map does them (the DPS client guide, "Camera
// controllers to build"; the rules in HrotDpsCamera.h, ported from MapCamera3D / FollowCameraDriver / UnitCameraRig).
//
//   free flight   right button held: look around the eye, W/S/A/D along the view, E/Q up / down, the speed from the eye's height (Shift ×4,
//                 Ctrl ×20, both ×100, ramping while held); the wheel steps along the view
//   select        a left click picks the entity under the cursor (its actor → its id)
//   follow        F follows the selected unit on the distance continuum (the wheel walks it: orbit far, over the shoulder near, his eyes
//                 at 0); the right button orbits (at his eyes it offsets the look, springing back); W/A/S/D or Esc hand back to free flight
//                 at the same spot; the unit's end too
//
//   menu          a RIGHT-CLICK (press and release, no drag — a drag is the look / the orbit) on a unit opens its menu, as HROT's map:
//                 "Follow with the camera" (F) · "Possess (FPS)" (P; greyed with the reason when this station may not play it)
//   director (U-18p) V shows the STATION's suggested view (DPS View, DD-022 §3d): bound to a unit (orbit / shoulder / eyes / chase) the
//                 follow rig places it from the binding against OUR prediction of that unit (camera and unit share one curve, so the
//                 unit does not shake in the frame); unbound (free, fixed, a director flight) the view's own pose, dead-reckoned with its
//                 rates; a new CutSeq is a hard cut. Without a suggestion it holds where it is. V again, W/A/S/D or Esc: free flight there
//   player (U-18m) possessed — HROT's FpsCameraDriver: over the shoulder by default (O: his eyes), the wheel the distance; the mouse turns
//                 the view (his body lags 45° standing, turns at 540°/s moving; W/A/S/D relative to the camera); RMB zooms (45° → 30°);
//                 LMB fires (a press locks the shot, held the trigger); 1-4 weapon, R round, L sight / lock, F use, C / Z crouch / prone,
//                 Ctrl crouched while held, Shift run, Q / E lean (a flyer: Q / Space climb, Z / C descend), B binoculars; the crosshair's
//                 ray is the aim; Esc lets him go (Leave) and the camera flies free where it is. The frames go out through UHrotDpsStation.
//
// Placed every frame from the unit's pose for THIS frame (UHrotDpsWorld::PoseNow — HROT's BeforeDraw, CE-1352: no lag behind a fast
// vehicle). Works under a player controller (input) and without one (Simulate, a test, a render node driven by calls).
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Pawn.h"
#include "HrotDpsCamera.h"
#include "HrotDpsUseList.h"
#include "HrotDpsCameraPawn.generated.h"

class UCameraComponent;

UENUM(BlueprintType)
enum class EHrotCameraMode : uint8 { Free, Follow, Player, Director };

/** one row of the unit menu (right-click) */
USTRUCT(BlueprintType)
struct FHrotMenuRow
{
	GENERATED_BODY()
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Camera") FString Label;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Camera") FString Shortcut;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Camera") bool bEnabled = true;
	UPROPERTY(BlueprintReadOnly, Category = "HROT|Camera") FString Reason;   // why it is greyed
	int32 Action = 0;   // 1 follow, 2 possess
};

UCLASS()
class HROTDPS_API AHrotDpsCamera : public APawn
{
	GENERATED_BODY()

public:
	AHrotDpsCamera();

	/** spawns one in the world (a test or a render node without a placed one) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera", meta = (WorldContext = "WorldContext"))
	static AHrotDpsCamera* SpawnIn(UObject* WorldContext, FVector At, FRotator Rotation);

	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Follow(int32 EntityId, float Distance = 14.f);
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Free();
	/** U-18q — a render job's frame: free, at exactly this camera (Unreal; horizontal fov) — the LISTENER of its sounds too */
	void PlaceAt(const FVector& At, const FQuat& Rotation, float HFovDeg);
	/** U-18y — PlaceAt for a script (a rail's camera) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void PlaceAtRotator(FVector At, FRotator Rotation, float HFovDeg) { PlaceAt(At, Rotation.Quaternion(), HFovDeg); }
	/** U-18p — show the station's suggested view (V) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Director();
	/** turns the follow orbit (degrees; yaw counter-clockwise like HROT, pitch up +) — what a right-button drag does */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Orbit(float YawDeg, float PitchDeg);
	/** the follow's place on the continuum (m; 0 = his eyes) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void SetFollowDistance(float Distance) { FollowDistance = Distance; }
	/** a wheel move while following (+ in, − out) — the continuum's step */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Wheel(float Notches);
	/** U-19g test hooks: the view's rotation, and one follow placement with no time passing (the eyes' spring does not move: a stage change is read exactly) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera|Test") FRotator ViewRotation() const;
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera|Test") void StepFollowForTest() { if (Mode == EHrotCameraMode::Follow) PlaceFollow(0.f); }

	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") EHrotCameraMode GetMode() const { return Mode; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") int32 FollowedId() const { return FollowId; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") int32 SelectedId() const { return Selected; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Select(int32 EntityId) { Selected = EntityId; }
	/** U-19g - the entity a click at this screen point (px) means: the visibility trace (vehicles, buildings) AND every drawn entity projected to the screen
	 *  (a skeletal soldier is invisible to the trace); the nearest within HrotDpsPick::MaxPx wins unless the trace hit is nearer the camera */
	int32 PickAt(const FVector2D& ScreenPx) const;
	/** where an entity is on the screen (px, as the mouse position), (-1, -1) = unknown / behind the camera / off the viewport */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") FVector2D ScreenOf(int32 EntityId) const;
	/** U-19g: the screen rectangle of an entity's own body (the 8 corners of its box projected; + a small margin) - what the selection brackets enclose; bIsValid false = not on screen */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") FBox2D ScreenRectOf(int32 EntityId) const;
	/** U-19g probe: how many frames the HUD drew the selection brackets */
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "HROT|Camera|Test") int32 SelectionMarksDrawn = 0;
	/** U-19g test hook: a left (select) or right (select + the unit menu) CLICK at ScreenPx, as TickInput does for the mouse; returns the entity picked */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera|Test") int32 TestClick(FVector2D ScreenPx, bool bRight);
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") float GetFollowDistance() const { return FollowDistance; }
	/** the arm this frame (m) — shorter than the distance when a wall pulled it in */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") float GetArm() const { return Arm; }
	/** where the camera is and looks (Unreal) — what a test or a render node copies */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") FTransform CameraTransform() const;
	/** "free" or "follow <id> at <d> m (orbit|shoulder|eyes), arm <a> m" */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") FString Describe() const;

	// ── U-18m: the unit menu, possession, the player ──
	/** opens the unit menu for EntityId at a screen point (what a right-click does) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void OpenMenu(int32 EntityId, FVector2D ScreenAt);
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void CloseMenu() { MenuEntity = 0; }
	/** runs a menu row (0-based) — a click on it */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void MenuChoose(int32 Row);
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") bool IsMenuOpen() const { return MenuEntity != 0; }
	TArray<FHrotMenuRow> MenuRows() const;
	FVector2D MenuOrigin() const { return MenuAt; }
	int32 MenuEntityId() const { return MenuEntity; }
	static constexpr float MenuRowHeight = 24.f;
	static constexpr float MenuWidth = 300.f;
	static constexpr float MenuHeader = 26.f;
	/** a right click is a press and release within this time and travel (px); longer or further is the look / the orbit */
	static constexpr float MenuClickSeconds = 0.35f;
	static constexpr float MenuClickTravel = 6.f;
	/** ⚠ Unreal's mouse delta is not pixels: ~10 px a unit at the default sensitivity */
	static constexpr float MouseScale = 10.f;
	/** asks the station to play EntityId; the camera becomes his when the slot says Playing */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void Possess(int32 EntityId);
	/** lets him go (Esc) and flies free where the camera is */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void LetGo();
	/** test / render-node input while playing (no player controller): degrees of look, the move keys, the fire press / trigger, zoom */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void PlayerLook(float YawDeg, float PitchDeg) { TestLookYaw += YawDeg; TestLookPitch += PitchDeg; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void PlayerMove(float Forward, float Side) { TestForward = Forward; TestSide = Side; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void PlayerFire(bool bHeld) { if (bHeld && !TestTrigger) TestFirePress = true; TestTrigger = bHeld; }
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void PlayerZoom(bool bOn) { TestZoom = bOn; }
	/** U-19h: hold the run key (Shift) for a script - the live speed check */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void PlayerRun(bool bOn) { TestRun = bOn; }
	/** a tap of the use key (F) on what the crosshair rests on - for scripts (the drive check, 2026-11-23) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void PlayerUse() { TestUsePress = true; TestUseVerb = 0; }
	/** a press of the use key naming one of HROT's offered verbs (the contract's UseVerb), for scripts (U-19h) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void PlayerUseVerb(uint8 Verb) { TestUsePress = true; TestUseVerb = Verb; }
	/** what the crosshair rests on now (the UseTarget sent) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") int32 PlayerAimTarget() const { return AimTarget; }
	/** the player's view distance (m; 0 = his eyes, < 0 ahead of them) */
	UFUNCTION(BlueprintCallable, Category = "HROT|Camera") void SetPlayerDistance(float Distance) { PlayerDistance = Distance; }

	// read by the HUD
	int32 PlayedId() const { return PlayerUnit; }
	int32 PendingPossess() const { return PossessWanted; }
	/** a short message for the status line (e.g. why a unit cannot be possessed), shown for a few seconds; empty when none */
	FString NoticeLine() const { return FPlatformTime::Seconds() - NoticeAt < 5.0 ? Notice : FString(); }
	bool IsZooming() const { return bZooming; }
	float VerticalFov() const { return bZooming ? HrotPlayer::IronSightsFov : HrotPlayer::NormalFov; }
	int32 AimTargetId() const { return AimTarget; }
	FVector3d AimPointEnu() const { return AimPoint; }
	FVector3d ViewForwardEnu() const { return HrotCamera::Forward(PlayerYaw, PlayerPitch); }
	uint8 StanceWanted() const { return Stance; }
	bool IsRunning() const { return bRunning; }
	float PlayerViewDistance() const { return PlayerDistance; }
	HrotCamera::EScheme PlayerScheme() const { return PScheme; }
	int32 SelectedMount() const { return Mount; }
	/** the hold-F list (U-19h): open, and which row is highlighted (index into the station's Actions) */
	bool UseListOpen() const { return UseList.bOpen; }
	int32 UseListHighlight() const { return UseList.Highlight; }

	UPROPERTY(VisibleAnywhere, Category = "HROT|Camera") TObjectPtr<USceneComponent> Root;
	UPROPERTY(VisibleAnywhere, Category = "HROT|Camera") TObjectPtr<UCameraComponent> Cam;
	/** the camera never goes below this over the ground (m) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Camera") float AboveGround = 0.3f;

	virtual void Tick(float DeltaSeconds) override;
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type Why) override;
	/** draw the HUD onto the viewport when there is no player controller (the debug-draw path; on by default) */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HROT|Camera") bool bDrawHud = true;

private:
	EHrotCameraMode Mode = EHrotCameraMode::Free;
	int32 FollowId = 0, Selected = 0;
	float FollowDistance = HrotCamera::FollowStart;
	double Yaw = 0, Pitch = -0.35;               // the follow orbit (ENU radians)
	double LookYawOffset = 0, LookPitchOffset = 0;
	float Arm = 0;
	bool bPlaced = false;
	// free flight
	FVector FlyVelocity = FVector::ZeroVector;   // cm/s
	float FlyHeld = 0, WheelTravel = 0, WheelIdle = 10, WheelSign = 0;
	int32 WheelStreak = 0;

	void TickInput(float Dt);
	void TickPlayer(float Dt, class APlayerController* PC);
	void EnterPlayer();
	FDelegateHandle DebugHud;
	void DrawDebugHud(class UCanvas* Canvas, class APlayerController* PC);
	void LeavePlayerView();
	void PlacePlayer(float Dt, const FVector3d& Position);
	float FreeFov = 90.f;
	bool bAnimal = false;
	int32 ShoulderSide = 1;
	TOptional<double> HeldHeading;
	// the menu
	int32 MenuEntity = 0;
	FVector2D MenuAt = FVector2D::ZeroVector;
	double RmbDownAt = -1; float RmbTravel = 0;
	bool bWasEyes = false;                 // U-19g: the follow camera was at his eyes last frame
	bool bLookCaptured = false; FVector2D RmbPressAt = FVector2D::ZeroVector;   // U-19g: RMB held for look: the mouse is captured, the press point is restored on release
	// the player
	int32 PossessWanted = 0, PlayerUnit = 0;
	FString Notice; double NoticeAt = -100.0;
	/** possess when HROT offers the unit to this station (PlayerSlot.Offers, free); otherwise only says why — the P key and the offer keys */
	void TryPossess(int32 EntityId);
	double PlayerYaw = 0, PlayerPitch = 0, BodyYaw = 0;
	float PlayerDistance = HrotCamera::PersonDefault, PlayerArm = 0, LookIdle = 0;
	bool bPlayerArmPlaced = false, bZooming = false, bRunning = false, bCrouchHeld = false;
	HrotCamera::EScheme PScheme = HrotCamera::EScheme::Person;
	uint8 Stance = 0, ShotSeq = 0, CancelSeq = 0, AmmoSeq = 0, UseSeq = 0, FirePref = 0, Mount = 0;
	int32 ShotTarget = 0, UseTarget = 0, AimTarget = 0;
	FVector3d AimPoint = FVector3d::ZeroVector;
	// test input (no player controller)
	float TestLookYaw = 0, TestLookPitch = 0, TestForward = 0, TestSide = 0;
	bool TestFirePress = false, TestTrigger = false, TestZoom = false, TestUsePress = false, TestRun = false;
	uint8 TestUseVerb = 0, UseVerb = 0;
	HrotDpsUseList::FState UseList;
	void PlaceFollow(float Dt);
	void PlaceDirector(float Dt);
	int32 FollowSide = 1;          // the shoulder (+1 right); the director's binding sets it
	uint32 DirectorCut = 0;
	bool bDirectorBound = false;
	void FreeLook(float DYaw, float DPitch);
	float HeightAboveGround(const FVector& At) const;
	/** the first blocking hit's fraction along From → To (Unreal), ignoring the followed actor and its riders; −1 = none */
	double FirstHit(const FVector& From, const FVector& To) const;
	int32 PickUnderCursor() const;
	/** projects an entity's body: its screen rectangle, and the pick geometry (a man: the segment feet -> head; else the box centre, A == B) */
	bool ProjectBody(int32 EntityId, FBox2D& Rect, FVector2D& A, FVector2D& B, bool& bMan, double& Depth) const;
	/** ⚠ measured LIVE (U-18l): at his eyes the camera was INSIDE his head (helmet and gun from within) — his own meshes are hidden
	 *  while the camera is at his eyes (component visibility: the actor's hidden flag is the seat rule's) and shown again after */
	void HideBody(AActor* A, bool bHide);
	TWeakObjectPtr<AActor> HiddenBody;
	TArray<TWeakObjectPtr<UPrimitiveComponent>> HiddenParts;
};
