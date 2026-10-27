using BepInEx.Configuration;
using HG;
using RoR2;
using RoR2.ExpansionManagement;
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
        public override EventTags Tags => EventTags.Monsters | EventTags.MonsterPool;
        // it replaces the stage's monsters, which the others pick from, and SwarmQueen would bring Beetles and Lesser Wisps along
        public override EventTags ExcludedTags => EventTags.MonsterPool | EventTags.ExtraMonsters;
        private ConfigEntry<float> DamageMultiplier;
        private ConfigEntry<float> MoveSpeedMultiplier;
        private ConfigEntry<float> HealthMultiplier;
        private ConfigEntry<float> MaxBulletDamage;
        // the drones and turrets that items, equipment and survivors bring, which the drone catalog lacks
        private static readonly string[] SummonedBodies =
        {
            "BackupDroneBody", "DroneCommanderBody", "DTGunnerDroneBody", "DTHaulerDroneBody", "EngiTurretBody", "EngiWalkerTurretBody"
        };
        // its freezing is too strong against players
        private const string FreezeDroneBody = "CopycatDroneBody";
        // a spawn card for each drone's master, made once
        private readonly Dictionary<GameObject, CharacterSpawnCard> spawnCards = new Dictionary<GameObject, CharacterSpawnCard>();
        private readonly HashSet<BodyIndex> droneBodies = new HashSet<BodyIndex>();
        // whether a hostile drone's bullets are hitting right now, which they do while firing
        private bool firingBullets;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return !Helper.IsSimulacrumBossWaveNext() && GetDroneCandidates().Count > 0;
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
            return "Hostile drones spawn instead of monsters, also those of items and survivors. They have a quarter of their health, deal half their normal damage, move at half speed and leave no wreck. A hit of their bullets, which can't be dodged, takes at most 5% of a player's health.";
        }
        public override string GetPopupDescription()
        {
            return "The drones have turned on you! They're weaker and slower than usual, though.";
        }
        public override string GetConditionDescription()
        {
            return "Not a Simulacrum boss wave.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            DamageMultiplier = config.Bind(GetEventConfigName(), "Damage Multiplier", 0.5f, "The damage the hostile drones deal, relative to normal.");
            MoveSpeedMultiplier = config.Bind(GetEventConfigName(), "Move Speed Multiplier", 0.5f, "The hostile drones' movement speed, relative to normal.");
            HealthMultiplier = config.Bind(GetEventConfigName(), "Health Multiplier", 0.25f, "The hostile drones' health, relative to normal. They take more damage instead, so their health bars stay full-sized.");
            MaxBulletDamage = config.Bind(GetEventConfigName(), "Max Bullet Damage", 0.05f, "The most a hit of the hostile drones' bullets, which can't be dodged, takes of a player's health, relative to their full health.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.EntityStates.Drone.DeathState.OnImpactServer += DeathState_OnImpactServer;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.BulletAttack.Fire_FireArgs += BulletAttack_Fire_FireArgs;
        }

        // All drones of the run's expansions with their cost: those of the drone catalog, also those not found on
        // stages, and those that items and survivors bring, without the Freeze Drone, the healing drones and the utility
        // drones, which only help their side, e.g. healing it, the Junk Drone's temporary items or the Cleanup Drone's gold. The Equipment Drone gets one of
        // EquipmentOnly's equipment.
        private static List<(GameObject masterPrefab, int cost)> GetDroneCandidates()
        {
            var candidates = DroneCatalog.allDroneDefs
                .Where(droneDef => droneDef != null && droneDef.masterPrefab != null && droneDef.CheckRequiredExpansionEnabled() && droneDef.droneType != DroneType.Utility && droneDef.droneType != DroneType.Healing
                    && droneDef != DLC3Content.DroneDefs.JunkDrone && droneDef != DLC3Content.DroneDefs.CleanupDrone)
                // about the cost of monsters of similar strength
                .Select(droneDef => (droneDef.masterPrefab, droneDef.tier == ItemTier.Tier3 ? 120 : droneDef.tier == ItemTier.Tier2 ? 40 : 15))
                .ToList();
            foreach (var bodyName in SummonedBodies)
            {
                var bodyIndex = BodyCatalog.FindBodyIndex(bodyName);
                var masterIndex = bodyIndex != BodyIndex.None ? MasterCatalog.FindAiMasterIndexForBody(bodyIndex) : MasterCatalog.MasterIndex.none;
                var masterPrefab = masterIndex != MasterCatalog.MasterIndex.none ? MasterCatalog.GetMasterPrefab(masterIndex) : null;
                if (masterPrefab != null && IsExpansionEnabled(masterPrefab) && IsExpansionEnabled(BodyCatalog.GetBodyPrefab(bodyIndex)))
                {
                    candidates.Add((masterPrefab, 40));
                }
            }
            return candidates
                .Where(candidate => candidate.masterPrefab.GetComponent<CharacterMaster>()?.bodyPrefab?.name != FreezeDroneBody)
                .GroupBy(candidate => candidate.masterPrefab).Select(group => group.First()).ToList();
        }

        private static bool IsExpansionEnabled(GameObject prefab)
        {
            var requirement = prefab != null ? prefab.GetComponent<ExpansionRequirementComponent>() : null;
            return requirement == null || requirement.requiredExpansion == null || (Run.instance != null && Run.instance.IsExpansionEnabled(requirement.requiredExpansion));
        }

        private CharacterSpawnCard GetSpawnCard(GameObject masterPrefab, int cost)
        {
            if (spawnCards.TryGetValue(masterPrefab, out var spawnCard))
            {
                return spawnCard;
            }
            var master = masterPrefab.GetComponent<CharacterMaster>();
            var body = master != null && master.bodyPrefab != null ? master.bodyPrefab.GetComponent<CharacterBody>() : null;
            if (body == null)
            {
                return null;
            }
            spawnCard = ScriptableObject.CreateInstance<CharacterSpawnCard>();
            spawnCard.name = "cscHostile" + masterPrefab.name;
            spawnCard.prefab = masterPrefab;
            spawnCard.sendOverNetwork = true;
            spawnCard.hullSize = body.hullClassification;
            // turrets stand on the ground, the other drones fly
            spawnCard.nodeGraphType = (body.bodyFlags & CharacterBody.BodyFlags.Turret) != 0 || body.GetComponent<CharacterMotor>() != null ? MapNodeGroup.GraphType.Ground : MapNodeGroup.GraphType.Air;
            spawnCard.forbiddenFlags = NodeFlags.NoCharacterSpawn;
            spawnCard.directorCreditCost = cost;
            spawnCards[masterPrefab] = spawnCard;
            droneBodies.Add(body.bodyIndex);
            return spawnCard;
        }

        // Less health as more damage taken: lowering their maximum health would show as a mostly cursed health bar
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && IsActive() && damageInfo != null && self.GetComponent<HostileDrone>() != null)
            {
                damageInfo.damage /= Mathf.Clamp(HealthMultiplier.Value, 0.01f, 1f);
            }
            if (NetworkServer.active && firingBullets && damageInfo != null && self.body != null && self.body.isPlayerControlled)
            {
                // before the crit doubles it
                var attackerBody = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
                var maxDamage = self.fullCombinedHealth * Mathf.Max(0f, MaxBulletDamage.Value) / (damageInfo.crit ? Mathf.Max(1f, attackerBody != null ? attackerBody.critMultiplier : 2f) : 1f);
                damageInfo.damage = Mathf.Min(damageInfo.damage, maxDamage);
            }
            orig(self, damageInfo);
        }

        // Their bullets hit right away, so the players can't dodge them, and the Mega Drone's minigun or a Gunner
        // Turret at a high level would shred them. The bullets deal their damage while being fired.
        private void BulletAttack_Fire_FireArgs(On.RoR2.BulletAttack.orig_Fire_FireArgs orig, BulletAttack self, BulletAttack.FireArgs args)
        {
            var wasFiring = firingBullets;
            firingBullets = wasFiring || (NetworkServer.active && self.owner != null && self.owner.GetComponent<HostileDrone>() != null);
            try
            {
                orig(self, args);
            }
            finally
            {
                firingBullets = wasFiring;
            }
        }

        // weaker, so a swarm of them stays fair
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (!NetworkServer.active || body.master == null || body.teamComponent.teamIndex == TeamIndex.Player || !droneBodies.Contains(body.bodyIndex))
            {
                return;
            }
            var known = body.master.GetComponent<HostileDrone>() != null;
            if (!known && !IsActive())
            {
                return;
            }
            // every body of a hostile drone, also one revived by Dio's Best Friend or the like, even after the event;
            // a destroyed drone's master can be gone by the time its wreck hits the ground
            body.gameObject.EnsureComponent<HostileDrone>();
            Slowdown.Apply(body, 1f, MoveSpeedMultiplier.Value);
            if (known)
            {
                return;
            }
            // its items are in the master's inventory, which the revived body keeps
            body.master.gameObject.AddComponent<HostileDrone>();
            // an item without a name, which lowers the damage by 1% per stack
            var reduceDamage = Mathf.RoundToInt((1f - Mathf.Clamp01(DamageMultiplier.Value)) * 100f);
            if (reduceDamage > 0)
            {
                body.inventory.GiveItemPermanent(DLC4Content.Items.ReduceDamage, reduceDamage);
            }
            if (body.bodyIndex == BodyCatalog.FindBodyIndex("EquipmentDroneBody") && body.inventory.currentEquipmentIndex == EquipmentIndex.None)
            {
                var equipment = EventEquipmentOnly.EventEquipment;
                body.inventory.SetEquipmentIndex(equipment[RoR2Application.rng.RangeInt(0, equipment.Length)].equipmentIndex, body.inventory.activeEquipmentLocation);
            }
        }

        // a hostile drone leaves no broken drone for the players to repair, also after the event
        private void DeathState_OnImpactServer(On.EntityStates.Drone.DeathState.orig_OnImpactServer orig, EntityStates.Drone.DeathState self, Vector3 contactPoint)
        {
            if (self.characterBody != null && self.characterBody.GetComponent<HostileDrone>() != null)
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
            foreach (var (masterPrefab, cost) in GetDroneCandidates())
            {
                var spawnCard = GetSpawnCard(masterPrefab, cost);
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
