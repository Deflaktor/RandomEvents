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
        private ConfigEntry<int> ItemsPerSpawn;
        private ConfigEntry<float> MonsterGrabRadius;
        private ConfigEntry<float> StealTime;
        private ConfigEntry<float> StealRange;
        private const float MonsterCheckInterval = 0.25f;
        private float monsterCheckTimer;
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

        // only items picked up by walking over them, so not lunar or void items, which need a confirmation; meals and
        // Hallowed items too
        private static List<ItemIndex> GetItems()
        {
            return SafeItems.Get().Where(item =>
            {
                var tier = ItemCatalog.GetItemDef(item).tier;
                var tierDef = ItemTierCatalog.GetItemTierDef(tier);
                return tierDef == null || tierDef.pickupRules == ItemTierDef.PickupRules.Default || tier == ItemTier.FoodTier || tier == ItemTier.Cursed;
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
            return "Items appear around the players, in Simulacrum inside the safe zone, until the event ends. Monsters nearby take them too, and take those left lying.";
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
            SpawnInterval = config.Bind(GetEventConfigName(), "Spawn Interval", 2f, "Seconds between two times items appear.");
            MonsterGrabRadius = config.Bind(GetEventConfigName(), "Monster Grab Radius", 5f, "How close a monster has to get to an item to take it, in meters.");
            StealTime = config.Bind(GetEventConfigName(), "Steal Time", 10f, "Seconds after which an item no player took goes to the nearest monster in range. 0 for never.");
            StealRange = config.Bind(GetEventConfigName(), "Steal Range", 30f, "How close the nearest monster has to be to take an item left lying, in meters.");
            ItemsPerSpawn = config.Bind(GetEventConfigName(), "Items Per Spawn", 3, "How many items appear each time, each around a random player.");
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
            monsterCheckTimer -= Time.fixedDeltaTime;
            if (monsterCheckTimer <= 0f)
            {
                monsterCheckTimer = MonsterCheckInterval;
                LetMonstersTakeItems();
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
            for (int i = 0; i < ItemsPerSpawn.Value; i++)
            {
                var position = FindItemPosition(players[RoR2Application.rng.RangeInt(0, players.Count)]);
                if (position == null)
                {
                    continue;
                }
                var item = items[RoR2Application.rng.RangeInt(0, items.Count)];
                EventLoot.Drop(this, PickupCatalog.FindPickupIndex(item), position.Value + Vector3.up * 2f, Vector3.up * 10f, true);
            }
        }

        // Monsters close to an item take it, and the nearest one in range takes an item that lay too long.
        private void LetMonstersTakeItems()
        {
            var pickups = EventLoot.GetPickups(this);
            if (pickups.Count == 0)
            {
                return;
            }
            var monsters = CharacterBody.readOnlyInstancesList.Where(body => body != null && !body.isPlayerControlled && body.healthComponent.alive
                && body.teamComponent.teamIndex != TeamIndex.Player && body.teamComponent.teamIndex != TeamIndex.Neutral).ToList();
            if (monsters.Count == 0)
            {
                return;
            }
            foreach (var (pickup, age) in pickups)
            {
                var position = pickup.transform.position;
                var nearest = monsters.OrderBy(body => (body.corePosition - position).sqrMagnitude).First();
                var distance = (nearest.corePosition - position).magnitude;
                var steal = StealTime.Value > 0f && age >= StealTime.Value && distance <= StealRange.Value;
                if (distance <= MonsterGrabRadius.Value || steal)
                {
                    EventLoot.GiveToMonster(pickup, nearest);
                }
            }
        }

        // around the player, or in Simulacrum anywhere inside the safe zone, which the players have to stay in
        private static Vector3? FindItemPosition(CharacterBody player)
        {
            if (!(Run.instance is InfiniteTowerRun infiniteTowerRun) || infiniteTowerRun.safeWardController == null)
            {
                return Helper.FindGroundPositionNear(player.footPosition, 10f, 40f);
            }
            var ward = infiniteTowerRun.safeWardController;
            var radius = Mathf.Max(1f, ward.holdoutZoneController.currentRadius * 0.9f);
            for (int i = 0; i < 5; i++)
            {
                var position = Helper.FindGroundPositionNear(ward.transform.position, 0f, radius);
                if (position.HasValue && ward.safeZone.IsInBounds(position.Value))
                {
                    return position;
                }
            }
            return null;
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
