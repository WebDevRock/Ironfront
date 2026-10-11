using Ironfront.InputBindings;
using Ironfront.Match;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// In-match menu (F10 or the MENU button): pauses the game and offers Resume, Options (volume), Restart and
    /// Main Menu. Restores the previous time scale on resume.
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        public const string MainMenuScene = "MainMenu";

        [SerializeField] private GameObject panel;
        [SerializeField] private GameObject options;
        [SerializeField] private Button openButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button optionsButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button optionsBackButton;

        private float _previousScale = 1f;

        public bool IsOpen => panel != null && panel.activeSelf;

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (options != null) options.SetActive(false);
            if (openButton != null) openButton.onClick.AddListener(Toggle);
            if (resumeButton != null) resumeButton.onClick.AddListener(Close);
            if (optionsButton != null) optionsButton.onClick.AddListener(() => ShowOptions(true));
            if (optionsBackButton != null) optionsBackButton.onClick.AddListener(() => ShowOptions(false));
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ToMainMenu);
        }

        private void Update()
        {
            var key = RtsInput.GameMenu;
            if (key != null && key.WasPressedThisFrame()) Toggle();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (panel == null || IsOpen) return;
            var flow = MatchFlowController.Current;
            if (flow != null && flow.IsOver) return; // the result screen owns the end of the match
            _previousScale = Time.timeScale;
            Time.timeScale = 0f;
            panel.SetActive(true);
            ShowOptions(false);
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            Time.timeScale = _previousScale <= 0f ? 1f : _previousScale;
        }

        private void ShowOptions(bool show)
        {
            if (options != null) options.SetActive(show);
        }

        private void Restart()
        {
            panel.SetActive(false);
            Time.timeScale = 1f;
            var flow = MatchFlowController.Current;
            if (flow != null) flow.Restart();
            else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void ToMainMenu()
        {
            Time.timeScale = 1f;
            RtsInputMode.ResetAll();
            SceneManager.LoadScene(MainMenuScene);
        }
    }
}
