using BepInEx.Configuration;
using HG;
using RoR2;
using RoR2.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;
using static RoR2.HoldoutZoneController;
using static RoR2.SpawnCard;

namespace RandomEvents
{
    public class EventItemZone : AbstractEvent
    {
        private AsyncOperationHandle<GameObject> LowerPricedChestsGlow;
        private List<ItemZoneComponent> ItemZones = new List<ItemZoneComponent>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                var waveController = infiniteTowerRun.safeWardController;
                return waveController != null;
            }
            return false;
        }
        public override string GetEventConfigName()
        {
            return "ItemZone";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_ITEM_ZONE");
        }
        public override string GetDescription()
        {
            return "Spawns item zones which grant players standing in them temporary items.";
        }
        public override string GetConditionDescription()
        {
            return "Simulacrum mode only";
        }
        protected override void AddConfig(ConfigFile config)
        {

        }

        public override void Preload()
        {
            LowerPricedChestsGlow = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Items/LowerPricedChests/LowerPricedChestsGlow.prefab");
        }

        public override void Hook()
        {
            
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            ItemZones.Clear();
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                var safeWardController = infiniteTowerRun.safeWardController;
                if(safeWardController != null)
                {
                    var distance = safeWardController.holdoutZoneController.baseRadius;
                    int itemZoneCount = 4;
                    var objs = Helper.ApproximatePlacement(LowerPricedChestsGlow.WaitForCompletion(), safeWardController.gameObject.transform.position, 0f, distance, itemZoneCount);
                    objs.ForEach(obj => ItemZones.Add(obj.AddComponent<ItemZoneComponent>()));
                }
            }
        }

        public override void Stop()
        {
            ItemZones.ForEach(zone => GameObject.Destroy(zone.gameObject));
            ItemZones.Clear();
        }

        public class ItemZoneComponent : MonoBehaviour
        {
            List<TemporaryInventory> inventories = new List<TemporaryInventory>();
            int timer = 0;
            ItemIndex itemIndex;

            private void FixedUpdate()
            {
                timer++;
                if ((timer % 10) == 0)
                {
                    var result = Helper.GetPlayersInRadius(HoldoutZoneShape.Sphere, transform.position, 4f, TeamIndex.Player);
                    foreach (var player in result.playersInRadius)
                    {
                        var inventory = TemporaryInventory.Find(player.gameObject, inventories);
                        if (inventory == null)
                        {
                            inventory = player.gameObject.AddComponent<TemporaryInventory>();
                            inventories.Add(inventory);
                        }
                        inventory.GiveTemporaryItem(itemIndex);
                        PurchaseInteraction.CreateItemTakenOrb(transform.position, player.gameObject, itemIndex);
                    }
                    if (timer == 50)
                    {
                        foreach (var player in result.playersNotInRadius)
                        {
                            var inventory = TemporaryInventory.Find(player.gameObject, inventories);
                            if (inventory != null)
                            {
                                inventory.RemoveTemporaryItem(itemIndex);
                                // PurchaseInteraction.CreateItemTakenOrb(player.transform.position, gameObject, itemIndex);
                            }
                        }
                    }
                }
                if(timer > 50)
                {
                    timer = 0;
                }
            }

            private void OnEnable()
            {
                List<PickupIndex> pickupIndices = new List<PickupIndex>();
                pickupIndices.AddRange(Run.instance.availableTier1DropList);
                pickupIndices.AddRange(Run.instance.availableTier2DropList);
                pickupIndices.AddRange(Run.instance.availableTier3DropList);
                pickupIndices.AddRange(Run.instance.availableBossDropList);
                pickupIndices.AddRange(Run.instance.availableLunarItemDropList);
                List<ItemIndex> itemIndices = new List<ItemIndex>();
                // remove items which are candidates to be turned into void items
                foreach (var pickupIndex in pickupIndices)
                {
                    PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
                    if (pickupDef != null)
                    {
                        ItemIndex itemIndex = pickupDef.itemIndex;
                        if (ContagiousItemManager.GetTransformedItemIndex(itemIndex) == ItemIndex.None)
                        {
                            itemIndices.Add(itemIndex);
                        }
                    }
                }
                // remove prayer beads
                itemIndices.Remove(DLC2Content.Items.ExtraStatsOnLevelUp.itemIndex);
                // remove items that can be consumed
                itemIndices.Remove(RoR2Content.Items.ExtraLife.itemIndex);
                itemIndices.Remove(DLC1Content.Items.FragileDamageBonus.itemIndex);
                itemIndices.Remove(DLC1Content.Items.HealingPotion.itemIndex);
                itemIndices.Remove(DLC1Content.Items.RegeneratingScrap.itemIndex);
                itemIndices.Remove(DLC2Content.Items.TeleportOnLowHealth.itemIndex);
                itemIndices.Remove(DLC2Content.Items.LowerPricedChests.itemIndex);
                // may turn other items into lunarsun
                itemIndices.Remove(DLC1Content.Items.LunarSun.itemIndex);
                // bug: the buff does not get removed again when removing the item
                itemIndices.Remove(DLC2Content.Items.BarrageOnBoss.itemIndex);
                itemIndices.Remove(DLC2Content.Items.OnLevelUpFreeUnlock.itemIndex);
                // a bit too strong:
                itemIndices.Remove(DLC2Content.Items.ItemDropChanceOnKill.itemIndex);
                // items which summon allies instantly
                itemIndices.Remove(RoR2Content.Items.RoboBallBuddy.itemIndex);
                itemIndices.Remove(RoR2Content.Items.BeetleGland.itemIndex);
                // can cause crash
                itemIndices.Remove(RoR2Content.Items.Clover.itemIndex);
                var shuffled = itemIndices.OrderBy(x => UnityEngine.Random.Range(0f, 1f)).ToList();
                itemIndex = shuffled.First();
            }

            private void OnDisable()
            {
                inventories.ForEach(i => Destroy(i));
            }
        }
    }
}
