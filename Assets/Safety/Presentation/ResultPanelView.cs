using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class ResultPanelView : MonoBehaviour
    {
        [SerializeField]
        private GameObject panel;
        [Header("Outcome header")]
        [SerializeField] private TMP_Text outcomeTitle;
        [SerializeField] private TMP_Text outcomeSummary;
        [SerializeField] private UnityEngine.UI.Image outcomeIcon;
        [SerializeField] private Sprite fallIcon;
        [SerializeField] private Sprite completedIcon;
        [SerializeField] private Color fallColor = new Color(1f, 0.72f, 0.25f);
        [SerializeField] private Color completedColor = new Color(0.25f, 0.83f, 0.77f);

        [Header("Mistake cards")]
        [SerializeField] private TMP_Text mistakesHeading;
        [SerializeField] private UnityEngine.UI.ScrollRect mistakesScroll;
        [SerializeField] private RectTransform mistakesContent;
        [SerializeField] private ResultMistakeRow mistakeRowPrefab;
        [SerializeField] private TMP_Text noMistakesText;

        [Header("Legacy text (optional when cards are configured)")]
        [SerializeField]
        private TMP_Text resultText;
        [Header("Corrective guidance")]
        [SerializeField]
        [Tooltip("Match the recorded mistake text exactly to customize its corrective guidance.")]
        private MistakeGuidance[] guidance =
        {
            new MistakeGuidance("Climbing too quickly: wait until the rung movement finishes.",
                "Wait until you finish moving to the current rung before taking the next step."),
            new MistakeGuidance("Rushed climbing skipped a rung.",
                "Climb one rung at a time. Finish each step before moving again."),
            new MistakeGuidance("Climbing above the permitted standing limit.",
                "Stay at or below the permitted standing rung. Descend if you climb above it."),
            new MistakeGuidance("Overreach: leaning beyond the permitted range.",
                "Bring your balance back toward the center and keep your lean within the permitted range."),
            new MistakeGuidance("Jumping on the ladder is unsafe.",
                "Use the climb controls to move one rung at a time and descend to the bottom to dismount."),
            new MistakeGuidance("You fell after sustained ladder instability.",
                "Stop climbing when the ladder becomes unstable. Return your balance toward the center before continuing.")
        };

        private readonly List<string> rankedMistakes = new List<string>();
        private readonly StringBuilder textBuilder = new StringBuilder();
        private readonly List<ResultMistakeRow> rows = new List<ResultMistakeRow>();
        private bool showingResults;
        private int displayedAttempt = -1;

        public GameObject Panel => panel;
        private bool HasCardLayout => outcomeTitle != null && outcomeSummary != null &&
            mistakesScroll != null && mistakesContent != null && mistakeRowPrefab != null &&
            noMistakesText != null;
        public bool IsConfigured => panel != null && (HasCardLayout || resultText != null);

        [Serializable]
        private sealed class MistakeGuidance
        {
            public string Mistake;
            [Tooltip("Optional short title for the card. Leave empty to use the built-in title.")]
            public string Title;
            [TextArea(2, 4)]
            public string CorrectAction;

            public MistakeGuidance(string mistake, string correctAction)
            {
                Mistake = mistake;
                CorrectAction = correctAction;
            }
        }

        public void Initialize(GameObject existingPanel, TMP_Text existingText)
        {
            if (panel == null) panel = existingPanel;
            if (resultText == null) resultText = existingText;
        }

        // Call through SafetyHud.Instance.ResultPanel, or let SafetyHud.SetSession refresh it.
        public void Refresh(SessionManager session)
        {
            if (!IsConfigured) return;

            var showResults = session != null && (session.HasFallen || session.IsCompleted);
            if (!showResults)
            {
                if (resultText != null) resultText.text = string.Empty;
                foreach (var row in rows)
                {
                    if (row != null) row.gameObject.SetActive(false);
                }
                showingResults = false;
                panel.SetActive(false);
                return;
            }

            textBuilder.Clear();
            textBuilder.AppendLine(session.HasFallen ? "Attempt ended after a fall." :
                session.IsInvalid ? "Scenario completed with safety mistakes." : "Scenario completed safely.");

            rankedMistakes.Clear();
            foreach (var mistake in session.Mistakes)
            {
                rankedMistakes.Add(mistake);
            }

            // Stable insertion sort preserves the order first recorded when counts are equal.
            for (var i = 1; i < rankedMistakes.Count; i++)
            {
                var mistake = rankedMistakes[i];
                var count = session.GetMistakeCount(mistake);
                var j = i - 1;
                while (j >= 0 && session.GetMistakeCount(rankedMistakes[j]) < count)
                {
                    rankedMistakes[j + 1] = rankedMistakes[j];
                    j--;
                }

                rankedMistakes[j + 1] = mistake;
            }

            if (HasCardLayout)
            {
                var resetScroll = !showingResults || displayedAttempt != session.AttemptId;
                RefreshCards(session);
                panel.SetActive(true);
                if (resetScroll)
                {
                    Canvas.ForceUpdateCanvases();
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(mistakesContent);
                    mistakesScroll.StopMovement();
                    mistakesScroll.verticalNormalizedPosition = 1f;
                }
                showingResults = true;
                displayedAttempt = session.AttemptId;
                return;
            }

            textBuilder.AppendLine();
            if (rankedMistakes.Count == 0)
            {
                textBuilder.AppendLine("No safety mistakes recorded.");
            }
            else
            {
                textBuilder.AppendLine("Safety mistakes (most frequent first)");
                for (var i = 0; i < rankedMistakes.Count; i++)
                {
                    var mistake = rankedMistakes[i];
                    var count = session.GetMistakeCount(mistake);
                    textBuilder.Append(i + 1).Append(". ").Append(mistake)
                        .Append(" — ").Append(count).AppendLine(count == 1 ? " time" : " times");
                    textBuilder.Append("Correct action: ").AppendLine(GetCorrectAction(mistake));
                    textBuilder.AppendLine();
                }
            }

            resultText.text = textBuilder.ToString();
            panel.SetActive(true);
        }

        private void RefreshCards(SessionManager session)
        {
            var fallen = session.HasFallen;
            outcomeTitle.text = fallen ? "Attempt ended after a fall" : "Checklist completed";
            outcomeSummary.text = fallen ? "Review your mistakes before trying again." :
                session.IsInvalid ? "All tasks completed with safety mistakes." :
                "All tasks completed safely. Well done!";

            if (outcomeIcon != null)
            {
                outcomeIcon.sprite = fallen ? fallIcon : completedIcon;
                outcomeIcon.color = fallen ? fallColor : completedColor;
                outcomeIcon.gameObject.SetActive(outcomeIcon.sprite != null);
            }

            if (resultText != null) resultText.gameObject.SetActive(false);
            var hasMistakes = rankedMistakes.Count > 0;
            if (mistakesHeading != null)
            {
                mistakesHeading.text = "Mistakes • most frequent first";
                mistakesHeading.gameObject.SetActive(hasMistakes);
            }
            mistakesScroll.gameObject.SetActive(hasMistakes);
            noMistakesText.text = "No safety mistakes recorded.";
            noMistakesText.gameObject.SetActive(!hasMistakes);

            // Reuse generated cards when the panel is refreshed or an attempt is retried.
            for (var i = 0; i < rankedMistakes.Count; i++)
            {
                if (i >= rows.Count)
                {
                    rows.Add(Instantiate(mistakeRowPrefab, mistakesContent, false));
                }
                else if (rows[i] == null)
                {
                    rows[i] = Instantiate(mistakeRowPrefab, mistakesContent, false);
                }

                var mistake = rankedMistakes[i];
                rows[i].SetMistake(i + 1, GetTitle(mistake), session.GetMistakeCount(mistake),
                    GetCorrectAction(mistake));
                rows[i].transform.SetSiblingIndex(i);
                rows[i].gameObject.SetActive(true);
            }

            for (var i = rankedMistakes.Count; i < rows.Count; i++)
            {
                if (rows[i] != null) rows[i].gameObject.SetActive(false);
            }
        }

        private string GetTitle(string mistake)
        {
            var entry = FindGuidance(mistake);
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Title)) return entry.Title;

            switch (mistake)
            {
                case "Climbing too quickly: wait until the rung movement finishes.": return "Climbed too quickly";
                case "Rushed climbing skipped a rung.": return "Skipped a rung";
                case "Climbing above the permitted standing limit.": return "Exceeded standing limit";
                case "Overreach: leaning beyond the permitted range.": return "Overreached";
                case "Jumping on the ladder is unsafe.": return "Jumped on the ladder";
                case "You fell after sustained ladder instability.": return "Lost ladder balance";
                default: return mistake;
            }
        }

        private string GetCorrectAction(string mistake)
        {
            var entry = FindGuidance(mistake);
            return entry != null && !string.IsNullOrWhiteSpace(entry.CorrectAction) ? entry.CorrectAction :
                "Stop the unsafe action and follow the scenario's safety instructions before continuing.";
        }

        private MistakeGuidance FindGuidance(string mistake)
        {
            if (guidance != null)
            {
                foreach (var entry in guidance)
                {
                    if (entry != null && string.Equals(entry.Mistake, mistake, StringComparison.Ordinal))
                    {
                        return entry;
                    }
                }
            }

            return null;
        }
    }
}
