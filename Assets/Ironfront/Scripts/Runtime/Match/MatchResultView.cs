using Ironfront.Core.Victory;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.Match
{
    /// <summary>Full-screen victory / defeat / draw panel with a restart button. Starts hidden.</summary>
    public class MatchResultView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text title;
        [SerializeField] private Text detail;
        [SerializeField] private Button restartButton;
        [SerializeField] private MatchFlowController flow;

        public bool IsShown => panel != null && panel.activeSelf;
        public string TitleText => title != null ? title.text : "";

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (restartButton != null) restartButton.onClick.AddListener(OnRestart);
        }

        public void Show(MatchOutcome outcome, int playerFaction, float duration)
        {
            if (panel == null) return;
            bool won = outcome.Result == MatchResult.Victory && outcome.WinnerFactionId == playerFaction;
            bool draw = outcome.Result == MatchResult.Draw;
            title.text = draw ? "DRAW" : won ? "VICTORY" : "DEFEAT";
            title.color = draw ? new Color(1f, 0.85f, 0.4f) : won ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.35f, 0.3f);
            string reason = outcome.Reason == EndReason.Exhaustion
                ? "Stalemate: no ore, credits or forces remain for either side to continue."
                : draw ? "Both headquarters were destroyed at the same moment."
                : won ? "The enemy headquarters has been destroyed." : "Your headquarters has been destroyed.";
            detail.text = $"{reason}\nMatch time {(int)(duration / 60)}:{(int)(duration % 60):00}";
            panel.SetActive(true);
        }

        private void OnRestart()
        {
            if (flow != null) flow.Restart();
        }
    }
}
