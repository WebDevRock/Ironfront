using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Title screen: Start Match, Options (volume) and Quit, over a slowly drifting view of an Ironfront
    /// diorama. Loads <see cref="matchScene"/> to start.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private string matchScene = "RTS_Match";
        [SerializeField] private Button startButton;
        [SerializeField] private Button optionsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button optionsBackButton;
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject optionsPanel;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float driftDegreesPerSecond = 2.5f;

        private void Awake()
        {
            Time.timeScale = 1f;
            if (startButton != null) startButton.onClick.AddListener(StartMatch);
            if (optionsButton != null) optionsButton.onClick.AddListener(() => ShowOptions(true));
            if (optionsBackButton != null) optionsBackButton.onClick.AddListener(() => ShowOptions(false));
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            ShowOptions(false);
        }

        private void Update()
        {
            if (cameraPivot != null) cameraPivot.Rotate(0f, driftDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.World);
        }

        public void StartMatch()
        {
            Ironfront.InputBindings.RtsInputMode.ResetAll();
            SceneManager.LoadScene(matchScene);
        }

        private void ShowOptions(bool show)
        {
            if (optionsPanel != null) optionsPanel.SetActive(show);
            if (mainPanel != null) mainPanel.SetActive(!show);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
