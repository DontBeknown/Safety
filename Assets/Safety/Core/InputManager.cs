using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SafetySimulation
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class InputManager : MonoBehaviour
    {
        [SerializeField]
        private InputActionAsset inputActions;
        [SerializeField]
        private InputActionAsset movementActions;
        private InputActionAsset runtimeActions;
        private InputActionAsset runtimeMovement;
        private InputAction move;
        private InputAction look;
        private InputAction walkJump;
        private InputAction sprint;
        private InputAction interact;
        private InputAction use;
        private InputAction extend;
        private InputAction secondary;
        private InputAction pause;
        private InputAction up;
        private InputAction down;
        private InputAction rush;
        private InputAction jump;
        private InputAction lean;
        private readonly List<KeyValuePair<InputAction, int>> slots = new List<KeyValuePair<InputAction, int>>();
        public event Action FrameReady;
        public event Action InputSuspended;
        public static InputManager Instance
        {
            get;
            private set;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (runtimeActions != null)
            {
                Destroy(runtimeActions);
            }

            if (runtimeMovement != null)
            {
                Destroy(runtimeMovement);
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool IsReady
        {
            get;
            private set;
        }

        public Vector2 Move
        {
            get
            {
                return move.ReadValue<Vector2>();
            }
        }

        public Vector2 Look
        {
            get
            {
                return look.ReadValue<Vector2>();
            }
        }

        public bool IsMouseLook
        {
            get
            {
                return look.activeControl != null && look.activeControl.device is Mouse;
            }
        }

        public bool WalkJumpPressed
        {
            get
            {
                return walkJump.WasPressedThisFrame();
            }
        }

        public bool SprintHeld
        {
            get
            {
                return sprint.IsPressed();
            }
        }

        public bool InteractPressed
        {
            get
            {
                return interact.WasPressedThisFrame();
            }
        }

        public bool PausePressed
        {
            get
            {
                return pause.WasPressedThisFrame();
            }
        }

        public bool PauseHeld
        {
            get
            {
                return pause.IsPressed();
            }
        }

        public bool UsePressed
        {
            get
            {
                return use.WasPressedThisFrame();
            }
        }

        public bool UseReleased
        {
            get
            {
                return use.WasReleasedThisFrame();
            }
        }

        public bool UseHeld
        {
            get
            {
                return use.IsPressed();
            }
        }

        public bool ExtendHeld
        {
            get
            {
                return extend.IsPressed();
            }
        }

        public bool SecondaryPressed
        {
            get
            {
                return secondary.WasPressedThisFrame();
            }
        }

        public bool UpPressed
        {
            get
            {
                return up.WasPressedThisFrame();
            }
        }

        public bool DownPressed
        {
            get
            {
                return down.WasPressedThisFrame();
            }
        }

        public bool RushHeld
        {
            get
            {
                return rush.IsPressed();
            }
        }

        public bool JumpPressed
        {
            get
            {
                return jump.WasPressedThisFrame();
            }
        }

        public float Lean
        {
            get
            {
                return lean.ReadValue<float>();
            }
        }

        public bool StepsHeld
        {
            get
            {
                return up.IsPressed() || down.IsPressed() || jump.IsPressed();
            }
        }

        public bool MovementHeld
        {
            get
            {
                return Move.sqrMagnitude > 0f || walkJump.IsPressed() || sprint.IsPressed() || Lean != 0f;
            }
        }

        public bool ActionsHeld
        {
            get
            {
                if (use.IsPressed() || extend.IsPressed() || secondary.IsPressed() || interact.IsPressed())
                {
                    return true;
                }

                foreach (var slot in slots)
                {
                    if (slot.Key.IsPressed())
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int SelectedSlot
        {
            get
            {
                foreach (var slot in slots)
                {
                    if (slot.Key.WasPressedThisFrame())
                    {
                        return slot.Value;
                    }
                }

                return -1;
            }
        }

        private void Start()
        {
            try
            {
                if (inputActions == null || movementActions == null)
                {
                    throw new InvalidOperationException("Assign SafetyControls and StarterAssets input assets.");
                }

                runtimeActions = Instantiate(inputActions);
                runtimeMovement = Instantiate(movementActions);
                move = Find(runtimeMovement, "Player/Move");
                look = Find(runtimeMovement, "Player/Look");
                walkJump = Find(runtimeMovement, "Player/Jump");
                sprint = Find(runtimeMovement, "Player/Sprint");
                interact = Find(runtimeActions, "Common/Interact");
                use = Find(runtimeActions, "Common/Use");
                extend = Find(runtimeActions, "Common/Extend");
                secondary = Find(runtimeActions, "Common/Secondary");
                // Remove the former flashlight F shortcut from the runtime copy only.
                // The flashlight now uses its numbered slot exclusively.
                for (var i = 0; i < secondary.bindings.Count; i++)
                {
                    if (string.Equals(secondary.bindings[i].path, "<Keyboard>/f", StringComparison.OrdinalIgnoreCase))
                        secondary.ApplyBindingOverride(i, string.Empty);
                }
                pause = Find(runtimeActions, "Common/Pause");
                up = Find(runtimeActions, "Ladder/Up");
                down = Find(runtimeActions, "Ladder/Down");
                rush = Find(runtimeActions, "Ladder/Rush");
                jump = Find(runtimeActions, "Ladder/Jump");
                lean = Find(runtimeActions, "Ladder/Lean");
                foreach (var action in runtimeActions.FindActionMap("Common", true).actions)
                {
                    if (!action.name.StartsWith("SelectSlot", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!int.TryParse(action.name.Substring("SelectSlot".Length), out var slot) || slot < 1)
                    {
                        throw new InvalidOperationException("Use slot action names SelectSlot1, SelectSlot2, etc.");
                    }

                    action.wantsInitialStateCheck = true;
                    slots.Add(new KeyValuePair<InputAction, int>(action, slot - 1));
                }

                IsReady = true;
                EnableActions();
            }
            catch (Exception exception)
            {
                Debug.LogError(exception.Message, this);
                enabled = false;
            }
        }

        private static InputAction Find(InputActionAsset asset, string path)
        {
            var action = asset.FindAction(path, true);
            if (action.bindings.Count == 0)
            {
                throw new InvalidOperationException($"Assign bindings for {path}.");
            }

            action.wantsInitialStateCheck = true;
            return action;
        }

        private void Update()
        {
            if (IsReady)
            {
                FrameReady?.Invoke();
            }
        }

        private void EnableActions()
        {
            runtimeActions.Enable();
            runtimeMovement.Enable();
        }

        private void OnEnable()
        {
            if (IsReady)
            {
                EnableActions();
                InputSuspended?.Invoke();
            }
        }

        private void OnDisable()
        {
            runtimeActions?.Disable();
            runtimeMovement?.Disable();
            InputSuspended?.Invoke();
        }
    }
}
