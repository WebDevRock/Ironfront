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
        [SerializeField] private CombatVisuals combatVisuals;
        [Tooltip("Research topics available at a Research Centre (Milestone 007).")]
        [SerializeField] private List<ResearchDefinition> research = new List<ResearchDefinition>();
        [Tooltip("Faction-specific visibility and exploration (Milestone 007).")]
        [SerializeField] private bool fogOfWar = true;
        [Tooltip("Repair costs, rates and recovery limits (Milestone 009).")]
        [SerializeField] private RepairSettings repair;
        [Tooltip("Building sales, MCV deployment and the territory overlay (Milestone 010).")]
        [SerializeField] private TerritorySettings territory;

        public IReadOnlyList<FactionSetup> Factions => factions;
        public IReadOnlyList<BuildingDefinition> ConstructionOptions => constructionOptions;
        public int CreditsPerOre => creditsPerOre;
        public float LowPowerProductionMultiplier => lowPowerProductionMultiplier;
        public int ProductionQueueCapacity => productionQueueCapacity;
        public CombatVisuals CombatVisuals => combatVisuals;
        public IReadOnlyList<ResearchDefinition> Research => research;
        public bool FogOfWar => fogOfWar;
        public RepairSettings Repair => repair;
        public TerritorySettings Territory => territory;

        public ResearchDefinition FindResearch(string id)
        {
            foreach (var r in research)
                if (r != null && r.Id == id) return r;
            return null;
        }

        public Color ColorOf(int factionId)
        {
            foreach (var f in factions)
                if (f.id == factionId) return f.color;
            return Color.gray;
        }
    }
}
