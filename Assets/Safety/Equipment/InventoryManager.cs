using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    // CinemachineBrain's package execution order is 100; use its final camera pose.
    [DefaultExecutionOrder(10000)]
    public sealed class InventoryManager : MonoBehaviour
    {
        public EquipmentCatalog Catalog;
        [SerializeField]
        private FirstPersonController player;
        [SerializeField]
        private Transform toolMount;
        [SerializeField, Tooltip("Separate player mount for the flashlight model. The meter continues using Tool Mount.")]
        private Transform flashlightMount;
        [SerializeField, Tooltip("Camera used for the meter's center ray. Falls back to Camera.main at initialization.")]
        private Camera viewCamera;
        [SerializeField]
        private Transform wornMount;
        [SerializeField]
        private InspectionPoint[] inspectionPoints = Array.Empty<InspectionPoint>();
        [SerializeField]
        private SafetyHud hud;
        [SerializeField]
        private AudioClip pickupSound;
        [SerializeField]
        private AudioClip equipSound;
        [SerializeField]
        private AudioClip unequipSound;
        private readonly HashSet<string> acquired = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, EquippableItem> worn = new Dictionary<string, EquippableItem>();
        private SessionManager session;
        private AchievementManager achievements;
        private string equippedId;
        private bool usingTool;
        private FlashlightTool flashlight;
        private int lastFlashlightToggleFrame = -1;

        public bool HasFlashlight => HasAcquiredTool<FlashlightTool>();
        public bool HasCurrentMeter => HasAcquiredTool<CurrentMeterTool>();
        public bool FlashlightIsOn => flashlight != null && flashlight.IsOn;
        public event Action EquipmentChanged;
        public static InventoryManager Instance
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
            DestroyFlashlight();
            DestroyHeldTool();
            foreach (var item in worn.Values)
            {
                if (item != null)
                {
                    item.Unequip();
                    Destroy(item.gameObject);
                }
            }

            worn.Clear();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public ToolBase EquippedTool
        {
            get;
            private set;
        }
        public bool IsReady
        {
            get;
            private set;
        }

        public LadderComponent ActiveLadder
        {
            get
            {
                return player != null ? player.ActiveLadder : null;
            }
        }

        public bool AllRequiredPpe
        {
            get
            {
                if (!IsReady)
                {
                    return false;
                }

                foreach (var id in Catalog.RequiredPpeIds)
                {
                    if (!HasPpe(id))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private void Start()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }
            session = SessionManager.Instance;
            achievements = AchievementManager.Instance;
            if (player == null || toolMount == null || flashlightMount == null || wornMount == null || Catalog == null || session == null ||
                achievements == null)
            {
                Debug.LogError("InventoryManager needs player, tool/flashlight/worn mounts, catalog, session and achievements.", this);
                enabled = false;
                return;
            }

            if (!Catalog.TryValidate(out var reason))
            {
                Debug.LogError(reason, this);
                enabled = false;
                return;
            }

            var targetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var point in inspectionPoints)
            {
                if (point == null || !point.TryValidate(out reason) || !targetIds.Add(point.Id))
                {
                    Debug.LogError("Inventory inspection points must be configured, valid and have unique IDs.", this);
                    enabled = false;
                    return;
                }
            }

            if (inspectionPoints.Length == 0)
            {
                Debug.LogError("Assign inventory inspection points for the tools.", this);
                enabled = false;
                return;
            }

            IsReady = true;
            RefreshPresentation();
        }

        public bool HasItem(string id)
        {
            return id != null && acquired.Contains(id);
        }

        private ItemDefinition FindToolDefinition<T>() where T : ToolBase
        {
            if (Catalog == null || Catalog.Items == null) return null;
            foreach (var definition in Catalog.Items)
                if (definition != null && definition.Category == EquipmentCategory.Held && definition.Prefab is T)
                    return definition;
            return null;
        }

        private bool HasAcquiredTool<T>() where T : ToolBase
        {
            var definition = FindToolDefinition<T>();
            return definition != null && HasItem(definition.Id);
        }

        public bool IsOwnedToolCollider(Collider collider)
        {
            return collider != null &&
                ((EquippedTool != null && collider.transform.IsChildOf(EquippedTool.transform)) ||
                 (flashlight != null && collider.transform.IsChildOf(flashlight.transform)));
        }

        public bool HasPpe(string id)
        {
            return id != null && worn.TryGetValue(id, out var item) && item != null && item.isActiveAndEnabled;
        }

        public bool TryAcquire(string id, out string reason)
        {
            if (!IsReady || !isActiveAndEnabled || !session.CanInteract)
            {
                reason = "Inventory is unavailable now.";
                return false;
            }

            var definition = Catalog.FindItem(id);
            if (definition == null)
            {
                reason = $"Unknown item '{id}'.";
                return false;
            }

            if (HasItem(id))
            {
                reason = "This item is already acquired.";
                return false;
            }

            if (definition.Category == EquipmentCategory.Worn)
            {
                worn.Add(id, Spawn(definition, wornMount));
            }

            acquired.Add(id);
            if (definition.Prefab is FlashlightTool)
                flashlight = (FlashlightTool)Spawn(definition, flashlightMount);
            ShowMessage($"Acquired {definition.DisplayName}.");
            SoundManager.Instance?.Play(pickupSound, 1f, true);
            RefreshPresentation();
            EquipmentChanged?.Invoke();
            ReportPpe();
            reason = null;
            return true;
        }

        public void ReportPpe()
        {
            if (AllRequiredPpe)
            {
                achievements.Report(AchievementType.Equip, "ppe");
            }
        }

        public bool TrySelectSlot(int index, out string reason)
        {
            if (!IsReady)
            {
                reason = "Inventory is not ready.";
                return false;
            }

            var heldIndex = 0;
            foreach (var definition in Catalog.Items)
            {
                if (definition.Category != EquipmentCategory.Held)
                {
                    continue;
                }

                if (heldIndex == index)
                {
                    return TryEquip(definition.Id, out reason);
                }

                heldIndex++;
            }

            reason = "No tool is configured in that slot.";
            return false;
        }

        public bool IsFlashlightSlot(int index)
        {
            if (!IsReady) return false;
            var heldIndex = 0;
            foreach (var definition in Catalog.Items)
            {
                if (definition.Category != EquipmentCategory.Held) continue;
                if (heldIndex++ == index) return definition.Prefab is FlashlightTool;
            }
            return false;
        }

        public bool TryEquip(string id, out string reason)
        {
            var definition = IsReady ? Catalog.FindItem(id) : null;
            if (!isActiveAndEnabled || definition == null || definition.Category != EquipmentCategory.Held || !HasItem(id))
            {
                reason = "Acquire this configured tool first.";
                return false;
            }

            if (!session.CanInteract)
            {
                reason = "Tool selection is unavailable now.";
                return false;
            }

            if (definition.Prefab is FlashlightTool)
                return TryToggleFlashlight(out reason);

            if (definition.Prefab is CurrentMeterTool && EquippedTool is CurrentMeterTool && equippedId == id)
            {
                DestroyHeldTool();
                equippedId = null;
                player.RequireActionInputRelease();
                ShowMessage($"Unequipped {definition.DisplayName}.");
                SoundManager.Instance?.Play(unequipSound, 1f, true);
                RefreshPresentation();
                EquipmentChanged?.Invoke();
                reason = null;
                return true;
            }

            DestroyHeldTool();
            equippedId = id;
            EquippedTool = (ToolBase)Spawn(definition, toolMount);
            player.RequireActionInputRelease();
            ShowMessage($"Equipped {definition.DisplayName}.");
            SoundManager.Instance?.Play(equipSound, 1f, true);
            EquipmentChanged?.Invoke();
            reason = null;
            return true;
        }

        private EquippableItem Spawn(ItemDefinition definition, Transform mount)
        {
            var item = Instantiate(definition.Prefab, mount);
            item.transform.localPosition = definition.EquipPosition;
            item.transform.localRotation = Quaternion.Euler(definition.EquipEuler);
            item.ItemId = definition.Id;
            item.Equip(new ToolUseContext(player, mount, inspectionPoints, session, achievements, this, hud, viewCamera));
            return item;
        }

        private bool CanUseTool()
        {
            return IsReady && isActiveAndEnabled && EquippedTool != null &&
                EquippedTool.isActiveAndEnabled && player != null &&
                player.isActiveAndEnabled && session.CanWork;
        }

        private void LateUpdate()
        {
            if (flashlight != null)
            {
                if (!IsReady || !isActiveAndEnabled || player == null || !player.isActiveAndEnabled || !session.CanWork)
                    flashlight.EndUse();
                else if (flashlight.isActiveAndEnabled)
                {
                    flashlight.TickAim(Time.deltaTime);
                    flashlight.TickUse(Time.deltaTime);
                }
            }

            if (EquippedTool == null)
            {
                return;
            }

            if (!CanUseTool())
            {
                CancelUse();
                return;
            }

            EquippedTool.TickAim(Time.deltaTime);
            EquippedTool.TickExtension(Time.deltaTime);
            EquippedTool.TickUse(Time.deltaTime);
            if (hud != null)
            {
                hud.SetToolProgress(EquippedTool.UseProgress, EquippedTool.UseProgressLabel);
            }
        }

        public void BeginUse()
        {
            if (EquippedTool == null || usingTool || !CanUseTool())
            {
                return;
            }

            usingTool = true;
            EquippedTool.BeginUse();
        }

        public void EndUse()
        {
            usingTool = false;
            if (EquippedTool != null)
            {
                EquippedTool.EndUse();
            }
        }

        public void CancelUse()
        {
            EndUse();
            if (EquippedTool != null)
            {
                EquippedTool.ResetExtension();
            }

            if (hud != null)
            {
                hud.SetToolProgress(0f, string.Empty);
            }
        }

        public void SetExtended(bool extended)
        {
            if (EquippedTool != null && (!extended || CanUseTool()))
            {
                EquippedTool.SetExtended(extended);
            }
        }

        public void SecondaryAction()
        {
            if (!TryToggleFlashlight(out var reason))
            {
                ShowMessage(reason, true);
            }
        }

        public bool TryToggleFlashlight(out string reason)
        {
            if (!IsReady || !isActiveAndEnabled || player == null || !player.isActiveAndEnabled || !session.CanWork)
            {
                reason = "The flashlight is unavailable now.";
                return false;
            }

            if (!HasFlashlight)
            {
                reason = "Acquire the flashlight first.";
                return false;
            }

            // Ignore duplicate toggle requests within the same input frame.
            if (lastFlashlightToggleFrame == Time.frameCount)
            {
                reason = null;
                return true;
            }

            if (flashlight == null)
                flashlight = (FlashlightTool)Spawn(FindToolDefinition<FlashlightTool>(), flashlightMount);
            lastFlashlightToggleFrame = Time.frameCount;
            flashlight.SecondaryAction();
            RefreshPresentation();
            EquipmentChanged?.Invoke();
            reason = null;
            return true;
        }

        private void DestroyFlashlight()
        {
            if (flashlight == null) return;
            flashlight.Unequip();
            Destroy(flashlight.gameObject);
            flashlight = null;
        }

        private void DestroyHeldTool()
        {
            CancelUse();
            if (EquippedTool != null)
            {
                EquippedTool.Unequip();
                EquippedTool.gameObject.SetActive(false);
                Destroy(EquippedTool.gameObject);
                EquippedTool = null;
            }
        }

        public void RecreateEquippedTool()
        {
            DestroyFlashlight();
            lastFlashlightToggleFrame = -1;
            foreach (var point in inspectionPoints)
                if (point != null) point.SetIlluminated(false);
            if (HasFlashlight)
                flashlight = (FlashlightTool)Spawn(FindToolDefinition<FlashlightTool>(), flashlightMount);
            DestroyHeldTool();
            if (!string.IsNullOrEmpty(equippedId))
            {
                EquippedTool = (ToolBase)Spawn(Catalog.FindItem(equippedId), toolMount);
            }
        }

        public void ShowMessage(string message, bool warning = false)
        {
            if (hud != null)
            {
                hud.SetMessage(message, warning);
            }

            if (warning) session?.PlayWarning();
        }

        private void RefreshPresentation()
        {
            if (hud != null)
            {
                hud.SetInventory(this);
            }
        }

        private void OnDisable()
        {
            if (flashlight != null) flashlight.EndUse();
            CancelUse();
            if (Instance == this && player != null)
            {
                player.RequireInputRelease();
            }
        }

        private void OnEnable()
        {
            if (IsReady)
            {
                RefreshPresentation();
                player.RequireInputRelease();
            }
        }
    }
}
