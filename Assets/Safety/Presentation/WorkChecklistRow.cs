using System;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace SafetySimulation
{
    /// <summary>An Inspector-authored row; DOTween animates changes on existing UI objects.</summary>
    [DisallowMultipleComponent]
    public sealed class WorkChecklistRow : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private TMP_Text numberText;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text currentText;
        [SerializeField] private UnityEngine.UI.Image background;
        [SerializeField] private GameObject pendingIcon;
        [SerializeField] private GameObject completedIcon;
        [SerializeField] private ProgressLine inspection = new ProgressLine();
        [SerializeField] private ProgressLine measurement = new ProgressLine();

        [Header("Colors")]
        [SerializeField] private Color normalColor = new Color(0.94f, 0.95f, 0.96f);
        [SerializeField] private Color currentColor = new Color(1f, 0.68f, 0.12f);
        [SerializeField] private Color completedColor = new Color(0.28f, 0.82f, 0.42f);
        [SerializeField, Range(0f, 1f)] private float highlightOpacity = 0.045f;

        [Header("Change animation")]
        [SerializeField, Tooltip("Optional CanvasGroup already on the row, used for a subtle fade.")]
        private CanvasGroup rowCanvasGroup;
        [SerializeField, Tooltip("Optional target for the scale pulse; defaults to the number label.")]
        private RectTransform pulseTarget;
        [SerializeField, Min(0.01f)] private float animationDuration = 0.3f;
        [SerializeField, Range(1f, 1.2f)] private float pulseScale = 1.04f;

        private Sequence changeTween;
        private Tween backgroundTween;
        private Vector3 originalScale;
        private float originalAlpha;
        private bool capturedAppearance, hasProgress;
        private int lastNumber, lastInspected, lastInspectTotal, lastMeasured, lastMeasureTotal;
        private string lastTitle;
        private bool lastCompleted, lastCurrent;

        [Serializable]
        private sealed class ProgressLine
        {
            [Tooltip("Existing parent containing this counter and its bar.")]
            public GameObject Root;
            public TMP_Text Label;
            public TMP_Text Count;
            [Tooltip("Existing Slider used as a read-only progress bar. Its range is normalized to 0–1.")]
            public UnityEngine.UI.Slider Slider;
            [NonSerialized] private Tween valueTween;
            [NonSerialized] private float targetValue;

            public void Set(string label, int completed, int total, Color color, bool animate, float duration)
            {
                if (Root != null) Root.SetActive(total > 0);
                if (Label != null) Label.text = label;
                if (Count != null) Count.text = total > 0 ? completed + " / " + total : string.Empty;
                valueTween?.Kill();
                valueTween = null;
                if (Slider == null) return;
                targetValue = total > 0 ? Mathf.Clamp01((float)completed / total) : 0f;
                Slider.minValue = 0f;
                Slider.maxValue = 1f;
                Slider.wholeNumbers = false;
                Slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
                Slider.transition = UnityEngine.UI.Selectable.Transition.None;
                Slider.interactable = false;
                Slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                var fillImage = Slider.fillRect != null
                    ? Slider.fillRect.GetComponent<UnityEngine.UI.Image>() : null;
                if (fillImage != null) fillImage.color = color;
                if (animate && total > 0 && Slider.gameObject.activeInHierarchy)
                {
                    var slider = Slider;
                    valueTween = DOTween.To(() => slider.value, slider.SetValueWithoutNotify, targetValue, duration)
                        .SetEase(Ease.OutCubic).SetUpdate(true);
                }
                else Slider.SetValueWithoutNotify(targetValue);
            }

            public void Stop()
            {
                valueTween?.Kill();
                valueTween = null;
                if (Slider != null) Slider.SetValueWithoutNotify(targetValue);
            }
        }

        public void SetProgress(int number, string title, bool completed, bool current,
            int inspected, int inspectTotal, int measured, int measureTotal, bool animate = true)
        {
            title = title ?? string.Empty;
            var changed = !hasProgress || number != lastNumber || title != lastTitle ||
                completed != lastCompleted || current != lastCurrent || inspected != lastInspected ||
                inspectTotal != lastInspectTotal || measured != lastMeasured || measureTotal != lastMeasureTotal;
            if (!changed && animate) return;

            CaptureAppearance();
            var playAnimation = animate && changed && hasProgress && Application.isPlaying && isActiveAndEnabled;
            var duration = Mathf.Max(0.01f, animationDuration);
            StopVisualTweens();
            var color = current ? currentColor : normalColor;
            if (numberText != null)
            {
                numberText.text = number.ToString("00", CultureInfo.InvariantCulture);
                numberText.color = color;
            }
            if (titleText != null)
            {
                titleText.text = title;
                titleText.color = color;
            }
            if (currentText != null)
            {
                currentText.text = "CURRENT";
                currentText.color = currentColor;
                currentText.gameObject.SetActive(current);
            }
            if (pendingIcon != null) pendingIcon.SetActive(!completed && !current);
            if (completedIcon != null) completedIcon.SetActive(completed);
            if (background != null)
            {
                var highlight = current ? new Color(currentColor.r, currentColor.g, currentColor.b, highlightOpacity) : Color.clear;
                if (playAnimation) backgroundTween = background.DOColor(highlight, duration).SetUpdate(true);
                else background.color = highlight;
            }
            inspection?.Set("Inspected", inspected, inspectTotal,
                inspectTotal > 0 && inspected == inspectTotal ? completedColor : currentColor, playAnimation, duration);
            measurement?.Set("Measured", measured, measureTotal,
                measureTotal > 0 && measured == measureTotal ? completedColor : currentColor, playAnimation, duration);

            if (playAnimation && (pulseTarget != null || rowCanvasGroup != null))
            {
                changeTween = DOTween.Sequence().SetUpdate(true);
                if (pulseTarget != null)
                {
                    changeTween.Append(pulseTarget.DOScale(originalScale * pulseScale, duration * 0.4f).SetEase(Ease.OutQuad));
                    changeTween.Append(pulseTarget.DOScale(originalScale, duration * 0.6f).SetEase(Ease.OutQuad));
                }
                if (rowCanvasGroup != null)
                {
                    rowCanvasGroup.alpha = originalAlpha * 0.75f;
                    changeTween.Insert(0f, rowCanvasGroup.DOFade(originalAlpha, duration).SetEase(Ease.OutQuad));
                }
            }

            lastNumber = number;
            lastTitle = title;
            lastCompleted = completed;
            lastCurrent = current;
            lastInspected = inspected;
            lastInspectTotal = inspectTotal;
            lastMeasured = measured;
            lastMeasureTotal = measureTotal;
            hasProgress = true;
        }

        private void Awake() => CaptureAppearance();

        private void CaptureAppearance()
        {
            if (capturedAppearance) return;
            if (pulseTarget == null && numberText != null) pulseTarget = numberText.rectTransform;
            if (pulseTarget != null) originalScale = pulseTarget.localScale;
            if (rowCanvasGroup != null) originalAlpha = rowCanvasGroup.alpha;
            capturedAppearance = true;
        }

        private void StopVisualTweens()
        {
            changeTween?.Kill();
            changeTween = null;
            backgroundTween?.Kill();
            backgroundTween = null;
            if (!capturedAppearance) return;
            if (pulseTarget != null) pulseTarget.localScale = originalScale;
            if (rowCanvasGroup != null) rowCanvasGroup.alpha = originalAlpha;
        }

        private void OnDisable()
        {
            StopVisualTweens();
            inspection?.Stop();
            measurement?.Stop();
            hasProgress = false;
        }

        private void OnDestroy()
        {
            StopVisualTweens();
            inspection?.Stop();
            measurement?.Stop();
        }
    }
}
