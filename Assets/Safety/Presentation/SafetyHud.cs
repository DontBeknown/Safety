using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class SafetyHud : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text promptText;
        [SerializeField]
        private HudNotification notification;
        [SerializeField]
        private WorkChecklistView checklist;
        [SerializeField]
        private TMP_Text resultText;
        [SerializeField]
        private ResultPanelView resultPanelView;
        [Header("Ladder HUD")]
        [SerializeField]
        private LadderHudView ladderHud;
        [Header("Other HUD")]
        [SerializeField]
        private ToolProgressBar toolProgressBar;
        [SerializeField]
        private GameObject pausePanel;
        [SerializeField]
        private GameObject resultPanel;
        [SerializeField]
        private Button retryButton;
        [SerializeField]
        private Button resumeButton;
        [SerializeField]
        private CanvasGroup fadeOverlay;
        [SerializeField]
        private PpeIcon[] ppeIcons = Array.Empty<PpeIcon>();
        [SerializeField]
        private Color normalColor = Color.white;
        [SerializeField]
        private Color equippedColor = new Color(0.25f, 0.9f, 0.4f);
        private SessionManager session;
        private bool callbacksAttached;
        private bool hideLadderHud;
        [Serializable]
        private sealed class PpeIcon
        {
            public string PpeId;
            public Image Icon;
        }

        public static SafetyHud Instance { get; private set; }

        public ToolProgressBar ToolProgressBar => toolProgressBar;

        public ResultPanelView ResultPanel => GetResultPanel();

        private ResultPanelView GetResultPanel()
        {
            if (resultPanelView == null)
            {
                resultPanelView = GetComponent<ResultPanelView>();
                if (resultPanelView == null)
                {
                    resultPanelView = gameObject.AddComponent<ResultPanelView>();
                }
            }

            // Keep existing scene references working without editing scenes or prefabs.
            resultPanelView.Initialize(resultPanel, resultText);
            return resultPanelView;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance()
        {
            Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
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
            GetResultPanel();

            SetToolProgress(0f, string.Empty);
            SetBalance(0f, 0f);
            notification?.Hide();
        }

        private void Start()
        {
            session = SessionManager.Instance;
            if (session == null || retryButton == null || resumeButton == null || pausePanel == null ||
                promptText == null || notification == null || !GetResultPanel().IsConfigured ||
                ladderHud == null || transform.IsChildOf(pausePanel.transform) ||
                transform.IsChildOf(GetResultPanel().Panel.transform))
            {
                Debug.LogError("Assign SafetyHud texts, LadderHudView, distinct panels/buttons; keep it outside those panels.",
                    this);
                enabled = false;
                return;
            }

            if (checklist == null)
            {
                Debug.LogWarning("Assign the scene-authored WorkChecklistView to SafetyHud.Checklist.", this);
            }

            AttachCallbacks();
            RefreshPresentation();
        }

        private void OnEnable()
        {
            AttachCallbacks();
            if (session != null)
            {
                RefreshPresentation();
            }
        }

        private void OnDisable()
        {
            notification?.Hide();
            if (!callbacksAttached)
            {
                return;
            }

            retryButton.onClick.RemoveListener(Retry);
            resumeButton.onClick.RemoveListener(Resume);
            callbacksAttached = false;
        }

        private void AttachCallbacks()
        {
            if (session == null || callbacksAttached || retryButton == null || resumeButton == null)
            {
                return;
            }

            retryButton.onClick.AddListener(Retry);
            resumeButton.onClick.AddListener(Resume);
            callbacksAttached = true;
        }

        private void Retry()
        {
            session.Retry();
        }

        private void Resume()
        {
            session.SetPaused(false);
        }

        private void RefreshPresentation()
        {
            SetSession(session);
            if (InventoryManager.Instance != null && InventoryManager.Instance.IsReady)
            {
                SetInventory(InventoryManager.Instance);
            }

            if (AchievementManager.Instance != null && AchievementManager.Instance.IsReady)
            {
                SetAchievements(AchievementManager.Instance);
            }
        }

        public void SetSession(SessionManager state)
        {
            if (state == null)
            {
                return;
            }

            if (pausePanel != null)
            {
                pausePanel.SetActive(state.IsPaused);
            }

            hideLadderHud = state.HasFallen;
            if (hideLadderHud && ladderHud != null)
            {
                ladderHud.gameObject.SetActive(false);
            }

            ResultPanel.Refresh(state);
        }

        public void SetInventory(InventoryManager inventory)
        {
            foreach (var entry in ppeIcons)
            {
                if (entry != null && entry.Icon != null)
                {
                    entry.Icon.color = inventory.HasPpe(entry.PpeId) ? equippedColor : normalColor;
                }
            }
        }

                public void SetAchievements(AchievementManager manager)
        {
            if (checklist != null)
            {
                checklist.Refresh(manager);
            }
        }

        public void SetBalance(float lean, float instability, bool onLadder = false,
            LadderTuning balanceTuning = null)
        {
            ladderHud?.SetBalance(lean, instability, onLadder && !hideLadderHud, balanceTuning);
        }

        public void SetToolProgress(float progress, string label)
        {
            toolProgressBar?.SetProgress(progress);
        }

        public void SetMessage(string message, bool warning)
        {
            notification?.Show(message, warning);
        }

        public void SetPrompt(string prompt)
        {
            SetText(promptText, prompt ?? string.Empty);
            promptText.gameObject.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(prompt));
        }

        public void SetReading(string targetId, float reading)
        {
            notification?.ShowReading(targetId, reading);
        }

        public void SetFade(float alpha)
        {
            if (fadeOverlay == null)
            {
                return;
            }

            fadeOverlay.alpha = Mathf.Clamp01(alpha);
            fadeOverlay.interactable = false;
            fadeOverlay.blocksRaycasts = false;
        }

        public void ClearTransient()
        {
            notification?.Hide();
            SetPrompt(string.Empty);
            SetToolProgress(0f, string.Empty);
            SetFade(0f);
            SetBalance(0f, 0f);
        }

        private static void SetText(TMP_Text target, string text)
        {
            if (target != null && target.text != text)
            {
                target.text = text;
            }
        }
    }
}
