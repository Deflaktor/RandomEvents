using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventUnitedWeStand : AbstractEvent
    {
        private const float UpdateInterval = 0.5f;
        private ConfigEntry<float> ShareDistance;
        // the temporary stacks each player has of the others' items
        private readonly Dictionary<CharacterMaster, int[]> shared = new Dictionary<CharacterMaster, int[]>();
        private float updateTimer;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the items that InventoryRoulette and SurvivorSwap lend would add up with the shared ones
            return Helper.GetLivingPlayerBodies().Count >= 2
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("InventoryRoulette", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("SurvivorSwap", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "UnitedWeStand";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_UNITED_WE_STAND");
        }
        public override string GetDescription()
        {
            return "Players close to each other share all their items.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive and events \"InventoryRoulette\" and \"SurvivorSwap\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            ShareDistance = config.Bind(GetEventConfigName(), "Share Distance", 10f, "How close players have to be to share their items, in meters. Players also share through others in between.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive())
            {
                return;
            }
            updateTimer -= Time.fixedDeltaTime;
            if (updateTimer > 0f)
            {
                return;
            }
            updateTimer = UpdateInterval;
            UpdateSharing();
        }

        // Players in reach of each other, also through others, form a group, and each member gets temporary stacks of
        // the other members' own items, as with InventoryRoulette's lending. Their own items never change.
        private void UpdateSharing()
        {
            var bodies = Helper.GetLivingPlayerBodies();
            var ownItems = bodies.ToDictionary(body => body, body => ItemSwap.GetItems(body.inventory));
            var reach = ShareDistance.Value * ShareDistance.Value;
            var wanted = new Dictionary<CharacterMaster, int[]>();
            var grouped = new HashSet<CharacterBody>();
            foreach (var start in bodies)
            {
                if (!grouped.Add(start))
                {
                    continue;
                }
                var group = new List<CharacterBody> { start };
                for (int i = 0; i < group.Count; i++)
                {
                    foreach (var other in bodies)
                    {
                        if (!grouped.Contains(other) && (other.corePosition - group[i].corePosition).sqrMagnitude <= reach)
                        {
                            grouped.Add(other);
                            group.Add(other);
                        }
                    }
                }
                if (group.Count < 2)
                {
                    continue;
                }
                var total = new int[ItemCatalog.itemCount];
                foreach (var member in group)
                {
                    var items = ownItems[member];
                    for (int j = 0; j < total.Length; j++)
                    {
                        total[j] += items[j];
                    }
                }
                foreach (var member in group)
                {
                    var items = ownItems[member];
                    wanted[member.master] = total.Select((count, j) => count - items[j]).ToArray();
                }
            }
            foreach (var master in shared.Keys.Union(wanted.Keys).ToList())
            {
                Apply(master, wanted.TryGetValue(master, out var items) ? items : null);
            }
        }

        // changes a player's temporary stacks from what they have to what they should have
        private void Apply(CharacterMaster master, int[] wantedItems)
        {
            shared.TryGetValue(master, out var current);
            if (master != null && master.inventory != null)
            {
                for (int i = 0; i < ItemCatalog.itemCount; i++)
                {
                    var change = (wantedItems != null ? wantedItems[i] : 0) - (current != null ? current[i] : 0);
                    if (change != 0)
                    {
                        master.inventory.GiveItemChanneled((ItemIndex)i, change);
                    }
                }
            }
            if (wantedItems != null)
            {
                shared[master] = wantedItems;
            }
            else
            {
                shared.Remove(master);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            updateTimer = 0f;
        }

        public override void Stop()
        {
            foreach (var master in shared.Keys.ToList())
            {
                Apply(master, null);
            }
            shared.Clear();
        }
    }
}
