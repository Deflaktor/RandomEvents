using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    // The Voidling's safe ward marks the zone, which moves around inside the Simulacrum's safe zone or the charging
    // teleporter's zone. Clients follow its radius but not its position, so it is spawned anew where it moves to.
    public class EventSafeZone : AbstractEvent
    {
        private const float TickPeriod = 1f;
        private ConfigEntry<float> Radius;
        private ConfigEntry<float> MoveInterval;
        private AsyncOperationHandle<GameObject> wardPrefab;
        private AsyncOperationHandle<GameObject> disappearEffectPrefab;
        private GameObject ward;
        private IZone zone;
        private float moveTimer;
        private float tickTimer;

        public override EventTags Tags => EventTags.EventItems;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // not for super boss waves
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && (infiniteTowerRun.waveIndex + 1) % 50 == 0)
            {
                return false;
            }
            // the safe ward comes with that expansion
            return DLC1Content.DLC1 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC1Content.DLC1) && Helper.TryGetArena(out _, out _);
        }
        public override string GetEventConfigName()
        {
            return "SafeZone";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SAFE_ZONE");
        }
        public override string GetDescription()
        {
            return "A small safe zone like the Voidling's appears in the Simulacrum's safe zone or the charging teleporter's zone and moves around in it every 15 seconds. Outside it, players get a Tonic Affliction every second; inside it, they lose them and get an Irradiant Pearl every second instead, which they lose again when they leave.";
        }
        public override string GetPopupDescription()
        {
            return "Stay in the moving safe zone to grow stronger. Outside it, you grow weaker!";
        }
        public override string GetConditionDescription()
        {
            return "Survivors of the Void is enabled, and in the Simulacrum but not a super boss wave, or while the teleporter charges.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            Radius = config.Bind(GetEventConfigName(), "Radius", 10f, "The safe zone's radius, in meters.");
            MoveInterval = config.Bind(GetEventConfigName(), "Move Interval", 15f, "Seconds between the safe zone's moves.");
        }
        public override void Preload()
        {
            wardPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/VoidRaidCrab/VoidRaidSafeWard.prefab");
            disappearEffectPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/VoidRaidCrab/VoidRaidSafeWardDisappearEffect.prefab");
        }
        public override void Hook()
        {
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            // once the teleporter is charged, the items are gone too
            if (!Helper.TryGetArena(out var center, out var arenaRadius))
            {
                RemoveWard();
                EventLoot.Clear(this);
                return;
            }
            var radius = Mathf.Max(1f, Radius.Value);
            moveTimer -= Time.fixedDeltaTime;
            if (ward == null || moveTimer <= 0f)
            {
                moveTimer = Mathf.Max(1f, MoveInterval.Value);
                Move(center, arenaRadius, radius);
            }
            tickTimer -= Time.fixedDeltaTime;
            if (tickTimer <= 0f)
            {
                tickTimer = TickPeriod;
                Tick();
            }
        }

        // somewhere wholly inside the arena, away from where it was if there is room
        private void Move(Vector3 center, float arenaRadius, float radius)
        {
            var maxDistance = Mathf.Max(0f, arenaRadius - radius);
            Vector3? position = null;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Helper.FindGroundPositionNear(center, 0f, maxDistance);
                if (candidate == null || HorizontalDistance(candidate.Value, center) > maxDistance)
                {
                    continue;
                }
                position = candidate;
                if (ward == null || HorizontalDistance(candidate.Value, ward.transform.position) >= radius)
                {
                    break;
                }
            }
            position ??= Helper.RaycastToFloor(center, 50f, true);
            if (position == null)
            {
                return;
            }
            RemoveWard();
            var prefab = wardPrefab.WaitForCompletion();
            if (prefab == null)
            {
                return;
            }
            ward = Object.Instantiate(prefab, position.Value, Quaternion.identity);
            if (ward.TryGetComponent(out VerticalTubeZone tube))
            {
                tube.Networkradius = radius;
            }
            zone = ward.GetComponent<IZone>();
            NetworkServer.Spawn(ward);
        }

        private void RemoveWard()
        {
            if (ward != null)
            {
                var effect = disappearEffectPrefab.WaitForCompletion();
                if (effect != null)
                {
                    EffectManager.SpawnEffect(effect, new EffectData { origin = ward.transform.position }, transmit: true);
                }
                Object.Destroy(ward);
            }
            ward = null;
            zone = null;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        private void Tick()
        {
            if (zone == null)
            {
                return;
            }
            var affliction = RoR2Content.Items.TonicAffliction.itemIndex;
            var pearl = RoR2Content.Items.ShinyPearl.itemIndex;
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                if (body.inventory == null)
                {
                    continue;
                }
                var inventory = EventLoot.GetInventory(this, body);
                var inside = zone.IsInBounds(body.footPosition);
                inventory.RemoveTemporaryItem(inside ? affliction : pearl, inventory.temporaryItems.GetValueOrDefault(inside ? affliction : pearl));
                inventory.GiveTemporaryItem(inside ? pearl : affliction);
            }
        }

        public override void Prepare()
        {
        }
        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            moveTimer = 0f;
            tickTimer = TickPeriod;
        }
        public override void Stop()
        {
            RemoveWard();
            EventLoot.Clear(this);
        }
    }
}
