using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BaneCounter.Patches
{
    /// <summary>
    /// Puts a Bane icon with the level on it into each row of the Manage Operators list.
    ///
    /// The roster already answers "what is this clone carrying / what class / how many
    /// augments / is it healing" at a glance, and Bane belongs in that set: it is the
    /// number that decides who you can afford to send. Without it you have to open the
    /// Skull Project screen one operator at a time.
    ///
    /// The icon is a clone of the existing implants diamond rather than a new object built
    /// from scratch. Those icons are prefab-authored - their images, sprites and sizes are
    /// serialized data this mod cannot see or reproduce - so cloning inherits the row's
    /// layout, anchoring and scale for free and stays correct if the prefab is restyled in
    /// a patch. Building one by hand would mean hardcoding a guess at the game's UI metrics
    /// and having it drift.
    /// </summary>
    [HarmonyPatch(typeof(MercenaryPanel))]
    public static class RosterBaneIconPatches
    {
        private const string CloneName = "BaneCounter_BaneIcon";

        private static readonly Dictionary<int, GameObject> _icons = new Dictionary<int, GameObject>();

        private static bool _failed;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(MercenaryPanel.Initialize))]
        public static void InitializePostfix(MercenaryPanel __instance, Mercenary mercenary)
        {
            if (!Config.ShowRosterIcon || _failed || __instance == null)
            {
                return;
            }

            try
            {
                Refresh(__instance, mercenary);
            }
            catch (Exception e)
            {
                // One failure per session, then stand down. This is cosmetic; it must never
                // take the roster screen down with it, and it must not log once per row.
                _failed = true;
                Debug.LogError("[" + ModEntry.ModId + "] roster icon disabled after error: " + e);
            }
        }

        private static void Refresh(MercenaryPanel panel, Mercenary mercenary)
        {
            var curse = mercenary == null ? null : mercenary.CurseData;
            var show = curse != null
                       && !string.IsNullOrEmpty(curse.CurrentBramfatura)
                       && (curse.CurseLevel > 0 || Config.ShowRosterIconAtZero);

            var icon = GetOrCreateIcon(panel);
            if (icon == null)
            {
                return;
            }

            // Panels are pooled and reused for whoever scrolls into the row, so the
            // not-applicable case has to hide explicitly rather than just return.
            icon.SetActive(show);
            if (!show)
            {
                return;
            }

            var label = icon.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = curse.CurseLevel.ToString();
            }

            // Rebound every refresh: rows are pooled, so the icon outlives the operator it
            // was last showing and would otherwise report a stale clone's Bane.
            var tooltip = icon.GetComponent<BaneRosterIconTooltip>();
            if (tooltip != null)
            {
                tooltip.Bind(mercenary);
            }
        }

        private static GameObject GetOrCreateIcon(MercenaryPanel panel)
        {
            var key = panel.GetInstanceID();

            GameObject existing;
            if (_icons.TryGetValue(key, out existing) && existing != null)
            {
                return existing;
            }

            var source = ImplantsIconField(panel);
            if (source == null)
            {
                return null;
            }

            DescribeLayoutOnce(panel, source.transform.parent);

            var clone = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            clone.name = CloneName;
            clone.transform.SetAsLastSibling();

            EnsureRoomForOneMoreIcon(source.transform.parent as RectTransform, clone.transform as RectTransform);

            // Read the image references off the clone's own component before destroying it -
            // they are serialized fields, so this is the only way to reach them by name.
            var cloneIcon = clone.GetComponent<MercenaryImplantsIcon>();
            Image iconImage = null;
            Image selectionBorder = null;
            if (cloneIcon != null)
            {
                iconImage = IconImageField(cloneIcon);
                selectionBorder = SelectionBorderField(cloneIcon);

                // The clone must not behave like an implants icon: it would show the augment
                // tooltip, raise OnClicked into the panel's implants handler, and register
                // as a controller navigation target.
                UnityEngine.Object.DestroyImmediate(cloneIcon);
            }

            // The source object is named "NoImplantsButton"; if it carries a Button it would
            // still swallow clicks and show a pressed state on an icon that does nothing.
            var button = clone.GetComponent<Button>();
            if (button != null)
            {
                UnityEngine.Object.DestroyImmediate(button);
            }

            if (selectionBorder != null)
            {
                selectionBorder.gameObject.SetActive(false);
            }

            if (iconImage != null)
            {
                var sprite = Data.TooltipIcons.GetSpriteByTag(BaneInfo.IconTag);
                if (sprite != null)
                {
                    iconImage.sprite = sprite;
                }
            }

            AddLabel(panel, clone);
            clone.AddComponent<BaneRosterIconTooltip>();

            _icons[key] = clone;
            return clone;
        }

        /// <summary>
        /// Adds the number over the icon, borrowing the row's own class-label font so the
        /// text matches the screen without this mod shipping or naming a font asset.
        /// </summary>
        private static void AddLabel(MercenaryPanel panel, GameObject clone)
        {
            var template = MercClassField(panel);
            if (template == null)
            {
                return;
            }

            var host = new GameObject("BaneCounter_Value", typeof(RectTransform));
            host.transform.SetParent(clone.transform, false);

            var rect = host.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var label = host.AddComponent<TextMeshProUGUI>();
            label.font = template.font;
            label.fontSharedMaterial = template.fontSharedMaterial;
            label.fontSize = template.fontSize * Config.RosterLabelScale;
            label.color = Colors.White;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.enableWordWrapping = false;

            // The diamond is small and the number can reach four digits, so let it shrink
            // rather than clip or spill outside the icon.
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = template.fontSize * Config.RosterLabelScale;
        }

        /// <summary>
        /// Describes the icon row once, so the next change can be made against the actual
        /// prefab layout instead of a guess. Screenshots show the clone landing outside the
        /// row's right edge, which says the container is fixed-width - but "which container,
        /// laid out by what" is exactly the part the decompiled source cannot answer.
        /// </summary>
        /// <summary>
        /// Widens the 'Statuses' container so a fifth icon fits, growing it leftwards so the
        /// strip stays flush with the row's right edge.
        ///
        /// Measured from the game: the container is 100 wide with a HorizontalLayoutGroup at
        /// spacing 2, holding four 22px icons - 4x22 + 3x2 = 94. It is full. A fifth icon
        /// needs 118, so vanilla's width leaves the clone hanging past the panel border where
        /// the scroll rect clips it, which is the sliver visible in the row.
        ///
        /// Everything here is read from the live objects rather than hardcoded to those
        /// numbers, so a restyle in a game patch resizes correctly instead of silently
        /// reintroducing the clipping.
        /// </summary>
        private static void EnsureRoomForOneMoreIcon(RectTransform container, RectTransform icon)
        {
            if (container == null || icon == null)
            {
                return;
            }

            // sizeDelta only means "width" when the anchors are a point rather than a
            // stretch. The roster container is anchored to the row's right edge, so this
            // holds - but a stretched container would need no resizing anyway.
            if (container.anchorMin != container.anchorMax)
            {
                return;
            }

            var spacing = 0f;
            var group = container.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (group != null)
            {
                spacing = group.spacing;
            }

            // Measured from the children that are actually there, including the clone, which
            // makes this idempotent: run it twice and the second call finds the container
            // already wide enough and does nothing. Deriving the requirement from the current
            // width instead would grow the strip by 24px every time it ran.
            var required = 0f;
            var count = 0;
            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i) as RectTransform;
                if (child == null)
                {
                    continue;
                }

                required += child.rect.width;
                count++;
            }

            if (count > 1)
            {
                required += spacing * (count - 1);
            }

            var delta = required - container.rect.width;
            if (delta <= 0f)
            {
                return;
            }

            // Keep the right edge where it is: the icon strip is visually anchored to the
            // row's border, and the free space is on the left, next to the operator's name.
            container.sizeDelta = new Vector2(container.sizeDelta.x + delta, container.sizeDelta.y);
            container.anchoredPosition = new Vector2(
                container.anchoredPosition.x - (1f - container.pivot.x) * delta,
                container.anchoredPosition.y);
        }

        private static void DescribeLayoutOnce(MercenaryPanel panel, Transform parent)
        {
            if (!Config.DebugLayout || _described || parent == null)
            {
                return;
            }

            _described = true;

            var sb = new StringBuilder();
            sb.AppendLine("roster row layout:");
            Describe(sb, "  panel  ", panel.transform);
            Describe(sb, "  parent ", parent);

            foreach (var component in parent.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                sb.Append("    parent component ").AppendLine(component.GetType().Name);

                var group = component as HorizontalOrVerticalLayoutGroup;
                if (group != null)
                {
                    sb.Append("      spacing=").Append(group.spacing)
                      .Append(" childForceExpandWidth=").Append(group.childForceExpandWidth)
                      .Append(" childControlWidth=").Append(group.childControlWidth)
                      .Append(" padding=").AppendLine(group.padding.ToString());
                }
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                Describe(sb, "    child " + i + " '" + child.name + "' active=" + child.gameObject.activeSelf + " ", child);

                var element = child.GetComponent<LayoutElement>();
                if (element != null)
                {
                    sb.Append("      LayoutElement preferredWidth=").Append(element.preferredWidth)
                      .Append(" minWidth=").Append(element.minWidth)
                      .Append(" flexibleWidth=").AppendLine(element.flexibleWidth.ToString());
                }
            }

            ModEntry.Log(sb.ToString());
        }

        private static void Describe(StringBuilder sb, string label, Transform transform)
        {
            var rect = transform as RectTransform;
            sb.Append(label).Append("'").Append(transform.name).Append("'");
            if (rect != null)
            {
                sb.Append(" rect=").Append(rect.rect.width).Append("x").Append(rect.rect.height)
                  .Append(" sizeDelta=").Append(rect.sizeDelta)
                  .Append(" anchoredPos=").Append(rect.anchoredPosition)
                  .Append(" anchors=").Append(rect.anchorMin).Append("..").Append(rect.anchorMax);
            }

            sb.AppendLine();
        }

        private static bool _described;

        private static readonly AccessTools.FieldRef<MercenaryPanel, MercenaryImplantsIcon> ImplantsIconField =            AccessTools.FieldRefAccess<MercenaryPanel, MercenaryImplantsIcon>("_implantsIcon");

        private static readonly AccessTools.FieldRef<MercenaryPanel, TextMeshProUGUI> MercClassField =
            AccessTools.FieldRefAccess<MercenaryPanel, TextMeshProUGUI>("_mercClass");

        private static readonly AccessTools.FieldRef<MercenaryImplantsIcon, Image> IconImageField =
            AccessTools.FieldRefAccess<MercenaryImplantsIcon, Image>("_icon");

        private static readonly AccessTools.FieldRef<MercenaryImplantsIcon, Image> SelectionBorderField =
            AccessTools.FieldRefAccess<MercenaryImplantsIcon, Image>("_selectionBorder");
    }
}
