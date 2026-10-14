using UnityEngine;

namespace SafetySimulation
{
    /// <summary>Fictional prototype values; these are not certified safety limits.</summary>
    [CreateAssetMenu(menuName = "Safety/Ladder Tuning")]
    public sealed class LadderTuning : ScriptableObject
    {
        [Min(0.01f)]
        public float RungMoveDuration = 0.45f;
        [Min(0.01f)]
        public float MaxLean = 0.50f;
        [Min(0f)]
        public float WarningLean = 0.25f;
        [Min(0f)]
        public float UnsafeLean = 0.35f;
        [Min(0.01f)]
        public float LeanSpeed = 0.50f;
        [Min(0.01f)]
        public float LeanReturnSpeed = 0.60f;
        [Min(0f)]
        public float InstabilityDecay = 0.20f;
        [Min(0f)]
        public float RushDisturbance = 0.25f;
        [Min(0f)]
        public float SkipDisturbance = 0.40f;
        [Min(0f)]
        public float JumpDisturbance = 0.60f;
        [Min(0f)]
        public float UnsafeGrowth = 0.50f;
        [Min(0.01f)]
        public float FallThreshold = 1.0f;
        [Min(0.01f)]
        public float FallDuration = 0.25f;
        [Range(0f, 10f)]
        public float MaximumTilt = 4f;
        public bool UseCameraRoll;
        [Range(0f, 2f)]
        public float MaximumCameraRoll = 1f;
        [Min(0.01f)]
        public float FallVisualDuration = 0.6f;
        public bool TryValidate(out string reason)
        {
            var values = new[]
            {
                RungMoveDuration,
                MaxLean,
                WarningLean,
                UnsafeLean,
                LeanSpeed,
                LeanReturnSpeed,
                InstabilityDecay,
                RushDisturbance,
                SkipDisturbance,
                JumpDisturbance,
                UnsafeGrowth,
                FallThreshold,
                FallDuration,
                MaximumTilt,
                MaximumCameraRoll,
                FallVisualDuration
            };
            foreach (var value in values)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                {
                    reason = $"Ladder tuning '{name}' contains a negative or non-finite value.";
                    return false;
                }
            }

            if (RungMoveDuration <= 0f || MaxLean <= 0f || LeanSpeed <= 0f || LeanReturnSpeed <= 0f || FallThreshold <= 0f ||
                FallDuration <= 0f || FallVisualDuration <= 0f || WarningLean >= UnsafeLean || UnsafeLean > MaxLean ||
                MaximumTilt > 10f || MaximumCameraRoll > 2f)
            {
                reason = $"Ladder tuning '{name}' requires positive durations/speeds, Warning Lean < Unsafe Lean <= Max Lean, and bounded tilt/roll.";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
