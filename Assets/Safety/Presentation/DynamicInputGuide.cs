using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

namespace SafetySimulation
{
    /// <summary>
    /// Attach to an always-active HUD object. Assign the references and row prefab in the Inspector.
    /// Put the equipment-area BoxCollider around the table and enable Is Trigger.
    /// Create the icon map through Assets > Create > Safety > Input Guide Icons.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DynamicInputGuide : MonoBehaviour
    {
        [Header("Gameplay")]
        [SerializeField]
        private FirstPersonController player;
        [SerializeField]
        private InventoryManager inventory;
        [SerializeField]
        private SessionManager session;
        [SerializeField, Tooltip("A trigger box around the table. Detection is independent of camera aim.")]
        private BoxCollider equipmentArea;

        [Header("Presentation")]
        [SerializeField]
        private InputGuideIcons icons;
        [SerializeField, Tooltip("A UI row prefab with InputGuideRow and its text/icon references assigned.")]
        private InputGuideRow rowPrefab;
        [SerializeField, Tooltip("The layout container in which row instances will be created.")]
        private Transform rowContainer;
        [SerializeField, Tooltip("Optional CanvasGroup to hide the panel background along with its rows.")]
        private CanvasGroup panelGroup;

        private readonly List<InputGuideRow> rows = new List<InputGuideRow>();
        private int lastContextKey = -1;

        private void OnEnable()
        {
            lastContextKey = -1;
        }

        private void LateUpdate()
        {
            // Managers may initialize after this HUD, so retry their singleton lookup until available.
            if (inventory == null)
            {
                inventory = InventoryManager.Instance;
            }

            if (session == null)
            {
                session = SessionManager.Instance;
            }

            var canInteract = player != null && player.isActiveAndEnabled &&
                              session != null && session.CanInteract &&
                              inventory != null && inventory.IsReady && inventory.isActiveAndEnabled;
            var ladder = canInteract ? player.ActiveLadder : null;
            // Equip/unequip text and mouse-use hints follow the meter, independently of the flashlight.
            var holdingMeter = canInteract && inventory.EquippedTool is CurrentMeterTool &&
                               inventory.EquippedTool.isActiveAndEnabled;
            var context = new InputGuideContext(
                canInteract,
                canInteract && IsInEquipmentArea(),
                canInteract && IsCollectionComplete(),
                holdingMeter,
                ladder != null,
                ladder != null && ladder.CurrentRung == 0,
                ladder != null && ladder.IsTransitioning,
                canInteract && inventory.HasFlashlight,
                canInteract && inventory.HasCurrentMeter,
                canInteract && inventory.FlashlightIsOn);

            if (context.Key == lastContextKey || rowPrefab == null || rowContainer == null)
            {
                if (rowPrefab == null || rowContainer == null)
                {
                    SetPanelVisible(false);
                }

                return;
            }

            lastContextKey = context.Key;
            var entries = InputGuideResolver.Resolve(context);
            for (var i = 0; i < entries.Length; i++)
            {
                if (i >= rows.Count)
                {
                    rows.Add(Instantiate(rowPrefab, rowContainer));
                }

                rows[i].SetEntry(entries[i], icons);
                rows[i].gameObject.SetActive(true);
            }

            for (var i = entries.Length; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }

            SetPanelVisible(entries.Length > 0);
        }

        private bool IsInEquipmentArea()
        {
            if (equipmentArea == null || !equipmentArea.enabled || !equipmentArea.gameObject.activeInHierarchy)
            {
                return false;
            }

            // Local coordinates also work for a rotated or scaled table area.
            var offset = equipmentArea.transform.InverseTransformPoint(player.transform.position) -
                         equipmentArea.center;
            var halfSize = equipmentArea.size * 0.5f;
            return Mathf.Abs(offset.x) <= halfSize.x &&
                   Mathf.Abs(offset.y) <= halfSize.y &&
                   Mathf.Abs(offset.z) <= halfSize.z;
        }

        private bool IsCollectionComplete()
        {
            var catalog = inventory.Catalog;
            if (catalog == null || catalog.Items == null || !inventory.AllRequiredPpe)
            {
                return false;
            }

            var hasHeldTools = false;
            foreach (var item in catalog.Items)
            {
                if (item == null || item.Category != EquipmentCategory.Held)
                {
                    continue;
                }

                hasHeldTools = true;
                if (!inventory.HasItem(item.Id))
                {
                    return false;
                }
            }

            return hasHeldTools;
        }

        /// <summary>Call after changing icon assignments at runtime to redraw the current guide.</summary>
        public void Refresh()
        {
            lastContextKey = -1;
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelGroup != null)
            {
                panelGroup.alpha = visible ? 1f : 0f;
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }
        }

        private void OnDisable()
        {
            foreach (var row in rows)
            {
                if (row != null)
                {
                    row.gameObject.SetActive(false);
                }
            }

            SetPanelVisible(false);
            lastContextKey = -1;
        }

        private void OnDestroy()
        {
            foreach (var row in rows)
            {
                if (row != null)
                {
                    Destroy(row.gameObject);
                }
            }

            rows.Clear();
        }
    }
}
