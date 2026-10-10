using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Data
{
    [Serializable]
    public class FactionSetup
    {
        public int id = 1;
        public string displayName = "Player";
        public Color color = new Color(0.2f, 0.45f, 0.95f);
        public bool humanControlled = true;
        [Min(0)] public int startingCredits = 2000;
    }

    /// <summary>Match-wide rules and the list of factions taking part.</summary>
    [CreateAssetMenu(menuName = "Ironfront/Match Setup", fileName = "MatchSetup")]
    public class MatchSetup : ScriptableObject
    {
        [SerializeField] private List<FactionSetup> factions = new List<FactionSetup> { new FactionSetup() };
        [Tooltip("Structures offered in the construction menu, in display order.")]
        [SerializeField] private List<BuildingDefinition> constructionOptions = new List<BuildingDefinition>();
        [SerializeField, Min(1)] private int creditsPerOre = 1;
        [Tooltip("Production speed multiplier while a faction's power drain exceeds its output.")]
        [SerializeField, Range(0.05f, 1f)] private float lowPowerProductionMultiplier = 0.5f;
        [SerializeField, Min(1)] private int productionQueueCapacity = 5;

        public IReadOnlyList<FactionSetup> Factions => factions;
        public IReadOnlyList<BuildingDefinition> ConstructionOptions => constructionOptions;
        public int CreditsPerOre => creditsPerOre;
        public float LowPowerProductionMultiplier => lowPowerProductionMultiplier;
        public int ProductionQueueCapacity => productionQueueCapacity;
    }
}
