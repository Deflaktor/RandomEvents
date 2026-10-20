using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventOssification : AbstractEvent
    {
        private ConfigEntry<int> HexCount;
        private ConfigEntry<float> HealthPerBrooch;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Hex of Ossification comes with that expansion
            return DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4)
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("Ghosts", StringComparison.InvariantCultureIgnoreCase))
                // Cursed Spoils' hexes would disable the event's items
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("CursedSpoils", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "Ossification";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_OSSIFICATION");
        }
        public override string GetDescription()
        {
            return "All players get 5 Hexes of Ossification, so their kills give nearby monsters barrier, but also enough Topaz Brooches for each kill to fill their own barrier.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled, and events \"Ghosts\" and \"CursedSpoils\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            HexCount = config.Bind(GetEventConfigName(), "Hex Count", 5, "How many Hexes of Ossification each player gets.");
            HealthPerBrooch = config.Bind(GetEventConfigName(), "Health Per Brooch", 15f, "Each player gets a Topaz Brooch for this much of their combined maximum health. A brooch gives 15 barrier per kill, so 15 lets a kill fill the whole barrier.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        // players who respawn during the event get theirs again
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveItems(body);
            }
        }

        private void GiveItems(CharacterBody body)
        {
            if (body == null || !body.isPlayerControlled || body.inventory == null)
            {
                return;
            }
            var inventory = EventLoot.GetInventory(this, body);
            if (inventory.temporaryItems.ContainsKey(DLC4Content.Items.BarrierEnemiesOnKill.itemIndex))
            {
                return;
            }
            var hexes = Mathf.Max(0, HexCount.Value);
            if (hexes > 0)
            {
                inventory.GiveTemporaryItem(DLC4Content.Items.BarrierEnemiesOnKill.itemIndex, hexes);
                ItemOrb.Fly(body.corePosition + Vector3.up * 4f, body.gameObject, DLC4Content.Items.BarrierEnemiesOnKill.itemIndex);
            }
            // the health with the items and stats the player has as the event starts
            var brooches = Mathf.RoundToInt(body.healthComponent.fullCombinedHealth / Mathf.Max(1f, HealthPerBrooch.Value));
            if (brooches > 0)
            {
                inventory.GiveTemporaryItem(RoR2Content.Items.BarrierOnKill.itemIndex, brooches);
                ItemOrb.Fly(body.corePosition + Vector3.up * 4f, body.gameObject, RoR2Content.Items.BarrierOnKill.itemIndex);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                GiveItems(body);
            }
        }

        public override void Stop()
        {
            EventLoot.Clear(this);
        }
    }
}
