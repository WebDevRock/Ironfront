using UnityEngine;

namespace Ironfront.Match
{
    /// <summary>Marks which faction owns a unit or structure.</summary>
    [DisallowMultipleComponent]
    public class FactionMember : MonoBehaviour
    {
        [SerializeField] private int factionId = MatchController.DefaultPlayerFactionId;

        public int FactionId
        {
            get => factionId;
            set => factionId = value;
        }

        /// <summary>Owner of a GameObject. Objects without a FactionMember belong to the default player faction.</summary>
        public static int Of(Component c)
        {
            var member = c != null ? c.GetComponentInParent<FactionMember>() : null;
            return member != null ? member.factionId : MatchController.DefaultPlayerFactionId;
        }
    }
}
