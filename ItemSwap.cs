using RoR2;
using System.Collections.Generic;
using System.Linq;

namespace RandomEvents
{
    // Lends players each other's items for an event. Nobody's own items change: each player gets temporary (channeled)
    // stacks of the difference between the borrowed items and their own, negative where they have more, which the
    // effective item counts add up. The game only uses up, scraps or prints permanent stacks, so whatever happens to
    // items during the event happens to the player's own ones, and returning means taking the temporary stacks back.
    // Some items still stay with their owner, see IsSwappable.
    public class ItemSwap
    {
        // the temporary stacks each player got
        private readonly Dictionary<CharacterMaster, int[]> differences = new Dictionary<CharacterMaster, int[]>();

        private static HashSet<ItemIndex> kept;

        public static bool IsSwappable(ItemIndex itemIndex)
        {
            var itemDef = ItemCatalog.GetItemDef(itemIndex);
            return itemDef != null && !itemDef.hidden && !GetKeptItems().Contains(itemIndex);
        }

        // the items that stay with their owner
        private static HashSet<ItemIndex> GetKeptItems()
        {
            if (kept != null)
            {
                return kept;
            }
            kept = new HashSet<ItemIndex>();
            foreach (var itemDef in ItemCatalog.allItemDefs)
            {
                // items that get used up, e.g. Dio's Best Friend or Sale Star, with their used up versions: borrowed,
                // they couldn't be used up, and the owner's own still could
                if (itemDef.name.EndsWith("Consumed"))
                {
                    kept.Add(itemDef.itemIndex);
                    var itemIndex = ItemCatalog.FindItemIndex(itemDef.name.Substring(0, itemDef.name.Length - "Consumed".Length));
                    if (itemIndex != ItemIndex.None)
                    {
                        kept.Add(itemIndex);
                    }
                }
                // the pages of the Cursed Book, which are used up as well
                if (itemDef.name.StartsWith("Page"))
                {
                    kept.Add(itemDef.itemIndex);
                }
            }
            foreach (var itemDef in new[]
            {
                // keeps its stats when removed
                DLC2Content.Items.ExtraStatsOnLevelUp,
                // turns the holder's own items into more of itself
                DLC1Content.Items.LunarSun,
                // the equipment capacity counts them, so equipment beyond it would drop to the ground or lose charges
                DLC3Content.Items.ExtraEquipment,
                RoR2Content.Items.EquipmentMagazine,
                // their allies would die with the owner and come anew for the holder
                RoR2Content.Items.BeetleGland,
                RoR2Content.Items.RoboBallBuddy,
            })
            {
                if (itemDef != null)
                {
                    kept.Add(itemDef.itemIndex);
                }
            }
            return kept;
        }

        // the swappable permanent items; temporary items, e.g. of other events, stay where they are
        public static int[] GetItems(Inventory inventory)
        {
            var stacks = new int[ItemCatalog.itemCount];
            inventory.WriteAllPermanentItemStacks(stacks);
            for (int i = 0; i < stacks.Length; i++)
            {
                if (!IsSwappable((ItemIndex)i))
                {
                    stacks[i] = 0;
                }
            }
            return stacks;
        }

        // holders[i] gets the items of owners[i]; each player should be one holder and one owner
        public void Lend(IList<CharacterMaster> holders, IList<CharacterMaster> owners)
        {
            Return();
            var ownItems = holders.Concat(owners).Distinct().ToDictionary(master => master, master => GetItems(master.inventory));
            for (int i = 0; i < holders.Count; i++)
            {
                var own = ownItems[holders[i]];
                var borrowed = ownItems[owners[i]];
                var difference = new int[own.Length];
                for (int j = 0; j < own.Length; j++)
                {
                    difference[j] = borrowed[j] - own[j];
                    if (difference[j] != 0)
                    {
                        holders[i].inventory.GiveItemChanneled((ItemIndex)j, difference[j]);
                    }
                }
                differences[holders[i]] = difference;
            }
        }

        public void Return()
        {
            foreach (var (master, difference) in differences)
            {
                if (master == null || master.inventory == null)
                {
                    continue;
                }
                for (int i = 0; i < difference.Length; i++)
                {
                    if (difference[i] != 0)
                    {
                        master.inventory.GiveItemChanneled((ItemIndex)i, -difference[i]);
                    }
                }
            }
            differences.Clear();
        }
    }
}
