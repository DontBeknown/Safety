using SafetySimulation;
using UnityEngine;

namespace StarterAssets
{
    public partial class FirstPersonController
    {
        private InputManager gameplayInput;
        private SessionManager session;
        private InventoryManager inventory;
        private InteractionManager interaction;
        private bool inputBound;
        private bool blockActions = true;
        private bool blockMovement = true;
        private bool blockSteps = true;
        private bool pauseReady;
        private int inputResetFrame;
        private bool originalInputEnabled;
        private bool originalPlayerInputEnabled;
        private bool originalCursorLocked;
        private bool originalCursorLook;
        private CursorLockMode originalCursorMode;
        private bool originalCursorVisible;
        private bool originalMovementEnabled;
        private bool originalLookEnabled;
        private bool gameplayInitialized;
        public LadderComponent ActiveLadder
        {
            get;
            private set;
        }

        private void InitializeGameplay()
        {
            gameplayInput = InputManager.Instance;
            session = SessionManager.Instance;
            inventory = InventoryManager.Instance;
            interaction = InteractionManager.Instance;
            if (gameplayInput == null || session == null || inventory == null || interaction == null || _input == null ||
                !gameplayInput.IsReady)
            {
                Debug.LogError("FirstPersonController requires input, session, inventory, interaction and StarterAssetsInputs.", this);
                enabled = false;
                return;
            }

            originalInputEnabled = _input.enabled;
            originalPlayerInputEnabled = _playerInput.enabled;
            originalCursorLocked = _input.cursorLocked;
            originalCursorLook = _input.cursorInputForLook;
            originalCursorMode = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            originalMovementEnabled = MovementEnabled;
            originalLookEnabled = LookEnabled;
            gameplayInitialized = true;
            BindGameplayInput();
        }

        private void OnEnable()
        {
            if (gameplayInitialized)
            {
                BindGameplayInput();
            }
        }

        private void BindGameplayInput()
        {
            if (inputBound)
            {
                return;
            }

            // Keep the legacy value container, but prevent PlayerInput message delivery.
            _playerInput.enabled = false;
            _input.enabled = false;
            gameplayInput.FrameReady += ReadGameplayInput;
            gameplayInput.InputSuspended += RefreshInputMode;
            inputBound = true;
            RefreshInputMode();
        }

        private void OnDisable()
        {
            if (!gameplayInitialized)
            {
                return;
            }

            if (inputBound && gameplayInput != null)
            {
                gameplayInput.FrameReady -= ReadGameplayInput;
                gameplayInput.InputSuspended -= RefreshInputMode;
            }

            inputBound = false;
            inventory?.CancelUse();
            ActiveLadder?.ResetForRetry();
            ResetMotion();
            Cursor.lockState = originalCursorMode;
            Cursor.visible = originalCursorVisible;
        }

        private void OnDestroy()
        {
            if (!gameplayInitialized)
            {
                return;
            }

            if (_input != null)
            {
                _input.enabled = originalInputEnabled;
                _input.cursorLocked = originalCursorLocked;
                _input.cursorInputForLook = originalCursorLook;
            }

            if (_playerInput != null)
            {
                _playerInput.enabled = originalPlayerInputEnabled;
            }
        }

        public void RequireActionInputRelease()
        {
            blockActions = true;
            inventory?.CancelUse();
        }

        public void RequireInputRelease()
        {
            blockActions = true;
            blockMovement = true;
            blockSteps = true;
            pauseReady = false;
            inputResetFrame = Time.frameCount;
            ResetMotion();
            if (_input != null)
            {
                _input.look = Vector2.zero;
            }
            inventory?.CancelUse();
            ActiveLadder?.SetLeanInput(0f);
        }

        public void RefreshInputMode()
        {
            if (!gameplayInitialized)
            {
                return;
            }

            RequireInputRelease();
            var frozen = session == null || !session.isActiveAndEnabled || session.IsPaused || session.HasFallen ||
                session.IsCompleted || gameplayInput == null || !gameplayInput.isActiveAndEnabled;
            MovementEnabled = originalMovementEnabled && ActiveLadder == null && !frozen;
            LookEnabled = originalLookEnabled && !frozen;
            _input.cursorInputForLook = originalCursorLook && !frozen;
            _input.cursorLocked = !frozen;
            Cursor.lockState = frozen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = frozen;
        }

        public void AttachLadder(LadderComponent ladder)
        {
            ActiveLadder = ladder;
            _controller.enabled = false;
            RefreshInputMode();
        }

        public void DetachLadder(LadderComponent ladder, Vector3 position, Quaternion rotation, bool resetLook = true)
        {
            if (ActiveLadder != ladder)
            {
                return;
            }

            ActiveLadder = null;
            Teleport(position, rotation, resetLook);
            _controller.enabled = true;
            RefreshInputMode();
        }

        public void Teleport(Vector3 position, Quaternion rotation, bool resetLook = true)
        {
            var wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = wasEnabled;
            ResetMotion(resetLook);
        }

        private void ReadGameplayInput()
        {
            if (Time.frameCount == inputResetFrame)
            {
                return;
            }

            if (!gameplayInput.PauseHeld)
            {
                pauseReady = true;
            }

            if (pauseReady && gameplayInput.PausePressed && !session.HasFallen && !session.IsCompleted)
            {
                session.TogglePause();
                return;
            }

            if (!session.CanInteract)
            {
                ResetMotion();
                _input.look = Vector2.zero;
                ActiveLadder?.SetLeanInput(0f);
                return;
            }

            if (!gameplayInput.ActionsHeld)
            {
                blockActions = false;
            }

            if (!gameplayInput.MovementHeld)
            {
                blockMovement = false;
            }

            if (!gameplayInput.StepsHeld)
            {
                blockSteps = false;
            }

            _input.look = LookEnabled ? gameplayInput.Look : Vector2.zero;
            _input.move = MovementEnabled && !blockMovement ? gameplayInput.Move : Vector2.zero;
            _input.sprint = MovementEnabled && !blockMovement && gameplayInput.SprintHeld;
            _input.jump = MovementEnabled && !blockMovement && gameplayInput.WalkJumpPressed;
            if (!blockActions && gameplayInput.InteractPressed)
            {
                HandleInteraction();
                if (Time.frameCount == inputResetFrame)
                {
                    return;
                }
            }

            HandleLadderInput();
            HandleToolInput();
        }

        private void HandleInteraction()
        {
            if (ActiveLadder != null)
            {
                if (!ActiveLadder.TryDismount(out var reason))
                {
                    ActiveLadder.ShowWarning(reason);
                }

                return;
            }

            interaction.TryInteract();
        }

        private void HandleLadderInput()
        {
            if (ActiveLadder == null)
            {
                return;
            }

            if (!blockSteps)
            {
                if (gameplayInput.UpPressed)
                {
                    ActiveLadder.RequestStep(1, gameplayInput.RushHeld);
                }
                else if (gameplayInput.DownPressed)
                {
                    ActiveLadder.RequestStep(-1);
                }

                if (gameplayInput.JumpPressed)
                {
                    ActiveLadder.UnsafeAction("Jumping on the ladder is unsafe.", ActiveLadder.Tuning.JumpDisturbance);
                }
            }

            ActiveLadder.SetLeanInput(blockMovement ? 0f : gameplayInput.Lean);
        }

        private void HandleToolInput()
        {
            if (!session.CanWork || blockActions)
            {
                return;
            }

            if (gameplayInput.SelectedSlot >= 0)
            {
                if (!inventory.TrySelectSlot(gameplayInput.SelectedSlot, out var reason))
                {
                    inventory.ShowMessage(reason, true);
                }

                if (!inventory.IsFlashlightSlot(gameplayInput.SelectedSlot)) return;
            }

            inventory.SetExtended(gameplayInput.ExtendHeld);
            if (gameplayInput.UsePressed)
            {
                inventory.BeginUse();
            }

            if (gameplayInput.UseReleased)
            {
                inventory.EndUse();
            }
        }
    }
}
