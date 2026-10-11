using System.Collections.Generic;
using System.Linq;
using Ironfront.Core.Research;
using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>A research topic conducted at a Research Centre (Milestone 007).</summary>
    [CreateAssetMenu(menuName = "Ironfront/Research Definition", fileName = "ResearchDefinition")]
    public class ResearchDefinition : ScriptableObject, IResearchTopic
    {
        [SerializeField] private string id = "research";
        [SerializeField] private string displayName = "Research";
        [TextArea] [SerializeField] private string description = "";
        [SerializeField, Min(0)] private int cost = 1000;
        [SerializeField, Min(1f)] private float researchTime = 40f;
        [SerializeField] private UpgradeEffect effect;
        [Tooltip("Effect size: armour points, or the fraction added to damage / harvest rate.")]
        [SerializeField] private float value = 0.1f;
        [SerializeField] private List<ResearchDefinition> requires = new List<ResearchDefinition>();
        [SerializeField] private Sprite icon;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public int Cost => cost;
        public float ResearchTime => researchTime;
        public UpgradeEffect Effect => effect;
        public float Value => value;
        public Sprite Icon => icon;
        public IReadOnlyList<ResearchDefinition> Requires => requires;
        public IReadOnlyList<string> RequiredResearch => requires.Where(r => r != null).Select(r => r.Id).ToList();
    }
}
