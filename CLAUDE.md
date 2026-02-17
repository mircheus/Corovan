# Corovan — Multiplayer FPS with Vehicles

## Tech Stack
- **Unity 6** (6000.0.60f1)
- **Mirror** networking (Assets/Mirror/)
- **New Input System** (Send Messages mode on PlayerInput component)
- **ParrelSync** for local multiplayer testing

## Project Structure

```
Assets/Game/
├── Input/
│   └── InputSystem_Actions.inputactions    # Input actions: Move, Look, Jump, Crouch, Sprint, Aim, Attack, Reload, Interact
├── Prefabs/
│   └── Player/Player.prefab                # Player prefab with NetworkIdentity + NetworkTransformUnreliable
├── Scripts/
│   ├── Player/
│   │   ├── PlayerInputHandler.cs           # Routes Input System callbacks to other components; handles vehicle enter/exit
│   │   ├── PlayerController.cs             # CharacterController-based FPS movement, gravity, crouch, sprint
│   │   ├── PlayerCamera.cs                 # First-person camera with mouse look
│   │   ├── PlayerShooting.cs               # Weapon handling, aiming, firing (raycasts)
│   │   └── PlayerHealth.cs                 # SyncVar-based health, death/respawn
│   ├── Vehicle/
│   │   └── CarController.cs                # Arcade car: NetworkBehaviour, Rigidbody MovePosition, SyncVar driver tracking
│   ├── Networking/
│   │   ├── CustomNetworkManager.cs         # Spawn point rotation
│   │   ├── PlayerSpawnManager.cs
│   │   └── NetworkMessages.cs
│   ├── Weapons/
│   │   ├── WeaponData.cs                   # ScriptableObject weapon definitions
│   │   └── WeaponBase.cs
│   ├── UI/
│   │   ├── HUD.cs
│   │   ├── ScoreboardUI.cs
│   │   ├── PauseMenu.cs
│   │   └── SettingsMenu.cs
│   ├── Config/
│   │   └── GameSettings.cs
│   └── Editor/
│       └── FPSGameSetup.cs
```

## Architecture

### Networking Pattern
- All networked scripts inherit `NetworkBehaviour` (Mirror)
- Player has **client authority** — owns their PlayerInput, PlayerController, PlayerCamera
- Position synced via `NetworkTransformUnreliable` on both player and car prefabs
- State synced via `[SyncVar]` with hooks (health, vehicle driver)
- Client-to-server calls via `[Command]`, server-to-client via `[ClientRpc]`

### Player System
- `PlayerInputHandler` is the central input router — uses Input System **Send Messages** mode (`OnMove`, `OnJump`, `OnInteract`, etc.)
- Only the local player (`isOwned`) processes input; remote players have `PlayerInput` disabled
- `PlayerController` uses `CharacterController` for movement (not Rigidbody)
- `PlayerController.SetVehicleMode(bool)` disables CharacterController and movement when in a vehicle

### Vehicle System
- `CarController` is a `NetworkBehaviour` on the car prefab
- **Arcade movement**: no Y-axis rotation, moves in car-local forward/right via `rb.MovePosition()` in FixedUpdate
- Velocity tracked internally (`currentVelocity` field) with smooth acceleration/deceleration — do NOT use `rb.linearVelocity` directly (network sync components overwrite it)
- **Enter/exit flow**: Player presses Interact → `PlayerInputHandler` sends `[Command]` → server validates & calls `CarController.ServerEnter()` → server assigns client authority over car via `netIdentity.AssignClientAuthority()` → `[SyncVar] driverNetId` hook fires on all clients to hide/show driver
- When driving, `PlayerInputHandler` routes Move input to `CarController.SetMoveInput()` instead of `PlayerController`
- Car prefab requires: Rigidbody (freeze rotation), NetworkIdentity, NetworkTransformUnreliable, CarController, BoxCollider

### Input Actions
Actions defined in `Assets/Game/Input/InputSystem_Actions.inputactions`:
- Move (Vector2), Look (Vector2), Jump (Button), Crouch (Button)
- Sprint (Button), Aim (Button), Attack (Button), Reload (Button)
- Interact (Button) — E key / Gamepad North — used for vehicle enter/exit

## Important Lessons

### Mirror + Rigidbody Sync
- `NetworkRigidbodyUnreliable` overwrites `rb.linearVelocity` every frame even on the authority client — do NOT use it with manual velocity control
- Use `NetworkTransformUnreliable` + `rb.MovePosition()` for physics objects that need manual movement control
- Track velocity in a plain `Vector3` field instead of relying on `rb.linearVelocity`

### Namespace
- All game scripts use `FPSGame.*` namespaces: `FPSGame.Player`, `FPSGame.Vehicle`, `FPSGame.Networking`, `FPSGame.Weapons`, `FPSGame.UI`, `FPSGame.Config`
