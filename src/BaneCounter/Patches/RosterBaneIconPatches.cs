using System;
using System.Collections.Generic;
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

            var clone = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            clone.name = CloneName;
            clone.transform.SetAsLastSibling();

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

        private static readonly AccessTools.FieldRef<MercenaryPanel, MercenaryImplantsIcon> ImplantsIconField =
            AccessTools.FieldRefAccess<MercenaryPanel, MercenaryImplantsIcon>("_implantsIcon");

        private static readonly AccessTools.FieldRef<MercenaryPanel, TextMeshProUGUI> MercClassField =
            AccessTools.FieldRefAccess<MercenaryPanel, TextMeshProUGUI>("_mercClass");

        private static readonly AccessTools.FieldRef<MercenaryImplantsIcon, Image> IconImageField =
            AccessTools.FieldRefAccess<MercenaryImplantsIcon, Image>("_icon");

        private static readonly AccessTools.FieldRef<MercenaryImplantsIcon, Image> SelectionBorderField =
            AccessTools.FieldRefAccess<MercenaryImplantsIcon, Image>("_selectionBorder");
    }
}
