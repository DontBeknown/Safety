using System.Globalization;
using TMPro;
using UnityEngine;

namespace SafetySimulation
{
    public sealed class CurrentMeterTool : ToolBase
    {
        [Tooltip("Prefab-local anchor at the visible probe tip. Camera aim rotates the probe's authored +Y axis.")]
        public Transform Tip;
        [Tooltip("Optional probe-tip Sphere, Capsule or Box Collider. If empty, a small trigger sphere is created at Tip when equipped.")]
        public Collider TipCollider;
        [Tooltip("Assign the runtime TMP display in this prefab and disable the model's static DisplayText manually.")]
        public TMP_Text Display;
        [SerializeField, Tooltip("World layers that block the meter ray. Configured inspection-point collider layers are always included.")]
        private LayerMask visibilityMask = ~0;
        [SerializeField, Min(1f), Tooltip("Maximum aiming rotation in degrees per second while retracted.")]
        private float aimRotationSpeed = 360f;
        [SerializeField] private AudioClip measurementSound;
        [SerializeField, Tooltip("Log measurement checks once per second to explain why the meter cannot measure.")]
        private bool debugChecks = false;
        private float nextDebugLogTime;
        private readonly DwellTimer dwell = new DwellTimer();
        private bool held;
        private bool measuredThisHold;
        private Quaternion equipRotation;
        private Quaternion aimRotation;
        private Vector3 aimDirection;
        private Vector3 tipRootOffset;
        private float desiredTravel;
        private bool aimReachable;
        private InspectionPoint aimedPoint;
        private Collider aimedCollider;
        private Vector3 aimHitPoint;
        private int inspectionLayers;
        private string aimFailure;
        private string aimWarning;
        private string illuminationWarning;

        protected override Vector3 LocalExtensionDirection => transform.parent != null
            ? transform.parent.InverseTransformVector(aimDirection) : aimDirection;
        protected override float DesiredExtension => ExtensionRequested && aimReachable ? desiredTravel : 0f;

        protected override void ResetAim()
        {
            transform.localRotation = equipRotation;
            aimRotation = transform.rotation;
            aimDirection = transform.up;
            desiredTravel = 0f;
            aimReachable = false;
            aimedPoint = null;
            aimedCollider = null;
        }

        protected override void UpdateAim(float deltaTime)
        {
            // Keep an existing extension on its original world axis when the mount turns.
            // Retract along that axis before rotating, so the probe cannot sweep through walls.
            transform.rotation = aimRotation;
            aimReachable = false;
            aimedPoint = null;
            aimedCollider = null;
            desiredTravel = 0f;
            aimFailure = "Aim at a reachable inspection point.";
            var camera = Context.ViewCamera;
            if (!Context.CanUse || camera == null || !camera.isActiveAndEnabled)
            {
                aimFailure = "The meter's player camera or session is unavailable.";
                dwell.Reset();
                return;
            }

            Physics.SyncTransforms();
            var centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // Start at the camera, so foreground geometry before the near plane also blocks aim.
            var ray = new Ray(camera.transform.position, centerRay.direction);
            var aimMask = visibilityMask.value | inspectionLayers;
            foreach (var collider in Physics.OverlapSphere(ray.origin, 0.001f, aimMask, QueryTriggerInteraction.Ignore))
            {
                if (!ContactRules.IsOwnedCollider(collider, Context.Player, this))
                {
                    aimFailure = $"The camera is inside '{collider.name}'; move clear of it.";
                    dwell.Reset();
                    return;
                }
            }

            RaycastHit nearest = default;
            var nearestDistance = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(ray, camera.farClipPlane, aimMask, QueryTriggerInteraction.Collide))
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

                if (hit.distance < nearestDistance)
                {
                    nearest = hit;
                    nearestDistance = hit.distance;
                }
            }

            if (float.IsPositiveInfinity(nearestDistance))
            {
                dwell.Reset();
                return;
            }

            // Aim from the resting root; the dedicated tip lies on the authored +Y axis.
            // Calculate reach from the tip AFTER this candidate rotation, never the extended pose.
            var toHit = nearest.point - RestingWorldPosition;
            if (toHit.sqrMagnitude < 0.000001f)
            {
                dwell.Reset();
                return;
            }

            var baseline = transform.parent != null ? transform.parent.rotation * equipRotation : equipRotation;
            var desiredRotation = Quaternion.FromToRotation(baseline * Vector3.up, toHit.normalized) * baseline;
            var localRotation = transform.parent != null
                ? Quaternion.Inverse(transform.parent.rotation) * desiredRotation : desiredRotation;
            var tipOffset = localRotation * Vector3.Scale(tipRootOffset, transform.localScale);
            var restingTip = RestingWorldPosition + (transform.parent != null
                ? transform.parent.TransformVector(tipOffset) : tipOffset);
            var offset = nearest.point - restingTip;
            var direction = desiredRotation * Vector3.up;
            var travel = Vector3.Dot(offset, direction);
            if (CurrentExtension > 0.0001f && Quaternion.Angle(aimRotation, desiredRotation) > 0.05f)
            {
                aimFailure = "Hold your aim steady while the probe retracts and realigns.";
                dwell.Reset();
                return;
            }

            if (CurrentExtension <= 0.0001f)
            {
                aimRotation = Quaternion.RotateTowards(aimRotation, desiredRotation,
                    aimRotationSpeed * Mathf.Max(0f, deltaTime));
                aimDirection = (aimRotation * Vector3.up).normalized;
                transform.rotation = aimRotation;
                if (Quaternion.Angle(aimRotation, desiredRotation) > 0.05f)
                {
                    aimFailure = "Hold your aim steady while the probe realigns.";
                    dwell.Reset();
                    return;
                }
            }

            if (offset.magnitude > MaximumExtension + 0.0001f || travel < -0.001f ||
                (offset - direction * travel).sqrMagnitude > 0.000001f)
            {
                aimFailure = $"The aimed surface is out of reach ({offset.magnitude:0.00} m; maximum {MaximumExtension:0.00} m). Move closer.";
                dwell.Reset();
                return;
            }

            var selected = nearest.collider.GetComponentInParent<InspectionPoint>();
            // Only scene-configured inspection points can produce a reading.
            foreach (var point in Context.InspectionPoints)
            {
                if (point != null && point == selected && point.isActiveAndEnabled)
                {
                    aimedPoint = point;
                    break;
                }
            }

            if (aimedPoint == null || aimedPoint.ContactAnchor == null)
            {
                aimFailure = $"Aim at an inspection point; '{nearest.collider.name}' is in front of the meter.";
                dwell.Reset();
                return;
            }

            desiredTravel = Mathf.Max(0f, travel);
            aimReachable = true;
            aimHitPoint = nearest.point;
            aimedCollider = nearest.collider;
            aimFailure = null;
        }
        public override float UseProgress
        {
            get
            {
                return Mathf.Clamp01(dwell.Elapsed);
            }
        }

        public override string UseProgressLabel
        {
            get
            {
                return "Measuring";
            }
        }

        protected override Transform ExtensionProbe
        {
            get
            {
                return Tip != null ? Tip : transform;
            }
        }

        protected override Collider ExtensionContactCollider
        {
            get
            {
                return TipCollider;
            }
        }

        public override bool TryValidate(out string reason)
        {
            if (!base.TryValidate(out reason))
            {
                return false;
            }

            if (Tip == null || Display == null || !Tip.IsChildOf(transform) || !Display.transform.IsChildOf(transform))
            {
                reason = $"Meter '{name}' needs prefab-local Tip and TMP Display references.";
                return false;
            }

            if (TipCollider != null && (!TipCollider.transform.IsChildOf(transform) || !TipCollider.enabled ||
                !IsPrefabHierarchyActive(TipCollider.transform) || !(TipCollider is SphereCollider ||
                TipCollider is CapsuleCollider || TipCollider is BoxCollider)))
            {
                reason = $"Meter '{name}' needs an enabled prefab-local Sphere, Capsule or Box Tip Collider.";
                return false;
            }

            reason = null;
            return true;
        }

        private bool IsPrefabHierarchyActive(Transform child)
        {
            // Prefab assets have no active scene hierarchy. Validate only authored nodes,
            // including the tool root, without depending on an instance's scene ancestors.
            for (var node = child; node != null; node = node.parent)
            {
                if (!node.gameObject.activeSelf)
                {
                    return false;
                }

                if (node == transform)
                {
                    return true;
                }
            }

            return false;
        }

        public override void Equip(ToolUseContext context)
        {
            InitializePose(context);
            equipRotation = transform.localRotation;
            aimRotation = transform.rotation;
            aimDirection = transform.up;
            tipRootOffset = transform.InverseTransformPoint(Tip.position);
            inspectionLayers = 0;
            foreach (var point in context.InspectionPoints)
            {
                if (point == null)
                {
                    continue;
                }

                foreach (var collider in point.GetComponentsInChildren<Collider>(true))
                {
                    inspectionLayers |= 1 << collider.gameObject.layer;
                }
            }
            if (TipCollider == null)
            {
                var contact = new GameObject("MeterTipContact");
                contact.layer = Tip.gameObject.layer;
                contact.transform.SetParent(Tip, false);
                var sphere = contact.AddComponent<SphereCollider>();
                sphere.radius = 0.015f;
                sphere.isTrigger = true;
                TipCollider = sphere;
            }

            EndUse();
            Display.text = "— A";
        }

        public override void Unequip()
        {
            EndUse();
        }

        public override void BeginUse()
        {
            if (measuredThisHold)
            {
                return;
            }

            held = Context.CanMeasure;
            dwell.Reset();
            if (Context.CanUse && !Context.Inventory.AllRequiredPpe)
            {
                Context.Hud?.SetMessage("Equip all required PPE before measuring.", true);
                Context.Session.PlayWarning();
            }
        }

        public override void EndUse()
        {
            held = false;
            measuredThisHold = false;
            dwell.Reset();
            aimWarning = null;
            illuminationWarning = null;
        }

        public override void TickUse(float deltaTime)
        {
            if (measuredThisHold)
            {
                return;
            }

            if (ExtensionRequested && !aimReachable)
            {
                WarnOnce(ref aimWarning, aimFailure);
            }
            else
            {
                aimWarning = null;
            }

            var logChecks = debugChecks && Time.unscaledTime >= nextDebugLogTime;
            if (logChecks)
            {
                nextDebugLogTime = Time.unscaledTime + 1f;
            }

            if (!held || !ExtensionRequested || !aimReachable || !Context.CanMeasure)
            {
                illuminationWarning = null;
                if (logChecks)
                {
                    Debug.Log($"Current meter: cannot measure. Use held={held}, RMB={ExtensionRequested}, extended={IsExtended}, aim reachable={aimReachable}, reason={aimFailure}, " +
                        $"context allows measurement={Context.CanMeasure}, all required PPE={Context.Inventory.AllRequiredPpe}. " +
                        "Equip all required PPE and hold LMB and RMB.", this);
                }

                dwell.Reset();
                return;
            }

            InspectionPoint target = null;
            // A camera-selected point must also be reached by the physical tip.
            foreach (var point in Context.InspectionPoints)
            {
                if (point != aimedPoint)
                {
                    continue;
                }

                if (point == null)
                {
                    if (logChecks)
                    {
                        Debug.Log("Current meter: null entry in Scene Inspection Points.", this);
                    }

                    continue;
                }

                if (!point.isActiveAndEnabled || point.ContactAnchor == null)
                {
                    if (logChecks)
                    {
                        Debug.Log($"Current meter -> '{point.Id}': skipped. " +
                            $"Active={point.isActiveAndEnabled}, contact anchor assigned={point.ContactAnchor != null}.", point);
                    }

                    continue;
                }

                if (!point.IsIlluminated)
                {
                    WarnOnce(ref illuminationWarning,
                        "You need to shine the flashlight on the inspection point while measuring.");
                    if (logChecks)
                    {
                        Debug.Log($"Current meter -> '{point.Id}': keep the flashlight shining on this point while measuring.", point);
                    }

                    continue;
                }

                illuminationWarning = null;
                Physics.SyncTransforms();
                var reachedHit = TipCollider != null &&
                    (TipCollider.ClosestPoint(aimHitPoint) - aimHitPoint).sqrMagnitude <= 0.000004f;
                // Solid sweeps leave a 1 mm stand-off. A <=2 mm surface gap also counts
                // as physical contact, including a trigger coincident with the solid surface.
                var touchingTarget = aimedCollider != null && aimedCollider.enabled &&
                    aimedCollider.gameObject.activeInHierarchy &&
                    aimedCollider.GetComponentInParent<InspectionPoint>() == point &&
                    (aimedCollider.ClosestPoint(aimHitPoint) - aimHitPoint).sqrMagnitude <= 0.000004f;
                var inContact = reachedHit && (ContactRules.InContact(TipCollider, point) || touchingTarget);
                if (debugChecks && Tip != null)
                {
                    Debug.DrawLine(Tip.position, point.ContactAnchor.position, inContact ? Color.green : Color.red, 0f,
                        false);
                }

                if (!inContact)
                {
                    if (logChecks)
                    {
                        Debug.Log($"Current meter -> '{point.Id}': no collider contact. " +
                            $"Tip Collider '{TipCollider?.name}' must overlap an enabled collider on this InspectionPoint or a child object.", point);
                    }

                    continue;
                }

                // Trace the complete physical reach from the held mount, rather than only the tiny tip-to-target gap.
                if (!ContactRules.HasLineOfSight(Context.ToolMount.position, point, aimHitPoint, Context, visibilityMask.value | inspectionLayers,
                    out var blocker))
                {
                    if (logChecks)
                    {
                        Debug.Log($"Current meter -> '{point.Id}': visibility blocked by " +
                            $"'{blocker?.name}' (layer {blocker?.gameObject.layer}).",
                            blocker != null ? (Object)blocker : this);
                    }

                    continue;
                }

                // Scene order gives a stable target when multiple point colliders overlap.
                if (target == null)
                {
                    target = point;
                }
            }

            if (target == null)
            {
                if (logChecks)
                {
                    Debug.Log("Current meter: no eligible point in collider contact with a clear path. Check Tip Collider and the InspectionPoint colliders.", this);
                }

                dwell.Reset();
                return;
            }
            if (!dwell.Tick(target.Id, true, deltaTime, 1f))
            {
                if (logChecks)
                {
                    Debug.Log($"Current meter -> '{target.Id}': checks passed; maintain contact and both buttons for 1 second.", target);
                }

                return;
            }

            // One reading per hold, regardless of whether it earns achievement credit.
            // EndUse clears this latch when Use is released or the tool is cancelled.
            measuredThisHold = true;
            Display.text = target.CurrentReading.ToString("0.000", CultureInfo.InvariantCulture) + " A";
            Context.Hud?.SetReading(target.Id, target.CurrentReading);
            SoundManager.Instance?.Play(measurementSound);

            var credited = Context.Achievements.Report(AchievementType.Measure, target.Id, Context.AttemptId);
            if (debugChecks)
            {
                Debug.Log($"Current meter -> '{target.Id}': measured {Display.text}; achievement credited={credited}.", this);
            }

            dwell.Reset();
        }
    }
}
