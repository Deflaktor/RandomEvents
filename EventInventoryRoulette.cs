using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RandomEvents
{
    public class EventInventoryRoulette : AbstractEvent
    {
        private readonly ItemSwap itemSwap = new ItemSwap();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // SurvivorSwap swaps the items as well
            return Helper.GetLivingPlayerBodies().Count >= 2
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("SurvivorSwap", StringComparison.InvariantCultureIgnoreCase));
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
            return "Every player gets the items of another player until the event ends.";
        }
        public override string GetPopupDescription()
        {
            return "You get another player's items for a while.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive and event \"SurvivorSwap\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
        }

        public override bool SuppressesHereticTransformation => true;

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
            }
        }

        public override void Stop()
        {
            itemSwap.Return();
        }
    }
}
