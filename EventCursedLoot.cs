using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventCursedLoot : AbstractEvent
    {
        private ConfigEntry<float> HexDamage;
        // damage each player took from monsters since their last hex
        private readonly Dictionary<CharacterBody, float> damageTaken = new Dictionary<CharacterBody, float>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the hex comes with that expansion
            return DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4);
        }
        public override string GetEventConfigName()
        {
            return "CursedLoot";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_CURSED_LOOT");
        }
        public override string GetDescription()
        {
            return "Monsters drop an item when they die, which its finder keeps until the event ends. Each time a player has taken damage from monsters adding up to 10% of their base health, they get a hex that disables some of their items until the event ends.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            HexDamage = config.Bind(GetEventConfigName(), "Hex Damage", 0.1f, "Damage a player takes from monsters per hex, relative to their base health: their survivor's health at their level, without items.");
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
            // the rarities of a small chest
            var selector = Run.instance.smallChestDropTierSelector;
            if (selector.Count == 0)
            {
                return;
            }
            var dropList = selector.Evaluate(RoR2Application.rng.nextNormalizedFloat);
            if (dropList == null || dropList.Count == 0)
            {
                return;
            }
            var pickupIndex = dropList[RoR2Application.rng.RangeInt(0, dropList.Count)];
            EventLoot.Drop(this, pickupIndex, damageReport.victimBody.corePosition + Vector3.up * 1.5f, Vector3.up * 20f);
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
