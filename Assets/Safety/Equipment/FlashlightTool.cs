using UnityEngine;

namespace SafetySimulation
{
    public sealed class FlashlightTool : ToolBase
    {
        [Tooltip("Assign an existing spotlight and its forward-facing beam origin on this prefab.")]
        public Light Beam;
        public Transform BeamOrigin;
        [SerializeField, Min(0.01f)]
        private float inspectionRange = 3f;
        [SerializeField, Min(1f), Tooltip("Maximum aiming rotation in degrees per second.")]
        private float aimRotationSpeed = 360f;
        [SerializeField]
        private LayerMask visibilityMask = ~0;
        [SerializeField] private AudioClip switchSound;
        private Quaternion equipRotation;
        private Quaternion aimRotation;
        private Vector3 beamLocalForward;
        private Vector3 beamRootOffset;
        private Vector3 cameraAimPoint;
        private bool hasCameraAim;

        public bool IsOn => isActiveAndEnabled && Beam != null && Beam.isActiveAndEnabled;

        protected override void UpdateAim(float deltaTime)
        {
            transform.rotation = aimRotation;
            hasCameraAim = false;
            var camera = Context.ViewCamera;
            if (!Context.CanUse || camera == null || !camera.isActiveAndEnabled)
            {
                return;
            }

            var centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var ray = new Ray(camera.transform.position, centerRay.direction);
            var distance = Mathf.Max(inspectionRange, Beam.range);
            cameraAimPoint = ray.GetPoint(distance);
            Physics.SyncTransforms();
            // Aim at the visible surface, independently of the inspection visibility mask.
            foreach (var hit in Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide))
            {
                if (ContactRules.IsOwnedCollider(hit.collider, Context.Player, this))
                {
                    continue;
                }

                var point = hit.collider.GetComponentInParent<InspectionPoint>();
                if (hit.collider.isTrigger && (point == null || !point.isActiveAndEnabled))
                {
                    continue;
                }

                if (hit.distance < distance)
                {
                    distance = hit.distance;
                    cameraAimPoint = hit.point;
                }
            }

            hasCameraAim = true;
        }

        private void AlignBeamToCameraAim(float deltaTime)
        {
            if (!hasCameraAim)
            {
                return;
            }

            var offset = cameraAimPoint - BeamOrigin.position;
            if (offset.sqrMagnitude < 0.000001f)
            {
                return;
            }

            var baseline = transform.parent != null ? transform.parent.rotation * equipRotation : equipRotation;
            var desiredRotation = baseline;
            // Rotation also moves an offset beam origin; converge on the camera's target from the spotlight itself.
            for (var i = 0; i < 3; i++)
            {
                var origin = transform.position + desiredRotation * Vector3.Scale(beamRootOffset, transform.lossyScale);
                offset = cameraAimPoint - origin;
                if (offset.sqrMagnitude < 0.000001f) break;
                desiredRotation = Quaternion.FromToRotation(baseline * beamLocalForward, offset.normalized) * baseline;
            }
            aimRotation = Quaternion.RotateTowards(aimRotation, desiredRotation,
                aimRotationSpeed * Mathf.Max(0f, deltaTime));
            transform.rotation = aimRotation;
        }

        protected override void ResetAim()
        {
            hasCameraAim = false;
            transform.localRotation = equipRotation;
            aimRotation = transform.rotation;
        }
        public override bool TryValidate(out string reason)
        {
            if (!base.TryValidate(out reason))
            {
                return false;
            }

            if (Beam == null || BeamOrigin == null || !Beam.transform.IsChildOf(transform) ||
                !BeamOrigin.IsChildOf(transform) || Beam.type != LightType.Spot || Beam.transform != BeamOrigin ||
                !Finite(inspectionRange) || inspectionRange <= 0f || Beam.range <= 0f || Beam.spotAngle <= 0f)
            {
                reason = $"Flashlight '{name}' needs a spotlight on its prefab-local Beam Origin and a positive range.";
                return false;
            }

            reason = null;
            return true;
        }

        public override void Equip(ToolUseContext context)
        {
            InitializePose(context);
            equipRotation = transform.localRotation;
            aimRotation = transform.rotation;
            beamLocalForward = Quaternion.Inverse(transform.rotation) * BeamOrigin.forward;
            beamRootOffset = transform.InverseTransformPoint(BeamOrigin.position);
            hasCameraAim = false;
            EndUse();
        }

        public override void Unequip()
        {
            EndUse();
        }

        public override void BeginUse()
        {
            if (Context != null && Context.CanUse)
            {
                gameObject.SetActive(true);
                Beam.enabled = true;
            }
        }

        public override void EndUse()
        {
            if (Beam != null)
            {
                Beam.enabled = false;
            }

            ClearIllumination();
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public override void SecondaryAction()
        {
            if (IsOn)
            {
                PlaySound(switchSound);
                EndUse();
            }
            else
            {
                BeginUse();
                if (IsOn) PlaySound(switchSound);
            }
        }

        public override void TickUse(float deltaTime)
        {
            // Inventory updates this independent flashlight before the meter checks illumination.
            AlignBeamToCameraAim(deltaTime);
            if (!Context.CanUse || !IsOn)
            {
                EndUse();
                return;
            }
            if (!hasCameraAim)
            {
                ClearIllumination();
                return;
            }

            Physics.SyncTransforms();
            foreach (var point in Context.InspectionPoints)
            {
                if (point == null) continue;
                var illuminated = point.isActiveAndEnabled && point.ContactAnchor != null &&
                    ContactRules.InBeam(BeamOrigin, point, Mathf.Min(inspectionRange, Beam.range), Beam.spotAngle) &&
                    ContactRules.HasLineOfSight(BeamOrigin.position, point, Context, visibilityMask);
                point.SetIlluminated(illuminated);
            }
        }

        private void ClearIllumination()
        {
            if (Context == null) return;
            foreach (var point in Context.InspectionPoints)
                if (point != null) point.SetIlluminated(false);
        }

        private void OnDisable()
        {
            if (Beam != null) Beam.enabled = false;
            ClearIllumination();
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void OnDestroy() => ClearIllumination();

        private void PlaySound(AudioClip clip)
        {
            SoundManager.Instance?.Play(clip);
        }
    }
}
