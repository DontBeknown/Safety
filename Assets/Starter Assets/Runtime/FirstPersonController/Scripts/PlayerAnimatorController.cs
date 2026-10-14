using UnityEngine;
using SafetySimulation;

namespace StarterAssets
{
    /// <summary>
    /// Drives the Starter Assets Animator parameters from a character controller.
    /// Movement, jump decisions, and fall timing remain owned by the caller.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerAnimatorController : MonoBehaviour
    {
        [Tooltip("Animator to drive. If empty, searches this object and its children, including inactive objects.")]
        public Animator TargetAnimator;

        [Header("Animation Event Audio")]
        [Tooltip("For footstep and landing events, place this component on the same object as the Animator.")]
        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0f, 1f)] public float FootstepAudioVolume = 0.5f;

        private static readonly int SpeedID = Animator.StringToHash("Speed");
        private static readonly int GroundedID = Animator.StringToHash("Grounded");
        private static readonly int JumpID = Animator.StringToHash("Jump");
        private static readonly int FreeFallID = Animator.StringToHash("FreeFall");
        private static readonly int MotionSpeedID = Animator.StringToHash("MotionSpeed");

        private float _animationBlend;

        private bool CanAnimate => isActiveAndEnabled && TargetAnimator != null;

        private void Awake()
        {
            if (TargetAnimator == null)
            {
                TargetAnimator = GetComponentInChildren<Animator>(true);
            }
        }

        public void UpdateMovement(float targetSpeed, float inputMagnitude, float speedChangeRate, float deltaTime)
        {
            if (!CanAnimate) return;

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, deltaTime * speedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            TargetAnimator.SetFloat(SpeedID, _animationBlend);
            TargetAnimator.SetFloat(MotionSpeedID, inputMagnitude);
        }

        public void SetGrounded(bool grounded)
        {
            if (CanAnimate) TargetAnimator.SetBool(GroundedID, grounded);
        }

        public void SetJump(bool jumping)
        {
            if (CanAnimate) TargetAnimator.SetBool(JumpID, jumping);
        }

        public void SetFreeFall(bool falling)
        {
            if (CanAnimate) TargetAnimator.SetBool(FreeFallID, falling);
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (!CanAnimate || animationEvent.animatorClipInfo.weight <= 0.5f ||
                FootstepAudioClips == null || FootstepAudioClips.Length == 0) return;

            AudioClip clip = FootstepAudioClips[Random.Range(0, FootstepAudioClips.Length)];
            if (clip != null)
            {
                SoundManager.Instance?.PlayAtPosition(clip, TargetAnimator.transform.position, FootstepAudioVolume, true);
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (!CanAnimate || animationEvent.animatorClipInfo.weight <= 0.5f || LandingAudioClip == null) return;

            SoundManager.Instance?.PlayAtPosition(LandingAudioClip, TargetAnimator.transform.position, FootstepAudioVolume);
        }
    }
}
