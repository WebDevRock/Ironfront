using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.CameraControl;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Core;
using Ironfront.Core.Economy;
using Ironfront.Economy;
using Ironfront.InputBindings;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Player notifications: attacks on the base, units or harvesters, losses, completed construction,
    /// units ready, low power, depleted ore and insufficient credits. Messages of the same kind are throttled.
    /// Attack alerts remember their location: Space centres the camera there, and the minimap pings it.
    /// </summary>
    public class NotificationCenter : MonoBehaviour
    {
        public enum Kind { Info, Good, Warning, Danger }

        [SerializeField] private RectTransform feed;
        [SerializeField] private RtsCameraController cameraController;
        [SerializeField] private BuildingPlacementController placement;
        [SerializeField] private float lifetime = 6f;
        [SerializeField] private int maxLines = 5;

        private sealed class Line
        {
            public Text Text;
            public float Age;
        }

        private readonly NotificationThrottle _throttle = new NotificationThrottle();
        private readonly List<Line> _lines = new List<Line>();
        private readonly List<string> _recent = new List<string>();
        private FactionState _player;
        private bool _wasLowPower;
        private Selection.ControlGroupInput _groups;

        public static NotificationCenter Current { get; private set; }

        /// <summary>Most recent messages, newest last (bounded to 30).</summary>
        public IReadOnlyList<string> Recent => _recent;
        public Vector3? LastAlertPosition { get; private set; }
        public float LastAlertTime { get; private set; } = float.NegativeInfinity;

        public event Action<string, Kind> Posted;

        private int PlayerFaction => MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;

        private void Awake() => Current = this;

        private void Start()
        {
            if (cameraController == null) cameraController = FindAnyObjectByType<RtsCameraController>();
            if (placement == null) placement = FindAnyObjectByType<BuildingPlacementController>();
            Health.AnyDamaged += OnDamaged;
            Health.AnyDied += OnDied;
            BuildingPlacementController.AnyBuildingPlaced += OnPlaced;
            VehicleFactory.AnyUnitProduced += OnProduced;
            OreDeposit.AnyDepleted += OnDepleted;
            TerritorySystem.AnchorLost += OnAnchorLost;
            BuildingSale.AnySold += OnSold;
            Units.MobileCommandVehicle.AnyDeployFailed += OnDeployFailed;
            if (placement != null) placement.Message += OnPlacementMessage;
            _groups = FindAnyObjectByType<Selection.ControlGroupInput>();
            if (_groups != null) _groups.Feedback += OnGroupFeedback;
            if (MatchController.Current != null)
            {
                _player = MatchController.Current.PlayerFaction;
                _wasLowPower = _player.Power.IsLowPower;
                _player.Power.Changed += OnPowerChanged;
                _player.Research.Started += OnResearchStarted;
                _player.Research.Completed += OnResearchCompleted;
            }
        }

        private string TopicName(string id)
        {
            var t = MatchController.Current != null && MatchController.Current.Setup != null ? MatchController.Current.Setup.FindResearch(id) : null;
            return t != null ? t.DisplayName : id;
        }

        private void OnResearchStarted(string id) => Post($"Research started: {TopicName(id)}", Kind.Info);

        private void OnResearchCompleted(string id)
        {
            Post($"Research complete: {TopicName(id)}", Kind.Good);
            Ironfront.Audio.GameAudio.PlayFlat(Ironfront.Audio.SoundId.ResearchComplete);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            Health.AnyDamaged -= OnDamaged;
            Health.AnyDied -= OnDied;
            BuildingPlacementController.AnyBuildingPlaced -= OnPlaced;
            VehicleFactory.AnyUnitProduced -= OnProduced;
            OreDeposit.AnyDepleted -= OnDepleted;
            TerritorySystem.AnchorLost -= OnAnchorLost;
            BuildingSale.AnySold -= OnSold;
            Units.MobileCommandVehicle.AnyDeployFailed -= OnDeployFailed;
            if (placement != null) placement.Message -= OnPlacementMessage;
            if (_groups != null) _groups.Feedback -= OnGroupFeedback;
            if (_player != null)
            {
                _player.Power.Changed -= OnPowerChanged;
                _player.Research.Started -= OnResearchStarted;
                _player.Research.Completed -= OnResearchCompleted;
            }
        }

        private void Update()
        {
            var jump = RtsInput.JumpToAlert;
            if (jump != null && jump.WasPressedThisFrame() && LastAlertPosition.HasValue && cameraController != null)
                cameraController.Focus(LastAlertPosition.Value);

            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var line = _lines[i];
                line.Age += Time.unscaledDeltaTime;
                if (line.Age >= lifetime)
                {
                    Destroy(line.Text.gameObject);
                    _lines.RemoveAt(i);
                    continue;
                }
                var c = line.Text.color;
                c.a = Mathf.Clamp01((lifetime - line.Age) / 1f);
                line.Text.color = c;
            }
        }

        /// <summary>Shows a message unless one with the same <paramref name="throttleKey"/> appeared within <paramref name="cooldown"/> seconds.</summary>
        public bool Post(string text, Kind kind, string throttleKey = null, float cooldown = 0f, Vector3? location = null)
        {
            if (throttleKey != null && !_throttle.TryFire(throttleKey, Time.unscaledTime, cooldown)) return false;
            if (location.HasValue && (kind == Kind.Danger || kind == Kind.Warning))
            {
                LastAlertPosition = location;
                LastAlertTime = Time.unscaledTime;
                text += "  [Space]";
            }

            _recent.Add(text);
            if (_recent.Count > 30) _recent.RemoveAt(0);
            Posted?.Invoke(text, kind);

            if (feed != null)
            {
                var t = UiTheme.Label(feed, "Notification", 15, ColorOf(kind), TextAnchor.MiddleCenter);
                t.text = text;
                t.fontStyle = kind == Kind.Danger ? FontStyle.Bold : FontStyle.Normal;
                t.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
                var shadow = t.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
                _lines.Add(new Line { Text = t });
                while (_lines.Count > maxLines)
                {
                    Destroy(_lines[0].Text.gameObject);
                    _lines.RemoveAt(0);
                }
            }
            return true;
        }

        private static Color ColorOf(Kind kind) =>
            kind == Kind.Good ? UiTheme.Good : kind == Kind.Warning ? UiTheme.Warning : kind == Kind.Danger ? UiTheme.Danger : UiTheme.Text;

        // ---------- Event handlers ----------

        private void OnDamaged(Health victim, float amount, Health attacker)
        {
            if (victim == null || victim.FactionId != PlayerFaction) return;
            if (attacker != null && attacker.FactionId == PlayerFaction) return;
            Vector3 where = victim.transform.position;
            if (victim.IsStructure)
            {
                // Checked first so sustained fire does not format a message per hit.
                if (_throttle.IsReady("base-attack", Time.unscaledTime, 15f))
                    Post($"Our base is under attack: {victim.DisplayName}", Kind.Danger, "base-attack", 15f, where);
            }
            else if (victim.GetComponent<Harvester>() != null)
                Post("Harvester under attack", Kind.Warning, "harvester-attack", 12f, where);
            else
                Post("Our units are under attack", Kind.Warning, "unit-attack", 12f, where);
        }

        private void OnDied(Health dead)
        {
            if (dead == null) return;
            bool mine = dead.FactionId == PlayerFaction;
            // Fog of war: the loss of an enemy structure is only reported if the player actually saw it go.
            if (!mine && !Ironfront.Vision.VisionSystem.CanSee(PlayerFaction, dead)) return;
            if (dead.IsStructure)
                Post(mine ? $"Structure destroyed: {dead.DisplayName}" : $"Enemy {dead.DisplayName} destroyed", mine ? Kind.Danger : Kind.Good,
                    location: mine ? dead.transform.position : (Vector3?)null);
            else if (mine && _throttle.IsReady("unit-lost", Time.unscaledTime, 4f))
                Post($"Unit lost: {dead.DisplayName}", Kind.Warning, "unit-lost", 4f);
        }

        private void OnPlaced(Building b)
        {
            if (b == null || b.FactionId != PlayerFaction || b.Definition == null) return;
            if (b.Definition.TerritoryRadius > 0f)
                Post($"{b.Definition.DisplayName} established: new construction territory", Kind.Good, location: b.transform.position);
            else
                Post($"Construction complete: {b.Definition.DisplayName}", Kind.Good);
        }

        // ---------- Milestone 010 ----------

        private void OnAnchorLost(Building b)
        {
            if (b == null || b.FactionId != PlayerFaction || MatchFlowController.Current == null || MatchFlowController.Current.IsOver) return;
            if (!Application.isPlaying || b.gameObject.scene.isLoaded == false) return; // scene unload
            Post($"Construction territory lost: {b.Definition.DisplayName}", Kind.Danger, "territory-lost", 2f, b.transform.position);
            Ironfront.Audio.GameAudio.PlayFlat(Ironfront.Audio.SoundId.TerritoryLost);
        }

        private void OnSold(Building b, int refund)
        {
            if (b != null && b.FactionId == PlayerFaction && b.Definition != null)
                Post($"Sold {b.Definition.DisplayName}: +{refund} credits", Kind.Info);
        }

        private void OnDeployFailed(Units.MobileCommandVehicle m, Core.Construction.DeployCheck why)
        {
            if (m != null && m.FactionId == PlayerFaction && why != Core.Construction.DeployCheck.Busy)
                Post($"Deployment failed: {Core.Construction.DeployProcess.Describe(why)}", Kind.Warning, "deploy-failed", 1f, m.transform.position);
        }

        private void OnProduced(VehicleFactory factory, GameObject unit)
        {
            if (factory == null || factory.FactionId != PlayerFaction || unit == null) return;
            var id = unit.GetComponent<UnitIdentity>();
            string name = id != null ? id.DisplayName : unit.name;
            Post($"Unit ready: {name}", Kind.Info, "ready-" + name, 1f);
        }

        private void OnDepleted(OreDeposit d) => Post("An ore field has been depleted", Kind.Warning, "ore-depleted", 5f);

        private void OnPlacementMessage(string message)
        {
            if (message != null && message.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0)
                Post("Insufficient credits", Kind.Warning, "credits", 3f);
        }

        private void OnGroupFeedback(string message) => Post(message, Kind.Info, "group", 0.2f);

        private void OnPowerChanged()
        {
            bool low = _player.Power.IsLowPower;
            if (low && !_wasLowPower) Post("Low power: production slowed. Build a Power Plant", Kind.Warning, "low-power", 10f);
            _wasLowPower = low;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
