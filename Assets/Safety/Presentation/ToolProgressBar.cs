using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class ToolProgressBar : MonoBehaviour
    {
        [SerializeField]
        private UnityEngine.UI.Image fillImage;

        public void SetProgress(float progress)
        {
            var normalizedProgress = Mathf.Clamp01(progress);
            if (fillImage != null)
            {
                fillImage.type = UnityEngine.UI.Image.Type.Filled;
                fillImage.fillAmount = normalizedProgress;
            }

            gameObject.SetActive(normalizedProgress > 0f);
        }
    }
}
