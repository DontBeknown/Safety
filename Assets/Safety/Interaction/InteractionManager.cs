using StarterAssets;
using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class InteractionManager : MonoBehaviour
    {
        [SerializeField, Min(0.1f)]
        private float range = 2.5f;
        [SerializeField]
        private LayerMask interactionMask = ~0;
        [SerializeField]
        private FirstPersonController player;
        [SerializeField]
        private Camera playerCamera;
        [SerializeField]
        private SafetyHud hud;
        private InteractionContext context;
        private IInteractable focused;
        public static InteractionManager Instance
        {
            get;
            private set;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public IInteractable Focused
        {
            get
            {
                return focused;
            }
        }

        private void Start()
        {
            if (player == null || playerCamera == null || SessionManager.Instance == null ||
                InventoryManager.Instance == null)
            {
                Debug.LogError("InteractionManager needs player, camera, session and inventory.", this);
                enabled = false;
                return;
            }

            context = new InteractionContext(player, playerCamera, SessionManager.Instance, InventoryManager.Instance);
        }

        private void Update()
        {
            RefreshFocus();
        }

        private void RefreshFocus()
        {
            focused = null;
            if (context == null || !context.Session.CanInteract || player.ActiveLadder != null)
            {
                hud?.SetPrompt(string.Empty);
                return;
            }

            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            foreach (var collider in Physics.OverlapSphere(ray.origin, 0.001f, interactionMask,
                QueryTriggerInteraction.Ignore))
            {
                if (!ContactRules.IsOwnedCollider(collider, player, context.Inventory.EquippedTool))
                {
                    hud?.SetPrompt(string.Empty);
                    return;
                }
            }

            var hits = Physics.RaycastAll(ray, range, interactionMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
            foreach (var hit in hits)
            {
                if (ContactRules.IsOwnedCollider(hit.collider, player, context.Inventory.EquippedTool))
                {
                    continue;
                }

                foreach (var component in hit.collider.GetComponentsInParent<MonoBehaviour>())
                {
                    if (component is IInteractable interactable && component.isActiveAndEnabled)
                    {
                        focused = interactable;
                        break;
                    }
                }

                // The nearest world surface blocks interaction with objects behind it.
                break;
            }

            var prompt = string.Empty;
            if (focused != null)
            {
                prompt = focused.CanInteract(context, out var reason) ? focused.Prompt : reason;
            }

            hud?.SetPrompt(prompt);
        }

        public bool TryInteract()
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            RefreshFocus();
            if (focused == null)
            {
                return false;
            }

            if (!focused.CanInteract(context, out var reason))
            {
                hud?.SetMessage(reason, true);
                context.Session.PlayWarning();
                return false;
            }

            focused.Interact(context);
            RefreshFocus();
            return true;
        }

        private void OnDisable()
        {
            focused = null;
            if (Instance == this)
            {
                hud?.SetPrompt(string.Empty);
            }
        }
    }
}
