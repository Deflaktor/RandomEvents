using RoR2;
using System.Collections.Generic;
using System.Linq;

namespace RandomEvents
{
    // Lends players each other's items for an event and gives them back afterwards. Items gained while borrowing stay
    // with the player who gained them, and items used up while borrowing, e.g. Dio's Best Friend or scrap, are gone for
    // their owner. Hidden items and Prayer Beads, which keep their stats when removed, stay with their owner.
    public class ItemSwap
    {
        private class Loan
        {
            public CharacterMaster holder;
            public CharacterMaster owner;
            public int[] given;
        }
        private readonly List<Loan> loans = new List<Loan>();
        private readonly Dictionary<CharacterMaster, int[]> ownItems = new Dictionary<CharacterMaster, int[]>();

        private static bool IsSwappable(ItemIndex itemIndex)
        {
            var itemDef = ItemCatalog.GetItemDef(itemIndex);
            return itemDef != null && !itemDef.hidden && itemDef != DLC2Content.Items.ExtraStatsOnLevelUp;
        }

        // the swappable permanent items; temporary items, e.g. of other events, stay where they are
        private static int[] GetItems(Inventory inventory)
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

        private static void SetItems(Inventory inventory, int[] items)
        {
            var current = GetItems(inventory);
            for (int i = 0; i < items.Length; i++)
            {
                if (current[i] > items[i])
                {
                    inventory.RemoveItemPermanent((ItemIndex)i, current[i] - items[i]);
                }
            }
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] > current[i])
                {
                    inventory.GiveItemPermanent((ItemIndex)i, items[i] - current[i]);
                }
            }
        }

        // holders[i] gets the items of owners[i]; each player should be one holder and one owner
        public void Lend(IList<CharacterMaster> holders, IList<CharacterMaster> owners)
        {
            Return();
            foreach (var master in holders.Concat(owners).Distinct())
            {
                ownItems[master] = GetItems(master.inventory);
            }
            for (int i = 0; i < holders.Count; i++)
            {
                loans.Add(new Loan { holder = holders[i], owner = owners[i], given = ownItems[owners[i]] });
            }
            foreach (var loan in loans)
            {
                SetItems(loan.holder.inventory, loan.given);
            }
        }

        public void Return()
        {
            var finalItems = ownItems.ToDictionary(entry => entry.Key, entry => (int[])entry.Value.Clone());
            foreach (var loan in loans)
            {
                if (loan.holder == null || !finalItems.ContainsKey(loan.owner) || !finalItems.ContainsKey(loan.holder))
                {
                    continue;
                }
                var current = GetItems(loan.holder.inventory);
                for (int i = 0; i < current.Length; i++)
                {
                    var change = current[i] - loan.given[i];
                    if (change < 0)
                    {
                        finalItems[loan.owner][i] += change;
                    }
                    else
                    {
                        finalItems[loan.holder][i] += change;
                    }
                }
            }
            foreach (var (master, items) in finalItems)
            {
                if (master != null && master.inventory != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (items[i] < 0)
                        {
                            items[i] = 0;
                        }
                    }
                    SetItems(master.inventory, items);
                }
            }
            loans.Clear();
            ownItems.Clear();
        }
    }
}
