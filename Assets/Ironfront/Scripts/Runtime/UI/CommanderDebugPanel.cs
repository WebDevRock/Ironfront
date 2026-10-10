using System.Text;
using Ironfront.AI;
using Ironfront.InputBindings;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Optional AI debug view (toggle with F9): each commander's strategic state, resources, unit counts,
    /// objective, last planner step and its recent decisions (bounded log). Hidden by default.
    /// </summary>
    public class CommanderDebugPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text text;
        [SerializeField] private bool visibleOnStart;
        [SerializeField] private float refreshInterval = 0.5f;

        private float _timer;

        public bool IsVisible => panel != null && panel.activeSelf;

        private void Start() => SetVisible(visibleOnStart);

        public void SetVisible(bool visible)
        {
            if (panel != null) panel.SetActive(visible);
            _timer = 0f;
        }

        private void Update()
        {
            var toggle = RtsInput.ToggleAiDebug;
            if (toggle != null && toggle.WasPressedThisFrame()) SetVisible(!IsVisible);
            if (!IsVisible || text == null) return;

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            text.text = Describe();
        }

        public static string Describe()
        {
            var sb = new StringBuilder("AI DEBUG  (F9 to hide)\n");
            if (EnemyCommander.All.Count == 0) sb.Append("No commanders in scene.");
            foreach (var c in EnemyCommander.All)
            {
                var s = c.LastSnapshot;
                sb.Append($"\nFaction {c.FactionId}  [{(c.IsHalted ? "HALTED" : c.State.ToString())}]  {c.TimeInState:0}s in state\n");
                sb.Append($"Credits {s.Credits}  Power {s.PowerConsumed}/{s.PowerProduced}\n");
                sb.Append($"Refineries {s.Refineries}  Power plants {s.PowerPlants}  Factories {s.Factories}\n");
                sb.Append($"Harvesters {s.Harvesters} (+{s.QueuedHarvesters})  Light {s.LightTanks} (+{s.QueuedLight})  Heavy {s.HeavyTanks} (+{s.QueuedHeavy})\n");
                sb.Append($"Army strength {s.ArmyStrength:0.0}  Attackers {c.AttackersCount}  Threat {s.ThreatStrength:0.0}\n");
                sb.Append($"Seen enemies: {s.KnownEnemyLight} light, {s.KnownEnemyHeavy} heavy\n");
                sb.Append($"Objective: {c.Objective}\n");
                sb.Append($"Planner: {c.LastAction}\n");
                sb.Append($"Decisions {c.Decisions}  Orders {c.OrdersIssued}  Built {c.BuildingsPlaced}  Produced {c.UnitsProduced}  Attacks {c.AttacksLaunched}\n");
                sb.Append("Recent:\n");
                int n = 0;
                foreach (var line in c.Log.NewestFirst())
                {
                    sb.Append("  ").Append(line).Append('\n');
                    if (++n >= 10) break;
                }
            }
            return sb.ToString();
        }
    }
}
