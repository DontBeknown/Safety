using TMPro;
using UnityEngine;

namespace SafetySimulation
{
    /// <summary>Inspector-authored result card instantiated by ResultPanelView.</summary>
    [DisallowMultipleComponent]
    public sealed class ResultMistakeRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text rankText;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private TMP_Text correctActionText;

        public void SetMistake(int rank, string title, int count, string correctAction)
        {
            if (rankText != null) rankText.text = rank.ToString();
            if (titleText != null) titleText.text = title ?? string.Empty;
            if (countText != null) countText.text = count + (count == 1 ? " time" : " times");
            if (correctActionText != null) correctActionText.text = correctAction ?? string.Empty;
        }
    }
}
