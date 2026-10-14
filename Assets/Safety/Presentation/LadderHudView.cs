using TMPro;
using UnityEngine;

namespace SafetySimulation
{
    /// <summary>Attach to the ladder HUD panel. SafetyHud forwards ladder state here.</summary>
    [DisallowMultipleComponent]
    public sealed class LadderHudView : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private UnityEngine.UI.Slider leanSlider;
        [SerializeField] private UnityEngine.UI.Slider instabilitySlider;
        [Tooltip("Child object containing the instability label, status, and slider. Hidden at zero instability.")]
        [SerializeField] private GameObject instabilityHudRoot;
        [SerializeField] private UnityEngine.UI.Image leanDot;
        [Tooltip("Safe zone inside the balance slider's Handle Area.")]
        [SerializeField] private RectTransform leanSafeZone;
        [SerializeField] private TMP_Text instabilityStatusText;

        [Header("Colors")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color safeBalanceColor = new Color(0.25f, 0.75f, 0.5f);
        [SerializeField] private Color warningColor = new Color(1f, 0.65f, 0.1f);
        [SerializeField] private Color unsafeColor = new Color(1f, 0.25f, 0.2f);

        [Header("Fallback Ranges (Used Without Ladder Tuning)")]
        [SerializeField, Min(0.001f)] private float leanDisplayRange = 0.5f;
        [SerializeField, Min(0.001f)] private float instabilityDisplayRange = 1f;
        [SerializeField, Min(0f)] private float warningLeanDisplay = 0.25f;
        [SerializeField, Min(0f)] private float unsafeLeanDisplay = 0.35f;

        private bool initialized;
        private UnityEngine.UI.Image safeZoneImage;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ConfigureSlider(leanSlider, -1f, 1f);
            ConfigureSlider(instabilitySlider, 0f, 1f);

            if (leanSafeZone != null)
            {
                safeZoneImage = leanSafeZone.GetComponent<UnityEngine.UI.Image>();
                if (safeZoneImage != null)
                {
                    safeZoneImage.color = safeBalanceColor;
                    safeZoneImage.raycastTarget = false;
                }
            }
        }

        private static void ConfigureSlider(UnityEngine.UI.Slider slider, float minimum, float maximum)
        {
            if (slider == null)
            {
                return;
            }

            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.wholeNumbers = false;
            slider.interactable = false;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.transition = UnityEngine.UI.Selectable.Transition.None;
            slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        }

        public void SetBalance(float lean, float instability, bool onLadder = false,
            LadderTuning balanceTuning = null)
        {
            // SafetyHud can call this before Awake, including while this panel is inactive.
            Initialize();
            if (gameObject.activeSelf != onLadder)
            {
                gameObject.SetActive(onLadder);
            }

            leanSlider?.gameObject.SetActive(true);
            var showInstability = onLadder && instability > 0f;
            if (instabilityHudRoot != null)
            {
                instabilityHudRoot.SetActive(showInstability);
            }

            instabilitySlider?.gameObject.SetActive(showInstability);
            instabilityStatusText?.gameObject.SetActive(showInstability);

            var leanRange = Mathf.Max(0.001f, balanceTuning != null ? balanceTuning.MaxLean : leanDisplayRange);
            var instabilityRange = Mathf.Max(0.001f,
                balanceTuning != null ? balanceTuning.FallThreshold : instabilityDisplayRange);
            var warningLean = balanceTuning != null ? balanceTuning.WarningLean : warningLeanDisplay;
            var unsafeLean = balanceTuning != null ? balanceTuning.UnsafeLean : unsafeLeanDisplay;
            var normalizedInstability = Mathf.Clamp01(instability / instabilityRange);
            leanSlider?.SetValueWithoutNotify(Mathf.Clamp(lean / leanRange, -1f, 1f));
            instabilitySlider?.SetValueWithoutNotify(normalizedInstability);

            if (leanDot != null)
            {
                var magnitude = Mathf.Abs(lean);
                leanDot.color = magnitude >= unsafeLean ? unsafeColor :
                    magnitude >= warningLean ? warningColor : normalColor;
            }

            if (leanSafeZone != null)
            {
                var halfWidth = Mathf.Clamp01(warningLean / leanRange) * 0.5f;
                leanSafeZone.anchorMin = new Vector2(0.5f - halfWidth, 0f);
                leanSafeZone.anchorMax = new Vector2(0.5f + halfWidth, 1f);
                leanSafeZone.offsetMin = Vector2.zero;
                leanSafeZone.offsetMax = Vector2.zero;
            }

            var instabilityColor = normalizedInstability >= 0.8f ? unsafeColor : warningColor;
            if (instabilitySlider != null && instabilitySlider.targetGraphic != null)
            {
                instabilitySlider.targetGraphic.color = instabilityColor;
            }

            if (instabilityStatusText != null)
            {
                var status = normalizedInstability <= 0f ? "Stable" :
                    normalizedInstability < 0.5f ? "Low" :
                    normalizedInstability < 0.8f ? "High" : "Critical";
                if (instabilityStatusText.text != status)
                {
                    instabilityStatusText.text = status;
                }

                instabilityStatusText.color = normalizedInstability <= 0f ? normalColor : instabilityColor;
            }
        }
    }
}
