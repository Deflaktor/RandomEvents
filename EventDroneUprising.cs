using BepInEx.Configuration;
using RoR2;
using RoR2.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventDroneUprising : AbstractEvent
    {
        private ConfigEntry<float> DamageReduction;
        // a spawn card for each drone, made once
        private readonly Dictionary<DroneDef, CharacterSpawnCard> spawnCards = new Dictionary<DroneDef, CharacterSpawnCard>();
        private readonly HashSet<BodyIndex> droneBodies = new HashSet<BodyIndex>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // they pick from the stage's own monsters, which this event replaces
            return GetDroneCandidates().Count > 0 && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("Mitosis", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("RitualHelmets", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("FallingEnemies", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("Bouncy", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "DroneUprising";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_DRONE_UPRISING");
        }
        public override string GetDescription()
        {
            return "Hostile drones spawn instead of monsters. They deal half their normal damage and leave no wreck.";
        }
        public override string GetConditionDescription()
        {
            return "Events \"Mitosis\", \"RitualHelmets\", \"FallingEnemies\" and \"Bouncy\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            DamageReduction = config.Bind(GetEventConfigName(), "Damage Reduction", 0.5f, "How much less damage the hostile drones deal than they normally do.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.EntityStates.Drone.DeathState.OnImpactServer += DeathState_OnImpactServer;
        }

        // the drones that can be found on stages, without the Equipment Drone, which would have no equipment
        private static List<DroneDef> GetDroneCandidates()
        {
            return DroneCatalog.allDroneDefs
                .Where(droneDef => droneDef != null && droneDef != RoR2Content.DroneDefs.EquipmentDrone && droneDef.droneBrokenSpawnCard != null
                    && droneDef.masterPrefab != null && droneDef.CheckRequiredExpansionEnabled())
                .ToList();
        }

        private CharacterSpawnCard GetSpawnCard(DroneDef droneDef)
        {
            if (spawnCards.TryGetValue(droneDef, out var spawnCard))
            {
                return spawnCard;
            }
            var master = droneDef.masterPrefab.GetComponent<CharacterMaster>();
            var body = master != null && master.bodyPrefab != null ? master.bodyPrefab.GetComponent<CharacterBody>() : null;
            if (body == null)
            {
                return null;
            }
            spawnCard = ScriptableObject.CreateInstance<CharacterSpawnCard>();
            spawnCard.name = "cscHostile" + droneDef.name;
            spawnCard.prefab = droneDef.masterPrefab;
            spawnCard.sendOverNetwork = true;
            spawnCard.hullSize = body.hullClassification;
            // turrets stand on the ground, the other drones fly
            spawnCard.nodeGraphType = (body.bodyFlags & CharacterBody.BodyFlags.Turret) != 0 || body.GetComponent<CharacterMotor>() != null ? MapNodeGroup.GraphType.Ground : MapNodeGroup.GraphType.Air;
            spawnCard.forbiddenFlags = NodeFlags.NoCharacterSpawn;
            // about the cost of monsters of similar strength
            spawnCard.directorCreditCost = droneDef.tier == ItemTier.Tier3 ? 120 : droneDef.tier == ItemTier.Tier2 ? 40 : 15;
            spawnCards[droneDef] = spawnCard;
            droneBodies.Add(body.bodyIndex);
            return spawnCard;
        }

        // weaker, so a swarm of them stays fair
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (!NetworkServer.active || !IsActive() || body.master == null || body.teamComponent.teamIndex == TeamIndex.Player
                || !droneBodies.Contains(body.bodyIndex) || body.master.GetComponent<HostileDrone>() != null)
            {
                return;
            }
            body.master.gameObject.AddComponent<HostileDrone>();
            // an item without a name, which lowers the damage by 1% per stack
            var reduceDamage = Mathf.RoundToInt(Mathf.Clamp01(DamageReduction.Value) * 100f);
            if (reduceDamage > 0)
            {
                body.inventory.GiveItemPermanent(DLC4Content.Items.ReduceDamage, reduceDamage);
            }
        }

        // a hostile drone leaves no broken drone for the players to repair, also after the event
        private void DeathState_OnImpactServer(On.EntityStates.Drone.DeathState.orig_OnImpactServer orig, EntityStates.Drone.DeathState self, Vector3 contactPoint)
        {
            var master = self.characterBody ? self.characterBody.master : null;
            if (master != null && master.GetComponent<HostileDrone>() != null)
            {
                return;
            }
            orig(self, contactPoint);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            var selection = new WeightedSelection<DirectorCard>();
            foreach (var droneDef in GetDroneCandidates())
            {
                var spawnCard = GetSpawnCard(droneDef);
                if (spawnCard != null)
                {
                    selection.AddChoice(new DirectorCard
                    {
                        spawnCard = spawnCard,
                        selectionWeight = 1,
                        spawnDistance = DirectorCore.MonsterSpawnDistance.Standard
                    }, 1f);
                }
            }
            MonsterPool.Replace(this, selection);
        }

        public override void Stop()
        {
            MonsterPool.Restore(this);
        }

        public class HostileDrone : MonoBehaviour
        {
        }
    }
}
