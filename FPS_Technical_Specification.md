**Technical Specification**

FPS Multiplayer Game

Unity Engine + Mirror Networking

Version 1.0 \| February 2026

Status: Draft

1\. Project Overview

This document defines the technical specification for a first-person
shooter (FPS) game with multiplayer support. The project is built on the
Unity engine using C# and leverages the Mirror Networking asset for all
networked gameplay. The initial phase focuses on core FPS mechanics:
player movement, camera control, shooting, and multiplayer
synchronization. A simplified vehicle (car) controller is planned for a
future phase and is scoped here at a high level only.

  ------------------------------- ---------------------------------------
  **Parameter**                   **Value**

  **Engine**                      Unity 2022.3 LTS (or latest LTS)

  **Language**                    C#

  **Networking**                  Mirror (via Unity Asset Store / GitHub)

  **Target Platforms**            Windows (primary), Linux (secondary)

  **Render Pipeline**             URP (Universal Render Pipeline)

  **Min. Players**                2

  **Max. Players**                16 (configurable via NetworkManager)

  **Perspective**                 First Person
  ------------------------------- ---------------------------------------

2\. Architecture Overview

The project follows a client-authoritative model with server
reconciliation via Mirror. Each player instance runs its own local
simulation for responsiveness, while the server validates and replicates
state. All networked components inherit from Mirror's NetworkBehaviour
base class.

2.1 Project Structure

  ------------------------ ----------------------------------------------
  **Folder**               **Contents**

  **Scripts/Player**       PlayerController, PlayerCamera, PlayerHealth,
                           PlayerShooting

  **Scripts/Networking**   CustomNetworkManager, PlayerSpawnManager,
                           NetworkMessages

  **Scripts/Weapons**      WeaponBase, WeaponData (ScriptableObject),
                           HitscanWeapon, ProjectileWeapon

  **Scripts/Vehicle**      (Future) CarController, VehicleSync ---
                           placeholder only

  **Scripts/UI**           HUD, ScoreboardUI, SettingsMenu, PauseMenu

  **Scripts/Config**       GameSettings (ScriptableObject), InputConfig

  **Prefabs/Player**       PlayerPrefab (with NetworkIdentity,
                           NetworkTransform)

  **Prefabs/Weapons**      Weapon prefabs with NetworkIdentity

  **ScriptableObjects**    WeaponData assets, GameSettings asset
  ------------------------ ----------------------------------------------

2.2 Core Dependencies

-   Mirror Networking (latest stable) --- NetworkManager,
    NetworkIdentity, NetworkTransform, SyncVar, Command/ClientRpc

-   Unity Input System (new) --- action-based input with rebindable
    controls

-   Universal Render Pipeline (URP) --- post-processing, shader
    compatibility

-   TextMeshPro --- all UI text rendering

3\. Player Controller (FPS)

The PlayerController is the central gameplay component. It handles
movement, gravity, ground detection, and integrates with Mirror for
network synchronization. Movement parameters are exposed as tweakable
fields in the Inspector and can also be sourced from a GameSettings
ScriptableObject for runtime consistency across clients.

3.1 Movement Parameters

  ------------------------- ----------- ------------- ----------------------------------
  **Parameter**             **Type**    **Default**   **Description**

  **walkSpeed**             float       5.0           Base walking speed (units/sec)

  **sprintSpeed**           float       8.5           Speed while holding sprint input

  **crouchSpeed**           float       2.5           Speed while crouching

  **acceleration**          float       10.0          Ground acceleration rate

  **deceleration**          float       12.0          Ground deceleration rate
                                                      (friction)

  **airControl**            float       0.3           Multiplier for mid-air movement
                                                      influence (0--1)

  **jumpForce**             float       7.0           Vertical impulse on jump

  **gravity**               float       -20.0         Custom gravity (not
                                                      Physics.gravity)

  **groundCheckRadius**     float       0.3           SphereCast radius for ground
                                                      detection

  **groundCheckDistance**   float       0.1           SphereCast distance below feet

  **groundLayer**           LayerMask   ---           Layers considered as walkable
                                                      ground

  **stepHeight**            float       0.35          Max step height for auto-step
                                                      (stairs)

  **slopeLimit**            float       45.0          Maximum walkable slope angle in
                                                      degrees
  ------------------------- ----------- ------------- ----------------------------------

3.2 Movement Implementation Notes

-   Use CharacterController (not Rigidbody) for deterministic,
    frame-rate-independent movement.

-   Ground detection via Physics.SphereCast from the player's feet
    downward.

-   Sprint toggled by holding the sprint key (default: Left Shift).
    Cannot sprint while crouching.

-   Crouch lowers CharacterController.height and camera Y offset;
    transition is smoothed via Mathf.Lerp.

-   Apply custom gravity each frame: verticalVelocity += gravity \*
    Time.deltaTime. Reset to a small negative value (−2f) when grounded
    to maintain ground contact.

-   Slope handling: project movement direction onto the slope normal
    when angle \< slopeLimit; apply slide force when angle \>
    slopeLimit.

3.3 Network Synchronization

Player movement is synchronized using Mirror's NetworkTransform
component with client authority enabled. The local player runs its own
movement simulation, and Mirror replicates the transform to all other
clients. For competitive scenarios, the server can optionally validate
position deltas and reject impossible moves (anti-cheat stub).

  ---------------------- --------------- -------------------------------------
  **Component**          **Authority**   **Notes**

  **NetworkTransform**   Client          Sync position + rotation; compress
                                         for bandwidth

  **PlayerController**   Local only      Only executes on isLocalPlayer

  **PlayerHealth**       Server          \[SyncVar\] with hook for UI update
                                         on all clients

  **PlayerShooting**     Client → Server \[Command\] to request shot; server
                                         validates and applies
  ---------------------- --------------- -------------------------------------

4\. Camera System

The PlayerCamera component controls the first-person camera. It is
attached to a child GameObject of the player prefab (typically at head
height). All sensitivity, smoothing, and constraint parameters are
exposed for runtime tweaking. Camera logic only runs on the local player
(isLocalPlayer check).

4.1 Camera Parameters

  ------------------------ ---------- ------------- ------------------------------
  **Parameter**            **Type**   **Default**   **Description**

  **mouseSensitivity**     Vector2    (2.0, 2.0)    Horizontal (X) and vertical
                                                    (Y) sensitivity

  **smoothing**            float      0.05          Mouse smoothing factor (0 =
                                                    raw input, 1 = max smooth)

  **minPitch**             float      -85.0         Minimum vertical look angle
                                                    (looking down)

  **maxPitch**             float      85.0          Maximum vertical look angle
                                                    (looking up)

  **fieldOfView**          float      90.0          Base camera FOV in degrees

  **sprintFOV**            float      100.0         FOV while sprinting (lerped)

  **fovLerpSpeed**         float      8.0           Speed of FOV transition

  **headBobEnabled**       bool       true          Toggle head bobbing while
                                                    walking/sprinting

  **headBobFrequency**     float      1.8           Oscillation frequency of head
                                                    bob

  **headBobAmplitude**     float      0.06          Vertical amplitude of head bob
                                                    (units)

  **cameraShakeDecay**     float      5.0           How quickly recoil/shake
                                                    dampens

  **adsZoomFOV**           float      55.0          FOV when aiming down sights

  **adsTransitionSpeed**   float      10.0          Speed of ADS zoom transition
  ------------------------ ---------- ------------- ------------------------------

4.2 Camera Implementation Notes

-   Lock and hide the cursor on play: Cursor.lockState =
    CursorLockMode.Locked.

-   Horizontal rotation applied to the player body Transform (Y-axis);
    vertical rotation applied to the camera Transform (X-axis, clamped
    between minPitch and maxPitch).

-   Smoothing uses Vector2.Lerp between raw input delta and the current
    smoothed value. A value of 0 is recommended for competitive play.

-   Head bob uses a sine wave: cameraLocalPos.y = Mathf.Sin(Time.time \*
    frequency) \* amplitude. Only active when grounded and moving.

-   Recoil is additive pitch offset that decays over time via Mathf.Lerp
    toward zero, controlled by cameraShakeDecay.

-   FOV transitions (sprint, ADS) use Mathf.Lerp with fovLerpSpeed \*
    Time.deltaTime.

5\. Shooting System

Weapons are defined as ScriptableObject assets (WeaponData) to allow
easy balancing without code changes. The PlayerShooting component reads
from the equipped WeaponData and handles input, fire rate enforcement,
and network commands.

5.1 WeaponData ScriptableObject

  ------------------------- ------------- -------------------------------------
  **Field**                 **Type**      **Description**

  **weaponName**            string        Display name

  **fireRate**              float         Rounds per second

  **damage**                int           Damage per hit

  **range**                 float         Max effective range (hitscan)

  **spread**                float         Base bullet spread cone angle
                                          (degrees)

  **recoilPattern**         Vector2\[\]   Per-shot recoil offsets (pitch, yaw)

  **magazineSize**          int           Rounds before reload

  **reloadTime**            float         Reload duration in seconds

  **isAutomatic**           bool          Full-auto vs. semi-auto

  **adsSpreadMultiplier**   float         Spread multiplier while ADS (\< 1.0)
  ------------------------- ------------- -------------------------------------

5.2 Shooting Flow

1\) The local client detects fire input and checks fire rate cooldown.
2) A local raycast is performed from the camera center for immediate
visual feedback (tracer, hit effect). 3) A \[Command\] is sent to the
server with the shot origin and direction. 4) The server performs its
own raycast to validate the hit. 5) If valid, the server applies damage
to the target's PlayerHealth \[SyncVar\] and triggers a \[ClientRpc\]
for hit confirmation VFX on all clients.

5.3 Hit Detection

-   Hitscan weapons use Physics.Raycast from camera center. Layer mask
    should include only the "Player" and "Environment" layers.

-   Headshot detection: player collider hierarchy with separate head
    collider tagged "Head" for damage multiplier.

-   Projectile weapons (future): instantiate a networked projectile
    prefab with NetworkServer.Spawn.

6\. Game Settings (Runtime Config)

A GameSettings ScriptableObject centralizes all tweakable values. A
SettingsMenu UI allows players to modify their local preferences
(sensitivity, FOV, volume) at runtime. These are saved to PlayerPrefs
and applied on load.

  --------------- -------------------------------------------------------
  **Category**    **Settings**

  **Controls**    Mouse sensitivity X/Y, invert Y-axis, toggle/hold
                  sprint, toggle/hold crouch

  **Video**       FOV slider (60--120), V-Sync, resolution, quality
                  preset, fullscreen mode

  **Audio**       Master volume, SFX volume, music volume, voice chat
                  volume

  **Gameplay**    Crosshair style/color, hit markers on/off, head bob
                  on/off, damage numbers on/off

  **Network**     Player name, preferred region (if relay), show ping
                  on/off
  --------------- -------------------------------------------------------

7\. Multiplayer Architecture (Mirror)

7.1 Network Topology

The game uses Mirror's host-client model. One player acts as the host
(server + client) or a dedicated server build can be used. All game
state authority resides on the server. Mirror's KCP Transport is the
default (UDP-based, reliable/unreliable channels).

7.2 CustomNetworkManager

Inherits from Mirror's NetworkManager. Responsibilities include: player
spawn point management (round-robin or random from SpawnPoint\[\]
array), player prefab registration, handling OnServerAddPlayer and
OnServerDisconnect for scoreboard updates, match state management
(lobby, in-game, post-game), and optional server-side anti-cheat
validation stubs.

7.3 Synchronized State

  ---------------------- ------------------------ -----------------------
  **Data**               **Sync Method**          **Update Frequency**

  **Player               NetworkTransform         Every frame
  position/rotation**                             (interpolated)

  **Player health**      \[SyncVar\] + hook       On change

  **Ammo count**         \[SyncVar\]              On change

  **Score / kills /      \[SyncVar\] on           On change
  deaths**               ScoreManager             

  **Weapon equipped**    \[SyncVar\] +            On change
                         \[Command\] to switch    

  **Match timer**        \[SyncVar\] on           Every second
                         MatchManager             
  ---------------------- ------------------------ -----------------------

8\. HUD and UI

8.1 In-Game HUD Elements

-   Crosshair (dynamic spread indicator based on movement/firing state)

-   Health bar / numeric health display

-   Ammo counter: current magazine / reserve

-   Kill feed (top-right, scrolling text with fade-out)

-   Minimap (optional, toggle-able)

-   Network ping indicator

-   Hit marker overlay (center screen, brief flash on successful hit)

8.2 Menus

-   Main Menu: Host Game, Join Game (IP input), Settings, Quit

-   Pause Menu: Resume, Settings, Disconnect

-   Scoreboard: Hold Tab to display; shows player names, kills, deaths,
    ping

-   Settings Menu: tabs for Controls, Video, Audio, Gameplay

9\. Vehicle System (Future Phase)

A simplified car controller will be implemented in a later development
phase. This section outlines the planned scope for reference.

9.1 Planned Scope

-   Arcade-style car physics (not simulation): simplified wheel
    colliders or custom physics.

-   Enter/exit vehicle interaction triggered by proximity + input key
    (default: F).

-   Network sync: vehicle ownership transfers on enter; NetworkTransform
    with client authority on the driver.

-   First-person driver camera with adjustable FOV.

-   Basic vehicle stats: max speed, acceleration, turn radius, brake
    force.

-   Collision damage (vehicle-to-player, vehicle-to-vehicle) --- TBD.

9.2 Implementation Notes (Deferred)

Detailed vehicle specification will be written when this phase begins.
Placeholder scripts (CarController.cs, VehicleSync.cs) should be created
with empty class bodies and TODO comments to reserve the namespace.

10\. Development Milestones

  --------------- ------------------------------- ------------------------
  **Phase**       **Scope**                       **Deliverable**

  **Phase 1 ---   Player controller, camera       Single-player FPS
  Core FPS**      system, basic shooting          prototype (offline)
                  (hitscan), health system        

  **Phase 2 ---   Mirror integration, player      Playable LAN multiplayer
  Multiplayer**   spawning, synchronized          (2--16 players)
                  movement/health/shooting,       
                  scoreboard                      

  **Phase 3 ---   Settings menu, HUD, weapon      Feature-complete FPS
  Polish**        balancing, map design, audio    build

  **Phase 4 ---   Simplified car controller,      FPS + vehicle gameplay
  Vehicles**      enter/exit, network sync        
  --------------- ------------------------------- ------------------------

11\. Technical Constraints and Notes

-   Target frame rate: 60 FPS minimum on mid-range hardware.

-   Mirror tick rate: default 60 Hz (configurable via
    NetworkManager.sendRate).

-   All MonoBehaviour-based scripts must handle isLocalPlayer checks to
    avoid running camera/input logic on remote player instances.

-   Use \[SerializeField\] private fields with public properties for
    Inspector-exposed parameters. Avoid public fields.

-   Follow Unity's component-based architecture: keep scripts
    single-responsibility (e.g., separate PlayerMovement, PlayerCamera,
    PlayerShooting).

-   ScriptableObjects for all data definitions (weapons, settings) to
    avoid hard-coded magic numbers.

-   Use Unity's new Input System exclusively; do not mix with legacy
    Input.GetKey.

*End of Technical Specification --- Version 1.0*
