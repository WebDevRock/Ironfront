using UnityEngine;

namespace Ironfront.UI
{
    /// <summary>Generated interface sprites (Milestone 006): chamfered panels, buttons, bars and glyphs.</summary>
    [CreateAssetMenu(menuName = "Ironfront/UI Skin", fileName = "UiSkin")]
    public class UiSkin : ScriptableObject
    {
        [Tooltip("9-sliced panel with chamfered corners and a light rim.")]
        [SerializeField] private Sprite panel;
        [Tooltip("9-sliced button background.")]
        [SerializeField] private Sprite button;
        [Tooltip("9-sliced solid bar (progress, power).")]
        [SerializeField] private Sprite bar;
        [SerializeField] private Sprite creditsIcon;
        [SerializeField] private Sprite powerIcon;
        [SerializeField] private Sprite emblem;

        public Sprite Panel => panel;
        public Sprite Button => button;
        public Sprite Bar => bar;
        public Sprite CreditsIcon => creditsIcon;
        public Sprite PowerIcon => powerIcon;
        public Sprite Emblem => emblem;
    }
}
