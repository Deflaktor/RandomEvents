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
using static RandomEvents.EventHaste;
using static RoR2.HoldoutZoneController;
using static RoR2.SpawnCard;

namespace RandomEvents
{
    public class EventCasinoItem : AbstractEvent
    {
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return true;
        }
        public override string GetEventConfigName()
        {
            return "CasinoItem";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_CASINO_ITEM");
        }
        public override string GetDescription()
        {
            return "Gives all players 10 items of random type. The type of the item changes every 10 seconds.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.teamComponent.teamIndex == TeamIndex.Player && body.isPlayerControlled)
            {
                body.gameObject.EnsureComponent<CasinoItemComponent>();
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            ReadOnlyCollection<TeamComponent> teamMembers = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var teamMember in teamMembers)
            {
                if (teamMember.body != null && teamMember.body.isPlayerControlled)
                {
                    teamMember.body.gameObject.EnsureComponent<CasinoItemComponent>();
                }
            }
        }

        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    if (body.TryGetComponent(out CasinoItemComponent casinoItem))
                    {
                        GameObject.Destroy(casinoItem);
                    }
                }
            }
        }

        public class CasinoItemComponent : MonoBehaviour
        {
            TemporaryInventory inventory;
            int timer = 0;
            int index = 0;
            List<ItemIndex> itemCandidates;
            private void FixedUpdate()
            {
                timer++;
                if(timer > 500)
                {
                    var body = GetComponent<CharacterBody>();
                    if(body != null && body.inventory != null)
                    {
                        if(inventory == null)
                        {
                            inventory = gameObject.AddComponent<TemporaryInventory>();
                        }
                        var previousItem = itemCandidates[index];
                        inventory.RemoveTemporaryItem(previousItem, 10);
                        index = (index + 1) % itemCandidates.Count;
                        var nextItem = itemCandidates[index];
                        inventory.GiveTemporaryItem(nextItem, 10);
                        PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, nextItem);
                    }
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
                //itemIndices.Remove(DLC1Content.Items.LunarSun.itemIndex);
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
                itemCandidates = itemIndices.OrderBy(x => UnityEngine.Random.Range(0f, 1f)).ToList();
            }

            private void OnDisable()
            {
                Destroy(inventory);
            }
        }
    }
}
