using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventChestMimics : AbstractEvent
    {
        private ConfigEntry<float> MimicChance;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return InstanceTracker.GetInstancesList<ChestBehavior>().Any(chest => chest != null && IsMimicCandidate(chest) && chest.TryGetComponent(out PurchaseInteraction purchaseInteraction) && purchaseInteraction.available);
        }
        public override string GetEventConfigName()
        {
            return "ChestMimics";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_CHEST_MIMICS");
        }
        public override string GetDescription()
        {
            return "Chests bought with gold may release a monster instead of their item, a boss for a legendary or boss item. Killing it drops twice the chest's items.";
        }
        public override string GetConditionDescription()
        {
            return "The stage has an unopened chest bought with gold.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            MimicChance = config.Bind(GetEventConfigName(), "Mimic Chance", 0.5f, "Probability for a chest opened during the event to be a mimic.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.ChestBehavior.ItemDrop += ChestBehavior_ItemDrop;
            GlobalEventManager.onCharacterDeathServer += GlobalEventManager_onCharacterDeathServer;
        }

        private void ChestBehavior_ItemDrop(On.RoR2.ChestBehavior.orig_ItemDrop orig, ChestBehavior self)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && IsMimicCandidate(self) && RoR2Application.rng.nextNormalizedFloat < MimicChance.Value && SpawnMimic(self))
            {
                // the mimic carries the items instead
                return;
            }
            orig(self);
        }

        // also after the event: a mimic keeps its loot until it dies
        private void GlobalEventManager_onCharacterDeathServer(DamageReport damageReport)
        {
            if (!NetworkServer.active || damageReport.victimMaster == null || damageReport.victimBody == null || !damageReport.victimMaster.TryGetComponent(out MimicLoot loot))
            {
                return;
            }
            GameObject.Destroy(loot);
            var angle = 360f / loot.count;
            var velocity = Vector3.up * 20f + Vector3.forward * 3f;
            for (int i = 0; i < loot.count; i++)
            {
                PickupDropletController.CreatePickupDroplet(loot.pickup, damageReport.victimBody.corePosition + Vector3.up * 1.5f, Quaternion.AngleAxis(angle * i, Vector3.up) * velocity, false);
            }
        }

        // chests bought with gold, which hold an item; not Lunar Pods, lockboxes or the Artifact of Delusion's chests
        private static bool IsMimicCandidate(ChestBehavior chest)
        {
            if (!chest.TryGetComponent(out PurchaseInteraction purchaseInteraction) || purchaseInteraction.costType != CostTypeIndex.Money)
            {
                return false;
            }
            return !(DelusionChestController.isDelusionEnable && chest.GetComponent<DelusionChestController>() != null);
        }

        private bool SpawnMimic(ChestBehavior chest)
        {
            if (!chest.currentPickup.isValid || chest.dropCount < 1)
            {
                return false;
            }
            var pickupDef = PickupCatalog.GetPickupDef(chest.currentPickup.pickupIndex);
            var itemTier = pickupDef != null && pickupDef.itemIndex != ItemIndex.None ? ItemCatalog.GetItemDef(pickupDef.itemIndex).tier : ItemTier.NoTier;
            bool allowChampions = itemTier == ItemTier.Tier3 || itemTier == ItemTier.Boss;
            var selection = new WeightedSelection<DirectorCard>();
            foreach (var choice in MonsterPool.GetChoices())
            {
                var master = choice.value?.spawnCard?.prefab != null ? choice.value.spawnCard.prefab.GetComponent<CharacterMaster>() : null;
                var body = master != null && master.bodyPrefab != null ? master.bodyPrefab.GetComponent<CharacterBody>() : null;
                if (choice.weight > 0f && body != null && (allowChampions || !body.isChampion))
                {
                    selection.AddChoice(choice);
                }
            }
            if (selection.Count == 0)
            {
                return false;
            }
            var card = selection.Evaluate(RoR2Application.rng.nextNormalizedFloat);
            var mimic = Helper.SpawnMonster(card.spawnCard, chest.transform.position + Vector3.up);
            if (mimic == null)
            {
                return false;
            }
            var loot = mimic.gameObject.AddComponent<MimicLoot>();
            loot.pickup = chest.currentPickup;
            loot.count = chest.dropCount * 2;
            ChatHelper.SendEventMessage("EVENT_CHEST_MIMICS_REVEALED");
            return true;
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
        }

        public override void Stop()
        {
        }

        public class MimicLoot : MonoBehaviour
        {
            public UniquePickup pickup;
            public int count;
        }
    }
}
