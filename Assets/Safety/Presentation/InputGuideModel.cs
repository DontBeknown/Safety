using System.Collections.Generic;

namespace SafetySimulation
{
    public enum InputGuideAction
    {
        Move,
        Look,
        Interact,
        EquipFlashlight,
        EquipCurrentMeter,
        Ascend,
        Descend,
        Use,
        Extend
    }

    public readonly struct InputGuideContext
    {
        public readonly bool CanInteract;
        public readonly bool InEquipmentArea;
        public readonly bool CollectionComplete;
        public readonly bool HoldingTool;
        public readonly bool OnLadder;
        public readonly bool AtBottom;
        public readonly bool IsTransitioning;
        public readonly bool HasFlashlight;
        public readonly bool HasCurrentMeter;
        public readonly bool FlashlightIsOn;

        public InputGuideContext(bool canInteract, bool inEquipmentArea, bool collectionComplete,
            bool holdingTool, bool onLadder, bool atBottom, bool isTransitioning,
            bool hasFlashlight = false, bool hasCurrentMeter = false, bool flashlightIsOn = false)
        {
            CanInteract = canInteract;
            InEquipmentArea = inEquipmentArea;
            CollectionComplete = collectionComplete;
            HoldingTool = holdingTool;
            OnLadder = onLadder;
            AtBottom = atBottom;
            IsTransitioning = isTransitioning;
            HasFlashlight = hasFlashlight;
            HasCurrentMeter = hasCurrentMeter;
            FlashlightIsOn = flashlightIsOn;
        }

        // Lets the view skip rebuilding its rows when the context has not changed.
        public int Key => (CanInteract ? 1 : 0) |
                          (InEquipmentArea ? 2 : 0) |
                          (CollectionComplete ? 4 : 0) |
                          (HoldingTool ? 8 : 0) |
                          (OnLadder ? 16 : 0) |
                          (AtBottom ? 32 : 0) |
                          (IsTransitioning ? 64 : 0) |
                          (HasFlashlight ? 128 : 0) |
                          (HasCurrentMeter ? 256 : 0) |
                          (FlashlightIsOn ? 512 : 0);
    }

    public readonly struct InputGuideEntry
    {
        public readonly string Instruction;
        public readonly InputGuideAction? Action;

        public InputGuideEntry(string instruction, InputGuideAction? action = null)
        {
            Instruction = instruction;
            Action = action;
        }
    }

    public static class InputGuideResolver
    {
        public static InputGuideEntry[] Resolve(InputGuideContext context)
        {
            if (!context.CanInteract)
            {
                return System.Array.Empty<InputGuideEntry>();
            }

            var entries = new List<InputGuideEntry>(7);
            if (context.OnLadder)
            {
                entries.Add(new InputGuideEntry("Ascend", InputGuideAction.Ascend));
                entries.Add(new InputGuideEntry("Descend", InputGuideAction.Descend));
                if (context.AtBottom)
                {
                    entries.Add(new InputGuideEntry("Dismount", InputGuideAction.Interact));
                }
            }
            else if (context.InEquipmentArea && !context.CollectionComplete)
            {
                entries.Add(new InputGuideEntry("Look at an item", InputGuideAction.Look));
                entries.Add(new InputGuideEntry("Acquire", InputGuideAction.Interact));
            }
            else
            {
                entries.Add(new InputGuideEntry("Move", InputGuideAction.Move));
                entries.Add(new InputGuideEntry("Look", InputGuideAction.Look));
            }

            if (context.HasFlashlight)
                entries.Add(new InputGuideEntry(context.FlashlightIsOn ? "Unequip flashlight" : "Equip flashlight",
                    InputGuideAction.EquipFlashlight));
            if (context.HasCurrentMeter)
                entries.Add(new InputGuideEntry(context.HoldingTool ? "Unequip current meter" : "Equip current meter",
                    InputGuideAction.EquipCurrentMeter));

            if (context.HoldingTool)
            {
                entries.Add(new InputGuideEntry("Use", InputGuideAction.Use));
                entries.Add(new InputGuideEntry("Extend", InputGuideAction.Extend));
            }

            return entries.ToArray();
        }

        public static string GetControlLabel(InputGuideAction action)
        {
            switch (action)
            {
                case InputGuideAction.Move: return "WASD";
                case InputGuideAction.Look: return "Mouse";
                case InputGuideAction.Interact: return "E";
                case InputGuideAction.EquipFlashlight: return "1";
                case InputGuideAction.EquipCurrentMeter: return "2";
                case InputGuideAction.Ascend: return "W";
                case InputGuideAction.Descend: return "S";
                case InputGuideAction.Use: return "Left click";
                case InputGuideAction.Extend: return "Right click";
                default: return string.Empty;
            }
        }
    }
}
