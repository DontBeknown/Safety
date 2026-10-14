using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class HudNotification : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text notificationText;
        [SerializeField, Min(0.1f)]
        private float displayDuration = 3f;
        [SerializeField, Min(0f)]
        private float fadeDuration = 0.2f;
        [SerializeField]
        private Color normalColor = Color.white;
        [SerializeField]
        private Color warningColor = new Color(1f, 0.65f, 0.1f);

        private CanvasGroup canvasGroup;
        private Sequence notificationSequence;

        private void Awake()
        {
            Initialize();
            Hide();
        }

        private void Initialize()
        {
            if (notificationText == null || canvasGroup != null)
            {
                return;
            }

            canvasGroup = notificationText.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = notificationText.gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            notificationText.raycastTarget = false;
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            notificationSequence?.Kill();
            notificationSequence = null;
        }

        public void Show(string text, bool warning = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Hide();
                return;
            }

            if (notificationText == null)
            {
                return;
            }

            Initialize();
            notificationSequence?.Kill();
            notificationText.text = text;
            notificationText.color = warning ? warningColor : normalColor;
            notificationText.enabled = true;
            var lifetime = Mathf.Max(0.1f, displayDuration);
            var duration = Mathf.Clamp(fadeDuration, 0f, lifetime * 0.5f);
            canvasGroup.alpha = duration > 0f ? 0f : 1f;

            notificationSequence = DOTween.Sequence().SetUpdate(true);
            if (duration > 0f)
            {
                notificationSequence.Append(canvasGroup.DOFade(1f, duration).SetEase(Ease.OutQuad));
            }

            notificationSequence.AppendInterval(lifetime - duration * 2f);
            if (duration > 0f)
            {
                notificationSequence.Append(canvasGroup.DOFade(0f, duration).SetEase(Ease.InQuad));
            }

            notificationSequence.OnComplete(() =>
            {
                notificationSequence = null;
                Hide();
            });
        }

        public void ShowReading(string targetId, float reading)
        {
            Show("Current reading — " + targetId + ": " +
                reading.ToString("0.000", CultureInfo.InvariantCulture) + " A");
        }

        public void Hide()
        {
            notificationSequence?.Kill();
            notificationSequence = null;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (notificationText != null)
            {
                notificationText.text = string.Empty;
                notificationText.enabled = false;
            }
        }
    }
}
