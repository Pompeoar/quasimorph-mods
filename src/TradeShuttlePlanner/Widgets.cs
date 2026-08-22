using System;
using HarmonyLib;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TradeShuttlePlanner
{
    /// <summary>
    /// A pointer sink for a hand-built cell or panel. The item-slot prefab we clone for the goods
    /// and hold grids has its own ItemSlot behaviour ripped out (it would try to drag and would
    /// clear the icon on enable), so clicks and hovers have to be re-introduced with a component
    /// of our own rather than by re-using ItemSlot's events.
    /// </summary>
    internal sealed class CellInput : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action OnClick;
        public Action OnEnter;

        public void OnPointerClick(PointerEventData e)
        {
            if (e != null && e.button != PointerEventData.InputButton.Left) { return; }
            try { OnClick?.Invoke(); } catch (Exception ex) { Debug.LogError("[TradeShuttlePlanner] cell click: " + ex.Message); }
        }

        public void OnPointerEnter(PointerEventData e)
        {
            try { OnEnter?.Invoke(); } catch (Exception ex) { Debug.LogError("[TradeShuttlePlanner] cell enter: " + ex.Message); }
        }

        public void OnPointerExit(PointerEventData e)
        {
            // No per-cell exit behaviour: the highlighted item's name stays shown until another
            // cell is entered, which reads better than flickering the label empty on every exit.
        }
    }

    /// <summary>
    /// A re-pointable click handler for a pooled button. CommonButton.OnClick is an event, so it
    /// cannot be cleared from outside; instead we subscribe this relay once and swap its delegate
    /// on every refresh, so reusing a pooled row never stacks stale handlers.
    /// </summary>
    internal sealed class ButtonRelay : MonoBehaviour
    {
        public Action Handler;
        public void Fire()
        {
            try { Handler?.Invoke(); } catch (Exception e) { Debug.LogError("[TradeShuttlePlanner] button: " + e.Message); }
        }
    }

    /// <summary>An icon cell built from the game's item-slot prefab: native background sprite,
    /// native icon material, a stack-count label, and our own click/hover handler.</summary>
    internal sealed class Cell
    {
        public GameObject Root;
        public Image Icon;
        public TextMeshProUGUI Count;
        public CellInput Input;
        public Image Highlight;

        public void SetActive(bool on) { if (Root != null) { Root.SetActive(on); } }
    }

    /// <summary>
    /// Everything about cloning native widgets and, crucially, making the clones behave. Two game
    /// behaviours actively fight a naive clone:
    ///
    ///  * <see cref="LocalizableLabel"/> re-applies its serialized <c>_label</c> tag in Start,
    ///    OnLangChanged and (via <see cref="CommonButton"/>) OnEnable, so a caption set with
    ///    SetRawCaption silently reverts to "UNLOAD ALL". Clearing the label tag on both the button
    ///    and the label is what makes a set caption stick.
    ///  * <see cref="HotkeyButton"/> owns a <see cref="GameKeyPanel"/> that it re-initialises on
    ///    enable and on every input-mode change, re-spawning the pooled "G" key glyph each time.
    ///    Disabling the glyph does nothing because it is rebuilt; the only durable fix is to strip
    ///    the HotkeyButton behaviour entirely and delete the panel.
    /// </summary>
    internal static class Widgets
    {
        internal static CommonButton CloneButton(CommonButton template, Transform parent, string name)
        {
            if (template == null) { return null; }
            var go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.transform.SetAsLastSibling();
            var button = Sanitize(go);
            return button;
        }

        /// <summary>
        /// Removes the hotkey glyph from a cloned button. If the clone is a HotkeyButton it is
        /// replaced by a plain CommonButton carrying the same visuals, because a HotkeyButton left
        /// in place re-spawns its key glyph on every enable. Returns the CommonButton to drive.
        ///
        /// CONTRACT: pass only a GameObject you have just Instantiated. This destroys components
        /// and child objects outright, and the game's UI views are created once at startup and
        /// never rebuilt, so doing this to an original would break that widget for the rest of
        /// the session with no way back short of restarting.
        /// </summary>
        internal static CommonButton Sanitize(GameObject go)
        {
            if (go == null) { return null; }

            var iconTab = go.GetComponent<IconTabButton>();
            var hotkeyButton = go.GetComponent<HotkeyButton>();
            CommonButton button;

            if (hotkeyButton != null && iconTab == null)
            {
                button = SwapToPlainButton(go, hotkeyButton);
            }
            else
            {
                button = go.GetComponent<CommonButton>();
                if (hotkeyButton != null)
                {
                    // An IconTabButton requires its CommonButton, so we cannot swap it out; clearing
                    // the key id at least stops it from re-binding a real glyph.
                    Traverse.Create(hotkeyButton).Field("_keyId").SetValue(string.Empty);
                }
            }

            // Kill any key-glyph panels. With the HotkeyButton gone (or its id cleared) nothing
            // rebuilds them, so the panel object can be deleted outright.
            foreach (var panel in go.GetComponentsInChildren<GameKeyPanel>(true))
            {
                Traverse.Create(panel).Field("_keyId").SetValue(string.Empty);
                if (hotkeyButton == null || iconTab == null)
                {
                    UnityEngine.Object.DestroyImmediate(panel.gameObject);
                }
                else
                {
                    panel.gameObject.SetActive(false);
                }
            }

            return button;
        }

        private static CommonButton SwapToPlainButton(GameObject go, HotkeyButton hotkey)
        {
            // Snapshot the serialized look before the behaviour is destroyed.
            var t = Traverse.Create(hotkey);
            var background = hotkey.background;
            var captionText = hotkey.captionText;
            var captionLabel = hotkey.CaptionLabel;
            var normalBg = hotkey.normalBgSprite;
            var hoverBg = hotkey.hoverBgSprite;
            var pressedBg = hotkey.pressedBgSprite;
            var disabledBg = hotkey.disabledBgSprite;
            var normalCol = hotkey.normalCaptionColor;
            var hoverCol = hotkey.hoverCaptionColor;
            var pressedCol = hotkey.pressedCaptionColor;
            var disabledCol = hotkey.disabledCaptionColor;

            UnityEngine.Object.DestroyImmediate(hotkey);

            var button = go.AddComponent<CommonButton>();
            button.background = background;
            button.captionText = captionText;
            button.normalBgSprite = normalBg;
            button.hoverBgSprite = hoverBg;
            button.pressedBgSprite = pressedBg;
            button.disabledBgSprite = disabledBg;
            button.normalCaptionColor = normalCol;
            button.hoverCaptionColor = hoverCol;
            button.pressedCaptionColor = pressedCol;
            button.disabledCaptionColor = disabledCol;

            var bt = Traverse.Create(button);
            bt.Field("_captionLabel").SetValue(captionLabel);
            bt.Field("_captionTag").SetValue(string.Empty);
            bt.Field("_interactable").SetValue(true);

            if (background != null && normalBg != null) { background.sprite = normalBg; }
            return button;
        }

        /// <summary>
        /// Sets a cloned button's caption so it actually renders and stays put. SetRawCaption clears
        /// the button's own tag, but the LocalizableLabel keeps a private tag that its own Start and
        /// language-change handlers re-apply; clearing that too is what stops the caption reverting.
        /// </summary>
        /// <summary>Points a button at an action, re-pointable across refreshes without stacking
        /// handlers. Use for pooled buttons (rows, tabs) that are reused.</summary>
        internal static void SetClick(CommonButton button, Action action)
        {
            if (button == null) { return; }
            var relay = button.gameObject.GetComponent<ButtonRelay>();
            if (relay == null)
            {
                relay = button.gameObject.AddComponent<ButtonRelay>();
                button.OnClick += (b, c) => relay.Fire();
            }
            relay.Handler = action;
        }

        internal static void SetCaption(CommonButton button, string text)
        {
            if (button == null) { return; }
            try
            {
                var label = button.CaptionLabel;
                if (label != null)
                {
                    Traverse.Create(label).Field("_label").SetValue(string.Empty);
                    label.SetRawText(text);
                }
                Traverse.Create(button).Field("_captionTag").SetValue(string.Empty);
            }
            catch (Exception e) { Debug.LogError("[TradeShuttlePlanner] set caption: " + e.Message); }
            if (button.captionText != null) { button.captionText.text = text; }
        }

        /// <summary>
        /// Builds an icon cell from the shared item-slot prefab. The ItemSlot behaviour is stripped
        /// (it drags items and blanks the icon on enable), and its incidental child widgets
        /// (durability/usability bars, status glyphs) are hidden, leaving a clean background+icon
        /// cell that inherits the game's own sprites and font.
        /// </summary>
        internal static Cell CreateCell(Transform parent, string name, float size)
        {
            var prefab = TryGetItemSlotPrefab();
            if (prefab == null) { return null; }

            var go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = name;

            var slot = go.GetComponent<ItemSlot>();
            Image icon = null;
            Image background = null;
            TextMeshProUGUI count = null;
            Sprite normalBg = null;
            if (slot != null)
            {
                var st = Traverse.Create(slot);
                icon = slot.Icon;
                background = st.Field("_background").GetValue<Image>();
                count = st.Field("_count").GetValue<TextMeshProUGUI>();
                normalBg = st.Field("_normalBgSprite").GetValue<Sprite>();
                HideChild(st.Field("_durabilityBar").GetValue<Component>());
                HideChild(st.Field("_usabilityBar").GetValue<Component>());
                HideChild(st.Field("_statusIcon").GetValue<Component>());
                HideChild(st.Field("_modifiedIcon").GetValue<Component>());
                HideChild(st.Field("_statusText").GetValue<Component>());
                var hover = st.Field("_hoverBorder").GetValue<GameObject>();
                if (hover != null) { hover.SetActive(false); }
                UnityEngine.Object.DestroyImmediate(slot);
            }

            if (background != null && normalBg != null) { background.sprite = normalBg; background.enabled = true; }

            var rt = go.transform as RectTransform;
            if (rt != null) { rt.sizeDelta = new Vector2(size, size); }

            if (icon != null)
            {
                var irt = icon.rectTransform;
                irt.anchorMin = new Vector2(0f, 0f);
                irt.anchorMax = new Vector2(1f, 1f);
                irt.offsetMin = new Vector2(3f, 3f);
                irt.offsetMax = new Vector2(-3f, -3f);
                icon.raycastTarget = false;
                icon.preserveAspect = true;
                icon.enabled = false;
            }

            if (count != null)
            {
                var crt = count.rectTransform;
                crt.anchorMin = new Vector2(0f, 0f);
                crt.anchorMax = new Vector2(1f, 0f);
                crt.offsetMin = new Vector2(0f, 0f);
                crt.offsetMax = new Vector2(-2f, 12f);
                count.alignment = TextAlignmentOptions.BottomRight;
                count.raycastTarget = false;
                count.text = string.Empty;
            }

            // A selection ring on top of the background, hidden until the cell is chosen.
            Image highlight = null;
            if (background != null)
            {
                var hgo = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
                hgo.transform.SetParent(go.transform, false);
                var hrt = hgo.transform as RectTransform;
                hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
                hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
                hrt.SetAsLastSibling();
                highlight = hgo.GetComponent<Image>();
                highlight.sprite = background.sprite;
                highlight.type = Image.Type.Sliced;
                highlight.color = new Color(0.35f, 0.85f, 0.85f, 0.5f);
                highlight.raycastTarget = false;
                highlight.enabled = false;
            }

            if (background != null) { background.raycastTarget = true; }
            var input = go.AddComponent<CellInput>();

            return new Cell { Root = go, Icon = icon, Count = count, Input = input, Highlight = highlight };
        }

        private static void HideChild(Component c)
        {
            if (c != null && c.gameObject != null) { c.gameObject.SetActive(false); }
        }

        private static GameObject TryGetItemSlotPrefab()
        {
            try
            {
                var pool = UI.Pools?.ItemSlots;
                return pool != null ? pool.prefab : null;
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] item-slot prefab unavailable: " + e.Message);
                return null;
            }
        }

        internal static Sprite ResolveIcon(BasePickupItem item)
        {
            if (item == null) { return null; }
            try
            {
                var desc = item.View<ItemContentDescriptor>();
                if (desc == null) { return null; }
                return SingletonMonoBehaviour<ItemFactory>.Instance.ResolveIcon(desc, Mathf.Max(1, item.InventoryWidthSize));
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] resolve icon: " + e.Message);
                return null;
            }
        }
    }
}
