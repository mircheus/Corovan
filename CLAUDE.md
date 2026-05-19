# Corovan — Multiplayer Convoy FPS

## Tech Stack
- **Unity 6** (6000.0.60f1)
- **Mirror** networking (Assets/Mirror/)
- **New Input System** (Send Messages mode on PlayerInput component)
- **ParrelSync** for local multiplayer testing
- **ZeroTier** for peer-to-peer LAN networking

## Project Structure

```
Assets/Game/
├── Input/
│   └── InputSystem_Actions.inputactions
├── Prefabs/
│   ├── Player/Player.prefab
│   └── Network/NetworkDebugUI.prefab
├── Scripts/
│   ├── Player/
│   │   ├── PlayerInputHandler.cs    # Central input router; vehicle/carry/interact dispatch
│   │   ├── PlayerController.cs      # CharacterController FPS movement, crouch, sprint, jump
│   │   ├── PlayerCamera.cs          # First-person mouse look, ADS FOV, recoil
│   │   ├── PlayerShooting.cs        # Multi-weapon system, ammo, reload, melee/ranged
│   │   ├── PlayerHealth.cs          # SyncVar health, death/respawn, optional regen
│   │   └── PlayerCarry.cs           # Pick up, drop, throw CarryableObjects
│   ├── Vehicle/
│   │   ├── CarController.cs         # Arcade car: MovePosition, authority assignment, vehicle health
│   │   └── CargoTrigger.cs          # Trigger child on car; deposits loot into ScoreManager
│   ├── Environment/
│   │   ├── Enemy.cs                 # Turret enemy: detects players/cars, rotates, fires projectiles
│   │   ├── EnemyProjectile.cs       # Server-spawned projectile
│   │   ├── CarryableObject.cs       # Networked loot: pickup/drop/throw with client authority
│   │   ├── InteractableObject.cs    # Hold-E interaction (doors etc.) via UnityEvent
│   │   ├── DestroyableObject.cs     # Hit-count destruction (SyncVar)
│   │   ├── DestructionTarget.cs     # Single destructible tracked by DestructionObjective
│   │   ├── DestructionObjective.cs  # Tracks N targets; fires UnityEvent when complete
│   │   ├── DoorOpener.cs
│   │   └── InfiniteRoad.cs          # Client-side scrolling tile recycler (no networking)
│   ├── Hazards/
│   │   ├── Barrel.cs                # Pooled kinematic barrel; bounces on AnimationCurve, damages car/player
│   │   └── BarrelSpawner.cs         # Server-side object pool + timed spawn loop
│   ├── Networking/
│   │   ├── CustomNetworkManager.cs  # Spawn point rotation
│   │   ├── PlayerSpawnManager.cs
│   │   ├── NetworkMessages.cs
│   │   ├── NetworkDebugUI.cs        # Runtime connection info overlay
│   │   └── ScoreManager.cs          # Singleton NetworkBehaviour; SyncVar totalScore
│   ├── Weapons/
│   │   ├── WeaponData.cs            # ScriptableObject: damage, spread, recoil, reload, melee flag
│   │   └── WeaponBase.cs
│   ├── Animations/
│   │   └── WheelAnimation.cs
│   ├── UI/
│   │   ├── HUD.cs
│   │   ├── ScoreboardUI.cs
│   │   ├── PauseMenu.cs
│   │   └── SettingsMenu.cs
│   ├── Config/
│   │   └── GameSettings.cs
│   ├── Utility/
│   │   ├── SceneReloader.cs         # F9 hot-reload
│   │   ├── AutoTile.cs
│   │   ├── AxisSpacer.cs
│   │   └── TestEvent.cs
│   └── Editor/
│       └── FPSGameSetup.cs
├── Scenes/
│   ├── JoinMenu.unity
│   ├── Road.unity                   # Main gameplay scene (infinite road + convoy)
│   ├── SampleScene.unity
│   ├── Test.unity                   # (untracked, WIP)
│   └── WagonsConstruct.unity        # (untracked, WIP)
```

## Architecture

### Namespace Convention
All game scripts: `FPSGame.<Folder>` — `FPSGame.Player`, `FPSGame.Vehicle`, `FPSGame.Environment`, `FPSGame.Hazards`, `FPSGame.Networking`, `FPSGame.Weapons`, `FPSGame.UI`, `FPSGame.Config`

### Networking Pattern
- All networked scripts inherit `NetworkBehaviour` (Mirror)
- Client-to-server: `[Command]`, server-to-client: `[ClientRpc]`, server-only: `[Server]`
- State synced via `[SyncVar]` with hooks
- Position synced via `NetworkTransformUnreliable` on player and car prefabs
- Server validates all game-state mutations; clients send intent via Commands

### Player System
- `PlayerInputHandler` is the sole input router (Input System **Send Messages** mode: `OnMove`, `OnLook`, `OnJump`, `OnCrouch`, `OnInteract`, `OnReload`, `OnPrevious`, `OnNext`)
- Only local player (`isOwned`) processes input; remote players have `PlayerInput` disabled in `OnStartClient`
- `PlayerController` uses `CharacterController` — disabled for remote players (NetworkTransform handles their position)
- `PlayerController.SetVehicleMode(bool)` disables CharacterController + zeroes velocity when entering a vehicle
- `PlayerCarry` manages loot carrying; while carrying, LMB=drop, RMB=throw; normal shooting blocked
- `PlayerShooting` supports multiple `WeaponData` slots with per-slot saved ammo; optimistic local weapon switch + server SyncVar confirmation

### Vehicle System
- `CarController` is a `NetworkBehaviour` on the car prefab
- **Arcade movement**: `rb.MovePosition()` in FixedUpdate; velocity tracked in `currentVelocity` (do NOT use `rb.linearVelocity` — network sync overwrites it)
- **Enter/exit flow**: Interact key → `CmdEnterVehicle` → `car.ServerEnter()` → `netIdentity.AssignClientAuthority(conn)` → `[SyncVar] driverNetId` hook hides/shows driver on all clients
- Vehicle has health (`vehicleHealth` SyncVar); enemies and barrels call `car.TakeDamage()`
- `CargoTrigger` child (trigger collider, no Rigidbody) calls `car.ServerDepositLoot()` when a carried object enters — adds to `ScoreManager`

### Weapon System
- `WeaponData` ScriptableObject: `Damage`, `Range`, `FireRate`, `MagazineSize`, `MaxReserveAmmo`, `ReloadTime`, `BaseSpread`, `AdsSpreadMultiplier`, `MovementSpreadMultiplier`, `IsAutomatic`, `IsMelee`, `MeleeRadius`, recoil pattern, `ImpactEffectPrefab`
- Ranged: Raycast on server; `RpcOnHit` spawns impact effect on all clients
- Melee: SphereCast for wide sweep; no ammo consumed
- Weapon switch: optimistic local model swap → `CmdSwitchWeapon` → SyncVar hook confirms on all clients

### Enemy System
- `Enemy` (NetworkBehaviour): SphereCollider trigger for detection, server-only Update loop
- Targets either `PlayerHealth` (on foot) or `CarController` (driven vehicle)
- Fires `EnemyProjectile` via `NetworkServer.Spawn`; optional `keepAttackAfterTriggerExit` flag
- `DestructionObjective` monitors an array of `DestructionTarget`s; fires a UnityEvent when enough are destroyed

### Hazard System
- `BarrelSpawner`: server-side object pool (`poolSize` pre-spawned barrels). Returns barrel to pool after despawn delay
- `Barrel`: kinematic Rigidbody, moves via `rb.MovePosition()`; bounces using an `AnimationCurve`; damages car or player on trigger contact, then deactivates

### Interaction System
- `InteractableObject`: hold-E with configurable `HoldDuration`; progress tracked in `PlayerInputHandler`; on completion `CmdInteract` → `ServerInteract` → `RpcOnInteracted` → UnityEvent fires on all clients
- `CarryableObject`: pickup assigns client authority; drop/throw removes it; `SetCarriedLocally` gives instant feedback; SyncVar confirms on other clients

### Score / Objective
- `ScoreManager`: singleton `NetworkBehaviour`; `[SyncVar] totalScore`; `AddScore(int)` called server-side when loot deposited via `CargoTrigger`

## Important Lessons

### Mirror + Rigidbody Sync
- `NetworkRigidbodyUnreliable` overwrites `rb.linearVelocity` every frame — do NOT use it with manual velocity control
- Use `NetworkTransformUnreliable` + `rb.MovePosition()` for physics objects with manual movement
- Track velocity in a plain `Vector3` field instead of `rb.linearVelocity`

### Client Authority Pattern
- Car and CarryableObject both use `netIdentity.AssignClientAuthority(conn)` / `RemoveClientAuthority()`
- Always validate on the server that the requesting connection actually holds authority before acting

### Animator + NavMeshAgent / Manual Movement
- Always disable **Apply Root Motion** on any enemy Animator that uses NavMeshAgent or manual transform movement — root motion overrides `transform.position` and `transform.rotation` every frame, fighting both NavMeshAgent pathfinding and manual `FaceTarget` rotation
- With root motion disabled, the Animator drives only visual bone poses; all locomotion is code-controlled

### Input System
- `PlayerInput` in Send Messages mode — callback methods must be named `On<ActionName>(InputValue value)`
- Disable `PlayerInput` on remote players in `OnStartClient` (not `OnStartAuthority`) to avoid double-processing

## Current State (2026-05-12)

See `PROJECT_STATE.md` for the full system-by-system inventory. Summary:

### Working
- Core gameplay loop: join → drive convoy car → carry loot into cargo (`CargoTrigger`) → `ScoreManager` updates
- FPS player: movement/crouch/sprint/jump, camera (ADS FOV, recoil, head bob), multi-weapon shooting (ranged + melee, per-slot ammo, reload, optimistic switch), health/damage/regen/death
- Carry/throw loot props (client-authority pickup)
- Vehicle: arcade driving, authority transfer on enter/exit, vehicle health → destruction (fire + explosion FX, kills driver), collision damage, driverless backward drift
- Enemy turrets shoot at players and driven vehicles; barrel hazard spawner (server pool) on Road scene
- Destruction objective system (`DestructionObjective` / `DestructionTarget`)
- Hold-E interactables, `WindZone` push, `KillVolume` instant-kill
- Floor-cutting tool: runtime mesh hole (`FloorMeshModifier`) + `Safe` falling through (`SafeFallTrigger`, `SafeMount` car attach)
- `LocalPlayerBodyHider` (hide own body from FP camera)
- ZeroTier for remote peers; `NetworkDebugUI` connection overlay; F9 scene hot-reload (`SceneReloader`)

### In progress
- **Vehicle remote control** (`RemoteVehicleControlTool` + `CarController` autopilot: `ServerStartAutopilot`/`ServerStopAutopilot`, `autopilot*` SyncVars, L-path `ServerTickAutopilot`). Code complete; not yet wired into `Player.prefab` / `PlayerShooting.weaponTools[]`. See PROJECT_STATE.md §4.

### Exists in code but NOT integrated
- `ZombieEnemy` — full NavMesh state machine, not spawned in any scene
- Grappling hook (`GrapplingHookController` / `GrapplingHookInput`) — **local-only prototype, no networking**: only the owner's resulting position syncs (via NetworkTransform); grapple state, target, rope visual, and server validation are absent
- `NetworkMessages.cs` structs — declared, never sent/handled
- `WeaponBase` abstract class — unused; `PlayerShooting` does firing inline

### Known gaps
- No respawn flow (`PlayerHealth.Respawn` exists server-side, no caller)
- No match win/lose conditions (`MatchStateMessage` unused)
- Vehicle destruction has no wreck/persistent state beyond explosion FX + `vehicleIsAlive`
- `Enemy.OnAliveChanged` hook empty (no turret death visuals)
- `Safe` has no loot drop / visual change on destruction
- UI stubs: `PauseMenu.OpenSettings`/`Disconnect`, `GameOverUI.OnRestartButtonClick`
- `GameSettings` not networked / not persisted per client
- `PlayerSpawnManager.FurthestFromPlayers` ignores `minSpawnDistance`
- WIP scenes (untracked): `Test.unity`, `WagonsConstruct.unity`, `Wall Laser.unity`, `GrapplingHook_Test.unity`
