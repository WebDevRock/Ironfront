using System;
using Ironfront.Construction;
using Ironfront.Core.Construction;
using Ironfront.InputBindings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>Shows a tooltip (text produced on demand) while the pointer rests on this UI element.</summary>
    public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> Text;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (TooltipView.Current != null) TooltipView.Current.Show(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (TooltipView.Current != null) TooltipView.Current.Hide(this);
        }

        private void OnDisable()
        {
            if (TooltipView.Current != null) TooltipView.Current.Hide(this);
        }

        /// <summary>Adds (or updates) a tooltip on a UI object.</summary>
        public static TooltipTrigger Attach(GameObject target, Func<string> text)
        {
            var t = target.GetComponent<TooltipTrigger>();
            if (t == null) t = target.AddComponent<TooltipTrigger>();
            t.Text = text;
            return t;
        }
    }
}
