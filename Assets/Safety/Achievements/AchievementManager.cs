using System;
using System.Collections.Generic;
using UnityEngine;

namespace SafetySimulation
{
    [Serializable]
    public sealed class AchievementDisplayCategory
    {
        public string Description;
        public List<AchievementDefinition> Achievements = new List<AchievementDefinition>();
    }

    [DisallowMultipleComponent]
    public sealed class AchievementManager : MonoBehaviour
    {
        [SerializeField]
        private AchievementDefinition[] achievements = Array.Empty<AchievementDefinition>();
        [SerializeField]
        private List<AchievementDisplayCategory> displayCategories = new List<AchievementDisplayCategory>();
        [SerializeField]
        private SafetyHud hud;
        [SerializeField]
        private AudioClip taskCompleteSound;
        private List<AchievementDisplayCategory> defaultDisplayCategories;
        private readonly HashSet<AchievementDefinition> completed = new HashSet<AchievementDefinition>();
        private SessionManager session;
        public event Action ProgressChanged;
        public static AchievementManager Instance
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

        public IReadOnlyList<AchievementDefinition> Definitions
        {
            get
            {
                return Array.AsReadOnly(achievements);
            }
        }

        public bool AllRequiredCompleted
        {
            get
            {
                if (!IsReady)
                {
                    return false;
                }

                foreach (var achievement in achievements)
                {
                    if (achievement.Type != AchievementType.Inspect && achievement.Required && !completed.Contains(achievement))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public IReadOnlyList<AchievementDisplayCategory> DisplayCategories
        {
            get
            {
                if (displayCategories != null && displayCategories.Count > 0)
                {
                    return displayCategories.AsReadOnly();
                }

                if (defaultDisplayCategories == null)
                {
                    defaultDisplayCategories = new List<AchievementDisplayCategory>
                    {
                        CreateDisplayCategory("Wear PPE", AchievementType.Equip),
                        CreateDisplayCategory("Climb ladder", AchievementType.Climb),
                        CreateDisplayCategory("Measure current", AchievementType.Measure)
                    };
                }

                return defaultDisplayCategories.AsReadOnly();
            }
        }

        private AchievementDisplayCategory CreateDisplayCategory(string description, params AchievementType[] types)
        {
            var category = new AchievementDisplayCategory { Description = description };
            foreach (var achievement in achievements)
            {
                if (achievement != null && Array.IndexOf(types, achievement.Type) >= 0)
                {
                    category.Achievements.Add(achievement);
                }
            }

            return category;
        }

        private void Start()
        {
            session = SessionManager.Instance;
            if (session == null)
            {
                Debug.LogError("AchievementManager needs SessionManager.", this);
                enabled = false;
                return;
            }

            if (!TryValidate(out var reason))
            {
                Debug.LogError(reason, this);
                enabled = false;
                return;
            }

            IsReady = true;
            RefreshPresentation();
        }

        public bool TryValidate(out string reason)
        {
            if (achievements == null || achievements.Length == 0)
            {
                reason = "Assign the scenario's achievements.";
                return false;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            var configured = new HashSet<AchievementDefinition>(achievements);
            var hasRequired = false;
            foreach (var achievement in achievements)
            {
                if (achievement == null || string.IsNullOrWhiteSpace(achievement.Id) ||
                    !Enum.IsDefined(typeof(AchievementType), achievement.Type) || !keys.Add(achievement.Type + ":" +
                    achievement.Id))
                {
                    reason = "Achievements need valid types and unique, non-empty type/ID pairs.";
                    return false;
                }

                hasRequired |= achievement.Required && achievement.Type != AchievementType.Inspect;
                if (achievement.Prerequisites == null)
                {
                    reason = "Achievement prerequisites cannot be null.";
                    return false;
                }

                foreach (var prerequisite in achievement.Prerequisites)
                {
                    if (prerequisite == null || !configured.Contains(prerequisite))
                    {
                        reason = $"Achievement '{achievement.Id}' references an unconfigured prerequisite.";
                        return false;
                    }
                }
            }

            var visiting = new HashSet<AchievementDefinition>();
            var visited = new HashSet<AchievementDefinition>();
            foreach (var achievement in achievements)
            {
                if (HasCycle(achievement, visiting, visited))
                {
                    reason = $"Achievement '{achievement.Id}' has a prerequisite cycle.";
                    return false;
                }
            }

            reason = hasRequired ? null : "Configure at least one required achievement.";
            return hasRequired;
        }

        private bool HasCycle(AchievementDefinition achievement, HashSet<AchievementDefinition> visiting,
            HashSet<AchievementDefinition> visited)
        {
            if (visited.Contains(achievement))
            {
                return false;
            }

            if (!visiting.Add(achievement))
            {
                return true;
            }

            foreach (var prerequisite in achievement.Prerequisites)
            {
                if (HasCycle(prerequisite, visiting, visited))
                {
                    return true;
                }
            }

            visiting.Remove(achievement);
            visited.Add(achievement);
            return false;
        }

        public AchievementDefinition Find(AchievementType type, string id)
        {
            foreach (var achievement in achievements)
            {
                if (achievement != null && achievement.Type == type && achievement.Id == id)
                {
                    return achievement;
                }
            }

            return null;
        }

        public AchievementState GetState(AchievementDefinition achievement)
        {
            if (!IsReady || achievement == null || Array.IndexOf(achievements, achievement) < 0)
            {
                return AchievementState.Locked;
            }

            if (completed.Contains(achievement))
            {
                return AchievementState.Completed;
            }

            foreach (var prerequisite in achievement.Prerequisites)
            {
                if (!IsPrerequisiteSatisfied(prerequisite))
                {
                    return AchievementState.Locked;
                }
            }

            return AchievementState.Available;
        }

        private bool IsPrerequisiteSatisfied(AchievementDefinition prerequisite)
        {
            if (prerequisite == null) return false;
            if (prerequisite.Type != AchievementType.Inspect)
                return completed.Contains(prerequisite);

            // Existing assets can still reference Inspect. Preserve its underlying safety gates
            // without requiring a separate, permanently completed flashlight inspection.
            foreach (var ancestor in prerequisite.Prerequisites)
                if (!IsPrerequisiteSatisfied(ancestor)) return false;
            return true;
        }

        public bool IsCompleted(AchievementType type, string id)
        {
            return GetState(Find(type, id)) == AchievementState.Completed;
        }

        public bool Report(AchievementType type, string id)
        {
            return Report(type, id, session != null ? session.AttemptId : -1);
        }

        public bool Report(AchievementType type, string id, int attemptId)
        {
            if (type == AchievementType.Inspect) return false;
            if (!isActiveAndEnabled || !IsReady || session == null || !session.CanWork || session.AttemptId != attemptId)
            {
                return false;
            }

            if (type == AchievementType.Inspect || type == AchievementType.Measure)
            {
                var inventory = InventoryManager.Instance;
                var ladder = inventory != null ? inventory.ActiveLadder : null;
                if (ladder == null || !ladder.IsAttached ||
                    (type == AchievementType.Measure && !inventory.AllRequiredPpe))
                {
                    return false;
                }
            }

            // Unique type/ID pairs mean one report can complete only one definition.
            var achievement = Find(type, id);
            if (GetState(achievement) != AchievementState.Available)
            {
                return false;
            }

            completed.Add(achievement);
            RefreshPresentation();
            ProgressChanged?.Invoke();
            session.TryComplete();
            if (!session.IsCompleted) SoundManager.Instance?.Play(taskCompleteSound);
            return true;
        }

        public AchievementDefinition[] CaptureCheckpoint()
        {
            var snapshot = new AchievementDefinition[completed.Count];
            completed.CopyTo(snapshot);
            return snapshot;
        }

        public void RestoreCheckpoint(AchievementDefinition[] snapshot)
        {
            completed.Clear();
            if (snapshot != null)
            {
                foreach (var achievement in snapshot)
                {
                    if (achievement != null && Array.IndexOf(achievements, achievement) >= 0)
                    {
                        completed.Add(achievement);
                    }
                }
            }

            RefreshPresentation();
            ProgressChanged?.Invoke();
        }

        private void OnEnable()
        {
            if (IsReady)
            {
                RefreshPresentation();
            }
        }

        private void RefreshPresentation()
        {
            if (hud != null)
            {
                hud.SetAchievements(this);
            }
        }
    }
}
