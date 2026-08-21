using System.Collections.Generic;
using MGSC;

namespace TradeShuttlePlanner
{
    /// <summary>
    /// Localisation lookups with honest fallbacks. Localization.Get hands back the key when
    /// a tag is missing, so every helper here checks for that rather than printing
    /// "spaceobject.foo.name" into the report.
    /// </summary>
    internal static class Names
    {
        private static readonly Dictionary<string, string> ItemCache = new Dictionary<string, string>();

        public static string Item(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) { return "?"; }
            if (ItemCache.TryGetValue(itemId, out var cached)) { return cached; }
            var name = Resolve("item." + itemId + ".name", itemId);
            ItemCache[itemId] = name;
            return name;
        }

        public static string Orbit(string spaceObjectId)
        {
            return Resolve("spaceobject." + spaceObjectId + ".name", spaceObjectId);
        }

        public static string Faction(string factionId)
        {
            return Resolve("faction." + factionId + ".name", factionId);
        }

        public static string Category(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
            {
                return Resolve("ui.tradeshuttle.category.none", "None");
            }
            return Resolve("ui.tradeshuttle.category." + categoryId, categoryId);
        }

        private static string Resolve(string tag, string fallback)
        {
            string value;
            try { value = Localization.Get(tag); }
            catch { return fallback; }
            if (string.IsNullOrEmpty(value) || value == tag) { return fallback; }
            return value;
        }
    }
}
