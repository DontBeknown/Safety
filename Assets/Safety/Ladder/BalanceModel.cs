using System;

namespace SafetySimulation
{
    /// <summary>Time-based balance rules with exact integration at lean/fall threshold crossings.</summary>
    public sealed class BalanceModel
    {
        private readonly float maxLean;
        private readonly float warningLean;
        private readonly float unsafeLean;
        private readonly float leanSpeed;
        private readonly float returnSpeed;
        private readonly float decay;
        private readonly float growth;
        private readonly float fallThreshold;
        private readonly float fallDuration;
        private double instability;
        private double aboveThresholdTime;
        public float Lean
        {
            get;
            private set;
        }

        public float Instability
        {
            get
            {
                return (float)instability;
            }
        }

        public bool Fallen
        {
            get;
            private set;
        }

        public bool IsWarning
        {
            get
            {
                return Math.Abs(Lean) >= warningLean;
            }
        }

        public BalanceModel(LadderTuning tuning)
        {
            if (tuning == null)
            {
                throw new ArgumentNullException(nameof(tuning));
            }

            if (!tuning.TryValidate(out var reason))
            {
                throw new ArgumentException(reason, nameof(tuning));
            }

            maxLean = tuning.MaxLean;
            warningLean = tuning.WarningLean;
            unsafeLean = tuning.UnsafeLean;
            leanSpeed = tuning.LeanSpeed;
            returnSpeed = tuning.LeanReturnSpeed;
            decay = tuning.InstabilityDecay;
            growth = tuning.UnsafeGrowth;
            fallThreshold = tuning.FallThreshold;
            fallDuration = tuning.FallDuration;
        }

        public void Disturb(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (!Fallen)
            {
                instability += amount;
            }
        }

        public void Tick(float lean, bool unsafePosition, float deltaTime, bool returnToCenter = true)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f || float.IsNaN(lean) ||
                float.IsInfinity(lean))
            {
                throw new ArgumentOutOfRangeException();
            }

            if (Fallen || deltaTime == 0f)
            {
                return;
            }

            lean = Math.Max(-1f, Math.Min(1f, lean));
            var start = (double)Lean;
            var velocity = lean == 0f ? (returnToCenter ? -Math.Sign(start) * returnSpeed : 0d) : lean * leanSpeed;
            var destination = lean == 0f ? (returnToCenter ? 0d : start) : Math.Sign(lean) * maxLean;
            var ramp = velocity == 0d ? 0d : Math.Min(deltaTime, Math.Abs((destination - start) / velocity));
            var end = start + velocity * ramp;
            // Split at both lean boundaries, then at the point the lean reaches its clamp/center.
            var first = ramp;
            var second = ramp;
            if (velocity != 0d)
            {
                var negativeCrossing = (-unsafeLean - start) / velocity;
                var positiveCrossing = (unsafeLean - start) / velocity;
                if (negativeCrossing > 0d && negativeCrossing < ramp)
                {
                    first = negativeCrossing;
                }

                if (positiveCrossing > 0d && positiveCrossing < ramp)
                {
                    second = Math.Max(first == ramp ? 0d : first, positiveCrossing);
                    first = Math.Min(first, positiveCrossing);
                }

                if (second < first)
                {
                    var swap = first;
                    first = second;
                    second = swap;
                }
            }

            IntegrateLeanSegment(start, velocity, 0d, first, unsafePosition);
            if (second > first && second < ramp)
            {
                IntegrateLeanSegment(start, velocity, first, second, unsafePosition);
            }

            var last = second < ramp ? second : first;
            if (ramp > last)
            {
                IntegrateLeanSegment(start, velocity, last, ramp, unsafePosition);
            }

            if (deltaTime > ramp)
            {
                IntegrateInstability(deltaTime - ramp, unsafePosition || Math.Abs(end) >= unsafeLean);
            }

            Lean = (float)Math.Max(-maxLean, Math.Min(maxLean, end));
        }

        private void IntegrateLeanSegment(double start, double velocity, double from, double to, bool unsafePosition)
        {
            if (to <= from || Fallen)
            {
                return;
            }

            var midpoint = start + velocity * (from + to) * 0.5d;
            IntegrateInstability(to - from, unsafePosition || Math.Abs(midpoint) >= unsafeLean);
        }

        private void IntegrateInstability(double duration, bool unsafePosition)
        {
            if (Fallen || duration <= 0d)
            {
                return;
            }

            var rate = unsafePosition ? growth : -decay;
            var before = instability;
            var after = Math.Max(0d, before + rate * duration);
            if (before >= fallThreshold)
            {
                var above = rate < 0d ? Math.Min(duration, Math.Max(0d, (before - fallThreshold) / -rate)) : duration;
                aboveThresholdTime += above;
                if (aboveThresholdTime + 1e-9d >= fallDuration)
                {
                    Fallen = true;
                }

                if (after < fallThreshold && !Fallen)
                {
                    aboveThresholdTime = 0d;
                }
            }
            else
            {
                aboveThresholdTime = 0d;
                if (rate > 0d && after >= fallThreshold)
                {
                    aboveThresholdTime = duration - (fallThreshold - before) / rate;
                    if (aboveThresholdTime + 1e-9d >= fallDuration)
                    {
                        Fallen = true;
                    }
                }
            }

            instability = after;
        }

        public void Reset()
        {
            Lean = 0f;
            instability = 0d;
            aboveThresholdTime = 0d;
            Fallen = false;
        }
    }
}
