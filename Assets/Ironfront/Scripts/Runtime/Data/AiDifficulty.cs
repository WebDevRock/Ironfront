using Ironfront.Core.AI;
using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>
    /// Computer commander tuning. Every setting changes behaviour only: the AI pays the same costs, obeys the
    /// same placement rules and deals the same damage as the player. There are no resource or damage multipliers.
    /// </summary>
    [CreateAssetMenu(menuName = "Ironfront/AI Difficulty", fileName = "AiDifficulty")]
    public class AiDifficulty : ScriptableObject
    {
        [SerializeField] private string displayName = "Normal";
        [Tooltip("Seconds between strategic decisions.")]
        [SerializeField, Min(0.2f)] private float decisionInterval = 1.5f;
        [SerializeField] private CommanderSettings strategy = CommanderSettings.Normal;

        [Header("Knowledge")]
        [Tooltip("How far the AI's units and buildings can see enemies.")]
        [SerializeField, Min(5f)] private float visionRange = 22f;
        [Tooltip("Enemy units are remembered this long after they were last seen (for army composition).")]
        [SerializeField, Min(0f)] private float threatMemorySeconds = 20f;
        [Tooltip("Configured knowledge: the AI knows where the opponent started (standard for 1v1 maps).")]
        [SerializeField] private bool knowsEnemyStartLocation = true;

        [Header("Tactics")]
        [Tooltip("Enemies within this distance of an AI structure trigger the Defend state.")]
        [SerializeField, Min(5f)] private float defendRadius = 24f;
        [Tooltip("Distance from the HQ, towards the enemy, where the army assembles.")]
        [SerializeField, Min(4f)] private float rallyDistance = 14f;
        [Tooltip("Decisions without progress before a unit's order is re-issued (double this: unit is sent home).")]
        [SerializeField, Min(1)] private int stuckDecisions = 3;

        [Header("Debug")]
        [SerializeField, Min(5)] private int decisionLogCapacity = 20;

        public string DisplayName => displayName;
        public float DecisionInterval => decisionInterval;
        public CommanderSettings Strategy => strategy;
        public float VisionRange => visionRange;
        public float ThreatMemorySeconds => threatMemorySeconds;
        public bool KnowsEnemyStartLocation => knowsEnemyStartLocation;
        public float DefendRadius => defendRadius;
        public float RallyDistance => rallyDistance;
        public int StuckDecisions => stuckDecisions;
        public int DecisionLogCapacity => decisionLogCapacity;
    }
}
