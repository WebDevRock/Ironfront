using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Construction;
using Ironfront.Core.Economy;
using Ironfront.Core.Production;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.Selection;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Sidebar with credits, power, the construction menu, unit production and the production queue.
    /// Buttons are generated from the match's data definitions. Values update from faction, queue and
    /// placement events; only the head item's progress bar is refreshed per frame.
    /// </summary>
    public class EconomyHud : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private BuildingPlacementController placement;
        [SerializeField] private SelectionManager selectionManager;
        [SerializeField] private Font font;
        [SerializeField] private float messageDuration = 4f;

        private static Color ButtonColor => UiTheme.Button;
        private static Color PlacingColor => UiTheme.ButtonActive;
        private static Color LowPowerColor => UiTheme.Danger;

        private class BuildEntry { public BuildingDefinition Def; public Button Button; public Text Label; public Image Image; }
        private class UnitEntry { public UnitDefinition Def; public Button Button; public Text Label; }
        private class QueueSlot { public Button Button; public Text Label; public RectTransform Fill; }

        private FactionState _faction;
        private VehicleFactory _factory;
        private Text _credits, _power, _factoryLabel, _message;
        private Image _powerFill;
        private RectTransform _unitRoot;
        private readonly List<BuildEntry> _buildEntries = new List<BuildEntry>();
        private readonly List<UnitEntry> _unitEntries = new List<UnitEntry>();
        private readonly List<QueueSlot> _queueSlots = new List<QueueSlot>();
        private float _messageTimer;

        public VehicleFactory ActiveFactory => _factory;

        private void Start()
        {
            var match = MatchController.Current;
            if (match == null || content == null)
            {
                Debug.LogError("[Ironfront] EconomyHud needs a MatchController and a content RectTransform.", this);
                enabled = false;
                return;
            }
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _faction = match.PlayerFaction;
            BuildLayout(match.Setup);

            _faction.Wallet.Changed += OnWalletChanged;
            _faction.Power.Changed += RefreshEconomy;
            _faction.Structures.Changed += RefreshEconomy;
            if (placement != null)
            {
                placement.Message += ShowMessage;
                placement.PlacementModeChanged += RefreshBuildButtons;
            }
            if (selectionManager != null) selectionManager.StructureSelectionChanged += OnStructureSelected;
            VehicleFactory.FactoriesChanged += BindFactory;
            Building.AnyRegistrationChanged += OnBuildingRegistration;
            OreDeposit.AnyDepleted += OnDepositDepleted;

            BindFactory();
            RefreshEconomy();
        }

        private void OnDestroy()
        {
            if (_faction != null)
            {
                _faction.Wallet.Changed -= OnWalletChanged;
                _faction.Power.Changed -= RefreshEconomy;
                _faction.Structures.Changed -= RefreshEconomy;
            }
            if (placement != null)
            {
                placement.Message -= ShowMessage;
                placement.PlacementModeChanged -= RefreshBuildButtons;
            }
            if (selectionManager != null) selectionManager.StructureSelectionChanged -= OnStructureSelected;
            VehicleFactory.FactoriesChanged -= BindFactory;
            Building.AnyRegistrationChanged -= OnBuildingRegistration;
            OreDeposit.AnyDepleted -= OnDepositDepleted;
            if (_factory != null) _factory.Queue.Changed -= RefreshQueue;
        }

        private void Update()
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.Hud.Auto();
            if (_factory != null && _factory.Queue.Count > 0 && _queueSlots.Count > 0)
            {
                SetFill(_queueSlots[0].Fill, _factory.Queue.HeadProgress01);
                string head = HeadLabel();
                if (_queueSlots[0].Label.text != head) _queueSlots[0].Label.text = head;
            }

            if (_messageTimer > 0f)
            {
                _messageTimer -= Time.unscaledDeltaTime;
                if (_messageTimer <= 0f) _message.text = "";
            }
        }

        // ---------- Event handlers ----------

        private void OnWalletChanged(int _) => RefreshEconomy();
        private void OnStructureSelected(Building _) => BindFactory();

        // Ownership is final once a building registers with its faction: re-check which factory is ours.
        private void OnBuildingRegistration(Building b)
        {
            if (b != null && b.GetComponent<VehicleFactory>() != null) BindFactory();
        }
        private void OnDepositDepleted(OreDeposit d) => ShowMessage("An ore deposit has been depleted");

        public void ShowMessage(string text)
        {
            if (_message == null) return;
            _message.text = text;
            _messageTimer = messageDuration;
        }

        private void RefreshEconomy()
        {
            _credits.text = $"Credits: {_faction.Wallet.Credits}";
            var p = _faction.Power;
            _power.text = p.IsLowPower
                ? $"Power: {p.Consumed} / {p.Produced}  LOW - production slowed"
                : $"Power: {p.Consumed} / {p.Produced}";
            _power.color = p.IsLowPower ? LowPowerColor : UiTheme.Text;
            if (_powerFill != null)
            {
                float load = p.Produced > 0 ? Mathf.Clamp01((float)p.Consumed / p.Produced) : (p.Consumed > 0 ? 1f : 0f);
                _powerFill.rectTransform.anchorMax = new Vector2(load, 1f);
                _powerFill.color = p.IsLowPower ? UiTheme.Danger : load > 0.85f ? UiTheme.Warning : UiTheme.Good;
            }
            RefreshBuildButtons();
            RefreshUnitButtons();
        }

        private void RefreshBuildButtons()
        {
            foreach (var e in _buildEntries)
            {
                var check = ConstructionRules.Check(e.Def, _faction);
                e.Button.interactable = check == BuildCheck.Ok;
                string suffix = check == BuildCheck.MissingPrerequisite ? "  (locked)" : "";
                e.Label.text = $"{e.Def.DisplayName}  ${e.Def.Cost}{suffix}";
                bool placingThis = placement != null && placement.Pending == e.Def;
                e.Image.color = placingThis ? PlacingColor : ButtonColor;
            }
        }

        private void RefreshUnitButtons()
        {
            foreach (var e in _unitEntries)
            {
                bool full = _factory == null || _factory.Queue.Count >= _factory.Queue.Capacity;
                e.Button.interactable = _factory != null && !full && _faction.Wallet.CanAfford(e.Def.Cost);
            }
        }

        private void BindFactory()
        {
            int player = MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;
            VehicleFactory next = null;
            if (selectionManager != null && selectionManager.SelectedStructure != null)
            {
                var selected = selectionManager.SelectedStructure.GetComponent<VehicleFactory>();
                if (selected != null && selected.FactionId == player) next = selected;
            }
            if (next == null && _factory != null && _factory.isActiveAndEnabled && _factory.FactionId == player) next = _factory;
            if (next == null) next = VehicleFactory.FirstOwnedBy(player);

            if (next != _factory)
            {
                if (_factory != null) _factory.Queue.Changed -= RefreshQueue;
                _factory = next;
                if (_factory != null) _factory.Queue.Changed += RefreshQueue;
                RebuildUnitButtons();
            }

            string factoryName = _factory == null ? null
                : _factory.Building.Definition != null ? _factory.Building.Definition.DisplayName : _factory.name;
            _factoryLabel.text = _factory != null ? $"Production: {factoryName}" : "Production: build a Vehicle Factory";
            RefreshQueue();
        }

        private void RefreshQueue()
        {
            var items = _factory != null ? _factory.Queue.Items : null;
            for (int i = 0; i < _queueSlots.Count; i++)
            {
                var slot = _queueSlots[i];
                bool used = items != null && i < items.Count;
                slot.Button.gameObject.SetActive(used);
                if (!used) continue;
                slot.Label.text = i == 0 ? HeadLabel() : $"{items[i].DisplayName}  (queued - click to cancel)";
                SetFill(slot.Fill, i == 0 ? _factory.Queue.HeadProgress01 : 0f);
            }
            RefreshUnitButtons();
        }

        /// <summary>"Heavy Tank  42%  6s" for the item in production (" PAUSED: low power" style hints included).</summary>
        private string HeadLabel()
        {
            var items = _factory.Queue.Items;
            if (items.Count == 0) return "";
            int pct = Mathf.FloorToInt(_factory.Queue.HeadProgress01 * 100f);
            float remaining = _factory.HeadSecondsRemaining;
            string time = float.IsInfinity(remaining) ? "waiting" : $"{Mathf.CeilToInt(remaining)}s";
            string slow = _faction != null && _faction.Power.IsLowPower ? "  LOW POWER" : "";
            return $"{items[0].DisplayName}  {pct}%  {time}{slow}";
        }

        private string BuildTooltip(BuildingDefinition def)
        {
            var check = ConstructionRules.Check(def, _faction);
            string power = def.PowerOutput > 0 ? $"Power +{def.PowerOutput}" : def.PowerDrain > 0 ? $"Power -{def.PowerDrain}" : "No power use";
            string status = check == BuildCheck.Ok ? "Click, then place on the map."
                : check == BuildCheck.MissingPrerequisite ? "Locked: requires " + Prerequisites(def)
                : check == BuildCheck.InsufficientFunds ? $"Need ${def.Cost - _faction.Wallet.Credits} more credits"
                : check.ToString();
            return $"{def.DisplayName.ToUpperInvariant()}   ${def.Cost}\nHealth {def.MaxHealth:0}  Armour {def.Armour:0}\n{power}\n{status}";
        }

        private static string Prerequisites(BuildingDefinition def)
        {
            var names = new List<string>();
            foreach (var p in def.Prerequisites) if (p != null) names.Add(p.DisplayName);
            return names.Count > 0 ? string.Join(", ", names) : "another structure";
        }

        private string UnitTooltip(UnitDefinition def)
        {
            string status = _factory == null ? "No factory"
                : _factory.Queue.Count >= _factory.Queue.Capacity ? "Queue full"
                : !_faction.Wallet.CanAfford(def.Cost) ? $"Need ${def.Cost - _faction.Wallet.Credits} more credits"
                : "Click to queue (cost is paid now, refunded on cancel)";
            var w = def.Weapon;
            string stats = $"Health {def.MaxHealth:0}  Armour {def.Armour:0}  Speed {def.MoveSpeed:0.#}" +
                           (w != null ? $"\nWeapon: {w.Damage:0} dmg / {w.Cooldown:0.#}s, range {w.Range:0}" : "\nUnarmed");
            return $"{def.DisplayName.ToUpperInvariant()}   ${def.Cost}   {def.BuildTime:0}s\n{stats}\n{status}";
        }

        private string QueueTooltip(int index)
        {
            if (_factory == null || index >= _factory.Queue.Count) return null;
            var item = _factory.Queue.Items[index];
            return $"{item.DisplayName}: click to cancel and refund ${item.Cost}";
        }

        // ---------- Layout ----------

        private void BuildLayout(MatchSetup setup)
        {
            var creditsRow = IconRow(content, "CreditsRow", UiTheme.Skin != null ? UiTheme.Skin.CreditsIcon : null, 30f);
            _credits = CreateText(creditsRow, "Credits", 20, FontStyle.Bold, 28);
            FitRowText(_credits, 30f);
            _credits.color = UiTheme.Accent;
            UiTheme.Shadow(_credits);
            var powerRow = IconRow(content, "PowerRow", UiTheme.Skin != null ? UiTheme.Skin.PowerIcon : null, 20f);
            _power = CreateText(powerRow, "Power", 14, FontStyle.Normal, 20);
            FitRowText(_power, 20f);
            _powerFill = PowerMeter(content);

            Header(content, "CONSTRUCT");
            if (setup != null)
            {
                foreach (var def in setup.ConstructionOptions)
                {
                    if (def == null || !def.Constructible) continue;
                    var captured = def;
                    var (button, label, image) = CreateButton(content, def.DisplayName, 34, () => OnBuildClicked(captured), def.Icon);
                    button.name = "Build_" + def.Id;
                    TooltipTrigger.Attach(button.gameObject, () => BuildTooltip(captured));
                    _buildEntries.Add(new BuildEntry { Def = def, Button = button, Label = label, Image = image });
                }
            }

            _factoryLabel = Header(content, "Production");
            _unitRoot = CreateGroup(content, "UnitButtons");

            Header(content, "QUEUE");
            int capacity = setup != null ? setup.ProductionQueueCapacity : 5;
            for (int i = 0; i < capacity; i++)
            {
                int index = i;
                var (button, label, _) = CreateButton(content, "", 24, () => OnQueueSlotClicked(index));
                button.name = "QueueSlot_" + i;
                TooltipTrigger.Attach(button.gameObject, () => QueueTooltip(index));
                label.fontSize = 12;
                var fill = CreateFill(button.transform);
                _queueSlots.Add(new QueueSlot { Button = button, Label = label, Fill = fill });
                button.gameObject.SetActive(false);
            }

            _message = CreateText(content, "", 13, FontStyle.Italic, 40);
            _message.color = UiTheme.Warning;
        }

        private void RebuildUnitButtons()
        {
            foreach (var e in _unitEntries) Destroy(e.Button.gameObject);
            _unitEntries.Clear();
            if (_factory == null || _factory.Building.Definition == null) return;

            foreach (var def in _factory.Building.Definition.ProducibleUnits)
            {
                if (def == null) continue;
                var captured = def;
                var (button, label, _) = CreateButton(_unitRoot, $"{def.DisplayName}  ${def.Cost}  ({def.BuildTime:0}s)", 34,
                    () => OnUnitClicked(captured), def.Icon);
                button.name = "Produce_" + def.Id;
                TooltipTrigger.Attach(button.gameObject, () => UnitTooltip(captured));
                _unitEntries.Add(new UnitEntry { Def = def, Button = button, Label = label });
            }
        }

        // ---------- Clicks ----------

        private void OnBuildClicked(BuildingDefinition def)
        {
            if (placement == null) return;
            if (placement.Pending == def) placement.CancelPlacement();
            else if (placement.BeginPlacement(def)) ShowMessage($"Place {def.DisplayName}: LMB to build, RMB/Esc to cancel");
        }

        private void OnUnitClicked(UnitDefinition def)
        {
            if (_factory == null) return;
            switch (_factory.Enqueue(def))
            {
                case EnqueueResult.Ok: ShowMessage($"{def.DisplayName} queued (-{def.Cost})"); break;
                case EnqueueResult.QueueFull: ShowMessage("Production queue is full"); break;
                case EnqueueResult.InsufficientFunds:
                    ShowMessage($"Insufficient credits for {def.DisplayName}");
                    if (NotificationCenter.Current != null)
                        NotificationCenter.Current.Post("Insufficient credits", NotificationCenter.Kind.Warning, "credits", 3f);
                    break;
            }
        }

        private void OnQueueSlotClicked(int index)
        {
            if (_factory == null || index >= _factory.Queue.Count) return;
            var item = _factory.Queue.Items[index];
            if (_factory.Cancel(index)) ShowMessage($"{item.DisplayName} cancelled (+{item.Cost})");
        }

        // ---------- Widget helpers ----------

        private Text CreateText(Transform parent, string text, int size, FontStyle style, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = UiTheme.Text;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return t;
        }

        private RectTransform CreateGroup(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return (RectTransform)go.transform;
        }

        private (Button, Text, Image) CreateButton(Transform parent, string text, float height, UnityEngine.Events.UnityAction onClick,
            Sprite icon = null)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            UiTheme.StyleButton(button, image, ButtonColor);
            button.onClick.AddListener(onClick);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            float textLeft = 8f;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.layer = gameObject.layer;
                iconGo.transform.SetParent(go.transform, false);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = new Vector2(0f, 0f);
                irt.anchorMax = new Vector2(0f, 1f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.offsetMin = new Vector2(3f, 2f);
                irt.offsetMax = new Vector2(3f + height * 1.2f, -2f);
                var img = iconGo.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
                textLeft = height * 1.2f + 10f;
            }

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.layer = gameObject.layer;
            labelGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)labelGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(textLeft, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            var label = labelGo.AddComponent<Text>();
            label.font = font;
            label.fontSize = 14;
            label.color = UiTheme.Text;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.text = text;
            return (button, label, image);
        }

        private static void SetFill(RectTransform fill, float progress01) =>
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress01), 1f);

        private RectTransform CreateFill(Transform parent)
        {
            var go = new GameObject("Progress", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            UiTheme.ApplyBar(img, UiTheme.Progress);
            img.raycastTarget = false;
            SetFill(rt, 0f);
            return rt;
        }

        /// <summary>Section heading: amber caps with a thin rule underneath.</summary>
        private Text Header(Transform parent, string text)
        {
            var t = CreateText(parent, text, 13, FontStyle.Bold, 24);
            t.color = UiTheme.Accent;
            t.alignment = TextAnchor.LowerLeft;
            var rule = new GameObject("Rule", typeof(RectTransform));
            rule.layer = gameObject.layer;
            rule.transform.SetParent(t.transform, false);
            var r = (RectTransform)rule.transform;
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(0f, -1f);
            var img = rule.AddComponent<Image>();
            img.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.35f);
            img.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// Row with a glyph on the left; the text created next (as the row's child) is stretched to the right of it
        /// by <see cref="FitRowText"/>.
        /// </summary>
        private Transform IconRow(Transform parent, string name, Sprite icon, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            if (icon != null)
            {
                var iconGo = new GameObject("Glyph", typeof(RectTransform));
                iconGo.layer = gameObject.layer;
                iconGo.transform.SetParent(go.transform, false);
                var r = (RectTransform)iconGo.transform;
                r.anchorMin = new Vector2(0f, 0.5f);
                r.anchorMax = new Vector2(0f, 0.5f);
                r.pivot = new Vector2(0f, 0.5f);
                r.sizeDelta = new Vector2(height * 0.8f, height * 0.8f);
                var img = iconGo.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            return go.transform;
        }

        private static void FitRowText(Text t, float height)
        {
            var r = t.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(height * 0.8f + 8f, 0f);
            r.offsetMax = Vector2.zero;
            t.alignment = TextAnchor.MiddleLeft;
        }

        /// <summary>Power load meter (consumed / produced) under the power text; returns the fill image.</summary>
        private Image PowerMeter(Transform parent)
        {
            var go = new GameObject("PowerMeter", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 6f;
            var bg = go.AddComponent<Image>();
            UiTheme.ApplyBar(bg, new Color(0f, 0f, 0f, 0.55f));
            bg.raycastTarget = false;
            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.layer = gameObject.layer;
            fillGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)fillGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(0f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            UiTheme.ApplyBar(fill, UiTheme.Good);
            fill.raycastTarget = false;
            return fill;
        }
    }
}
