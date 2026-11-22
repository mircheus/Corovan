# Corovan — Multiplayer Convoy FPS

## Tech Stack
- **Unity 6** (6000.0.60f1)
- **Mirror** networking (Assets/Mirror/)
- **New Input System** (Send Messages mode on PlayerInput component) + FPSF's generated `Controls`
- **FPS Framework** (Akila, v1.92) — forked in `Assets/ExternalAssets/FPS Framework/`; every edit marked `// CORVAN:`
- **ParrelSync** for local multiplayer testing
- **ZeroTier** for peer-to-peer LAN networking
- **Unity Pipeline** (`com.unity.pipeline`) — lets the Unity CLI drive the open Editor (prefab edits, compile checks)

## Project Structure

```
Assets/Game/
├── Input/
│   └── InputSystem_Actions.inputactions
├── Prefabs/
│   ├── Player/Player.prefab         # + FPSF camera rig, Inventory loadout, RemoteWeaponAnchor
│   ├── Enemies/RagdollBot.prefab    # FPSF Bot dummy with networked health + ragdoll
│   ├── Weapons/                     # *_Pickup (WeaponPickup), NetGrenade (networked FPSF grenade)
│   ├── UI/FPSF_CombatHUD.prefab     # FPSF UIManager + hit marker + damage indicator
│   └── Network/NetworkDebugUI.prefab
├── Scripts/
│   ├── Player/
│   │   ├── PlayerInputHandler.cs    # Game-action router (interact/vehicle/carry/revive); owns FPSF input block
│   │   ├── PlayerController.cs      # CharacterController movement; reads FPSF CharacterInput, is its ICharacterController
│   │   ├── PlayerCamera.cs          # Pitch/yaw from FPSF CharacterInput, cursor, aim ray
│   │   ├── PlayerLoadout.cs         # Weapon/tool slots over the FPSF Inventory; unlocks, hands-busy, HUD feed
│   │   ├── LoadoutItem.cs           # Slot marker (id, default unlock) on each Inventory item
│   │   ├── MeleeItem.cs / ToolItem.cs # Axe; tool slot enabling a tool Behaviour
│   │   ├── PlayerHitReporter.cs     # Client hits -> server (validation, friendly fire, kill confirm)
│   │   ├── PlayerGrenades.cs        # FPSF throw -> server-spawned NetGrenade
│   │   ├── RemoteWeaponView.cs      # Third-person placeholder weapon + replayed shot FX
│   │   ├── RemotePlayerHitbox.cs    # PlayerHitbox capsule on other players' copies
│   │   ├── FPSFPlayerBridge.cs      # FPSF runs on the owner only
│   │   ├── PlayerDamageFeedback.cs  # FPSF damage flash + camera shake
│   │   ├── PlayerHealth.cs          # SyncVar LifeState (Alive/Downed/Dead), bleedout, optional regen
│   │   ├── PlayerRevive.cs          # Hold-E revive of a downed teammate; server-validated
│   │   ├── PlayerRespawnPoint.cs    # Scene marker: nearest one is where a player respawns (no free car seat)
│   │   ├── PlayerDownedVisual.cs    # Standing/lying model swap + downed marker on all clients
│   │   └── PlayerCarry.cs           # Pick up, drop, throw CarryableObjects
│   ├── Combat/
│   │   ├── IDamageReceiver.cs       # int damage target (+ IHitForceReceiver)
│   │   ├── DamageRouter.cs          # Single server-side entry for weapon damage
│   │   └── FPSFDamageable.cs        # Our receivers as FPSF IDamageable
│   ├── Enemies/
│   │   ├── RagdollEnemy.cs          # Networked FPSF Bot dummy (ragdoll on death, respawn)
│   │   ├── ShooterDroid.cs          # RagdollBot that chases on foot players and shoots projectiles
│   │   ├── EnemySpawner.cs          # Abstract server spawn loop (activation, alive cap, spawn points) + director requests
│   │   ├── ShooterDroidSpawner.cs   # EnemySpawner for droids
│   │   ├── MeleeZombie.cs           # RagdollBot-variant zombie: chases players on foot, scratches on an animation event
│   │   ├── MeleeZombieSpawner.cs    # EnemySpawner for MeleeZombie
│   │   ├── EnemyCarSpawner.cs       # EnemySpawner for EnemyCarController prefabs (VehicleAgent NavMesh, aggro on driven cars)
│   │   ├── EnemyDirector.cs         # Two parallel wave tracks (platform / vehicles): Combat {spawner,count}[] or Pause
│   │   ├── AgentLinkJumper.cs       # NavMesh links crossed as a parabolic jump (server; zombie + droid)
│   │   ├── EnemyCarController.cs    # EnemyCar core: health, death/wreck, target registration + selection, proximity aggro
│   │   ├── EnemyCarBehaviour.cs     # Abstract per-prefab behaviour (movement + attack) the controller ticks on the server
│   │   ├── GunnerCarBehaviour.cs    # Chase + gun turret + EnemyProjectile (the original EnemyCar)
│   │   ├── ObstacleLayerCarBehaviour.cs # Unarmed: holds a Z band ahead of the target, lines up on X, drops RoadObstacles
│   │   ├── ObstacleLayerZoneCarBehaviour.cs # Branch of the above: attack point while a player car is in the zone, else safe point + wait
│   │   ├── ObstacleLayerZone.cs     # Scene setup for it: attack point, safe point, zone collider (spawned cars take the nearest)
│   │   └── EnemyAggroZone.cs        # Trigger zone → EnemyCarController.RegisterTarget
│   ├── Vehicle/
│   │   ├── CarController.cs         # Arcade car: MovePosition, authority assignment, vehicle health
│   │   ├── CarMachineGun.cs         # Car's forward hitscan gun: single shots, car-owned synced ammo
│   │   ├── CargoTrigger.cs          # Trigger child on car; Deposit (destroy + score) or Stow (pin loot to the car + score)
│   │   ├── CarPassengerSeat.cs      # Passenger seat: E (driver seat taken) from the roof / mid-jump / next to the car; E to leave
│   │   ├── PlayerCarSpawner.cs      # Call-a-car panel: hold-E spawns the car at a point, blocked while a player car is alive / area occupied
│   │   └── VehicleWheelVisuals.cs   # Any vehicle: constant wheel spin + steering wheel from sideways movement; cosmetic, all peers
│   ├── Environment/
│   │   ├── Enemy.cs                 # Turret enemy: detects players/cars, rotates, fires projectiles
│   │   ├── EnemyProjectile.cs       # Server-spawned projectile
│   │   ├── CarryableObject.cs       # Networked loot: pickup/drop/throw with client authority
│   │   ├── InteractableObject.cs    # Hold-E interaction (doors etc.) via UnityEvent
│   │   ├── DestroyableObject.cs     # Hit-count destruction (SyncVar)
│   │   ├── DestructionTarget.cs     # Single destructible tracked by DestructionObjective
│   │   ├── DestructionObjective.cs  # Tracks N targets; fires UnityEvent when complete
│   │   ├── ExtractionZone.cs        # Win condition: hold the driven car in the zone N seconds (IWinCondition)
│   │   ├── DoorOpener.cs            # Sliding door (rises/lowers at moveSpeed, pose from NetworkTime); auto-close; never closes on anyone
│   │   ├── InfiniteRoad.cs          # Client-side scrolling tile recycler (no networking)
│   │   └── CodeVault/               # Scenario "code from the radar" (layer 0), see Code Vault below
│   │       ├── DecryptParkingZone.cs  # Box + anchor; server-polled ParkingStatus (no car / no driver / too far / dashing)
│   │       ├── CodeVaultController.cs # Server state machine Idle→Decrypting⇄Paused→InputWindow→Solved; opens a DoorOpener
│   │       ├── CodeConsole.cs         # World keypad: Cmd(requiresAuthority=false) → ServerPressDigit → RpcFeedback; screen
│   │       ├── CodeConsoleButton.cs   # One key (digit + collider), picked by PlayerInputHandler's aim ray
│   │       └── LootReturnToVault.cs   # Loot that hits the road goes back to the vault after a delay
│   ├── Hazards/
│   │   ├── Barrel.cs                # Pooled kinematic barrel; bounces on AnimationCurve, damages car/player
│   │   ├── BarrelSpawner.cs         # Server-side object pool + timed spawn loop
│   │   ├── Explodable.cs            # Stationary Barrel: damage threshold / car-player contact → explosion FX, hide, onExploded
│   │   ├── FlameHazard.cs           # Fire zone: periodic damage to players/cars in its trigger; optional on/off cycle, public TurnOn/TurnOff
│   │   └── FlameHazardController.cs # Runs a FlameHazard[] through looping step patterns (which flames, duration, pause); server-only
│   ├── Networking/
│   │   ├── CustomNetworkManager.cs  # Spawn point rotation; registers players with GameStateManager
│   │   ├── GameStateManager.cs      # Singleton NetworkBehaviour; SyncVar MatchResult, defeat on squad wipe
│   │   ├── IWinCondition.cs         # Server-polled win requirement (stub — no implementors yet)
│   │   ├── PlayerSpawnManager.cs
│   │   ├── NetworkMessages.cs
│   │   ├── NetworkDebugUI.cs        # Runtime connection info overlay
│   │   └── ScoreManager.cs          # Singleton NetworkBehaviour; SyncVar totalScore
│   ├── Weapons/
│   │   ├── WeaponData.cs            # ScriptableObject; only Axe.asset still used (MeleeItem)
│   │   └── NetworkExplosive.cs      # Server-owned FPSF Explosive (grenade)
│   ├── Animations/
│   │   └── WheelAnimation.cs
│   ├── UI/
│   │   ├── HUD.cs
│   │   ├── MatchEndUI.cs            # Victory/defeat screen + host-only restart
│   │   ├── ExtractionUI.cs          # Progress bar while the car is held in the ExtractionZone
│   │   ├── LocalPlayerDeadUI.cs     # "You died" overlay while the match is still running
│   │   ├── RoadCrashOverlay.cs      # Code-built blackout + message + countdown after a fall onto the road
│   │   ├── ScoreboardUI.cs
│   │   ├── PauseMenu.cs
│   │   └── SettingsMenu.cs
│   ├── Config/
│   │   ├── GameSettings.cs
│   │   ├── BalanceConfig.cs         # Per-scene balance SO (player max health; grows as needed)
│   │   └── SceneBalance.cs          # Scene component: picks the BalanceConfig (static Current)
│   ├── Utility/
│   │   ├── SceneReloader.cs         # F9 hot-reload
│   │   ├── ObjectSpawner.cs         # Universal spawner: prefab at own position every N s, hotkey toggle (networked prefab → server only)
│   │   ├── AutoTile.cs
│   │   ├── AxisSpacer.cs
│   │   └── TestEvent.cs
│   └── Editor/
│       ├── FPSGameSetup.cs
│       ├── NavMeshDropLinkGenerator.cs # Finds ledges on the baked NavMesh, places one-way NavMeshLinks (drop down)
│       └── NavMeshDropLinkWindow.cs    # FPS Game → Navigation → Drop Links Generator
├── Scenes/
│   ├── JoinMenu.unity
│   ├── Road.unity                   # Main gameplay scene (infinite road + convoy)
│   ├── SampleScene.unity
│   ├── Test.unity                   # (untracked, WIP)
│   └── WagonsConstruct.unity        # (untracked, WIP)
```

## Architecture

### Namespace Convention
All game scripts: `FPSGame.<Folder>` — `FPSGame.Player`, `FPSGame.Vehicle`, `FPSGame.Environment`, `FPSGame.Hazards`, `FPSGame.Networking`, `FPSGame.Weapons`, `FPSGame.Combat`, `FPSGame.Enemies`, `FPSGame.UI`, `FPSGame.Config`. FPS Framework code stays in `Akila.FPSFramework`.

### FPS Framework Integration
FPSF is single-player code; we use its camera feel, weapons, grenades, ragdoll and combat HUD, and keep our own movement, health and game logic.
- **Owner only.** Every FPSF component reads the keyboard through its own `Controls` and drives cameras/UI. `FPSFPlayerBridge` disables all of them (except `Ragdoll`) on other players' copies in `OnStartClient`, before their `Start`. Inventory items are stored **inactive** in `Player.prefab` so FPSF per-item `Awake` (input, Weapon HUD canvas) runs only when the owner's `PlayerLoadout` activates one.
- **Fork, marked.** Edits to FPSF files carry `// CORVAN:` so an FPSF update can be merged by hand. Hooks we added: `Firearm.ResolveDamageable` / `Firearm.ShotFired`, `Throwable.ThrowOverride`, `Explosive.onExploded`, `CharacterInput.inputBlocked`, `CameraManager.fieldOfViewOffset` (extra FOV on top of the target, used by `PlayerCamera.PunchFOV`).
- **Movement:** our `PlayerController` reads FPSF `CharacterInput` and implements `ICharacterController`; FPSF's `FirstPersonController` is not used. `PlayerCamera` takes look from `CharacterInput.lookInput` (carries FPSF recoil); FOV/ADS/head bob belong to FPSF `CameraManager`.
- **Input split, no duplicate keys:** FPSF `Controls` = move/look/jump/crouch/sprint, fire/aim/reload/fire mode, slots. Our `InputSystem_Actions` = Interact (E), Attack/Aim (carry/keycard), Previous/Next. FPSF lean and Drop (B) are unbound. Block FPSF input with `CharacterInput.inputBlocked` (owned by `PlayerInputHandler`: paused OR dead/match over) — never by disabling `CharacterInput` (values would freeze).
- **Hits are client-authoritative.** The owner's FPSF weapon/axe decides the hit; `FPSFDamageable` → `PlayerHitReporter.CmdReportHit` (server checks shooter alive, distance, damage clamp) → friendly-fire multipliers → `DamageRouter`. Never replay an FPSF projectile on observers — on the host it would deal damage again (`RemoteWeaponView` draws a tracer instead).
- **Grenades are server objects:** `ThrowOverride` → `PlayerGrenades.CmdThrow` → `NetGrenade` (`NetworkExplosive`); only the server runs fuse + blast; clients play effects on `RpcExploded`. Register runtime-spawned prefabs in `CustomNetworkManager.runtimeSpawnPrefabs` — scenes override `spawnPrefabs`.
- **Layers** (FPSF layers were remapped onto ours): 12–20 = `FPSF ...` (18 `FPSF Hidden` = viewmodel, drawn by the stacked Overlay Camera; the main camera skips 12/18/19), 21 = `PlayerHitbox` (collides with nothing, only catches rays).
- **Editing prefabs from the CLI** (`unity command run_script`): keep every object in one prefab/preview scene (`MoveGameObjectToScene` before `SetParent`), never remap references generically through `SerializedObject` (hidden fields corrupt the hierarchy and crash the Editor), try on a copy first.

### Down / Revive System
- `PlayerHealth` holds a single `[SyncVar] LifeState { Alive, Downed, Dead, Crashed }` — never two bools. Hook order can't then report "revived" for a player who was finished off while down.
- Damage that empties health calls `GoDown()` instead of dying. While `Downed`, `TakeDamage` is a no-op — the bleedout timer is the only thing that ends it.
- **Respawn.** A bleedout that runs out (`bleedoutDuration`, 15 s on `Player.prefab`) calls `PlayerHealth.ServerRespawn()` (`respawnOnBleedout`; off = dies as before): full health, Downed → Alive (clients raise `OnRevived`). `ServerKillInstant()` skips the downed state → `Dead` with a respawn deadline (`respawnTime` SyncVar, `deadRespawnDelay` 15 s, < 0 = never; `RespawnRemaining` for UI) → Dead → Alive (`OnRespawn`). No respawn once the match is over (bleedout then kills). `ServerRespawn` flips the state first, then `PlayerInputHandler.ServerRelocateAfterRespawn` picks the spot: the nearest free `CarPassengerSeat` of a car another alive player drives (`ServerFindRespawnSeat` → `ServerSeatRespawned`, no reach check → `TargetRespawnInSeat`), else the nearest `PlayerRespawnPoint` scene marker, else `CustomNetworkManager.GetNextSpawnPoint` (→ `TargetRespawnAt`, owner teleports + faces the marker's yaw). Death locks are reversible: `PlayerInputHandler.deathLocked` (FPSF input + `PlayerInput`), `PlayerLoadout.SetDead`, `PlayerCamera.isDead`; `DisableForGood` / `fpsfInputLockedForGood` are match-end only. `LocalPlayerDeadUI.respawnText` (optional) shows the countdown.
- **Crash onto the road** (`FallHazardVolume`, detected owner-side by `PlayerFallHazard`): loot dropped, body frozen in place (`PlayerController.SetFrozen` — CC off, no movement/gravity), local `RoadCrashOverlay` (built in code, sorting 500: fade to black + the volume's `crashMessage` + countdown) → `CmdCrash` → `PlayerHealth.ServerCrash(fallDamage, instantKill)`: health − damage, `Crashed` for `deadRespawnDelay` (counts as incapacitated → defeat check via `OnCrashed`; `PlayerDownedVisual` hides the body), then `ServerRespawn` **keeps the remaining health** and relocates like any respawn. A lethal fall, an `instantKill` volume or a fall while downed is `Die()` instead (full health on respawn). A dead body that falls in is only frozen. The old instant snap-back (`LastSafePoint` / `MotionDirector.FallRespawnPoint`) is gone. After the respawn `cooldown` ignores the volume: on a host the `OnRespawn` hook unfreezes the body at the fall spot before the move RPC arrives.
- `ServerKillInstant()` skips the downed state. Use it wherever a revive makes no sense: `CarController.ServerExplode`.
- Bleedout is synced as a deadline (`bleedoutEndTime`, a `NetworkTime.time` double) plus a pause flag — never as a per-frame countdown. Clients derive the remaining seconds themselves via `BleedoutRemaining`. A revive in progress pauses it, so a hold started at the last second still finishes.
- `PlayerRevive` on the player prefab owns the hold. The reviving client times it (reusing `PlayerInputHandler`'s hold machinery and `OnInteractProgress`), but the server re-validates the reviver, the range, and that the hold really lasted `reviveHoldDuration`. A server-side watchdog in `PlayerRevive.Update` cancels the revive if the reviver dies, goes down, walks off, or disconnects.
- Only the first reviver owns a hold; a second player can't stack onto it to speed it up.
- Enemies filter targets on `IsTargetable` (alive only), so a downed player is ignored. `IsIncapacitated` (down **or** dead) is the check for "can this player still act / hold a console".
- `Enemy` re-sweeps its detection volume on `retargetInterval` while it has no target — `OnTriggerEnter` alone would never re-acquire a revived player who never left the trigger.
- A downed player keeps `PlayerInput` enabled (they still look around) and keeps their `CharacterController` (gravity + `MotionPlatform` ride still apply). Movement is blocked inside `PlayerController`'s setters, not by disabling components.

### Match State (Win / Lose)
- `GameStateManager` is a scene singleton `NetworkBehaviour` (same shape as `ScoreManager`) holding a single `[SyncVar] MatchResult { InProgress, Victory, Defeat }` — one enum, not a phase bool plus a result bool, for the same hook-ordering reason as `LifeState`.
- **Defeat** = no registered player is `Alive`. `IsIncapacitated` is the test, so a mix of downed and dead players still loses — a dead teammate can't revive anyone either. Evaluated on `OnDowned`/`OnDeath` and on disconnect, never polled.
- The roster is owned by `CustomNetworkManager` (`OnServerAddPlayer` / `OnServerDisconnect` → `ServerRegisterPlayer` / `ServerUnregisterPlayer`), not by `PlayerHealth` registering itself — a player who never finished spawning can't leave a dangling subscription.
- An empty roster never loses: `ServerReevaluateDefeat` returns early on `serverPlayers.Count == 0`, covering both the gap before the first join and the last disconnect.
- **Victory**: `GameStateManager` polls a serialized `MonoBehaviour[]` of `IWinCondition` on `winCheckInterval` (`requireAllWinConditions` = all vs any). `ExtractionZone` is the first implementor — hold the driven, alive car inside its trigger for `captureDuration` continuous seconds; it latches `IsMet` and the poll flips the SyncVar. `ServerEndMatch` / `ServerTriggerVictory` remain public for an objective to force an outcome directly.
- `ExtractionZone` (scene `NetworkBehaviour`, needs a `NetworkIdentity`): tracks overlapping car colliders in a `Dictionary<Collider,CarController>` (per-collider, so a partial exit doesn't drop a car still inside); `HasQualifyingCar` re-checks `HasDriver`/`VehicleIsAlive` each server tick. Capture is all-or-nothing — the deadline resets the instant the car stops qualifying. Countdown synced as a deadline (`captureEndTime`) + `capturing` flag, like bleedout; `CaptureProgress` (0–1) and `OnCapturingChanged` drive `ExtractionUI` (scene-level progress bar, shown only while capturing). `MatchEndUI` shows `ScoreManager.TotalScore` on victory only.
- Ending the match **does not stop the world** — turrets keep firing, `BarrelSpawner` keeps spawning, bleedout keeps ticking. It only flips the SyncVar. Clients react in `PlayerInputHandler.OnMatchEnded` (disable `PlayerInput`, block FPSF input, empty the hands, drop holds, exit vehicle, unlock cursor) and `MatchEndUI`.
- Restart reloads the active scene via `ServerRestartMatch`; the button is only shown when `NetworkServer.active`. `GameStateManager` is a scene object, so the roster and result reset themselves.
- `MatchEndUI` is separate from `LocalPlayerDeadUI` — the latter reacts to the local player's own death while the match is still running (a teammate may still win or lose it), and hides itself when the match ends so `MatchEndUI` owns the screen. It binds to the runtime local player via `NetworkClient.localPlayer` (no serialized player reference is possible on a scene object).

### Damage & Health Values
- All damage and health values are `int` — damage fields, `health`/`maxHealth` fields, `TakeDamage(int)` parameters, projectile/weapon damage. Never use `float` for a damage or health value.
- `PlayerHealth` regen accumulates fractional HP in a private `float regenBuffer` and applies whole `int` points; `regenerationRate` stays `float` (a per-second rate). `HealthPercentage` casts to `float` for the division.
- Anything weapons can hurt implements `IDamageReceiver` (found with `GetComponentInParent` from the hit collider) and is damaged through `DamageRouter.ServerApplyHit` — no per-type branches in weapon code. `DestroyableObject` and `DestructionTarget` take real damage (`maxHealth`, SyncVar `currentHealth`) and play their `destructionEffects` via an RPC on destruction (`DestructionEffects.Play`; an RPC so late joiners don't replay them). Effects are inactive Play-On-Awake children, activated in place — never instantiated. Both hide their `Renderer`s/`Collider`s (cached in `Awake`, before any effect is active) instead of deactivating the object. FPSF's float damage is rounded once, in `FPSFDamageable`.

### Scene Balance Config
- `BalanceConfig` (SO, `Data/Balance/`: `Balance_Default`, `Balance_Test`) holds per-scene values; `SceneBalance` in a scene points at one and exposes it as static `SceneBalance.Current`. No `SceneBalance` (or no config on it) = prefab values stay.
- **Pull, not push:** receivers read `SceneBalance.Current` when they spawn (players and enemies appear after scene load). The server applies it in `OnStartServer`; a value clients also need (e.g. `PlayerHealth.maxHealth` for the HUD) is a SyncVar so everyone uses the server's value. The scene exists on every peer, so client-side values (future weapon damage on the owner) can read it too.
- Add fields one at a time, in sections. Applied so far:
  - `player.maxHealth` → `PlayerHealth` (server, SyncVar).
  - `player.pistolDamage/shotgunDamage/rifleDamage` → `PlayerLoadout.ApplyWeaponBalance` (owner, matched by `LoadoutItem` id). FPSF `preset` is a shared asset: the firearm gets its own `Instantiate`d copy — never write into the original (in the Editor that would edit the asset). Semantics = FPSF `damage` (shotgun: whole shot split between pellets). Server still clamps each reported hit to `PlayerHitReporter.maxDamagePerHit`.
  - `shooterDroid.maxHealth/projectileDamage/projectileSpeed` → `ShooterDroid.ApplyBalance` (server) → `RagdollEnemy.ServerSetMaxHealth` (`maxHealth` is a SyncVar). The plain `RagdollBot` dummy is not configured.
  - `enemyCar.maxHealth/projectileDamage/projectileSpeed` → `EnemyCarController.ApplyBalance` (server).
  - `zombie.maxHealth/attackDamage` → `MeleeZombie.ApplyBalance` (server) → `RagdollEnemy.ServerSetMaxHealth`.

### Networking Pattern
- All networked scripts inherit `NetworkBehaviour` (Mirror)
- Client-to-server: `[Command]`, server-to-client: `[ClientRpc]`, server-only: `[Server]`
- State synced via `[SyncVar]` with hooks
- Position synced via `NetworkTransformUnreliable` on player and car prefabs
- Server validates all game-state mutations; clients send intent via Commands

### Player System
- `PlayerInputHandler` routes our game actions (Send Messages: `OnInteract`, `OnPrevious`, `OnNext`; `Attack`/`Aim` polled for carry/keycard; digit keys for slots). Movement/look/weapon input is FPSF's (see FPS Framework Integration); in a car it steers from `CharacterInput.moveInput`
- Only local player (`isOwned`) processes input; remote players have `PlayerInput` disabled in `OnStartClient`
- `PlayerController` uses `CharacterController` — disabled for remote players (NetworkTransform handles their position); other players are hit through `RemotePlayerHitbox`
- `PlayerController.SetVehicleMode(bool)` disables CharacterController + zeroes velocity when entering a vehicle
- `PlayerCarry` manages loot carrying; while carrying, LMB=throw, RMB=drop; weapons are put away
- `PlayerLoadout` owns the weapon slots (FPSF Inventory children, keys 1–9). Unlocks are server state (`unlockedMask`, `WeaponPickup` grants an item id); the slot in hand is the owner's, mirrored as `equippedSlot`. Anything that occupies the hands calls one of its busy setters (`SetCarryingMode`, `SetDowned`, `SetInVehicle`, `SetPaused`, `DisableForGood`) — nothing is in hand while any is set

### Vehicle System
- `CarController` is a `NetworkBehaviour` on the car prefab
- **Arcade movement**: `rb.MovePosition()` in FixedUpdate; velocity tracked in `currentVelocity` (do NOT use `rb.linearVelocity` — network sync overwrites it)
- **Enter/exit flow**: Interact key → `CmdEnterVehicle` → `car.ServerEnter()` → `netIdentity.AssignClientAuthority(conn)` → `[SyncVar] driverNetId` hook hides/shows driver on all clients
- **Dash**: Shift + A/D (sideways, `dashForce`) or Shift + W/S (forward/backward, `dashForwardForce`/`dashBackwardForce`; 0 = off) → `CarController.TryDash(moveInput)` (owner-only, dominant axis wins, tie = sideways, one shared `dashCooldown`; `DashDuration` = longest of the three). Impulse along local X/Z kept in its own `dashVelocity` (decays at `dashDecay`, counts toward collision impact speed); on success `PlayerInputHandler` calls `PlayerCamera.PunchFOV` with the car's `dashFovPunch`/`dashFovPunchDuration`
- **Wall blocking**: the driven car's move step (any direction, dash included) is checked first by box-casting the car's solid `BoxCollider`s with their bottom raised by `blockStepHeight` (step offset) against `blockingLayers` (default Default/Ground/Obstacles/Floor); the part going into the wall is cut `blockSkin` short of it, the part along it is kept (slide; one more cast for corners), and drive + dash speed into the wall are dropped. `MovePosition` on the dynamic body is a teleport and depenetration (10 m/s) can't keep up with a dash. Don't filter floors by `hit.point` or collider bounds — flat-on-flat contact reports an arbitrary point, and a level mesh's bounds are always high; the raised box keeps floor, seams and curbs out of the cast. Collision damage uses the pre-cancel `lastStepSpeed`. `debugWallBlocking` logs every block (and every hit during a dash)
- **Reload at the wheel** is allowed, but while `PlayerLoadout.IsReloading` (the FPSF firearm in hand is reloading) `PlayerInputHandler` zeroes the steering (`moveInput.x`) and skips `TryDash` — only forward/backward remains. FPSF's `Firearm.ReloadBlocked` hook stays in the fork, unused
- Vehicle has health (`vehicleHealth` SyncVar); enemies and barrels call `car.TakeDamage()`. Its hook raises client-side `OnVehicleHealthChanged`; the driver's `PlayerDamageFeedback` subscribes to the car on `PlayerInputHandler.OnVehicleChanged` (enter/exit/eject) and plays the same red flash + camera shake on car damage. `HUD` subscribes the same way (`OnVehicleChanged` fires for the driver seat and passenger seats) and shows `VehicleHealth` (bar + "МАШИНА hp/max") while the player sits in a car
- **Machine gun** (`CarMachineGun` on `Car_Muscle`): LMB while driving **with empty hands** fires one hitscan shot from `firePoint` along its forward (spread cone + random start offset in the fire point's local X/Y). In a car the hands start empty; the pistol slot key toggles the pistol, scroll cycles empty ↔ pistol (`PlayerLoadout.vehicleSlot`, `HandsFree`) — pistol and gun never share LMB. Owner raycasts (skips own car + driver) → `CmdFire` on the car (driver's authority) → server checks driver alive / ammo / rate / distance, decrements `ammo` (SyncVar, the car's, shared by drivers; `ServerAddAmmo` refills) and applies **its own** `damage` via the driver's `PlayerHitReporter.ServerApplyHit`. FX (flash prefab, built-in `shotEffect` ParticleSystem restarted per shot, sound behind `playFireSound` — off, tracer, impact) local on the owner, `RpcShot` for others. HUD shows `DisplayAmmo` (server ammo minus the owner's shots in flight)
- `CargoTrigger` child (trigger collider, no Rigidbody) calls `car.ServerDepositLoot()` when a carried object enters — adds to `ScoreManager`

### Weapon System
- Firearms are FPSF prefabs (presets in `FPS Framework/Data/Weapons`): fire, recoil, spread, attachments, reload, ammo and Weapon HUD run on the owner; ammo is client-side (FPSF `AmmoProfile`s in the Inventory)
- Axe = `MeleeItem` (camera SphereCast, `Axe.asset` WeaponData); tools = `ToolItem` slots enabling `FloorCutter` / `RemoteVehicleControlTool` (keep those Behaviours disabled in the prefab); grapple = `GrapplingHookController` active while item `grapple` is in hand
- Others see a placeholder world model per slot (`RemoteWeaponView`) and the owner's shots replayed (flash, sound, tracer, impact) — cosmetic only
- Adding a weapon: put the FPSF item under the Inventory (inactive, layer 18) with a `LoadoutItem` id, add its world model to `RemoteWeaponView`, and a `WeaponPickup` with that id

### Enemy System
- `Enemy` (NetworkBehaviour): SphereCollider trigger for detection, server-only Update loop
- Targets either `PlayerHealth` (on foot) or `CarController` (driven vehicle)
- Fires `EnemyProjectile` via `NetworkServer.Spawn`; optional `keepAttackAfterTriggerExit` flag
- `DestructionObjective` monitors an array of `DestructionTarget`s; fires a UnityEvent when enough are destroyed
- `RagdollEnemy` (`RagdollBot.prefab`): server health; death → `RpcDie(push)` flops the FPSF `Ragdoll` on every client (cosmetic, not synced) → stands up after `respawnDelay`. Ragdoll is for enemies only — the player ragdoll is deferred. Raises server events `ServerDied` / `ServerRespawned` for an AI on the same object
- `ShooterDroid` (`ShooterDroid.prefab`, variant of `RagdollBot` + NavMeshAgent + NetworkTransformUnreliable): server-only brain over `RagdollEnemy`. Re-sweeps `detectionRadius` while idle (players on foot only, `IsTargetable`), walks to `standoffDistance` (never backs off; closes in when sight is lost), fires `EnemyProjectile` from `FirePoint` (between the eyes) while moving. Shots pass through every droid and through each other (`EnemyProjectile` ignores live projectiles); the sight check skips droids and shots in flight to match — droids walk in a pack and the projectile is fat (r 0.3), so a ray-only check let them shoot each other. Optional target loss (leash radius / out of sight timeout), wake on being shot (`IAttackerListener`, called by `PlayerHitReporter` after a player's hit), alert nearby droids. Needs a baked NavMesh
- Droid animation: `Animations/Droid/Droid.controller` (1D blend tree Idle↔Walk on `Speed`, state speed = `WalkRate`) on the prefab Animator with the FPSF `Character.fbx` humanoid avatar. `DroidAnimator` feeds the parameters on every peer from the root's measured ground speed — nothing animation-related is synced. Clips are in-place humanoid loops (root motion baked); facing the player stays in `ShooterDroid.Face` on the server
- Droid detection and spawner zone/aggro checks iterate `ShooterDroid.ServerPlayers()` (from `NetworkServer.connections`), never a fixed-size overlap buffer — a droid pack's bone colliders filled the 32-slot buffer before the player appeared in it
- **EnemyCar = controller + behaviour.** `EnemyCarController` is the core (health, death, wreck, `candidates`, `PickTarget`, optional `proximityAggroRadius` scan on top of `EnemyAggroZone`); exactly one `EnemyCarBehaviour` on the same prefab decides what the car does with `Car.Target*` (`ServerTick` / `ServerIdle` / `ServerOnTargetChanged` / `ServerOnDied` / `ServerApplyBalance` / `ServerVelocity` for the wreck's launch speed). New enemy type = new behaviour class + prefab copy (not a variant: a variant can't drop the base's behaviour). Never touch the agent through `Car.Agent` in a behaviour's `Awake` — component Awake order is undefined. `GunnerCarBehaviour` = the description below (`EnemyCar.prefab`). `ObstacleLayerCarBehaviour` (`EnemyCarObstacleLayer.prefab`, gun hidden): no path — each frame `agent.Move`. Everything is measured from the *initial point* (the car's world position at `OnStartServer`) with hard per-axis limits (`maxZForward/Backward`, `maxXRight/Left`) that hold however far the player goes. Z: wanted place = target Z + `preferredZOffset` cut to the range; the car's own Z speed chases (target's measured speed + bounded correction, `zDeadZone`) with limited `zAcceleration` — that is the lag, so it never copies the player rigidly. X: lines up with the target inside its range. No target → drives back to the initial point at `returnSpeed`. It spawns nothing while the target is ahead of it (higher Z). Every `spawnInterval` it spawns `RoadObstacleSpawned.prefab` (networked root, `CarEjectionTrigger` with `destroyOnFinish`, registered in `runtimeSpawnPrefabs`) at its rear ± `spawnSpreadX`, facing -Z, flying at `obstacleSpeed`. The obstacle shoves cars instead of ejecting the driver (`ejectDriver` off): `CarPushTrigger` runs on every peer, but `CarController.ApplyPush` acts only on the peer that owns the driven car (client authority — the server can't move it); each physics step of overlap it cancels the car's speed into the obstacle and adds the obstacle's `CarEjectionTrigger.Velocity` (`speed`/`flying` are SyncVars) to the move step, so the car is carried back until it steers out or the obstacle despawns.
- `EnemyCarController` (`EnemyCar.prefab`, NavMeshAgent + NetworkTransformUnreliable server→client): chases player cars registered by `EnemyAggroZone`, turret gun yaw synced. Fires `EnemyCarProjectile` (variant of `Projectile.prefab` with `EnemyCarProjectileFx`, like the droid's); muzzle flash comes from the projectile's `ProjectileFxSet` in `RpcPlayShotFx`. `IDamageReceiver` (SyncVar `currentHealth`; death plays `destructionEffects` via RPC, swaps every mesh slot to `burntMaterial`, deactivates `hideOnDeath` children, turns on looping `burningEffects` (from the hook — state, so late joiners see the fire), and launches the wreck: a `RoadBody` kept **disabled** while alive — its despawn check would remove a live car — is enabled on every peer from the `isDead` hook, `ServerLaunch` with random up/side speed + spin, profile `EnemyCarWreck`; the gun stops applying its world yaw; the server removes the wreck `wreckLifetime` s after death, 0 = only `RoadBody`'s `despawnZ`); `IAttackerListener` — an idle car targets whoever shot it (their driven car, else the player on foot). **Hunt** (`huntOnFootAttacker`, on for `EnemyCar.prefab`, off for the unarmed obstacle layers): a player on foot (not driver, not passenger — `CarPassengerSeat.All`) who shoots the car overrides any target for `huntDuration` s (120; each new hit by them restarts it); the hunt follows them into a car they drive, ends when they are downed/dead or the time runs out, then normal `PickTarget` resumes; a second player can't steal a running hunt
- `ShooterDroidSpawner`: server spawn loop, `maxAlive` / `totalToSpawn`; `aggroOnSpawn` re-runs every tick for idle droids (an `OnStart` wave spawns before anyone joins), player inside `activationZone` or within `aggroRadius`; activation `OnStart` / `WhilePlayersInZone` (polled zone collider) / `Manual` (`ServerStartSpawning`/`ServerStopSpawning`, safe from UnityEvents on any peer). Droid + `Projectile` prefabs are in `NetworkManager.prefab` `runtimeSpawnPrefabs`
- `MeleeZombie` (`Zombie/MeleeZombie.prefab`, variant of `RagdollBot` + NavMeshAgent + NetworkTransformUnreliable, `respawnDelay` 0 = corpse removed): the melee sibling of `ShooterDroid` — server-only brain over `RagdollEnemy`, same idle re-sweep over `ShooterDroid.ServerPlayers()`, `NavMeshAgent` chase, wake on being shot. A swing starts at `attackRange` (agent stops for `attackDuration`, `attackCooldown` between swings); damage lands on the **`AttackHit` animation event** of `Armature_Zombie_Scratch`, not on a timer, so moving the event retimes the hit. The event fires on every peer that plays the clip, `AttackHit` returns unless `isServer` and re-checks range (`hitRangeMultiplier`) since the player may have stepped away. The swing reaches clients as `RpcSwing` → `Attack` trigger (nothing else is synced). `Animations/Zombie/ZombieMelee.controller` = Locomotion blend tree (Idle↔Walk on `Speed`, state speed `WalkRate`, fed by `DroidAnimator`) + Attack state. Tune `DroidAnimator.walkClipSpeed` and `moveSpeed` together (the walk clip has no root motion). The old `ZombieEnemy` / `Zombie.prefab` / `ZombieSpawner` (BreachConsole-gated) are untouched and unused
- **Drop links** (AI Navigation 2.x `NavMeshSurface` has no Drop Height): `FPS Game → Navigation → Drop Links Generator` reads `NavMesh.CalculateTriangulation`, samples border edges (wall rays at knee/head height, ground ray past the ledge, NavMesh of the same agent type below within `minDrop..maxDrop`) and writes one-way `NavMeshLink`s (area `Jump`) under the scene root `NavMesh Drop Links (generated)`; Generate replaces it. Bake first, regenerate after every re-bake. `AgentLinkJumper` (on `MeleeZombie` / `ShooterDroid` prefabs) crosses links as a ballistic jump: turns off `autoTraverseOffMeshLink`, walks the last bit to the link start (Detour enters a link ~2 radii early), arcs to `endPos` under its own `gravity` with `jumpHeight` apex, `CompleteOffMeshLink`, short `landingPause`. Server-only by construction (clients have the agent disabled; the NetworkTransform carries the arc); the brains skip their tick while `IsJumping`; death mid-air cancels it. Without the component Unity's straight glide is used
- **Spawners share `EnemySpawner`** (abstract `NetworkBehaviour`): one server tick every `spawnInterval`, at most one spawn per tick, `maxAlive` cap, spawn points with `spawnClearance`, `aggroOnSpawn`. Subclasses only give the prefab, `IsEnemyDead`, `AggroIfIdle` (+ optional `ZoneGateOpen` / `SnapToNavMesh`). `ShooterDroidSpawner`, `MeleeZombieSpawner`, `EnemyCarSpawner` (samples the prefab's own agent type, gate/aggro = driven alive player cars → `RegisterTarget`). Two spawn sources: the own loop (`activation`, `totalToSpawn`) and `ServerRequest(count)` → `SpawnRequest` (served first, ignores the activation gate and `totalToSpawn`, obeys `maxAlive`; `AllSpawned` / `Alive` / `AllDead`; `ServerCancel`). Enemy prefabs (droid, zombie, `EnemyCar`, `EnemyCarObstacleLayer`) are in `NetworkManager.prefab` `runtimeSpawnPrefabs`
- **`EnemyDirector`** (scene `NetworkBehaviour`): `platformWaves` and `vehicleWaves` run in parallel, independently. A `Wave` is `Combat` (`enemies` = list of {`spawner`, `count`}, `endCondition` `AllKilled` / `Timer` / `AllKilledOrTimer`, `duration`) or `Pause` (`duration`). It never spawns itself — it orders from spawners (set those to `Manual`); where/how fast stays the spawner's. A wave cut short by its timer keeps spawning its remainder alongside the next one. Track end: `Stop` (fires `onFinished`) / `LoopAll` / `RepeatLast`. `ServerStart/Stop[Platform|Vehicle]` are UnityEvent-safe; stop cancels unspawned remainders. Per-track wave index + `TrackState` are SyncVars for a future HUD

### Hazard System
- `BarrelSpawner`: server-side object pool (`poolSize` pre-spawned barrels). Returns barrel to pool after despawn delay
- `Barrel`: kinematic Rigidbody, moves via `rb.MovePosition()`; bounces using an `AnimationCurve`; damages car or player on trigger contact, then deactivates
- `FlameHazard` (`Prefabs/Hazards/FlameHazard.prefab`; place in a scene, or add to `runtimeSpawnPrefabs` if spawned at runtime): server-only damage per target every `damageInterval`, first hit on contact, cooldown survives leaving the zone. Cars are tracked with `OnTriggerEnter/Exit`; players are tested geometrically against the same collider (`ClosestPoint` + the disabled `CharacterController` shape), because other players' copies fire no triggers on the server. `isBurning` SyncVar drives `flameVisual`. `TurnOn`/`TurnOff` take over from the cycle until `StartCycle`; all are safe to call on any peer (UnityEvents)
- `FlameHazardController`: plain `MonoBehaviour` (no `NetworkIdentity`, so flames can be its children) acting on the server only. `patterns[]` → `steps[]` (`flames` indices, `duration`, `pauseAfter`), looped. `Play` / `PlayPattern(int)` / `Stop`; a request waits until the flames are spawned. It calls `TurnOn`/`TurnOff`, so it overrides each flame's own cycle
- `FlameTurbineRotation`: tunnel turbines, one `FlameHazard` per zone, all lit but one resting zone (random, never the same twice). Phases: `startDelay` → `burnDuration` → `pauseDuration` (all out) → next. `StartRotation`/`StopRotation`/`RestartRotation` (UnityEvent-safe); out before start and after stop. A `NetworkBehaviour` (unlike `FlameHazardController`) because lamps need the random next zone: SyncVars `phase`, `restingZone`, `nextRestingZone`, deadlines `phaseEndTime`/`nextIgnitionTime` (`NetworkTime`). One lamp per zone (Renderer via MaterialPropertyBlock + optional Light), computed on every client: red = burning, green = safe, blinking = lights up within `warningTime`. Turbines must not be its children (nested `NetworkIdentity`)

### Interaction System
- `InteractableObject`: hold-E with configurable `HoldDuration`; progress tracked in `PlayerInputHandler`; on completion `CmdInteract` → `ServerInteract` → `RpcOnInteracted` → UnityEvent fires on all clients
- Found through the static `InteractableObject.All` registry (OnEnable/OnDisable), never a physics overlap: only the player's distance to `RangePoint` (≤ `InteractRange`) and the look cone to `ViewPoint` decide. Optional `interactPoint` (e.g. the button) sets both; empty = range from the pivot, look at the first child collider's centre. The old 10-slot `OverlapSphereNonAlloc` was crowded out by nearby colliders/triggers and lost the button
- Doors: `InteractObjective` → UnityEvent → `DoorOpener.OpenDoor`. The door rises by `openHeight` (0 = its own collider height) along its parent's up at `moveSpeed`. Synced as target `isOpened` + `moveFromAmount` + `moveStartTime` (`NetworkTime`); every peer derives the pose itself (no per-frame sync, late joiners correct). Colliders stay on while moving — an almost-shut door physically blocks anyone slipping under it; never toggle them for the move (re-enabling inside a capsule = unpredictable depenetration). **Never closes on anyone:** `CloseDoor` while the doorway is occupied only schedules a retry (`blockedRetryDelay`; safe to call every frame, `KeycardLock` does), and a closing door goes back up the moment someone enters the doorway, then retries once fully open. Doorway = `doorwayArea` box or the door's own colliders, taken in the **closed pose** (the door moves); players by geometry (non-dead, non-driving capsules, as in `FlameHazard`), plus optional `obstructionLayers` overlap (enemies/loot/cars; the door assembly itself is ignored). `autoCloseDelay` > 0 = server deadline counted from fully open; re-opening restarts it. `onClosed` (server) fires when the door is **fully down** → `InteractObjective.ResetObjective` re-arms the latched objective; `refireWhileCompleted` lets interactions extend the timer or send a closing door back up
- **Blocked state:** `InteractableObject.ServerSetBlocked(reason)` (SyncVar string, empty = usable). While blocked the prompt shows the reason as a warning, E doesn't start the hold, a running hold is cancelled, and the server refuses `ServerInteract` / activation
- **Car spawn panel** (`Prefabs/Environment/PlayerCarSpawnPanel.prefab`: `NetworkIdentity` + `InteractableObject` + `PlayerCarSpawner` on the root, `interactPoint` = the button): hold duration/range/prompt are the `InteractableObject`'s. The server polls every `checkInterval` and re-checks on the press: blocked while any `CarController` is not `IsWreck` (= dead **and** exploded; a burning car still blocks — its driver may be inside), or the `spawnAreaSize` box at `spawnPoint` holds a player (by position) or a solid collider on `obstructionLayers`. Spawns `carPrefab` (`Car_Muscle`, in `runtimeSpawnPrefabs`) with `NetworkServer.Spawn`; the lamp turns red/green on every peer from `IsBlocked`; `onCarSpawned` fires on all clients. The `CarSpawnPoint` child can be moved anywhere or replaced by a scene transform. `removeWrecksOnSpawn` (on) first calls `CarController.ServerRemoveWreck` on every wreck: driver ejected, passengers evicted (`CarPassengerSeat.ServerEvictPassenger` → `PlayerInputHandler.ServerForceLeaveSeat`), safe detached, stowed loot `ServerUnstow`ed, then `NetworkServer.Destroy` after 0.3 s (the host handles TargetRpcs from a queue, after an immediate destroy) + `SetActive(false)` (Mirror only unspawns a scene car). An exploded car is invisible and colliderless, but stays an entity — and late joiners never got its explosion RPC
- `CarController.ServerExplode` sets `isExploded` on the server too (was RPC-only), so `IsWreck` works on a dedicated server
- `CarryableObject`: pickup assigns client authority; drop/throw removes it; `SetCarriedLocally` gives instant feedback; SyncVar confirms on other clients

### Code Vault (scenario «Код с радара», layer 0 — scene `8_CodeRadar`)
- Spec: `_meta/handoff-layer0.md`, `_meta/scenario-code.md`. Raid end = the usual `ExtractionZone`.
- `CodeVaultController` (server) polls `DecryptParkingZone.Evaluate` each frame. Everything is a SyncVar, the code included (co-op, no anti-cheat); only `DriverCodeHUD` (IMGUI, local driver only) shows it. Input window = `NetworkTime` deadline; `rotateCodeOnWindowExpire` (layer 3) regenerates the code.
- The dash runs on the driver's client only, so `CarController.TryDash` reports it with `CmdReportDash` → `ServerLastDashTime`; the zone blocks for `DashDuration + dashCooldownBlock`.
- Keypad keys are picked by a precise aim ray (`PlayerInputHandler.FindAimedKeypadButton`), not the look cone — ten keys side by side. Typing ahead of the reveal is ignored, a wrong digit wipes the input.
- Host debug: F5 overlay (off by default — it shows the code), F6 reveal all, F7 solve.
- Stowed loot (`CarryableObject.ServerStow`): kinematic, colliders and NetworkTransform off, pinned to the car's local pose in `LateUpdate` on every peer. `RoadBody.ServerResetToTruck` clears the road phase on stow/return.
- Passenger: E boards only if the driver seat is taken and the player is in the board box (roof / mid-jump) or within the car's `InteractDistance`. A free driver seat always wins from the same spots (`PlayerInputHandler.FindEnterableCar`, `CarPassengerSeat.InBoardingReach`); `CanBoard` requires `HasDriver` on client and server. Two players pressing E at once: the server seats the loser as passenger (`TargetEnterMovedToSeat`) or pulls them out (`TargetEnterRefused`). The owner boards locally (vehicle mode, pinned to the seat), `CmdBoardSeat` confirms; other peers pin the passenger in `CarPassengerSeat.LateUpdate` (skipping their own player). Weapons stay usable.

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
- Editing an `.inputactions` asset regenerates FPSF's `Controls.cs` on import — commit both

## Current State (2026-10-23)

See `PROJECT_STATE.md` for the full system-by-system inventory. Summary:

### Working
- Core gameplay loop: join → drive convoy car → carry loot into cargo (`CargoTrigger`) → `ScoreManager` updates
- FPS player: our movement (crouch/sprint/tactical sprint/jump, convoy riding) with the FPS Framework camera rig; FPSF firearms (pistol/rifle/shotgun/sniper), axe, networked grenades, tools as loadout slots; health/damage/regen/death
- Client-authoritative hits with full friendly fire (players, car), FPSF hit marker (server-confirmed kill highlight) and damage flash; `RagdollBot` target dummies
- Downed state + teammate revive (hold E): bleedout deadline paused during a revive, enemies ignore downed players, collapsed pose + marker synced to all clients
- Match defeat when the whole squad is down/dead (`GameStateManager`): input locked on every client, `MatchEndUI` with a host-only restart
- Match victory: drive the car into `ExtractionZone` and hold it there for `captureDuration` seconds; `MatchEndUI` shows the collected loot score
- Carry/throw loot props (client-authority pickup)
- Vehicle: arcade driving, authority transfer on enter/exit, vehicle health → destruction (fire + explosion FX, kills driver), collision damage, driverless backward drift
- Enemy turrets shoot at players and driven vehicles; barrel hazard spawner (server pool) on Road scene
- Destruction objective system (`DestructionObjective` / `DestructionTarget`)
- Hold-E interactables, `WindZone` push
- Floor-cutting tool: runtime mesh hole (`FloorMeshModifier`) + `Safe` falling through (`SafeFallTrigger`, `SafeMount` car attach)
- `LocalPlayerBodyHider` (hide own body from FP camera)
- ZeroTier for remote peers; `NetworkDebugUI` connection overlay; F9 scene hot-reload (`SceneReloader`)

### In progress
- **Vehicle remote control** (`RemoteVehicleControlTool` + `CarController` autopilot). Wired as loadout slot 8 (`car_remote`); needs a playtest. See PROJECT_STATE.md §4.

### Exists in code but NOT integrated
- `ZombieEnemy` — full NavMesh state machine, not spawned in any scene
- Grappling hook (`GrapplingHookController` / `GrapplingHookInput`) — **local-only prototype, no networking**: only the owner's resulting position syncs (via NetworkTransform); grapple state, target, rope visual, and server validation are absent
- `NetworkMessages.cs` structs — declared, never sent/handled
- `PauseMenu` — FPSF-aware (`PlayerInputHandler.SetPaused`) but not placed in any scene

### Known gaps
- No `PlayerRespawnPoint` is placed in any scene yet — respawns fall back to the spawn points
- `MatchStateMessage` / `MatchState` in `NetworkMessages.cs` remain unused (`GameStateManager` uses its own `MatchResult`)
- Vehicle destruction has no wreck/persistent state beyond explosion FX + `vehicleIsAlive`
- `Enemy.OnAliveChanged` hook empty (no turret death visuals)
- `Safe` has no loot drop / visual change on destruction
- UI stubs: `PauseMenu.OpenSettings`/`Disconnect`
- `GameSettings` not networked / not persisted per client
- `PlayerSpawnManager.FurthestFromPlayers` ignores `minSpawnDistance`
- FPSF integration leftovers: third-person view is a placeholder (fixed anchor, no head pitch, no remote axe swing); axe/tools have no FPSF sway; pistol pickup still shows the revolver; grenades don't inherit the thrower's velocity; lean unbound; player ragdoll deferred
- WIP scenes (untracked): `Test.unity`, `WagonsConstruct.unity`, `Wall Laser.unity`, `GrapplingHook_Test.unity`
