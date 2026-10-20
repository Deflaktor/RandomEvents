using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventSlippery : AbstractEvent
    {
        private ConfigEntry<int> Hexes;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Hex of Acceleration comes with that expansion
            return DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4);
        }
        public override string GetEventConfigName()
        {
            return "Slippery";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SLIPPERY");
        }
        public override string GetDescription()
        {
            return "All players and monsters get 3 Hexes of Acceleration, so all ground becomes slippery.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            Hexes = config.Bind(GetEventConfigName(), "Hexes", 3, "How many Hexes of Acceleration everyone gets.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        // also those spawning during the event
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive())
            {
                GiveHexes(body);
            }
        }

        private void GiveHexes(CharacterBody body)
        {
            if (body != null && body.inventory != null && body.teamComponent.teamIndex != TeamIndex.Neutral && Hexes.Value > 0)
            {
                EventLoot.GetInventory(this, body).GiveTemporaryItem(DLC4Content.Items.SlipperyGround.itemIndex, Hexes.Value);
                if (body.isPlayerControlled)
                {
                    ItemOrb.Fly(body.corePosition + Vector3.up * 5f, body.gameObject, DLC4Content.Items.SlipperyGround.itemIndex);
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveHexes(body);
            }
        }

        public override void Stop()
        {
            EventLoot.Clear(this);
        }
    }
}
