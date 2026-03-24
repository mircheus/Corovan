I need to implement a Grappling Hook feature for my FPS game in Unity.



\## Project context

\- Unity CharacterController component is used for player movement

\- New Input System (InputAction) is used

\- Existing scripts: PlayerController, Camera/Look script, Weapon/Shooting system

\- The weapon system uses an int slot/index to track the active weapon

\- This is an additive feature — do not modify existing scripts unless absolutely 

&#x20; necessary; prefer composition over modification



\## What to create

Create a self-contained GrapplingHook system consisting of:

1\. `GrapplingHookController.cs` — main logic script

2\. `GrapplingHookInput.cs` — Input Action wiring (or extend via partial class if 

&#x20;  the project uses a central InputActions asset)

3\. A placeholder prefab setup instruction in comments (no art assets needed)



\## Behaviour specification



\### Aiming phase (active only when Grappling Hook weapon slot is selected)

\- GrapplingHookController requires a reference to the WeaponController 

&#x20; (assign in Inspector)

\- Expose a public int `grappleWeaponIndex` (default: assign in Inspector) 

&#x20; that defines which weapon slot is the grappling hook

\- Aiming logic is only active when WeaponController's active weapon index 

&#x20; equals `grappleWeaponIndex`; otherwise hide the placeholder and do nothing

\- When active: every frame, cast a ray from the camera center forward

\- If the ray hits any geometry within `maxRange` (default: 30f) units:

&#x20; - Instantiate / move a semi-transparent green sphere placeholder 

&#x20;   at the hit point (use a simple sphere primitive with a transparent 

&#x20;   green material created at runtime)

&#x20; - The placeholder is visible only when a valid target exists

\- If no valid hit → hide the placeholder

\- When player switches away from grapple slot mid-aim → hide placeholder immediately



\### Fire phase (on input: left mouse button / Fire action)

\- Only fires if a valid target is currently aimed at

\- Starts GrapplingHook movement: player moves along the straight line 

&#x20; from current position to the target hit point

\- Movement speed: `grappleSpeed` (default: 20f)

\- During movement:

&#x20; - Disable normal locomotion input (WASD / move actions)

&#x20; - Camera look must remain fully functional

&#x20; - If player presses Jump → immediately cancel grapple, restore normal movement

\- On arrival (distance to target < 0.5f):

&#x20; - Cancel grapple cleanly

&#x20; - Restore normal movement

&#x20; - Let CharacterController and gravity handle the rest (player simply falls)



\## State machine

Use an explicit enum:

&#x20; Idle → Grappling → (Arrived | Cancelled) → Idle



\## Integration requirements

\- GrapplingHookController needs a reference to the CharacterController 

&#x20; (assign in Inspector)

\- It must call a method or set a flag on PlayerController to suppress 

&#x20; movement input during Grappling state — add a public bool `IsMovementSuppressed` 

&#x20; property to PlayerController if it doesn't exist, but make no other changes

\- GrapplingHookController needs a reference to WeaponController to read 

&#x20; the active weapon index — add a public int `ActiveWeaponIndex` property 

&#x20; to WeaponController if it doesn't exist, but make no other changes

\- Do NOT touch the camera/look script



\## Code style

\- C#, Unity 2022+

\- SerializeField for all tunable values

\- XML summary comments on public members

\- No coroutines — use Update-based state machine

