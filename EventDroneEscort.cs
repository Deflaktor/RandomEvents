using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RandomEvents
{
    public class EventDroneEscort : AbstractEvent
    {
        private readonly List<CharacterMaster> drones = new List<CharacterMaster>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return GetDroneCandidates().Count > 0;
        }
        public override string GetEventConfigName()
        {
            return "DroneEscort";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_DRONE_ESCORT");
        }
        public override string GetDescription()
        {
            return "Gives each player a random drone until the event ends.";
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
        }
        public override void Hook()
        {
            On.EntityStates.Drone.DeathState.OnImpactServer += DeathState_OnImpactServer;
        }

        // an escort drone that is destroyed leaves no broken drone to repair
        private void DeathState_OnImpactServer(On.EntityStates.Drone.DeathState.orig_OnImpactServer orig, EntityStates.Drone.DeathState self, Vector3 contactPoint)
        {
            var master = self.characterBody ? self.characterBody.master : null;
            if (master != null && master.GetComponent<EscortDrone>() != null)
            {
                return;
            }
            orig(self, contactPoint);
        }

        // all drones of the run's expansions, also those not found on stages, without the Gunner Turret, which can't
        // follow; the Equipment Drone gets one of EquipmentOnly's equipment
        private static List<DroneDef> GetDroneCandidates()
        {
            var excluded = new HashSet<DroneDef> { RoR2Content.DroneDefs.Turret1 };
            return DroneCatalog.allDroneDefs
                .Where(droneDef => droneDef != null && !excluded.Contains(droneDef) && droneDef.masterPrefab != null && droneDef.CheckRequiredExpansionEnabled())
                .ToList();
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            drones.Clear();
            var candidates = GetDroneCandidates();
            if (candidates.Count == 0)
            {
                return;
            }
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                var droneDef = candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
                var master = new MasterSummon
                {
                    masterPrefab = droneDef.masterPrefab,
                    position = body.corePosition + Vector3.up * 2f,
                    rotation = body.transform.rotation,
                    summonerBodyObject = body.gameObject,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true,
                    enablePrintController = true
                }.Perform();
                if (master != null)
                {
                    master.gameObject.AddComponent<EscortDrone>();
                    if (droneDef == RoR2Content.DroneDefs.EquipmentDrone)
                    {
                        var equipment = EventEquipmentOnly.EventEquipment;
                        master.inventory.SetEquipmentIndex(equipment[RoR2Application.rng.RangeInt(0, equipment.Length)].equipmentIndex, master.inventory.activeEquipmentLocation);
                    }
                    drones.Add(master);
                }
            }
        }

        public override void Stop()
        {
            // removed without dying, so they leave no broken drones
            foreach (var master in drones)
            {
                if (master != null)
                {
                    master.DestroyBody();
                    GameObject.Destroy(master.gameObject);
                }
            }
            drones.Clear();
        }

        public class EscortDrone : MonoBehaviour
        {
        }
    }
}
