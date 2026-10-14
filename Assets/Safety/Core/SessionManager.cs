using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class SessionManager : MonoBehaviour
    {
        [SerializeField]
        private FirstPersonController player;
        [SerializeField]
        private Transform spawn;
        [SerializeField]
        private SafetyHud hud;
        [SerializeField]
        private AudioClip warningSound;
        [SerializeField]
        private AudioClip failureSound;
        [SerializeField]
        private AudioClip successSound;
        private InventoryManager inventory;
        private AchievementManager achievements;
        private AchievementDefinition[] checkpoint;
        private float nextWarningTime;
        private readonly List<string> mistakes = new List<string>();
        private readonly Dictionary<string, int> mistakeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        public IReadOnlyList<string> Mistakes => mistakes.AsReadOnly();

        public int GetMistakeCount(string reason)
        {
            return reason != null && mistakeCounts.TryGetValue(reason, out var count) ? count : 0;
        }
        public event Action StateChanged;
        public static SessionManager Instance
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
            if (SoundManager.Instance == null)
            {
                gameObject.AddComponent<SoundManager>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public int AttemptId
        {
            get;
            private set;
        } = 1;
        public bool IsPaused
        {
            get;
            private set;
        }
        public bool IsInvalid
        {
            get;
            private set;
        }
        public bool HasFallen
        {
            get;
            private set;
        }
        public bool IsCompleted
        {
            get;
            private set;
        }
        public string FailureReason
        {
            get;
            private set;
        }

        public bool CanInteract
        {
            get
            {
                return isActiveAndEnabled && player != null && player.isActiveAndEnabled && inventory != null &&
                    inventory.IsReady && inventory.isActiveAndEnabled && achievements != null && achievements.IsReady &&
                    achievements.isActiveAndEnabled && !IsPaused && !HasFallen && !IsCompleted;
            }
        }

        public bool CanWork
        {
            get
            {
                return CanInteract;
            }
        }

        private void Start()
        {
            inventory = InventoryManager.Instance;
            achievements = AchievementManager.Instance;
            if (player == null || spawn == null || inventory == null || achievements == null)
            {
                Debug.LogError("SessionManager needs player, spawn, inventory and achievements.", this);
                enabled = false;
                return;
            }

            PublishState();
        }

        public void Invalidate(string reason, bool fallen = false)
        {
            if (IsPaused || IsCompleted || HasFallen)
            {
                return;
            }

            RecordMistake(reason, !fallen);
            if (!fallen)
            {
                return;
            }

            HasFallen = true;
            inventory.CancelUse();
            if (player.ActiveLadder != null)
            {
                player.ActiveLadder.CancelTransitions();
            }

            if (hud != null)
            {
                hud.SetMessage(reason, true);
            }

            PlaySound(failureSound);
            PublishState();
        }

        public void RecordMistake(string reason, bool playWarning = true)
        {
            if (IsPaused || IsCompleted || HasFallen || string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            IsInvalid = true;
            if (playWarning) PlayWarning();
            reason = reason.Trim();
            if (mistakeCounts.TryGetValue(reason, out var count))
            {
                mistakeCounts[reason] = count + 1;
            }
            else
            {
                mistakeCounts.Add(reason, 1);
                mistakes.Add(reason);
                FailureReason = string.Join("\n", mistakes);
            }

            // Feedback does not change the input mode or interrupt a held tool.
            if (hud != null)
            {
                hud.SetMessage(reason, true);
                hud.SetSession(this);
            }
        }

        public void SetPaused(bool paused)
        {
            if (HasFallen || IsCompleted || IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            SoundManager.Instance?.PauseAll(paused);
            inventory.CancelUse();
            PublishState();
        }

        public void TogglePause()
        {
            SetPaused(!IsPaused);
        }

        public void CaptureCheckpoint()
        {
            if (checkpoint == null && CanWork)
            {
                inventory.ReportPpe();
                checkpoint = achievements.CaptureCheckpoint();
            }
        }

        public void Retry()
        {
            if (inventory == null || !inventory.IsReady || achievements == null || !achievements.IsReady)
            {
                return;
            }

            inventory.CancelUse();
            if (player.ActiveLadder != null)
            {
                player.ActiveLadder.ResetForRetry();
            }

            AttemptId++;
            IsPaused = false;
            IsInvalid = false;
            HasFallen = false;
            IsCompleted = false;
            FailureReason = null;
            mistakes.Clear();
            mistakeCounts.Clear();
            achievements.RestoreCheckpoint(checkpoint);
            player.Teleport(spawn.position, spawn.rotation);
            player.ResetMotion(true);
            inventory.RecreateEquippedTool();
            if (hud != null)
            {
                hud.ClearTransient();
            }

            SoundManager.Instance?.StopAll();
            nextWarningTime = 0f;

            PublishState();
            inventory.ReportPpe();
        }

        public void TryComplete()
        {
            if (!CanWork || !achievements.AllRequiredCompleted)
            {
                return;
            }

            IsCompleted = true;
            inventory.CancelUse();
            PlaySound(IsInvalid ? failureSound : successSound);
            PublishState();
        }

        private void PublishState()
        {
            if (player != null)
            {
                player.RefreshInputMode();
            }

            if (hud != null)
            {
                hud.SetSession(this);
            }

            StateChanged?.Invoke();
        }

        private void PlaySound(AudioClip clip)
        {
            SoundManager.Instance?.Play(clip);
        }

        public void PlayWarning(AudioClip clip = null)
        {
            var sound = clip != null ? clip : warningSound;
            if (sound == null || IsPaused || IsCompleted || HasFallen || Time.unscaledTime < nextWarningTime)
                return;

            nextWarningTime = Time.unscaledTime + 1f;
            SoundManager.Instance?.Play(sound);
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                inventory?.CancelUse();
                if (player != null)
                {
                    player.RefreshInputMode();
                }
            }
        }

        private void OnEnable()
        {
            if (inventory != null)
            {
                PublishState();
            }
        }
    }
}
