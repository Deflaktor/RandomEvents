using BepInEx.Configuration;
using EntityStates.Captain.Weapon;
using RoR2;
using RoR2.Projectile;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventSupplyDrop : AbstractEvent
    {
        // Beacon: Healing, Shocking, Resupply, Hacking and Defense
        private static readonly string[] BeaconAddresses =
        {
            "RoR2/Base/Captain/CaptainSupplyDrop, Healing.prefab",
            "RoR2/Base/Captain/CaptainSupplyDrop, Shocking.prefab",
            "RoR2/Base/Captain/CaptainSupplyDrop, EquipmentRestock.prefab",
            "RoR2/Base/Captain/CaptainSupplyDrop, Hacking.prefab",
            "RoR2/Base/Captain/CaptainSupplyDrop, Plating.prefab",
        };
        private readonly List<AsyncOperationHandle<GameObject>> beaconPrefabs = new List<AsyncOperationHandle<GameObject>>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return true;
        }
        public override string GetEventConfigName()
        {
            return "SupplyDrop";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SUPPLY_DROP");
        }
        public override string GetDescription()
        {
            return "Drops a random Captain beacon next to each player, which stays for the rest of the stage.";
        }
        public override string GetPopupDescription()
        {
            return "You get a random Captain beacon.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
            foreach (var address in BeaconAddresses)
            {
                beaconPrefabs.Add(Addressables.LoadAssetAsync<GameObject>(address));
            }
        }
        public override void Hook()
        {
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            // every player a random beacon, all different while there are enough kinds
            var kinds = beaconPrefabs.OrderBy(_ => RoR2Application.rng.nextNormalizedFloat).ToList();
            var bodies = Helper.GetLivingPlayerBodies();
            for (int i = 0; i < bodies.Count; i++)
            {
                DropBeacon(bodies[i], kinds[i % kinds.Count].WaitForCompletion(), FindBeaconPosition(bodies[i]));
            }
        }

        // Next to the player, but inside Simulacrum's safe zone or the charging teleporter's zone, where the players
        // have to stay; within the zone instead if the player is outside it.
        private static Vector3 FindBeaconPosition(CharacterBody body)
        {
            IZone zone = null;
            Vector3 center = Vector3.zero;
            float radius = 0f;
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.safeWardController != null)
            {
                zone = infiniteTowerRun.safeWardController.safeZone;
                center = infiniteTowerRun.safeWardController.transform.position;
                radius = infiniteTowerRun.safeWardController.holdoutZoneController.currentRadius;
            }
            else if (TeleporterInteraction.instance != null && TeleporterInteraction.instance.isCharging)
            {
                zone = TeleporterInteraction.instance.holdoutZoneController;
                center = TeleporterInteraction.instance.transform.position;
                radius = TeleporterInteraction.instance.holdoutZoneController.currentRadius;
            }
            for (int i = 0; i < 5; i++)
            {
                var position = Helper.FindGroundPositionNear(body.footPosition, 4f, 10f);
                if (position.HasValue && (zone == null || zone.IsInBounds(position.Value)))
                {
                    return position.Value;
                }
            }
            if (zone == null)
            {
                return body.footPosition;
            }
            for (int i = 0; i < 5; i++)
            {
                var position = Helper.FindGroundPositionNear(center, 0f, Mathf.Max(1f, radius * 0.8f));
                if (position.HasValue && zone.IsInBounds(position.Value))
                {
                    return position.Value;
                }
            }
            return center;
        }

        // as Captain's skill does, but the beacon isn't one of the player's deployables, so it can't replace a Captain's own
        private void DropBeacon(CharacterBody owner, GameObject prefab, Vector3 position)
        {
            var beacon = GameObject.Instantiate(prefab, position, Quaternion.Euler(0f, RoR2Application.rng.RangeFloat(0f, 360f), 0f));
            beacon.GetComponent<TeamFilter>().teamIndex = owner.teamComponent.teamIndex;
            beacon.GetComponent<GenericOwnership>().ownerObject = owner.gameObject;
            var impactDamage = beacon.GetComponent<ProjectileDamage>();
            impactDamage.crit = owner.RollCrit();
            impactDamage.damage = owner.damage * CallSupplyDropBase.impactDamageCoefficient;
            impactDamage.damageColorIndex = DamageColorIndex.Default;
            impactDamage.force = CallSupplyDropBase.impactDamageForce;
            impactDamage.damageType = DamageType.Generic;
            NetworkServer.Spawn(beacon);
        }

        // the beacons stay for the rest of the stage, as Captain's own do
        public override void Stop()
        {
        }
    }
}
