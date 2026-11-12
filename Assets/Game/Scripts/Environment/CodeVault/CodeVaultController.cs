using System;
using System.Text;
using FPSGame.Vehicle;
using Mirror;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace FPSGame.Environment
{
    public enum CodeVaultState : byte
    {
        Idle,         // nothing decrypted yet, waiting for the car to park
        Decrypting,   // parking is active, the current symbol's progress grows
        Paused,       // parking broken; progress is kept but doesn't grow
        InputWindow,  // every symbol is revealed; the window timer runs
        Solved        // the right code was entered; the vault is open
    }

    public enum CodeInputResult : byte
    {
        Accepted,         // the digit was right and is now on the screen
        NotYetDecrypted,  // as many digits entered as revealed — press ignored
        Wrong,            // wrong digit — the whole entered code is wiped
        Solved,           // the last digit completed the code
        Ignored           // the vault is already solved
    }

    /// <summary>
    /// Server-side logic of the "code from the radar" vault (handoff F2). Generates the code, reveals it one
    /// symbol per <see cref="symbolDecryptTime"/> seconds of active parking, runs the input window, checks
    /// digits pressed on the <see cref="CodeConsole"/> and opens the vault.
    ///
    /// Everything is a SyncVar, the code included (co-op: no anti-cheat). Hiding it from the partner is the
    /// UI's job (<see cref="FPSGame.UI.DriverCodeHUD"/> draws only for the driver). The input window is a
    /// deadline (NetworkTime), like bleedout; the current symbol's progress is a plain float since it pauses.
    /// </summary>
    public class CodeVaultController : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private DecryptParkingZone parkingZone;
        [Tooltip("Vault door opened on Solved (another opening source next to a keycard). Optional — onSolved can do it instead.")]
        [SerializeField] private DoorOpener vaultDoor;

        [Header("Code")]
        [SerializeField, Range(1, 12)] private int codeLength = 5;
        [Tooltip("Seconds of active parking needed to reveal one symbol.")]
        [SerializeField, Min(0.1f)] private float symbolDecryptTime = 4f;
        [Tooltip("Seconds of the input window after the last symbol is revealed.")]
        [SerializeField, Min(0f)] private float inputWindowTime = 5f;
        [Tooltip("Layer 3: when the window runs out unsolved, a new code is generated, the input is wiped and " +
                 "decryption starts over. Off = the window is just a timer on the HUD.")]
        [SerializeField] private bool rotateCodeOnWindowExpire = false;

        [Header("Debug (host)")]
        [Tooltip("Off by default: it shows the whole code, which would spoil it for a host playing the partner.")]
        [SerializeField] private bool showDebugOverlay = false;
        [SerializeField] private Key toggleOverlayKey = Key.F5;
        [SerializeField] private Key revealAllKey = Key.F6;
        [SerializeField] private Key enterCodeKey = Key.F7;

        [Space]
        [Tooltip("Server only. Fires once when the right code is entered.")]
        [SerializeField] private UnityEvent onSolved;

        [SyncVar] private string code = "";
        [SyncVar] private string entered = "";
        [SyncVar] private int revealedCount;
        [SyncVar] private float symbolProgress;
        [SyncVar] private double windowEndTime;
        [SyncVar] private ParkingStatus parkingStatus = ParkingStatus.NoCar;
        [SyncVar] private CodeVaultState state = CodeVaultState.Idle;

        public string Code => code;
        public string Entered => entered;
        public int CodeLength => code.Length > 0 ? code.Length : codeLength;
        public int RevealedCount => revealedCount;
        public int EnteredCount => entered.Length;
        public float SymbolProgress => symbolProgress;
        public CodeVaultState State => state;
        public ParkingStatus Parking => parkingStatus;
        public bool IsSolved => state == CodeVaultState.Solved;
        public bool RotatesCode => rotateCodeOnWindowExpire;
        /// <summary>Seconds left in the input window (0 outside it or once it has run out).</summary>
        public float WindowRemaining => state == CodeVaultState.InputWindow
            ? Mathf.Max(0f, (float)(windowEndTime - NetworkTime.time))
            : 0f;

        /// <summary>Server only.</summary>
        public event Action ServerSolved;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (parkingZone == null)
                Debug.LogWarning($"[CodeVault] {name}: no DecryptParkingZone assigned — decryption never starts.", this);
            ServerNewCode();
        }

        [ServerCallback]
        private void Update()
        {
            HandleDebugKeys();

            if (state == CodeVaultState.Solved)
                return;

            CarController car = null;
            ParkingStatus status = parkingZone != null ? parkingZone.Evaluate(out car) : ParkingStatus.NoCar;
            if (parkingStatus != status)
                parkingStatus = status;

            if (state == CodeVaultState.InputWindow)
            {
                if (rotateCodeOnWindowExpire && NetworkTime.time >= windowEndTime)
                {
                    Debug.Log("[CodeVault] Input window expired — new code.");
                    ServerNewCode();
                }
                return;
            }

            bool active = status == ParkingStatus.Active;
            CodeVaultState next = active ? CodeVaultState.Decrypting
                                : state == CodeVaultState.Idle ? CodeVaultState.Idle
                                : CodeVaultState.Paused;
            if (state != next)
                state = next;

            if (state != CodeVaultState.Decrypting)
                return;

            float progress = symbolProgress + Time.deltaTime / symbolDecryptTime;
            if (progress < 1f)
            {
                symbolProgress = progress;
                return;
            }

            symbolProgress = 0f;
            revealedCount++;
            if (revealedCount >= code.Length)
                ServerOpenWindow();
        }

        [Server]
        private void ServerNewCode()
        {
            var sb = new StringBuilder(codeLength);
            for (int i = 0; i < codeLength; i++)
                sb.Append((char)('0' + UnityEngine.Random.Range(0, 10)));

            code = sb.ToString();
            entered = "";
            revealedCount = 0;
            symbolProgress = 0f;
            windowEndTime = 0;
            state = CodeVaultState.Idle;
            Debug.Log($"[CodeVault] New code generated ({codeLength} digits).");
        }

        [Server]
        private void ServerOpenWindow()
        {
            revealedCount = code.Length;
            symbolProgress = 0f;
            windowEndTime = NetworkTime.time + inputWindowTime;
            state = CodeVaultState.InputWindow;
        }

        /// <summary>Server: a digit pressed on the console (F4 rules). Returns what happened, for feedback.</summary>
        [Server]
        public CodeInputResult ServerPressDigit(int digit)
        {
            if (state == CodeVaultState.Solved)
                return CodeInputResult.Ignored;

            // Can't type ahead of the decryption — this is what stops brute-forcing unrevealed positions.
            if (entered.Length >= revealedCount)
                return CodeInputResult.NotYetDecrypted;

            if (code[entered.Length] != (char)('0' + digit))
            {
                entered = "";
                return CodeInputResult.Wrong;
            }

            entered += (char)('0' + digit);
            if (entered.Length < code.Length)
                return CodeInputResult.Accepted;

            ServerSolve();
            return CodeInputResult.Solved;
        }

        [Server]
        private void ServerSolve()
        {
            if (state == CodeVaultState.Solved)
                return;

            entered = code;
            revealedCount = code.Length;
            symbolProgress = 0f;
            state = CodeVaultState.Solved;
            Debug.Log("[CodeVault] Code entered — vault open.");

            if (vaultDoor != null)
                vaultDoor.OpenDoor();
            ServerSolved?.Invoke();
            onSolved?.Invoke();
        }

        // ── Debug ─────────────────────────────────────────────────────────

        [Server]
        private void HandleDebugKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb[toggleOverlayKey].wasPressedThisFrame)
                showDebugOverlay = !showDebugOverlay;

            if (state == CodeVaultState.Solved)
                return;

            if (kb[revealAllKey].wasPressedThisFrame && revealedCount < code.Length)
            {
                Debug.Log("[CodeVault] Cheat: all symbols revealed.");
                ServerOpenWindow();
            }

            if (kb[enterCodeKey].wasPressedThisFrame)
            {
                Debug.Log("[CodeVault] Cheat: code entered.");
                ServerSolve();
            }
        }

        private void OnGUI()
        {
            if (!showDebugOverlay || !isServer)
                return;

            string text =
                $"<b>CodeVault</b>  [{toggleOverlayKey}] hide  [{revealAllKey}] reveal all  [{enterCodeKey}] solve\n" +
                $"state: {state}   parking: {parkingStatus}\n" +
                $"code: {code}   revealed: {revealedCount}/{code.Length}   entered: {entered.Length} \"{entered}\"\n" +
                $"symbol progress: {symbolProgress:0.00}   window: {WindowRemaining:0.0} s   rotate: {rotateCodeOnWindowExpire}";

            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, richText = true, fontSize = 13 };
            GUI.Box(new Rect(10f, Screen.height - 100f, 520f, 88f), text, style);
        }
    }
}
