using System;

namespace SafetySimulation
{
    /// <summary>Only uninterrupted validity on the same target contributes elapsed seconds.</summary>
    public sealed class DwellTimer
    {
        private string target;
        public float Elapsed
        {
            get;
            private set;
        }

        public bool Tick(string targetId, bool valid, float deltaTime, float duration)
        {
            if (!valid || string.IsNullOrEmpty(targetId))
            {
                Reset();
                return false;
            }

            if (!string.Equals(target, targetId, StringComparison.Ordinal))
            {
                Reset();
                target = targetId;
            }

            Elapsed += Math.Max(0f, deltaTime);
            return Elapsed >= Math.Max(0f, duration);
        }

        public void Reset()
        {
            target = null;
            Elapsed = 0f;
        }
    }
}
