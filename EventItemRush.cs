using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventItemRush : AbstractEvent
    {
        private ConfigEntry<float> SpawnInterval;
        private List<ItemIndex> items = new List<ItemIndex>();
        private float spawnTimer;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return GetItems().Count > 0;
        }

        // only items picked up by walking over them, so not lunar or void items, which need a confirmation
        private static List<ItemIndex> GetItems()
        {
            return SafeItems.Get().Where(item =>
            {
                var tierDef = ItemTierCatalog.GetItemTierDef(ItemCatalog.GetItemDef(item).tier);
                return tierDef == null || tierDef.pickupRules == ItemTierDef.PickupRules.Default;
            }).ToList();
        }
        public override string GetEventConfigName()
        {
            return "ItemRush";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_ITEM_RUSH");
        }
        public override string GetDescription()
        {
            return "Items appear around the players until the event ends. Monsters can pick them up too.";
        }
        public override string GetPopupDescription()
        {
            return "Items appear around you until the event ends. Grab them before the monsters do!";
        }
        public override string GetConditionDescription()
        {
            return "At least one item is not in the setting Unsafe Items.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            SpawnInterval = config.Bind(GetEventConfigName(), "Spawn Interval", 2f, "Seconds between two items appearing.");
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
            if (!NetworkServer.active || !IsActive() || items.Count == 0)
            {
                return;
            }
            spawnTimer -= Time.fixedDeltaTime;
            if (spawnTimer > 0f)
            {
                return;
            }
            spawnTimer = Mathf.Max(0.1f, SpawnInterval.Value);
            var players = Helper.GetLivingPlayerBodies();
            if (players.Count == 0)
            {
                return;
            }
            var player = players[RoR2Application.rng.RangeInt(0, players.Count)];
            var position = Helper.FindGroundPositionNear(player.footPosition, 10f, 40f);
            if (position == null)
            {
                return;
            }
            var item = items[RoR2Application.rng.RangeInt(0, items.Count)];
            EventLoot.Drop(this, PickupCatalog.FindPickupIndex(item), position.Value + Vector3.up * 2f, Vector3.up * 10f, true);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            items = GetItems();
            spawnTimer = 0f;
        }

        public override void Stop()
        {
            EventLoot.Clear(this);
            items = new List<ItemIndex>();
        }
    }
}
