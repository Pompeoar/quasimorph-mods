using MGSC;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BaneCounter
{
    /// <summary>
    /// Hover behaviour for the roster Bane diamond.
    ///
    /// The clone loses its tooltip when the MercenaryImplantsIcon component is destroyed -
    /// that component was what handled pointer events - so the icon needs its own. Modelled
    /// on MercenaryImplantsIcon's own enter/exit pair, including the _createdTooltip latch,
    /// so a tooltip is never left on screen when the pointer leaves or the row is recycled.
    /// </summary>
    public class BaneRosterIconTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Mercenary _mercenary;
        private bool _createdTooltip;

        public void Bind(Mercenary mercenary)
        {
            _mercenary = mercenary;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_createdTooltip || _mercenary == null || _mercenary.CurseData == null)
            {
                return;
            }

            var factory = SingletonMonoBehaviour<TooltipFactory>.Instance;
            if (factory == null)
            {
                return;
            }

            _createdTooltip = true;

            var tooltip = factory.BuildEmptyTooltip(wide: false, red: true);
            tooltip.SetCaption1(Localization.Get("ui.label.curse"), factory.FirstLetterRedColor);
            tooltip.SetCaption2(Localization.Get("ui.label.pact_use_effect"));
            BaneInfo.AddTooltipRows(factory, _mercenary.CurseData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_createdTooltip)
            {
                return;
            }

            _createdTooltip = false;
            SingletonMonoBehaviour<TooltipFactory>.Instance.HideTooltip();
        }

        private void OnDisable()
        {
            OnPointerExit(null);
            _mercenary = null;
        }
    }
}
