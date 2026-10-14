using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class InspectionPoint : MonoBehaviour
    {
        public string Id;
        public Transform ContactAnchor;
        public float CurrentReading;
        [Tooltip("Object hidden only while this point is illuminated by the player's flashlight.")]
        public GameObject VisualBlock;

        public bool IsIlluminated { get; private set; }

        public void SetIlluminated(bool illuminated)
        {
            IsIlluminated = illuminated && isActiveAndEnabled;
            if (VisualBlock != null && VisualBlock.activeSelf == IsIlluminated)
            {
                VisualBlock.SetActive(!IsIlluminated);
            }
        }

        private void OnEnable() => SetIlluminated(false);

        private void OnDisable() => SetIlluminated(false);

        public bool TryValidate(out string reason)
        {
            if (string.IsNullOrWhiteSpace(Id) || ContactAnchor == null ||
                ContactAnchor.gameObject.scene != gameObject.scene || float.IsNaN(CurrentReading) ||
                float.IsInfinity(CurrentReading))
            {
                reason = $"Inspection point '{name}' needs an ID, scene contact anchor and finite reading.";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
