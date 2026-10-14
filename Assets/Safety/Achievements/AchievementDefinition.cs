using System;
using UnityEngine;

namespace SafetySimulation
{
    [CreateAssetMenu(menuName = "Safety/Achievement")]
    public sealed class AchievementDefinition : ScriptableObject
    {
        public AchievementType Type;
        public string Id;
        [TextArea]
        public string Description;
        public bool Required = true;
        public AchievementDefinition[] Prerequisites = Array.Empty<AchievementDefinition>();
    }
}
