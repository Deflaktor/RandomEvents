using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements.UIR;
using static Rewired.InputMapper;
using static Rewired.Utils.Classes.Utility.ObjectInstanceTracker;

namespace RandomEvents
{
    public class TemporaryInventory : MonoBehaviour
    {
        public Dictionary<ItemIndex, int> temporaryItems = new Dictionary<ItemIndex, int>();

        public static void Hook()
        {
            On.RoR2.LunarSunBehavior.TransformItem += LunarSunBehavior_TransformItem;
        }

        private static void LunarSunBehavior_TransformItem(On.RoR2.LunarSunBehavior.orig_TransformItem orig, LunarSunBehavior self)
        {
            if (self.body != null && self.body.inventory != null)
            {
                var temporaryInventories = self.body.gameObject.GetComponents<TemporaryInventory>();
                if (temporaryInventories.Length > 0)
                {
                    var temporaryCount = 0;
                    foreach (var tempInventory in temporaryInventories)
                    {
                        temporaryCount += tempInventory.temporaryItems.GetValueOrDefault(DLC1Content.Items.LunarSun.itemIndex);
                    }
                    if (self.body.inventory.GetItemCountChanneled(DLC1Content.Items.LunarSun.itemIndex) <= temporaryCount)
                    {
                        // do not transform items
                        return;
                    }
                }
            }
            orig(self);
        }

        public static TemporaryInventory Find(GameObject obj, TemporaryInventory candidate)
        {
            var inventories = obj.GetComponents<TemporaryInventory>();
            return inventories.FirstOrDefault(item => item == candidate);
        }

        public static TemporaryInventory Find(GameObject obj, List<TemporaryInventory> candidates)
        {
            var inventories = obj.GetComponents<TemporaryInventory>();
            return inventories.FirstOrDefault(item => candidates.Contains(item));
        }

        private void AddItemToDict(Dictionary<ItemIndex, int> items, ItemIndex item, int count = 1)
        {
            if (count == 0) return;
            if (items.ContainsKey(item))
            {
                items[item] = items[item] + count;
            }
            else
            {
                items.Add(item, count);
            }
            if (items[item] == 0)
            {
                items.Remove(item);
            }
        }

        public void GiveTemporaryItem(ItemIndex itemIndex, int count = 1)
        {
            if(count <= 0)
            {
                return;
            }
            if (TryGetComponent(out CharacterBody body))
            {
                body.inventory.GiveItemChanneled(itemIndex, count);
                AddItemToDict(temporaryItems, itemIndex, count);
            }
        }

        public void RemoveTemporaryItem(ItemIndex itemIndex, int count = 1)
        {
            if(!temporaryItems.ContainsKey(itemIndex)) {
                return;
            }
            if(count > temporaryItems[itemIndex])
            {
                count = temporaryItems[itemIndex];
            }
            if (count <= 0)
            {
                return;
            }
            if (TryGetComponent(out CharacterBody body))
            {
                var has = body.inventory.GetItemCountChanneled(itemIndex);
                if(has >= count)
                {
                    body.inventory.RemoveItemChanneled(itemIndex, count);
                    AddItemToDict(temporaryItems, itemIndex, -count);
                }
                else
                {
                    body.inventory.RemoveItemChanneled(itemIndex, has);
                    AddItemToDict(temporaryItems, itemIndex, -has);
                }
            }
        }

        private void OnDisable()
        {
            if (TryGetComponent(out CharacterBody body))
            {
                foreach (var (itemIndex, count) in temporaryItems)
                {
                    body.inventory.RemoveItemChanneled(itemIndex, count);
                }
            }
        }
    }
}
