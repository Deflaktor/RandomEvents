using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RandomEvents
{
    public static class SafeItems
    {
        private static readonly ItemTier[] Tiers =
        {
            ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss, ItemTier.Lunar,
            ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss
        };

        // the run's items that are fine for monsters to have: none that could kill players unfairly, see the
        // setting Unsafe Items, and none the monsters' AI can't use
        public static List<ItemIndex> Get()
        {
            var unsafeItems = new HashSet<string>(ModConfig.UnsafeItems.Value.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0), StringComparer.OrdinalIgnoreCase);
            return ItemCatalog.allItemDefs.Where(itemDef => itemDef != null && !itemDef.hidden && Tiers.Contains(itemDef.tier)
                    && (Run.instance == null || Run.instance.availableItems.Contains(itemDef.itemIndex))
                    && !itemDef.ContainsTag(ItemTag.AIBlacklist) && !unsafeItems.Contains(itemDef.name))
                .Select(itemDef => itemDef.itemIndex).ToList();
        }
    }
}
