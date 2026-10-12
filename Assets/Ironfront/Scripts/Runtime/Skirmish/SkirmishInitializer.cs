using Ironfront.AI;
using Ironfront.CameraControl;
using Ironfront.Core.Skirmish;
using Ironfront.Data;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Skirmish
{
    /// <summary>
    /// Starts a skirmish match from the chosen configuration (Milestone 008). Runs before every other component:
    /// <list type="number">
    /// <item>Awake: resolves the settings (<see cref="SkirmishSession.SettingsFor"/>) and configures, before they wake,
    /// the starting credits (<see cref="MatchController"/>), the computer's difficulty (<see cref="EnemyCommander"/>)
    /// and the victory rules (<see cref="MatchFlowController"/>).</item>
    /// <item>Start (first of all Starts): spawns each faction's headquarters, refinery and harvester at its start
    /// slot (a validated pair drawn from the map's spawn table with a recorded seed), creates the faction's own
    /// <see cref="FactionStartLocation"/> marker and moves the camera over the player's base. Nothing tells either
    /// side where the other started: both have to scout.</item>
    /// <see cref="MatchFlowController"/> collects the competitors afterwards, as in the fixed-layout scenes.</item>
    /// </list>
    /// Every match therefore starts from scene content plus plain settings; nothing survives from a previous match.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class SkirmishInitializer : MonoBehaviour
    {
        [SerializeField] private MapDefinition map;
        [SerializeField] private MapCatalog catalog;
        [SerializeField] private MatchController match;
        [SerializeField] private MatchFlowController flow;
        [SerializeField] private EnemyCommander commander;
        [SerializeField] private RtsCameraController cameraRig;
        [SerializeField] private GameObject headquartersPrefab;
        [SerializeField] private GameObject refineryPrefab;
        [SerializeField] private GameObject harvesterPrefab;
        [SerializeField] private int playerFaction = MatchController.DefaultPlayerFactionId;
        [SerializeField] private int computerFaction = 2;

        public static SkirmishInitializer Current { get; private set; }

        public MapDefinition Map => map;
        public SkirmishSettings Settings { get; private set; }
        public ResolvedSkirmish Resolved { get; private set; }
        public AiDifficulty Difficulty { get; private set; }
        public bool BasesSpawned { get; private set; }
        public int PlayerFaction => playerFaction;
        public int ComputerFaction => computerFaction;
        public GameObject HeadquartersPrefab => headquartersPrefab;
        public GameObject RefineryPrefab => refineryPrefab;
        public MapCatalog Catalog => catalog;

        private void Awake()
        {
            Current = this;
            string mapId = map != null ? map.Id : SkirmishSettings.DefaultMapId;
            Settings = SkirmishSession.SettingsFor(mapId).Sanitised(catalog != null ? catalog.HasMap : null, mapId);
            int seed = Settings.Seed != SkirmishSettings.NewSeedEachMatch ? Settings.Seed : SkirmishSession.NextSeed();
            // Slots register in OnEnable, which may not have run yet: read the scene objects directly.
            var slots = FindObjectsByType<StartSlot>(FindObjectsInactive.Exclude);
            Resolved = Settings.Resolve(seed, StartSlot.TableOf(slots));
            if (!Resolved.HasSpawns) Debug.LogError("[Ironfront] Skirmish map has no valid pair of start slots.", this);

            if (match != null) match.SetStartingCredits(Resolved.StartingCredits);
            if (flow != null) flow.Configure(Resolved.Victory);
            Difficulty = catalog != null ? catalog.FindDifficulty(Resolved.Difficulty) : null;
            if (commander != null && Difficulty != null) commander.SetDifficulty(Difficulty);
            else if (Difficulty == null) Debug.LogWarning($"[Ironfront] No AiDifficulty asset for {Resolved.Difficulty}; the commander keeps its default.", this);

            Debug.Log($"[Ironfront] Skirmish: {map?.DisplayName ?? mapId} ({(map != null ? map.Size : 0):0} m), {Resolved.Difficulty}, " +
                      $"{Resolved.StartingCredits} credits, player slot {Resolved.PlayerSlot}, computer slot {Resolved.EnemySlot}, " +
                      $"seed {Resolved.Seed}, victory {Resolved.Victory}");
        }

        private void Start() => SpawnBases();

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void SpawnBases()
        {
            if (BasesSpawned) return;
            var playerSlot = Resolved.HasSpawns ? StartSlot.Find(Resolved.PlayerSlot) : null;
            var computerSlot = Resolved.HasSpawns ? StartSlot.Find(Resolved.EnemySlot) : null;
            if (playerSlot == null || computerSlot == null)
            {
                Debug.LogError("[Ironfront] Skirmish map is missing a start slot; no bases spawned.", this);
                return;
            }
            SpawnBase(playerFaction, playerSlot);
            SpawnBase(computerFaction, computerSlot);
            BasesSpawned = true;

            if (cameraRig != null)
                cameraRig.Focus(Vector3.Lerp(playerSlot.Headquarters, playerSlot.Refinery, 0.5f));
        }

        private void SpawnBase(int faction, StartSlot slot)
        {
            var root = new GameObject($"Base_F{faction}").transform;
            var facing = Data.BuildingDefinition.Facing(slot.FacesSouth);
            Spawn(headquartersPrefab, slot.Headquarters, facing, faction, root, "Headquarters");
            Spawn(refineryPrefab, slot.Refinery, facing, faction, root, "Refinery");
            Spawn(harvesterPrefab, slot.Harvester, Quaternion.Euler(0f, slot.HarvesterYaw, 0f), faction, root, "Harvester");
            FactionStartLocation.Create(faction, slot.Headquarters, root);
        }

        private static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, int faction, Transform parent, string label)
        {
            if (prefab == null)
            {
                Debug.LogError($"[Ironfront] SkirmishInitializer has no {label} prefab.");
                return;
            }
            var go = FactionSpawner.Spawn(prefab, position, rotation, faction);
            go.name = $"{label}_F{faction}";
            go.transform.SetParent(parent, true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
