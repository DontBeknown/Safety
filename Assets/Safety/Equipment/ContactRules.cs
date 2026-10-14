using UnityEngine;

namespace SafetySimulation
{
    public static class ContactRules
    {
        public static bool IsOwnedCollider(Collider collider, StarterAssets.FirstPersonController player, ToolBase tool)
        {
            if (collider == null)
            {
                return false;
            }

            var t = collider.transform;
            // The attached ladder's coarse interaction volume encloses the player.
            // Ignore that root volume, while keeping child rail geometry as obstacles.
            var attachedLadderVolume = player != null && player.ActiveLadder != null &&
                player.ActiveLadder.IsAttached && collider.GetComponent<LadderComponent>() == player.ActiveLadder;
            var inventory = InventoryManager.Instance;
            var ownedEquipment = inventory != null && inventory.IsOwnedToolCollider(collider);
            return (player != null && t.IsChildOf(player.transform)) || (tool != null && t.IsChildOf(tool.transform)) ||
                ownedEquipment || attachedLadderVolume;
        }

        public static bool HasLineOfSight(Vector3 origin, InspectionPoint target, ToolUseContext context, LayerMask mask)
        {
            return HasLineOfSight(origin, target, context, mask, out _);
        }

        public static bool HasLineOfSight(Vector3 origin, InspectionPoint target, ToolUseContext context, LayerMask mask,
            out Collider blocker)
        {
            return HasLineOfSight(origin, target, target != null && target.ContactAnchor != null
                ? target.ContactAnchor.position : origin, context, mask, out blocker);
        }

        public static bool HasLineOfSight(Vector3 origin, InspectionPoint target, Vector3 targetPosition,
            ToolUseContext context, LayerMask mask, out Collider blocker)
        {
            blocker = null;
            if (target == null || target.ContactAnchor == null)
            {
                return false;
            }

            // A ray originating inside a collider can miss that collider entirely.
            foreach (var collider in Physics.OverlapSphere(origin, 0.001f, mask, QueryTriggerInteraction.Ignore))
            {
                if (!IsOwnedCollider(collider, context.Player, context.Inventory.EquippedTool) &&
                    collider.GetComponentInParent<InspectionPoint>() != target)
                {
                    blocker = collider;
                    return false;
                }
            }

            var offset = targetPosition - origin;
            foreach (var hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, mask,
                QueryTriggerInteraction.Ignore))
            {
                if (IsOwnedCollider(hit.collider, context.Player, context.Inventory.EquippedTool))
                {
                    continue;
                }

                if (hit.collider.GetComponentInParent<InspectionPoint>() == target)
                {
                    continue;
                }

                blocker = hit.collider;
                return false;
            }

            return true;
        }

        public static bool InBeam(Transform origin, InspectionPoint point, float range, float angle)
        {
            var offset = point.ContactAnchor.position - origin.position;
            return offset.sqrMagnitude <= range * range && Vector3.Angle(origin.forward, offset) <= angle * 0.5f;
        }

        public static bool InContact(Collider tip, InspectionPoint target)
        {
            if (tip == null || !tip.enabled || !tip.gameObject.activeInHierarchy || target == null ||
                !target.isActiveAndEnabled)
            {
                return false;
            }

            // Use current transforms directly: tool extension occurs this frame, before the next physics step.
            // Primitive probe shapes support solid or trigger target colliders without requiring a Rigidbody.
            foreach (var collider in target.GetComponentsInChildren<Collider>())
            {
                if (collider == tip || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                    collider.GetComponentInParent<InspectionPoint>() != target)
                {
                    continue;
                }

                if (Physics.ComputePenetration(tip, tip.transform.position, tip.transform.rotation, collider,
                    collider.transform.position, collider.transform.rotation, out _, out _))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
