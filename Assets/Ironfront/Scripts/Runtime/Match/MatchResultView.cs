using System.Text;
using Ironfront.Core;
using Ironfront.Core.Victory;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.Match
{
    /// <summary>Full-screen victory / defeat / draw panel with match statistics and a restart button. Starts hidden.</summary>
    public class MatchResultView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text title;
        [SerializeField] private Text detail;
        [SerializeField] private Text statLabels;
        [SerializeField] private Text statPlayer;
        [SerializeField] private Text statEnemy;
        [SerializeField] private Button restartButton;
        [SerializeField] private MatchFlowController flow;

        public bool IsShown => panel != null && panel.activeSelf;
        public string TitleText => title != null ? title.text : "";
        public string StatisticsText =>
            statLabels != null ? statLabels.text + "|" + statPlayer.text + "|" + statEnemy.text : "";

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
            ShowStatistics(playerFaction);
            panel.SetActive(true);
        }

        private void ShowStatistics(int playerFaction)
        {
            var tracker = MatchStatisticsTracker.Current;
            if (statLabels == null || statPlayer == null || statEnemy == null) return;
            if (tracker == null)
            {
                statLabels.text = statPlayer.text = statEnemy.text = "";
                return;
            }

            int enemyFaction = -1;
            if (MatchController.Current != null)
                foreach (var f in MatchController.Current.Factions)
                    if (f.Id != playerFaction) { enemyFaction = f.Id; break; }

            var p = tracker.Stats.For(playerFaction);
            var e = tracker.Stats.For(enemyFaction);
            statLabels.text = Rows("", "Units produced", "Units lost", "Enemy units destroyed", "Structures built",
                "Structures lost", "Enemy structures destroyed", "Ore harvested");
            statPlayer.text = Column("YOU", p);
            statEnemy.text = Column("ENEMY", e);
        }

        private static string Column(string header, MatchStatistics.FactionStats s) =>
            Rows(header, s.UnitsProduced.ToString(), s.UnitsLost.ToString(), s.EnemyUnitsDestroyed.ToString(),
                s.StructuresBuilt.ToString(), s.StructuresLost.ToString(), s.EnemyStructuresDestroyed.ToString(), s.OreHarvested.ToString());

        private static string Rows(params string[] rows)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(rows[i]);
            }
            return sb.ToString();
        }

        private void OnRestart()
        {
            if (flow != null) flow.Restart();
        }
    }
}
