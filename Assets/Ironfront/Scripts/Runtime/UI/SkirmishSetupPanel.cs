using System;
using System.Collections.Generic;
using Ironfront.Core.Skirmish;
using Ironfront.Data;
using Ironfront.Skirmish;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Skirmish setup screen in the main menu (Milestone 008): pick a battlefield from the catalog (preview, description),
    /// then cycle the computer difficulty, starting credits, start location and victory condition. Only options that
    /// exist are offered (difficulties come from the catalog). Start stores the settings in
    /// <see cref="SkirmishSession"/> and loads the map; the screen pre-selects the last choice next time.
    /// </summary>
    public class SkirmishSetupPanel : MonoBehaviour
    {
        [Serializable]
        public class Selector
        {
            public Button previous;
            public Button next;
            public Text value;
            public Text hint;
        }

        [SerializeField] private MapCatalog catalog;
        [SerializeField] private GameObject panel;
        [SerializeField] private List<Button> mapButtons = new List<Button>();
        [SerializeField] private RawImage preview;
        [SerializeField] private Text mapName;
        [SerializeField] private Text mapTagline;
        [SerializeField] private Text mapDescription;
        [SerializeField] private Selector difficulty = new Selector();
        [SerializeField] private Selector resources = new Selector();
        [SerializeField] private Selector start = new Selector();
        [SerializeField] private Selector victory = new Selector();
        [SerializeField] private Button startButton;
        [SerializeField] private Button backButton;

        private SkirmishSettings _settings;
        private readonly List<DifficultyLevel> _difficulties = new List<DifficultyLevel>();

        /// <summary>Raised when the player leaves the screen with Back.</summary>
        public event Action Closed;

        public bool IsOpen => panel != null && panel.activeSelf;
        public SkirmishSettings Current => _settings?.Clone();
        public MapCatalog Catalog => catalog;

        private void Awake()
        {
            if (catalog != null)
                foreach (var d in catalog.Difficulties)
                    if (d != null && !_difficulties.Contains(d.Level)) _difficulties.Add(d.Level);
            _difficulties.Sort();

            for (int i = 0; i < mapButtons.Count; i++)
            {
                int index = i;
                if (mapButtons[i] != null) mapButtons[i].onClick.AddListener(() => SelectMap(index));
            }
            Hook(difficulty, d => Cycle(_difficulties, _settings.Difficulty, d, v => _settings.Difficulty = v));
            Hook(resources, d => _settings.Resources = CycleEnum(_settings.Resources, d));
            Hook(start, d => _settings.StartSlot = CycleStart(_settings.StartSlot, d));
            Hook(victory, d => _settings.Victory = CycleEnum(_settings.Victory, d));
            if (startButton != null) startButton.onClick.AddListener(StartMatch);
            if (backButton != null) backButton.onClick.AddListener(Close);
            if (panel != null) panel.SetActive(false);
        }

        public void Open()
        {
            _settings = SkirmishSession.LastChosen.Sanitised(catalog != null ? catalog.HasMap : null, FirstMapId());
            if (_difficulties.Count > 0 && !_difficulties.Contains(_settings.Difficulty)) _settings.Difficulty = _difficulties[0];
            if (panel != null) panel.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
            Closed?.Invoke();
        }

        public void SelectMap(int index)
        {
            if (catalog == null || index < 0 || index >= catalog.Maps.Count || catalog.Maps[index] == null) return;
            if (_settings.MapId != catalog.Maps[index].Id) _settings.StartSlot = SkirmishSettings.RandomStart; // slots differ per map
            _settings.MapId = catalog.Maps[index].Id;
            Refresh();
        }

        private MapDefinition SelectedMap => catalog != null && _settings != null ? catalog.FindMap(_settings.MapId) : null;

        /// <summary>Random, then each of the selected map's start slots.</summary>
        private int CycleStart(int current, int step)
        {
            var map = SelectedMap;
            int count = map != null ? map.StartNames.Count : 0;
            int options = count + 1; // index 0 = Random
            int i = current < 0 || current >= count ? 0 : current + 1;
            i = (i + step + options) % options;
            return i == 0 ? SkirmishSettings.RandomStart : i - 1;
        }

        /// <summary>Applies a whole configuration (tests and tools); unknown values are sanitised.</summary>
        public void Apply(SkirmishSettings settings)
        {
            _settings = (settings ?? SkirmishSettings.Default).Sanitised(catalog != null ? catalog.HasMap : null, FirstMapId());
            Refresh();
        }

        public void StartMatch()
        {
            var map = catalog != null ? catalog.FindMap(_settings.MapId) : null;
            if (map == null || string.IsNullOrEmpty(map.SceneName))
            {
                Debug.LogError($"[Ironfront] Skirmish map '{_settings.MapId}' is not in the catalog.");
                return;
            }
            SkirmishSession.Launch(_settings, map.SceneName);
        }

        private string FirstMapId()
        {
            if (catalog != null)
                foreach (var m in catalog.Maps)
                    if (m != null) return m.Id;
            return SkirmishSettings.DefaultMapId;
        }

        private void Hook(Selector s, Action<int> change)
        {
            if (s == null) return;
            if (s.previous != null) s.previous.onClick.AddListener(() => { change(-1); Refresh(); });
            if (s.next != null) s.next.onClick.AddListener(() => { change(1); Refresh(); });
        }

        private static void Cycle<T>(List<T> options, T current, int step, Action<T> set)
        {
            if (options.Count == 0) return;
            int i = Mathf.Max(0, options.IndexOf(current));
            set(options[(i + step + options.Count) % options.Count]);
        }

        private static T CycleEnum<T>(T current, int step) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            int i = Array.IndexOf(values, current);
            return values[((i < 0 ? 0 : i) + step + values.Length) % values.Length];
        }

        private void Refresh()
        {
            if (_settings == null) return;
            var map = catalog != null ? catalog.FindMap(_settings.MapId) : null;
            if (mapName != null) mapName.text = map != null ? map.DisplayName.ToUpperInvariant() : _settings.MapId;
            if (mapTagline != null) mapTagline.text = map != null ? map.Tagline : "";
            if (mapDescription != null) mapDescription.text = map != null ? map.Description : "";
            if (preview != null)
            {
                preview.texture = map != null ? map.Preview : null;
                preview.color = preview.texture != null ? Color.white : new Color(0.1f, 0.1f, 0.1f, 1f);
            }
            for (int i = 0; i < mapButtons.Count; i++)
            {
                if (mapButtons[i] == null || catalog == null || i >= catalog.Maps.Count) continue;
                bool selected = catalog.Maps[i] != null && catalog.Maps[i].Id == _settings.MapId;
                var img = mapButtons[i].GetComponent<Image>();
                if (img != null) img.color = selected ? new Color(0.55f, 0.42f, 0.18f, 1f) : new Color(0.22f, 0.24f, 0.26f, 1f); // dark tiles: light text stays readable
            }

            var diff = catalog != null ? catalog.FindDifficulty(_settings.Difficulty) : null;
            Set(difficulty, diff != null ? diff.DisplayName : _settings.Difficulty.ToString(), diff != null ? diff.Summary : "");
            Set(resources, SkirmishSettings.Describe(_settings.Resources), "Credits each side starts with.");
            int slots = map != null ? map.StartNames.Count : 0;
            if (_settings.StartSlot >= slots) _settings.StartSlot = SkirmishSettings.RandomStart;
            Set(start, _settings.StartSlot < 0 ? "Random" : $"{_settings.StartSlot + 1}: {map.StartNames[_settings.StartSlot]}",
                $"{slots} possible starts (numbered on the map). The enemy's start is hidden: scout to find it.");
            Set(victory, _settings.Victory == VictoryMode.Headquarters ? "Destroy headquarters" : "Annihilation",
                _settings.Victory == VictoryMode.Headquarters
                    ? "Win by destroying the enemy headquarters. Forward Command Centres do not replace it."
                    : "Win by destroying every enemy headquarters, Forward Command Centre, factory and armed unit. A side that can no longer produce is revealed.");
        }

        private static void Set(Selector s, string value, string hint)
        {
            if (s == null) return;
            if (s.value != null) s.value.text = value;
            if (s.hint != null) s.hint.text = hint;
        }
    }
}
