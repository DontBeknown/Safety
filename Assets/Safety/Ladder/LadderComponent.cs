using DG.Tweening;
using StarterAssets;
using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class LadderComponent : MonoBehaviour, IInteractable
    {
        [SerializeField]
        private string id = "ladder";
        [SerializeField]
        private Transform mount;
        [SerializeField]
        private Transform dismount;
        [SerializeField]
        private Transform[] rungs = new Transform[5];
        [SerializeField]
        private Transform visualPivot;
        [SerializeField]
        private LadderTuning tuning;
        [SerializeField, Min(1)]
        private int standingLimit = 4;
        [SerializeField, Min(0.1f)]
        private float mountRange = 2f;
        [SerializeField, Min(0.01f)]
        private float mountDismountDuration = 0.35f;
        [SerializeField]
        private SafetyHud hud;
        [SerializeField]
        private AudioClip mountSound;
        [SerializeField]
        private AudioClip dismountSound;
        [SerializeField]
        private AudioClip climbSound;
        [SerializeField]
        private AudioClip creakSound;
        [SerializeField]
        private AudioClip warningSound;
        [Header("Step Lean")]
        [SerializeField, Range(0f, 100f)]
        [Tooltip("Percentage chance of leaning on each accepted step up or down. Zero disables step lean.")]
        private float stepLeanChance = 25f;
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Fraction of the tuning's Lean Speed applied during a step that triggers a random lean.")]
        private float stepLeanStrength = 0.75f;
        private FirstPersonController player;
        private SessionManager session;
        private InventoryManager inventory;
        private AchievementManager achievements;
        private float leanInput;
        private float stepLeanInput;
        private Sequence mountDismountTween;
        private float transitionTime;
        private int destinationRung;
        private Vector3 transitionStart;
        private Vector3 fallStart;
        private float fallTime;
        private float visualTime;
        private Quaternion initialVisualRotation;
        private bool warnedLean;
        private bool overreaching;
        private float nextWarningTime;
        private bool ready;
        public string Id
        {
            get
            {
                return id;
            }
        }

        public LadderTuning Tuning
        {
            get
            {
                return tuning;
            }
        }

        public bool IsAttached
        {
            get;
            private set;
        }
        public bool IsTransitioning
        {
            get;
            private set;
        }
        public int CurrentRung
        {
            get;
            private set;
        }
        public BalanceModel Balance
        {
            get;
            private set;
        }

        public string Prompt
        {
            get
            {
                return IsAttached ? "E — Dismount at the bottom" : "E — Climb ladder";
            }
        }

        public bool IsWorkingPosition
        {
            get
            {
                return ready && isActiveAndEnabled && IsAttached && !IsTransitioning && CurrentRung == standingLimit &&
                    Mathf.Abs(Balance.Lean) < tuning.UnsafeLean && session.CanWork && inventory.AllRequiredPpe;
            }
        }

        private void Start()
        {
            session = SessionManager.Instance;
            inventory = InventoryManager.Instance;
            achievements = AchievementManager.Instance;
            if (session == null || inventory == null || achievements == null || mount == null || dismount == null ||
                visualPivot == null || visualPivot == transform || tuning == null || rungs == null || standingLimit < 1 ||
                standingLimit >= rungs.Length || string.IsNullOrWhiteSpace(id) || float.IsNaN(mountRange) ||
                float.IsInfinity(mountRange) || mountRange <= 0f)
            {
                Debug.LogError("Configure ladder anchors, tuning, ID, standing limit and scene managers.", this);
                enabled = false;
                return;
            }

            if (!tuning.TryValidate(out var reason))
            {
                Debug.LogError(reason, this);
                enabled = false;
                return;
            }

            var anchors = new System.Collections.Generic.HashSet<Transform>();
            foreach (var rung in rungs)
            {
                if (rung == null || rung.gameObject.scene != gameObject.scene || rung.IsChildOf(visualPivot) ||
                    !anchors.Add(rung))
                {
                    Debug.LogError("Ladder rungs must be unique scene anchors outside the wobbling pivot.", this);
                    enabled = false;
                    return;
                }
            }

            if (mount.IsChildOf(visualPivot) || dismount.IsChildOf(visualPivot))
            {
                Debug.LogError("Mount and dismount must be outside the wobbling pivot.", this);
                enabled = false;
                return;
            }

            Balance = new BalanceModel(tuning);
            initialVisualRotation = visualPivot.localRotation;
            ready = true;
        }

        public Transform GetAnchor(int rung)
        {
            return rung == 0 ? mount : rungs[rung - 1];
        }

        public bool CanInteract(InteractionContext context, out string reason)
        {
            if (!ready || !isActiveAndEnabled || context == null || context.Player == null ||
                context.Player.ActiveLadder != null || !session.CanWork)
            {
                reason = "Cannot mount the ladder now.";
                return false;
            }

            if (!inventory.AllRequiredPpe)
            {
                reason = "Equip all required PPE before climbing.";
                return false;
            }

            if (Vector3.Distance(context.Player.transform.position, mount.position) > mountRange)
            {
                reason = "Move closer to the ladder base.";
                return false;
            }

            reason = null;
            return true;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context, out var reason))
            {
                ShowWarning(reason);
                return;
            }

            inventory.CancelUse();
            session.CaptureCheckpoint();
            if (!session.CanWork)
            {
                return;
            }

            player = context.Player;
            Balance.Reset();
            overreaching = false;
            CurrentRung = 0;
            IsAttached = true;
            player.AttachLadder(this);
            BeginMountDismount(mount, false);
            PlaySound(mountSound, true);
            if (hud != null)
            {
                hud.SetBalance(Balance.Lean, Balance.Instability, true, tuning);
            }
        }

        public bool TryDismount(out string reason)
        {
            if (!IsAttached || !session.CanInteract || IsTransitioning || CurrentRung != 0)
            {
                reason = "Descend to the bottom and stop before dismounting.";
                return false;
            }

            inventory.CancelUse();
            Balance.Reset();
            RestoreVisuals();
            BeginMountDismount(dismount, true);
            PlaySound(dismountSound, true);
            reason = null;
            return true;
        }

        private void BeginMountDismount(Transform anchor, bool detachOnComplete)
        {
            IsTransitioning = true;
            leanInput = 0f;
            stepLeanInput = 0f;
            player.LookEnabled = false;
            var duration = Mathf.Max(0.01f, mountDismountDuration);
            // Advance only from Update so session pause and input suspension also pause the tween.
            mountDismountTween = DOTween.Sequence()
                .SetUpdate(UpdateType.Manual)
                .Append(player.transform.DOMove(anchor.position, duration).SetEase(Ease.InOutSine))
                .Join(player.transform.DORotateQuaternion(anchor.rotation, duration).SetEase(Ease.InOutSine))
                .OnComplete(() =>
                {
                    mountDismountTween = null;
                    IsTransitioning = false;
                    if (detachOnComplete)
                    {
                        IsAttached = false;
                        player.DetachLadder(this, player.transform.position, player.transform.rotation, resetLook: false);
                        player = null;
                    }
                    else
                    {
                        player.RefreshInputMode();
                    }
                });
        }

        public void RequestStep(int direction, bool rushed = false)
        {
            if (!IsAttached || !session.CanInteract || direction == 0 || mountDismountTween != null)
            {
                return;
            }

            if (IsTransitioning)
            {
                UnsafeAction("Climbing too quickly: wait until the rung movement finishes.", tuning.RushDisturbance);
                return;
            }

            if (direction > 0 && !inventory.AllRequiredPpe)
            {
                ShowWarning("Equip all required PPE before climbing.");
                return;
            }

            var amount = direction > 0 && rushed ? 2 : 1;
            var next = CurrentRung + (direction > 0 ? amount : -1);
            if (next < 0 || next > rungs.Length)
            {
                ShowWarning("No rung is available in that direction.");
                return;
            }

            if (rushed && direction > 0)
            {
                UnsafeAction("Rushed climbing skipped a rung.", tuning.RushDisturbance + tuning.SkipDisturbance);
            }

            if (next > standingLimit)
            {
                UnsafeAction("Climbing above the permitted standing limit.", tuning.SkipDisturbance);
            }

            destinationRung = next;
            transitionStart = player.transform.position - transform.right * Balance.Lean;
            transitionTime = 0f;
            IsTransitioning = true;
            stepLeanInput = 0f;
            if (stepLeanChance > 0f && stepLeanStrength > 0f &&
                (Random.value * 100f < stepLeanChance || stepLeanChance >= 100f))
            {
                stepLeanInput = (Random.value < 0.5f ? -1f : 1f) * Mathf.Clamp01(stepLeanStrength);
            }
            PlaySound(climbSound, true);
        }

        public void SetLeanInput(float value)
        {
            leanInput = mountDismountTween != null ? 0f : Mathf.Clamp(value, -1f, 1f);
        }

        public void UnsafeAction(string reason, float disturbance)
        {
            if (!ready || session.IsPaused || session.HasFallen || session.IsCompleted)
            {
                return;
            }

            if (IsAttached)
            {
                Balance.Disturb(disturbance);
            }

            ShowWarning(reason);
            session.RecordMistake(reason);
        }

        private void Update()
        {
            if (!ready || player == null || !player.isActiveAndEnabled ||
                !session.isActiveAndEnabled || InputManager.Instance == null ||
                !InputManager.Instance.isActiveAndEnabled || session.IsPaused || session.IsCompleted)
            {
                return;
            }

            if (!IsAttached)
            {
                return;
            }

            var dt = Time.deltaTime;
            if (session.HasFallen)
            {
                fallTime = Mathf.Min(tuning.FallVisualDuration, fallTime + dt);
                var t = fallTime / tuning.FallVisualDuration;
                player.transform.position = fallStart + Vector3.down * (0.3f * Mathf.SmoothStep(0f, 1f, t));
                hud?.SetFade(t);
                return;
            }

            if (mountDismountTween != null)
            {
                player.LookEnabled = false;
                mountDismountTween.ManualUpdate(dt, Time.unscaledDeltaTime);
                return;
            }

            var arrived = false;
            if (IsTransitioning)
            {
                var remaining = Mathf.Max(0f, tuning.RungMoveDuration - transitionTime);
                var beforeArrival = Mathf.Min(dt, remaining);
                Balance.Tick(leanInput + stepLeanInput, CurrentRung > standingLimit, beforeArrival, returnToCenter: false);
                transitionTime += dt;
                var t = Mathf.Clamp01(transitionTime / tuning.RungMoveDuration);
                player.transform.position = Vector3.Lerp(transitionStart, GetAnchor(destinationRung).position,
                    Mathf.SmoothStep(0f, 1f, t)) + transform.right * Balance.Lean;
                if (t >= 1f)
                {
                    CurrentRung = destinationRung;
                    IsTransitioning = false;
                    stepLeanInput = 0f;
                    arrived = true;
                    Balance.Tick(leanInput, CurrentRung > standingLimit, dt - beforeArrival, returnToCenter: false);
                }
            }
            else
            {
                Balance.Tick(leanInput, CurrentRung > standingLimit, dt, returnToCenter: false);
            }

            var beyondLeanLimit = Mathf.Abs(Balance.Lean) >= tuning.UnsafeLean;
            if (beyondLeanLimit && !overreaching)
            {
                UnsafeAction("Overreach: leaning beyond the permitted range.", 0f);
            }

            overreaching = beyondLeanLimit;

            if (Balance.IsWarning && !warnedLean)
            {
                ShowWarning("Lean is approaching the unsafe range. Return toward the center.");
                PlaySound(creakSound, true);
            }

            warnedLean = Balance.IsWarning;
            if (Balance.Fallen)
            {
                IsTransitioning = false;
                fallStart = player.transform.position;
                fallTime = 0f;
                session.Invalidate("You fell after sustained ladder instability.", true);
                return;
            }

            if (!IsTransitioning)
            {
                player.transform.position = GetAnchor(CurrentRung).position + transform.right * Balance.Lean;
            }

            if (arrived)
            {
                if (CurrentRung == 1 && Mathf.Abs(Balance.Lean) < tuning.UnsafeLean &&
                    session.CanWork && inventory.AllRequiredPpe)
                {
                    achievements.Report(AchievementType.Climb, id, session.AttemptId);
                }
            }

            hud?.SetBalance(Balance.Lean, Balance.Instability, true, tuning);
            visualTime += dt;
            var leanTilt = Balance.Lean / tuning.MaxLean * tuning.MaximumTilt;
            var wobble = Mathf.Sin(visualTime * 9f) * Mathf.Clamp01(Balance.Instability) * tuning.MaximumTilt * 0.25f;
            visualPivot.localRotation = initialVisualRotation * Quaternion.Euler(0f, 0f, -leanTilt + wobble);
            player.ExternalCameraRoll = tuning.UseCameraRoll ? -Balance.Lean / tuning.MaxLean * tuning.MaximumCameraRoll : 0f;
        }

        public void CancelTransitions()
        {
            leanInput = 0f;
            stepLeanInput = 0f;
            var hadMountDismountTween = mountDismountTween != null;
            mountDismountTween?.Kill();
            mountDismountTween = null;
            if (!IsTransitioning)
            {
                return;
            }

            IsTransitioning = false;
            if (hadMountDismountTween && player != null)
            {
                player.RefreshInputMode();
            }
            if (player != null && IsAttached && !session.HasFallen)
            {
                player.transform.position = GetAnchor(CurrentRung).position;
            }
        }

        public void ResetForRetry()
        {
            CancelTransitions();
            IsAttached = false;
            CurrentRung = 0;
            Balance?.Reset();
            fallTime = 0f;
            visualTime = 0f;
            warnedLean = false;
            overreaching = false;
            nextWarningTime = 0f;
            RestoreVisuals();

            if (player != null)
            {
                player.DetachLadder(this, dismount.position, dismount.rotation);
                player = null;
            }

            if (hud != null)
            {
                hud.SetFade(0f);
            }
        }

        private void RestoreVisuals()
        {
            if (visualPivot != null)
            {
                visualPivot.localRotation = initialVisualRotation;
            }

            if (player != null)
            {
                player.ExternalCameraRoll = 0f;
            }

            if (hud != null)
            {
                hud.SetBalance(0f, 0f);
            }
        }

        public void ShowWarning(string message)
        {
            if (hud != null)
            {
                hud.SetMessage(message, true);
            }

            if (Time.unscaledTime >= nextWarningTime)
            {
                nextWarningTime = Time.unscaledTime + 1f;
                session.PlayWarning(warningSound);
            }
        }

        private void PlaySound(AudioClip clip, bool randomPitch = false)
        {
            SoundManager.Instance?.Play(clip, 1f, randomPitch);
        }

        private void OnDisable()
        {
            if (ready)
            {
                inventory?.CancelUse();
                ResetForRetry();
            }
        }
    }
}
