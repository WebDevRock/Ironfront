using UnityEngine;

namespace Ironfront.UI
{
    /// <summary>Publishes the scene's <see cref="UiSkin"/> to <see cref="UiTheme"/> before any HUD builds its widgets.</summary>
    [DefaultExecutionOrder(-1000)]
    public class UiSkinProvider : MonoBehaviour
    {
        [SerializeField] private UiSkin skin;

        private void Awake()
        {
            if (skin != null) UiTheme.Skin = skin;
        }
    }
}
