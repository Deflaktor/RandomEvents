using BepInEx.Configuration;
using EntityStates.Captain.Weapon;
using RoR2;
using RoR2.Projectile;
using System.Collections.Generic;
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
        private readonly List<GameObject> beacons = new List<GameObject>();

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
            return "Drops a random Captain beacon (Healing, Shocking, Resupply, Hacking or Defense) next to each player. The beacons disappear when the event ends.";
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
            beacons.Clear();
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                var prefab = beaconPrefabs[RoR2Application.rng.RangeInt(0, beaconPrefabs.Count)].WaitForCompletion();
                var position = Helper.FindGroundPositionNear(body.footPosition, 4f, 10f) ?? body.footPosition;
                DropBeacon(body, prefab, position);
            }
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
            beacons.Add(beacon);
        }

        public override void Stop()
        {
            foreach (var beacon in beacons)
            {
                if (beacon)
                {
                    GameObject.Destroy(beacon);
                }
            }
            beacons.Clear();
        }
    }
}
