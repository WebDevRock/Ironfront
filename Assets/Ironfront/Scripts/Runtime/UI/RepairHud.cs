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
    /// <para>Milestone 010 adds <b>Sell</b> (with confirmation for critical structures), <b>Cancel sale</b>, and for a
    /// selected Mobile Command Vehicle <b>Deploy [T]</b> / <b>Cancel deploy</b> with the site check and progress.</para>
    /// </summary>
    public class RepairHud : MonoBehaviour
    {
        private SelectionManager _selection;
        private UnitCommandController _commands;
        private RectTransform _panel;
        private RectTransform _ordersPanel;
        private Button _repair, _cancel, _toggle;
        private Button _sell, _cancelSale, _deploy, _cancelDeploy;
        private Text _toggleLabel, _status, _sellLabel;
        private Building _confirmTarget;
        private float _confirmUntil;
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
            _sell = MakeButton(row.transform, "Sell", "Sell", font, out _sellLabel, SellSelected);
            TooltipTrigger.Attach(_sell.gameObject, () =>
                "SELL\nDismantles the structure over a few seconds and refunds up to half its value, in proportion to its remaining health. " +
                "Queued units are refunded in full. Can be cancelled until it completes. The Headquarters cannot be sold.");
            _cancelSale = MakeButton(row.transform, "Sell_Cancel", "Cancel sale", font, out _, CancelSale);
            TooltipTrigger.Attach(_cancelSale.gameObject, () => "CANCEL SALE\nStop dismantling; the structure keeps working. Nothing is charged.");
            _deploy = MakeButton(row.transform, "Deploy", "Deploy [T]", font, out _, DeploySelected);
            TooltipTrigger.Attach(_deploy.gameObject, () =>
                "DEPLOY  [T]\nThe Mobile Command Vehicle stops and unfolds into a Forward Command Centre where it stands (15 s, vulnerable). " +
                "The centre grants new construction territory. Allowed outside your territory, not inside known enemy territory.");
            _cancelDeploy = MakeButton(row.transform, "Deploy_Cancel", "Cancel deploy", font, out _, CancelDeploy);
            TooltipTrigger.Attach(_cancelDeploy.gameObject, () => "CANCEL DEPLOY\nFold back up; the vehicle can drive again.");

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
            var deployKey = RtsInput.Deploy;
            if (deployKey != null && deployKey.WasPressedThisFrame() && !RtsInputMode.WorldPointerBlocked && IsShown && _deploy.gameObject.activeSelf)
                DeploySelected();
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

        // ---------- Milestone 010: sales and deployment ----------

        private void Say(string message, float seconds = 3f)
        {
            _message = message;
            _messageTimer = seconds;
        }

        /// <summary>Sell button: critical structures need a second click within the confirmation window.</summary>
        public void SellSelected()
        {
            var b = SelectedOwnStructure();
            if (b == null || b.Sale == null) return;
            if (!b.Sale.CanSell(out string why)) { Say(why); Refresh(); return; }
            if (b.Sale.NeedsConfirmation && (_confirmTarget != b || Time.unscaledTime > _confirmUntil))
            {
                _confirmTarget = b;
                _confirmUntil = Time.unscaledTime + Data.TerritorySettings.Current.ConfirmSeconds;
                Say($"{b.Definition.DisplayName} is critical: click Confirm to sell it for about {b.Sale.EstimatedRefund} credits");
                Refresh();
                return;
            }
            _confirmTarget = null;
            Say(b.Sale.Begin(out string reason) ? $"{reason}: refund about {b.Sale.EstimatedRefund} credits" : reason);
            Refresh();
        }

        public void CancelSale()
        {
            var b = SelectedOwnStructure();
            if (b != null && b.Sale != null && b.Sale.Cancel()) Say("Sale cancelled");
            Refresh();
        }

        private List<MobileCommandVehicle> SelectedVehicles()
        {
            var list = new List<MobileCommandVehicle>();
            if (_selection == null) return list;
            foreach (var u in _selection.Selection.Items)
            {
                if (u == null) continue;
                var m = u.GetComponent<MobileCommandVehicle>();
                if (m != null && m.FactionId == Player) list.Add(m);
            }
            return list;
        }

        /// <summary>Deploy button / [T]: every selected Mobile Command Vehicle tries to deploy where it stands.</summary>
        public void DeploySelected()
        {
            int started = 0;
            var reasons = new List<string>();
            foreach (var m in SelectedVehicles())
            {
                if (m.IsDeploying) continue;
                var r = m.Deploy();
                if (r == Core.Construction.DeployCheck.Valid) started++;
                else reasons.Add(Core.Construction.DeployProcess.Describe(r));
            }
            if (started > 0) Say($"Deploying: {started} Forward Command Centre{(started == 1 ? "" : "s")} in {Data.TerritorySettings.Current.DeploySeconds:0} s");
            else if (reasons.Count > 0) { Say("Cannot deploy here: " + reasons[0]); Audio.GameAudio.PlayFlat(Audio.SoundId.UiError); }
            Refresh();
        }

        public void CancelDeploy()
        {
            int n = 0;
            foreach (var m in SelectedVehicles()) if (m.CancelDeploy()) n++;
            if (n > 0) Say("Deployment cancelled");
            Refresh();
        }

        private string DescribeSale(Building b)
        {
            var sale = b.Sale;
            if (sale == null) return "";
            if (sale.IsDismantling) return $"Selling: {sale.Progress01 * 100f:0}%, {sale.SecondsRemaining:0} s left. Refund about {sale.EstimatedRefund} credits";
            return b.Definition.Sellable ? $"Sell value about {sale.EstimatedRefund} credits" : "Cannot be sold";
        }

        private string DescribeDeploy(List<MobileCommandVehicle> mcvs)
        {
            var deploying = mcvs.FirstOrDefault(m => m.IsDeploying);
            if (deploying != null) return $"Deploying: {deploying.Progress01 * 100f:0}%. The vehicle cannot move and can be attacked";
            var overlay = FindAnyObjectByType<TerritoryOverlay>();
            var check = overlay != null && overlay.PreviewVehicle != null ? overlay.DeployPreview : mcvs[0].CheckSite();
            return check == Core.Construction.DeployCheck.Valid
                ? "Deploy here [T]: the footprint is clear (the dashed ring shows the new territory)"
                : "Cannot deploy here: " + Core.Construction.DeployProcess.Describe(check);
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

            var mcvs = SelectedVehicles();
            bool deploying = mcvs.Any(m => m.IsDeploying);
            _deploy.gameObject.SetActive(vehicles.Count > 0 && mcvs.Count > 0 && !deploying);
            _cancelDeploy.gameObject.SetActive(vehicles.Count > 0 && deploying);
            _sell.gameObject.SetActive(vehicles.Count == 0);
            if (vehicles.Count > 0)
            {
                _cancelSale.gameObject.SetActive(false);
                _repair.gameObject.SetActive(true);
                _toggle.gameObject.SetActive(false);
                int damaged = vehicles.Count(RepairClient.IsDamaged);
                bool anyBusy = vehicles.Any(u => RepairClient.Of(u) != null && RepairClient.Of(u).IsBusy);
                _repair.interactable = damaged > 0;
                _cancel.gameObject.SetActive(anyBusy);
                _status.text = _messageTimer > 0f ? _message
                    : mcvs.Count > 0 && (deploying || damaged == 0 || mcvs.Count == vehicles.Count) ? DescribeDeploy(mcvs)
                    : DescribeVehicles(vehicles, damaged);
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
            // Milestone 010: sale state.
            var sale = structure.Sale;
            bool selling = sale != null && sale.IsDismantling;
            _cancelSale.gameObject.SetActive(selling);
            _sell.gameObject.SetActive(!selling);
            _sell.interactable = structure.Definition.Sellable;
            bool armed = sale != null && _confirmTarget == structure && Time.unscaledTime <= _confirmUntil;
            _sellLabel.text = armed ? $"Confirm sale (+{sale.EstimatedRefund})" : "Sell";
            if (selling) { _toggle.interactable = false; sb.Clear(); }
            sb.Insert(0, DescribeSale(structure) + (sb.Length > 0 ? "\n" : ""));
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
