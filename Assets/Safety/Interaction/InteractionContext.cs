using StarterAssets;
using UnityEngine;

namespace SafetySimulation
{
    public sealed class InteractionContext
    {
        public FirstPersonController Player
        {
            get;
        }
        public Camera Camera
        {
            get;
        }
        public SessionManager Session
        {
            get;
        }
        public InventoryManager Inventory
        {
            get;
        }

        public InteractionContext(FirstPersonController player, Camera camera, SessionManager session,
            InventoryManager inventory)
        {
            Player = player;
            Camera = camera;
            Session = session;
            Inventory = inventory;
        }
    }
}
