using StarterAssets;
using UnityEngine;

namespace SafetySimulation
{
    public sealed class ToolUseContext
    {
        public FirstPersonController Player
        {
            get;
        }
        public Transform ToolMount
        {
            get;
        }
        public Camera ViewCamera { get; }
        public InspectionPoint[] InspectionPoints
        {
            get;
        }
        public SessionManager Session
        {
            get;
        }
        public AchievementManager Achievements
        {
            get;
        }
        public InventoryManager Inventory
        {
            get;
        }
        public SafetyHud Hud
        {
            get;
        }
        public int AttemptId
        {
            get;
        }

        public LadderComponent Ladder
        {
            get
            {
                return Player != null ? Player.ActiveLadder : null;
            }
        }

        public bool CanUse
        {
            get
            {
                return Player != null && Player.isActiveAndEnabled && Session != null && Session.CanWork &&
                    Session.AttemptId == AttemptId && Inventory != null && Inventory.isActiveAndEnabled;
            }
        }

        public bool CanMeasure
        {
            get
            {
                return CanUse && Inventory.AllRequiredPpe;
            }
        }

        public ToolUseContext(FirstPersonController player, Transform toolMount, InspectionPoint[] inspectionPoints,
            SessionManager session, AchievementManager achievements, InventoryManager inventory, SafetyHud hud,
            Camera viewCamera = null)
        {
            Player = player;
            ToolMount = toolMount;
            ViewCamera = viewCamera != null ? viewCamera : Camera.main;
            InspectionPoints = inspectionPoints;
            Session = session;
            Achievements = achievements;
            Inventory = inventory;
            Hud = hud;
            AttemptId = session.AttemptId;
        }
    }
}
