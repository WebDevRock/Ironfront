using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ironfront.Buildings;
using Ironfront.Commands;
using Ironfront.Core.Repair;
using Ironfront.InputBindings;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Units;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Repair controls (Milestone 009), bottom centre above the combat orders:
    /// <list type="bullet">
    /// <item>Vehicles selected: <b>Repair [R]</b> (to the pad with the shortest wait; right-click a pad to choose it),
    /// <b>Cancel repair</b>, and the repair state of the selection (queue positions, progress, or why nothing happens).</item>
    /// <item>One of your structures selected: the <b>Repair</b> toggle with health and credit rates; for a Repair Pad also its
    /// state, queue length and the vehicle in the bay with its progress.</item>
    /// </list>
    /// The pad used by the selected vehicles is highlighted. Installs itself in every match scene with a HUD (no scene
    /// rebuild needed).
    /// </summary>
    public class RepairHud : MonoBehaviour
    {
        private SelectionManager _selection;
        private UnitCommandController _commands;
        private RectTransform _panel;
        private RectTransform _ordersPanel;
        private Button _repair, _cancel, _toggle;
        private Text _toggleLabel, _status;
        private float _timer;
        private string _message = "";
        private float _messageTimer;
        private readonly HashSet<RepairPad> _highlighted = new HashSet<RepairPad>();

        public string StatusText => _status != null ? _status.text : "";
        public bool IsShown => _panel != null && _panel.gameObject.activeSelf;

        // ---------- Self-install ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Ensure();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

        /// <summary>Adds the panel to the scene's HUD canvas if the scene has the RTS HUD and no repair panel yet.</summary>
        public static RepairHud Ensure()
        {
            var existing = FindAnyObjectByType<RepairHud>();
            if (existing != null) return existing;
            var orders = FindAnyObjectByType<OrdersHud>();
            var commands = FindAnyObjectByType<UnitCommandController>();
            var selection = FindAnyObjectByType<SelectionManager>();
            if (orders == null || commands == null || selection == null) return null;
            var hud = orders.gameObject.AddComponent<RepairHud>(); // the HUD canvas
            hud._selection = selection;
            hud._commands = commands;
            return hud;
        }

        private void Start()
        {
            if (_selection == null) _selection = FindAnyObjectByType<SelectionManager>();
            if (_commands == null) _commands = FindAnyObjectByType<UnitCommandController>();
            var orders = transform.Find("OrdersPanel");
            _ordersPanel = orders != null ? (RectTransform)orders : null;
            Build();
            _commands.RepairOrdered += OnRepairOrdered;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_commands != null) _commands.RepairOrdered -= OnRepairOrdered;
            foreach (var p in _highlighted) if (p != null) p.Highlighted = false;
        }

        private void OnRepairOrdered(int sent, RepairPad pad, string message)
        {
            _message = message;
            _messageTimer = 4f;
            Refresh();
        }

        private void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("RepairPanel", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _panel = (RectTransform)go.transform;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0f);
            _panel.sizeDelta = new Vector2(560f, 66f);
            var bg = go.AddComponent<Image>();
            UiTheme.ApplyPanel(bg, UiTheme.Panel);
            bg.raycastTarget = true;
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(8, 8, 6, 4);
            v.spacing = 3f;
            v.childControlHeight = v.childControlWidth = true;
            v.childForceExpandHeight = false;

            var row = new GameObject("Buttons", typeof(RectTransform));
            row.layer = go.layer;
            row.transform.SetParent(go.transform, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = true;
            row.AddComponent<LayoutElement>().preferredHeight = 30f;

            _repair = MakeButton(row.transform, "Repair_Send", "Repair [R]", font, out _, () => _commands.IssueRepair());
            TooltipTrigger.Attach(_repair.gameObject, () =>
                "REPAIR  [R]\nSend the selected damaged vehicles to the Repair Pad with the shortest wait. Right-click a pad to choose it.\n" +
                "One vehicle at a time is repaired; the rest queue beside the pad. Costs credits per health restored.");
            _cancel = MakeButton(row.transform, "Repair_Cancel", "Cancel repair", font, out _, () => _commands.IssueCancelRepair());
            TooltipTrigger.Attach(_cancel.gameObject, () => "CANCEL REPAIR\nThe selected vehicles leave the repair queue and stop.");
            _toggle = MakeButton(row.transform, "Repair_Toggle", "Repair: OFF", font, out _toggleLabel, ToggleBuilding);
            TooltipTrigger.Attach(_toggle.gameObject, () =>
                "REPAIR STRUCTURE\nRestores health gradually, also under attack. Costs credits in proportion to the health restored; pauses without credits.");

            var statusGo = new GameObject("Status", typeof(RectTransform));
            statusGo.layer = go.layer;
            statusGo.transform.SetParent(go.transform, false);
            _status = statusGo.AddComponent<Text>();
            _status.font = font;
            _status.fontSize = 13;
            _status.color = UiTheme.Accent;
            _status.alignment = TextAnchor.MiddleCenter;
            _status.raycastTarget = false;
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusGo.AddComponent<LayoutElement>().preferredHeight = 22f;
        }

        private Button MakeButton(Transform parent, string name, string label, Font font, out Text text, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            UiTheme.StyleButton(button, image, UiTheme.Button);
            button.onClick.AddListener(onClick);
            var t = new GameObject("Label", typeof(RectTransform));
            t.layer = go.layer;
            t.transform.SetParent(go.transform, false);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            text = t.AddComponent<Text>();
            text.font = font;
            text.fontSize = 14;
            text.color = UiTheme.Text;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.text = label;
            return button;
        }

        private void Update()
        {
            var key = RtsInput.Repair;
            if (key != null && key.WasPressedThisFrame() && !RtsInputMode.WorldPointerBlocked && IsShown && _repair.gameObject.activeSelf)
                _commands.IssueRepair();
            if (_messageTimer > 0f) _messageTimer -= Time.unscaledDeltaTime;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;
            Refresh();
        }

        private int Player => MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;

        private Building SelectedOwnStructure()
        {
            var b = _selection != null ? _selection.SelectedStructure : null;
            return b != null && b.FactionId == Player && _selection.Selection.Count == 0 ? b : null;
        }

        private void ToggleBuilding()
        {
            var b = SelectedOwnStructure();
            if (b == null || b.Repair == null) return;
            if (!b.Repair.Toggle())
            {
                _message = "Nothing to repair: the structure is at full health";
                _messageTimer = 3f;
            }
            Refresh();
        }

        /// <summary>Recomputes what the panel shows (public for tests).</summary>
        public void Refresh()
        {
            if (_panel == null || _selection == null) return;
            var vehicles = _selection.Selection.Items.Where(u => u != null && RepairClient.IsRepairableVehicle(u, Player)).ToList();
            var structure = SelectedOwnStructure();
            bool show = vehicles.Count > 0 || structure != null;
            if (_panel.gameObject.activeSelf != show) _panel.gameObject.SetActive(show);
            UpdateHighlights(vehicles);
            if (!show) return;

            bool ordersShown = _ordersPanel != null && _ordersPanel.gameObject.activeInHierarchy;
            _panel.anchoredPosition = new Vector2(0f, ordersShown ? 92f : 10f);

            if (vehicles.Count > 0)
            {
                _repair.gameObject.SetActive(true);
                _toggle.gameObject.SetActive(false);
                int damaged = vehicles.Count(RepairClient.IsDamaged);
                bool anyBusy = vehicles.Any(u => RepairClient.Of(u) != null && RepairClient.Of(u).IsBusy);
                _repair.interactable = damaged > 0;
                _cancel.gameObject.SetActive(anyBusy);
                _status.text = _messageTimer > 0f ? _message : DescribeVehicles(vehicles, damaged);
                return;
            }

            _repair.gameObject.SetActive(false);
            _cancel.gameObject.SetActive(false);
            _toggle.gameObject.SetActive(true);
            var repair = structure.Repair;
            var hp = structure.GetComponent<Combat.Health>();
            bool damagedStructure = hp != null && hp.Current < hp.Max - 0.01f;
            _toggle.interactable = repair != null && (repair.IsOn || damagedStructure);
            _toggleLabel.text = repair != null && repair.IsOn ? "Repair: ON" : "Repair: OFF";
            var sb = new StringBuilder();
            if (repair != null)
            {
                switch (repair.State)
                {
                    case BuildingRepair.Status.Repairing:
                        sb.Append($"Repairing +{repair.HealthPerSecond:0} HP/s, about -{repair.CreditsPerSecond:0.0} credits/s");
                        break;
                    case BuildingRepair.Status.PausedNoCredits: sb.Append("Repair paused: insufficient credits"); break;
                    default: sb.Append(damagedStructure ? $"Damaged: full repair costs about {RepairCost(structure)} credits" : "Full health"); break;
                }
                if (repair.TotalCharged > 0) sb.Append($"  (spent {repair.TotalCharged})");
            }
            var pad = structure.GetComponent<RepairPad>();
            if (pad != null) sb.Insert(0, DescribePad(pad) + "\n");
            _status.text = _messageTimer > 0f ? _message : sb.ToString();
        }

        private static int RepairCost(Building b)
        {
            var s = Data.RepairSettings.Current;
            var hp = b.GetComponent<Combat.Health>();
            float per = RepairMeter.CostPerHealthFor(BuildingRepair.RepairValue(b.Definition, s), hp.Max, s.BuildingCostFraction, b.Definition.RepairCostMultiplier);
            return RepairMeter.CostFor(hp.Max - hp.Current, per);
        }

        public static string DescribePad(RepairPad pad)
        {
            string state = !pad.IsAssembled ? $"Assembling {pad.Assembly01 * 100f:0}%"
                : pad.State == RepairPad.Status.NoPower ? "No power: repairs paused"
                : pad.State == RepairPad.Status.NoCredits ? "Paused: insufficient credits"
                : pad.Queue.Active != RepairPadQueue.None ? "Occupied" : "Ready";
            var c = pad.ActiveClient;
            string bay = c != null ? $"  |  In bay: {c.Health.DisplayName} {pad.ActiveProgress * 100f:0}%" : "";
            return $"Repair Pad: {state}  |  Queue {pad.Queue.WaitingCount}{bay}  |  Repaired {pad.Completed}";
        }

        private static string DescribeVehicles(List<SelectableUnit> vehicles, int damaged)
        {
            if (RepairPad.CountFor(FactionOf(vehicles)) == 0) return damaged > 0 ? "No Repair Pad: build one (requires a Vehicle Factory)" : "Selected vehicles are at full health";
            var parts = new List<string>();
            int repairing = 0, queued = 0, travelling = 0;
            var positions = new List<int>();
            float progress = -1f;
            foreach (var u in vehicles)
            {
                var c = RepairClient.Of(u);
                if (c == null || !c.IsBusy) continue;
                switch (c.State)
                {
                    case RepairState.Repairing:
                    case RepairState.EnteringPad:
                        repairing++;
                        progress = c.Health.Current / c.Health.Max;
                        break;
                    case RepairState.TravellingToQueue:
                        travelling++;
                        if (c.QueuePosition >= 0) positions.Add(c.QueuePosition + 1);
                        break;
                    case RepairState.Waiting:
                        queued++;
                        if (c.QueuePosition >= 0) positions.Add(c.QueuePosition + 1);
                        break;
                }
            }
            if (repairing > 0) parts.Add(progress >= 0f ? $"repairing {progress * 100f:0}%" : "entering the bay");
            if (queued + travelling > 0) parts.Add($"{queued + travelling} queued (place {string.Join(", ", positions.OrderBy(p => p))})");
            if (parts.Count == 0) return damaged > 0 ? $"{damaged} damaged: press Repair [R] or right-click a Repair Pad" : "Selected vehicles are at full health";
            return "Repair: " + string.Join(", ", parts);
        }

        private static int FactionOf(List<SelectableUnit> units) => units.Count > 0 ? units[0].GetComponent<Combat.Health>().FactionId : 1;

        private void UpdateHighlights(List<SelectableUnit> vehicles)
        {
            var now = new HashSet<RepairPad>();
            foreach (var u in vehicles)
            {
                var c = RepairClient.Of(u);
                if (c != null && c.IsBusy && c.Pad != null) now.Add(c.Pad);
            }
            foreach (var p in _highlighted) if (p != null && !now.Contains(p)) p.Highlighted = false;
            foreach (var p in now) p.Highlighted = true;
            _highlighted.Clear();
            _highlighted.UnionWith(now);
        }
    }
}
