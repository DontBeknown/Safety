using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace SafetySimulation
{
    /// <summary>Updates Inspector-assigned UI. Never creates UI objects at runtime.</summary>
    [DisallowMultipleComponent]
    public sealed class WorkChecklistView : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private TMP_Text completionText;
        [SerializeField] private UnityEngine.UI.Image panelBackground;
        [SerializeField, Tooltip("Existing rows: Wear PPE, Climb ladder, Measure current. Additional rows are hidden.")]
        private WorkChecklistRow[] rows = Array.Empty<WorkChecklistRow>();

        [Header("Background")]
        [SerializeField] private Color panelColor = new Color(0.10f, 0.13f, 0.15f, 1f);
        [SerializeField, Range(0f, 1f), Tooltip("0 is transparent; text opacity is unchanged.")]
        private float backgroundOpacity = 0.25f;

        [Header("Change animation")]
        [SerializeField] private bool animateChanges = true;
        [SerializeField, Min(0.01f)] private float countAnimationDuration = 0.25f;

        private readonly List<CategoryProgress> progress = new List<CategoryProgress>();
        private AchievementManager lastManager;
        private Tween countTween;
        private Vector3 countScale;
        private bool countScaleCaptured, hasRendered, warnedAboutRows;
        private int lastCompleted = -1, lastTotal = -1;

        private struct CategoryProgress
        {
            public string Title;
            public int Total, Completed, MeasureTotal, Measured;
            public bool Available;
        }

        private void Awake()
        {
            ApplyBackground();
            if (headerText != null) headerText.text = "WORK CHECKLIST";
            CaptureCountScale();
        }

        public void Refresh(AchievementManager manager)
        {
            var sameManager = lastManager == manager;
            lastManager = manager;
            progress.Clear();
            ApplyBackground();

            if (manager != null && manager.IsReady)
            {
                AddProgress(manager, AchievementType.Equip, "Wear PPE");
                AddProgress(manager, AchievementType.Climb, "Climb ladder");
                AddProgress(manager, AchievementType.Measure, "Measure current");
            }

            var animate = animateChanges && hasRendered && sameManager && Application.isPlaying && isActiveAndEnabled;
            var completedCategories = 0;
            var currentSelected = false;
            var rowCount = rows != null ? rows.Length : 0;
            var missingRow = progress.Count > rowCount;
            for (var i = 0; i < progress.Count; i++)
            {
                var item = progress[i];
                var done = item.Total > 0 && item.Completed == item.Total;
                var current = !done && item.Available && !currentSelected;
                currentSelected |= current;
                if (done) completedCategories++;
                if (i >= rowCount || rows[i] == null)
                {
                    missingRow = true;
                    continue;
                }
                var row = rows[i];
                row.gameObject.SetActive(true);
                row.SetProgress(i + 1, item.Title, done, current,
                    0, 0, item.Measured, item.MeasureTotal, animate);
            }
            for (var i = progress.Count; i < rowCount; i++)
                if (rows[i] != null) rows[i].gameObject.SetActive(false);

            if (missingRow && !warnedAboutRows)
            {
                Debug.LogWarning("Assign three WorkChecklistRows for Wear PPE, Climb ladder and Measure current. Rows are not created at runtime.", this);
                warnedAboutRows = true;
            }
            if (!missingRow) warnedAboutRows = false;

            if (completionText != null)
            {
                completionText.text = completedCategories + " / " + progress.Count + " COMPLETE";
                if (animate && (completedCategories != lastCompleted || progress.Count != lastTotal))
                {
                    StopCountTween();
                    CaptureCountScale();
                    var duration = Mathf.Max(0.01f, countAnimationDuration);
                    countTween = DOTween.Sequence()
                        .Append(completionText.transform.DOScale(countScale * 1.04f, duration * 0.4f).SetEase(Ease.OutQuad))
                        .Append(completionText.transform.DOScale(countScale, duration * 0.6f).SetEase(Ease.OutQuad))
                        .SetUpdate(true);
                }
                else if (!animate) StopCountTween();
            }
            lastCompleted = completedCategories;
            lastTotal = progress.Count;
            hasRendered = manager != null && manager.IsReady;
        }

        private void AddProgress(AchievementManager manager, AchievementType type, string title)
        {
            var item = new CategoryProgress { Title = title };
            foreach (var achievement in manager.Definitions)
            {
                if (achievement == null || achievement.Type != type) continue;
                var state = manager.GetState(achievement);
                var done = state == AchievementState.Completed;
                item.Total++;
                if (done) item.Completed++;
                item.Available |= state == AchievementState.Available;
                if (type == AchievementType.Measure)
                {
                    item.MeasureTotal++;
                    if (done) item.Measured++;
                }
            }
            progress.Add(item);
        }

        private void OnEnable()
        {
            hasRendered = false;
            if (lastManager != null) Refresh(lastManager);
        }

        private void OnDisable()
        {
            StopCountTween();
            hasRendered = false;
        }

        private void OnDestroy() => StopCountTween();

        private void CaptureCountScale()
        {
            if (countScaleCaptured || completionText == null) return;
            countScale = completionText.transform.localScale;
            countScaleCaptured = true;
        }

        private void StopCountTween()
        {
            countTween?.Kill();
            countTween = null;
            if (countScaleCaptured && completionText != null) completionText.transform.localScale = countScale;
        }

        private void ApplyBackground()
        {
            if (panelBackground == null) return;
            var color = panelColor;
            color.a = backgroundOpacity;
            panelBackground.color = color;
        }

        private void OnValidate() => ApplyBackground();
    }
}
