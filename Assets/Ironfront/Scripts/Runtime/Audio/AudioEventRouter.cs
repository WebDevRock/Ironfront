using Ironfront.Buildings;
using Ironfront.Commands;
using Ironfront.Construction;
using Ironfront.Core.Victory;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.UI;
using UnityEngine;

namespace Ironfront.Audio
{
    /// <summary>
    /// Connects existing gameplay events to sounds without touching the systems that raise them: unit
    /// acknowledgements (select / move / attack), placement errors, unit ready, ore delivered, alerts and the
    /// victory or defeat stinger. Only the human player's events are voiced.
    /// </summary>
    public class AudioEventRouter : MonoBehaviour
    {
        [SerializeField] private SelectionManager selection;
        [SerializeField] private UnitCommandController commands;
        [SerializeField] private BuildingPlacementController placement;
        [SerializeField] private NotificationCenter notifications;

        private int _lastSelectionCount;
        private MatchFlowController _flow;

        private static int PlayerFaction => MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;

        private void OnEnable()
        {
            if (selection != null) selection.Selection.Changed += OnSelectionChanged;
            if (commands != null)
            {
                commands.MoveOrdered += OnMove;
                commands.AttackMoveOrdered += OnAttackMove;
                commands.AttackOrdered += OnAttack;
            }
            if (placement != null) placement.Message += OnPlacementMessage;
            if (notifications != null) notifications.Posted += OnNotification;
            VehicleFactory.AnyUnitProduced += OnUnitProduced;
            Refinery.AnyDelivered += OnDelivered;
        }

        private void Start()
        {
            _flow = MatchFlowController.Current;
            if (_flow != null) _flow.MatchEnded += OnMatchEnded;
        }

        private void OnDisable()
        {
            if (selection != null) selection.Selection.Changed -= OnSelectionChanged;
            if (commands != null)
            {
                commands.MoveOrdered -= OnMove;
                commands.AttackMoveOrdered -= OnAttackMove;
                commands.AttackOrdered -= OnAttack;
            }
            if (placement != null) placement.Message -= OnPlacementMessage;
            if (notifications != null) notifications.Posted -= OnNotification;
            VehicleFactory.AnyUnitProduced -= OnUnitProduced;
            Refinery.AnyDelivered -= OnDelivered;
            if (_flow != null) _flow.MatchEnded -= OnMatchEnded;
        }

        private void OnSelectionChanged()
        {
            int count = selection.Selection.Count;
            if (count > 0 && count != _lastSelectionCount) GameAudio.PlayFlat(SoundId.SelectAck);
            _lastSelectionCount = count;
        }

        private void OnMove(Vector3 p) => GameAudio.PlayFlat(SoundId.MoveAck);
        private void OnAttackMove(Vector3 p) => GameAudio.PlayFlat(SoundId.AttackAck);
        private void OnAttack(Combat.Health target) => GameAudio.PlayFlat(SoundId.AttackAck);
        private void OnPlacementMessage(string message) => GameAudio.PlayFlat(SoundId.UiError);

        private void OnNotification(string text, NotificationCenter.Kind kind)
        {
            if (kind == NotificationCenter.Kind.Danger) GameAudio.PlayFlat(SoundId.Alert);
        }

        private void OnUnitProduced(VehicleFactory factory, GameObject unit)
        {
            if (factory != null && factory.FactionId == PlayerFaction) GameAudio.PlayFlat(SoundId.UnitReady);
        }

        private void OnDelivered(Refinery refinery, int ore)
        {
            if (refinery != null && refinery.FactionId == PlayerFaction) GameAudio.Play(SoundId.Delivery, refinery.DockPoint);
        }

        private void OnMatchEnded(MatchOutcome outcome)
        {
            bool won = outcome.Result == MatchResult.Victory && outcome.WinnerFactionId == PlayerFaction;
            GameAudio.PlayFlat(won ? SoundId.Victory : SoundId.Defeat);
        }
    }
}
