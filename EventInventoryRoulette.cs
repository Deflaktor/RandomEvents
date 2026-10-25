using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventInventoryRoulette : AbstractEvent
    {
        private readonly ItemSwap itemSwap = new ItemSwap();
        private readonly BuffTracker buffs = new BuffTracker();
        // the armor of each buff stack
        private const int ArmorPerBuff = 20;
        private ConfigEntry<int> Armor;
        // the players who got another's items, who have armor to make up for the unfamiliar items
        private readonly HashSet<CharacterMaster> armored = new HashSet<CharacterMaster>();

        public override bool LoadCondition()
        {
            return true;
        }
        // not while the teleporter or a Simulacrum boss wave has a boss to fight
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            var teleporter = TeleporterInteraction.instance;
            // SurvivorSwap swaps the items as well, and UnitedWeStand's shared items would add up with the lent ones
            return Helper.GetLivingPlayerBodies().Count >= 2 && !Helper.IsSimulacrumBossWaveNext()
                && (teleporter == null || !(teleporter.isIdleToCharging || teleporter.isCharging))
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("SurvivorSwap", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("UnitedWeStand", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "InventoryRoulette";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_INVENTORY_ROULETTE");
        }
        public override string GetDescription()
        {
            return "Every player gets the items of another player and 60 armor until the event ends.";
        }
        public override string GetPopupDescription()
        {
            return "You get another player's items and 60 armor for a while.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive, not during the teleporter event or a Simulacrum boss wave, and events \"SurvivorSwap\" and \"UnitedWeStand\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            Armor = config.Bind(GetEventConfigName(), "Armor", 60, "The armor each player gets on top of their own until the event ends, in steps of 20.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        public override bool SuppressesHereticTransformation => true;

        // also the bodies after a revive during the event, e.g. by Dio's Best Friend
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive() && body.master != null && armored.Contains(body.master))
            {
                AddArmor(body);
            }
        }

        // with the Minor Armor buff, which shows on the player's buff bar
        private void AddArmor(CharacterBody body)
        {
            int stacks = Mathf.Max(0, Mathf.RoundToInt(Armor.Value / (float)ArmorPerBuff));
            for (int i = 0; i < stacks; i++)
            {
                buffs.Add(body, DLC4Content.Buffs.MinorArmorBuff);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            // in a random order, each player gets the items of the next one
            var bodies = Helper.GetLivingPlayerBodies().OrderBy(body => RoR2Application.rng.nextNormalizedFloat).ToList();
            if (bodies.Count < 2)
            {
                return;
            }
            var holders = bodies.Select(body => body.master).ToList();
            var owners = holders.Skip(1).Concat(holders.Take(1)).ToList();
            itemSwap.Lend(holders, owners);
            for (int i = 0; i < bodies.Count; i++)
            {
                ChatHelper.SendEventMessage("EVENT_INVENTORY_ROULETTE_LENT", ChatHelper.GetColoredPlayerName(bodies[i]), ChatHelper.GetColoredPlayerName(bodies[(i + 1) % bodies.Count]));
                armored.Add(bodies[i].master);
                AddArmor(bodies[i]);
            }
        }

        public override void Stop()
        {
            itemSwap.Return();
            buffs.RemoveAll();
            armored.Clear();
        }
    }
}
