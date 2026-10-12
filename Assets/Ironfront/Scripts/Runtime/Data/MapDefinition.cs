using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>
    /// A skirmish battlefield as the setup menu presents it (Milestone 008): name, description, preview and the scene
    /// that holds its terrain, ore fields, start slots and baked navigation.
    /// </summary>
    [CreateAssetMenu(menuName = "Ironfront/Map Definition", fileName = "Map")]
    public class MapDefinition : ScriptableObject
    {
        [SerializeField] private string id = "map";
        [SerializeField] private string displayName = "Map";
        [Tooltip("Scene name as listed in Build Settings.")]
        [SerializeField] private string sceneName = "";
        [SerializeField, TextArea(2, 5)] private string description = "";
        [Tooltip("Short tags shown under the name, e.g. \"Open desert · 120 m\".")]
        [SerializeField] private string tagline = "";
        [SerializeField] private Texture2D preview;
        [SerializeField, Min(40f)] private float size = 100f;
        [Tooltip("Start slot names in slot-id order (shown in the setup menu and on the preview).")]
        [SerializeField] private string[] startNames = new string[0];
        [Tooltip("Spawn-pair rules the builder validated the start slots with.")]
        [SerializeField, Min(0f)] private float minSpawnSeparation = 200f;
        [SerializeField, Range(0f, 1f)] private float maxSpawnImbalance = 0.12f;

        public string Id => id;
        public string DisplayName => displayName;
        public string SceneName => sceneName;
        public string Description => description;
        public string Tagline => tagline;
        public Texture2D Preview => preview;
        public float Size => size;
        public System.Collections.Generic.IReadOnlyList<string> StartNames => startNames;
        public Core.Skirmish.SpawnPairRules SpawnRules => new Core.Skirmish.SpawnPairRules
        {
            MinSeparation = minSpawnSeparation,
            MaxOpportunityImbalance = maxSpawnImbalance,
        };
    }
}
