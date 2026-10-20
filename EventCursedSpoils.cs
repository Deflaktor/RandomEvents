using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventCursedSpoils : AbstractEvent
    {
        // events that give players items to play with, which Hex of Loss would disable
        private static readonly HashSet<string> ItemSetEvents = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "Behemoths", "Bouncy", "CasinoItem", "EquipmentOnly", "IndirectDamage", "ItemKits", "LavaFloor", "Ossification", "Slippery"
        };
        private ConfigEntry<float> HexDamage;
        // damage each player took from monsters since their last hex
        private readonly Dictionary<CharacterBody, float> damageTaken = new Dictionary<CharacterBody, float>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the hex comes with that expansion; the hexes would disable the items of events built around them
            return DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4)
                && !activeOtherEvents.Any(e => ItemSetEvents.Contains(e.GetEventConfigName()));
        }
        public override string GetEventConfigName()
        {
            return "CursedSpoils";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_CURSED_SPOILS");
        }
        public override string GetDescription()
        {
            return "Every kill gives each player a temporary item, but taking damage from monsters brings hexes that disable items.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled, and events \"Behemoths\", \"Bouncy\", \"CasinoItem\", \"EquipmentOnly\", \"IndirectDamage\", \"ItemKits\", \"LavaFloor\", \"Ossification\" and \"Slippery\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            HexDamage = config.Bind(GetEventConfigName(), "Hex Damage", 0.25f, "Damage a player takes from monsters per hex, relative to their base health: their survivor's health at their level, without items.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            GlobalEventManager.onCharacterDeathServer += GlobalEventManager_onCharacterDeathServer;
            GlobalEventManager.onServerDamageDealt += GlobalEventManager_onServerDamageDealt;
        }

        private void GlobalEventManager_onCharacterDeathServer(DamageReport damageReport)
        {
            if (!NetworkServer.active || !IsActive() || damageReport.victimBody == null || damageReport.victimTeamIndex == TeamIndex.Player || damageReport.victimTeamIndex == TeamIndex.Neutral
                || Run.instance == null)
            {
                return;
            }
            // every player gets an item of their own, flying to them from the monster
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                var itemIndex = RollItem();
                if (itemIndex == ItemIndex.None)
                {
                    return;
                }
                EventLoot.GetInventory(this, body).GiveTemporaryItem(itemIndex);
                ItemOrb.FlySilently(damageReport.victimBody.corePosition, body.gameObject, itemIndex);
            }
        }

        // an item with the rarities of a small chest
        private static ItemIndex RollItem()
        {
            var selector = Run.instance.smallChestDropTierSelector;
            if (selector.Count == 0)
            {
                return ItemIndex.None;
            }
            var dropList = selector.Evaluate(RoR2Application.rng.nextNormalizedFloat);
            if (dropList == null || dropList.Count == 0)
            {
                return ItemIndex.None;
            }
            return PickupCatalog.GetPickupDef(dropList[RoR2Application.rng.RangeInt(0, dropList.Count)])?.itemIndex ?? ItemIndex.None;
        }

        private void GlobalEventManager_onServerDamageDealt(DamageReport damageReport)
        {
            var body = damageReport.victimBody;
            if (!NetworkServer.active || !IsActive() || body == null || !body.isPlayerControlled || damageReport.damageDealt <= 0f
                || damageReport.attackerTeamIndex == TeamIndex.Player || damageReport.attackerTeamIndex == TeamIndex.None)
            {
                return;
            }
            // the survivor's health at its level, without items
            var baseHealth = body.baseMaxHealth + body.levelMaxHealth * (body.level - 1f);
            var threshold = Mathf.Max(1f, baseHealth * HexDamage.Value);
            var damage = damageTaken.GetValueOrDefault(body) + damageReport.damageDealt;
            var hexes = 0;
            while (damage >= threshold)
            {
                damage -= threshold;
                hexes++;
            }
            damageTaken[body] = damage;
            if (hexes > 0)
            {
                EventLoot.GetInventory(this, body).GiveTemporaryItem(DLC4Content.Items.RoamingItemDisable.itemIndex, hexes);
                // each hex flies to the player from the monster that hurt them
                var origin = damageReport.attackerBody != null ? damageReport.attackerBody.corePosition : body.corePosition + Vector3.up * 4f;
                for (int i = 0; i < hexes; i++)
                {
                    ItemOrb.FlySilently(origin, body.gameObject, DLC4Content.Items.RoamingItemDisable.itemIndex);
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            damageTaken.Clear();
        }

        public override void Stop()
        {
            EventLoot.Clear(this);
            damageTaken.Clear();
        }
    }
}
