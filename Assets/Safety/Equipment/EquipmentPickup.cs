using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class EquipmentPickup : MonoBehaviour, IInteractable
    {
        public string EquipmentId;
        [SerializeField]
        private string displayName;
        [SerializeField]
        private Renderer[] visuals;
        [SerializeField]
        private Collider[] pickupColliders;
        private InventoryManager inventory;
        private bool subscribed;
        public string Prompt
        {
            get
            {
                return $"E — Pick up {displayName}";
            }
        }

        private void Awake()
        {
            if (visuals == null || visuals.Length == 0)
            {
                visuals = GetComponentsInChildren<Renderer>(true);
            }

            if (pickupColliders == null || pickupColliders.Length == 0)
            {
                pickupColliders = GetComponentsInChildren<Collider>(true);
            }
        }

        private void Start()
        {
            inventory = InventoryManager.Instance;
            if (inventory == null)
            {
                Debug.LogError("EquipmentPickup needs InventoryManager.", this);
                enabled = false;
                return;
            }

            Subscribe();
        }

        private void Subscribe()
        {
            if (inventory == null || subscribed)
            {
                return;
            }

            inventory.EquipmentChanged += RefreshPresentation;
            subscribed = true;
            RefreshPresentation();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            if (subscribed && inventory != null)
            {
                inventory.EquipmentChanged -= RefreshPresentation;
            }

            subscribed = false;
        }

        private void RefreshPresentation()
        {
            var visible = !inventory.HasItem(EquipmentId);
            foreach (var visual in visuals)
            {
                if (visual != null && visual.transform.IsChildOf(transform))
                {
                    visual.enabled = visible;
                }
            }

            foreach (var collider in pickupColliders)
            {
                if (collider != null && collider.transform.IsChildOf(transform))
                {
                    collider.enabled = visible;
                }
            }
        }

        public bool CanInteract(InteractionContext context, out string reason)
        {
            if (context == null || !context.Session.CanInteract || !context.Inventory.IsReady)
            {
                reason = "Pickup is unavailable now.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(EquipmentId) || context.Inventory.Catalog.FindItem(EquipmentId) == null)
            {
                reason = "Configure a catalog item ID for this pickup.";
                return false;
            }

            if (context.Inventory.HasItem(EquipmentId))
            {
                reason = "This item is already acquired.";
                return false;
            }

            reason = null;
            return true;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context, out var reason))
            {
                context?.Inventory.ShowMessage(reason, true);
                return;
            }

            if (!context.Inventory.TryAcquire(EquipmentId, out reason))
            {
                context.Inventory.ShowMessage(reason, true);
            }
        }
    }
}
