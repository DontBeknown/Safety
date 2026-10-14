using UnityEngine;

namespace SafetySimulation
{
    public abstract class ToolBase : EquippableItem
    {
        public string ToolId
        {
            get
            {
                return ItemId;
            }
        }

        public virtual float UseProgress
        {
            get
            {
                return 0f;
            }
        }

        public virtual string UseProgressLabel
        {
            get
            {
                return string.Empty;
            }
        }

        [Tooltip("Local extension direction; extension translates the whole held tool without target snapping.")]
        [SerializeField]
        private Vector3 extensionDirection = Vector3.forward;
        [SerializeField, Min(0f)]
        private float maximumExtension = 0.45f;
        [SerializeField, Min(0.01f)]
        private float extensionSpeed = 0.5f;
        [SerializeField, Min(0.001f)]
        private float obstructionRadius = 0.01f;
        [SerializeField, Tooltip("Layers of solid colliders that stop extension. Trigger colliders are ignored.")]
        private LayerMask obstructionMask = ~0;
        protected ToolUseContext Context
        {
            get;
            private set;
        }

        private Vector3 restingPosition;
        private float extension;
        private bool extendRequested;
        private Collider[] extensionColliders;
        protected float CurrentExtension => extension;
        protected float MaximumExtension => maximumExtension;
        protected bool ExtensionRequested => extendRequested;
        protected Vector3 RestingWorldPosition => transform.parent != null
            ? transform.parent.TransformPoint(restingPosition) : restingPosition;
        protected virtual Vector3 LocalExtensionDirection => extensionDirection.normalized;
        protected virtual float DesiredExtension => extendRequested ? maximumExtension : 0f;

        // Called after the camera has finished updating, before extension and use checks.
        internal void TickAim(float deltaTime)
        {
            if (Context != null)
            {
                UpdateAim(deltaTime);
                ApplyExtensionPose();
            }
        }

        protected virtual void UpdateAim(float deltaTime) { }
        protected virtual void ResetAim() { }

        private void ApplyExtensionPose()
        {
            transform.localPosition = restingPosition + LocalExtensionDirection * extension;
        }
        // Geometry may stop the probe before its travel limit; physical extension plus contact is sufficient.
        public bool IsExtended
        {
            get
            {
                return extendRequested && extension > 0.001f;
            }
        }

        protected void InitializePose(ToolUseContext context)
        {
            Context = context;
            restingPosition = transform.localPosition;
            extension = 0f;
            extendRequested = false;
            extensionColliders = GetComponentsInChildren<Collider>(true);
        }

        protected virtual Transform ExtensionProbe
        {
            get
            {
                return transform;
            }
        }

        protected virtual Collider ExtensionContactCollider
        {
            get
            {
                return null;
            }
        }

        internal void TickExtension(float deltaTime)
        {
            if (Context == null)
            {
                return;
            }

            var next = Mathf.MoveTowards(extension, Mathf.Clamp(DesiredExtension, 0f, maximumExtension), extensionSpeed * deltaTime);
            if (next > extension)
            {
                // The held tool moves by Transform. Update physics before querying this frame's geometry.
                Physics.SyncTransforms();
                // Sphere casts omit colliders already overlapping their starting sphere. Refuse
                // outward movement from penetration, while still permitting retraction/recovery.
                foreach (var collider in Physics.OverlapSphere(ExtensionProbe.position, obstructionRadius, obstructionMask,
                    QueryTriggerInteraction.Ignore))
                {
                    if (!ContactRules.IsOwnedCollider(collider, Context.Player, this))
                    {
                        next = extension;
                        break;
                    }
                }

                var direction = transform.parent != null ? transform.parent.TransformVector(LocalExtensionDirection) : LocalExtensionDirection;
                if (direction.sqrMagnitude < 0.000001f)
                {
                    return;
                }

                var distance = (next - extension) * direction.magnitude;
                if (distance > 0f)
                {
                    foreach (var hit in Physics.SphereCastAll(ExtensionProbe.position, obstructionRadius,
                        direction.normalized, distance, obstructionMask, QueryTriggerInteraction.Ignore))
                    {
                        if (!ContactRules.IsOwnedCollider(hit.collider, Context.Player, this))
                        {
                            next = Mathf.Min(next, extension + Mathf.Max(0f, hit.distance - 0.001f) / direction.magnitude);
                        }
                    }

                    // Sweep the physical tool as well as its probe; a tip-only sphere misses body collisions.
                    foreach (var collider in extensionColliders)
                    {
                        if (collider != null && !collider.isTrigger && collider != ExtensionContactCollider)
                        {
                            next = LimitExtension(collider, direction, next);
                        }
                    }

                    if (ExtensionContactCollider != null)
                    {
                        next = LimitExtension(ExtensionContactCollider, direction, next);
                    }
                }
            }

            extension = next;
            ApplyExtensionPose();
        }

        private float LimitExtension(Collider probe, Vector3 direction, float next)
        {
            if (!probe.enabled || !probe.gameObject.activeInHierarchy || next <= extension)
            {
                return next;
            }

            var bounds = probe.bounds;
            if (bounds.extents.sqrMagnitude < 0.00000001f)
            {
                return next;
            }

            // Bounds conservatively cover rotated/irregular colliders and keep the complete tool out of walls.
            foreach (var obstacle in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, obstructionMask,
                QueryTriggerInteraction.Ignore))
            {
                if (ContactRules.IsOwnedCollider(obstacle, Context.Player, this))
                {
                    continue;
                }

                if (Physics.ComputePenetration(probe, probe.transform.position, probe.transform.rotation, obstacle,
                    obstacle.transform.position, obstacle.transform.rotation, out _, out _))
                {
                    return extension;
                }
            }

            var distance = (next - extension) * direction.magnitude;
            foreach (var hit in Physics.BoxCastAll(bounds.center, bounds.extents, direction.normalized, Quaternion.identity,
                distance, obstructionMask, QueryTriggerInteraction.Ignore))
            {
                if (ContactRules.IsOwnedCollider(hit.collider, Context.Player, this))
                {
                    continue;
                }

                next = Mathf.Min(next, extension + Mathf.Max(0f, hit.distance - 0.001f) / direction.magnitude);
            }

            return next;
        }

        public virtual void SetExtended(bool value)
        {
            extendRequested = value;
        }

        internal void ResetExtension()
        {
            extendRequested = false;
            extension = 0f;
            if (Context != null)
            {
                transform.localPosition = restingPosition;
                ResetAim();
            }
        }

        public override bool TryValidate(out string reason)
        {
            if (!Finite(extensionDirection.x) || !Finite(extensionDirection.y) || !Finite(extensionDirection.z) ||
                !Finite(maximumExtension) || !Finite(extensionSpeed) || !Finite(obstructionRadius) ||
                extensionDirection.sqrMagnitude < 0.0001f || maximumExtension < 0f || extensionSpeed <= 0f ||
                obstructionRadius <= 0f)
            {
                reason = $"Invalid extension settings on tool '{name}'.";
                return false;
            }

            reason = null;
            return true;
        }

        protected static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        protected virtual void OnDisable()
        {
            EndUse();
            ResetExtension();
            if (Context != null && Context.Player != null)
            {
                Context.Player.RequireActionInputRelease();
            }
        }

        protected virtual void OnEnable()
        {
            if (Context != null && Context.Player != null)
            {
                Context.Player.RequireActionInputRelease();
            }
        }

        protected void WarnOnce(ref string previousReason, string reason)
        {
            if (!string.IsNullOrEmpty(reason) && previousReason != reason)
            {
                Context.Hud?.SetMessage(reason, true);
                Context.Session.PlayWarning();
            }

            previousReason = reason;
        }

        public abstract override void Equip(ToolUseContext context);
        public abstract override void Unequip();
        public abstract void BeginUse();
        public abstract void TickUse(float deltaTime);
        public abstract void EndUse();
        public virtual void SecondaryAction()
        {
        }
    }
}
