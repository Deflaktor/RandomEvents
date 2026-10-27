using BepInEx.Configuration;
using HG;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Items;
using RoR2.Projectile;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using static RandomEvents.EventHaste;
using static RoR2.HoldoutZoneController;
using static RoR2.SpawnCard;

namespace RandomEvents
{
    public class EventEquipmentOnly : AbstractEvent
    {
        private EquipmentDef commonEquipment;
        public override EventTags Tags => EventTags.EventItems | EventTags.Equipment;
        public override EventTags ExcludedTags => EventTags.Equipment | EventTags.EquipmentSwaps;
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the experimental equipment is all the players fight with
            var experimental = activeOtherEvents.OfType<EventExperimentalEquipment>().FirstOrDefault();
            return !RunArtifactManager.enabledArtifactsEnumerable.Any(e => e == RoR2Content.Artifacts.Enigma)
                && (experimental == null || experimental.Current == null || experimental.Current.CanFight);
        }

        public static EventEquipmentOnly Find(IEnumerable<AbstractEvent> events)
        {
            return events.OfType<EventEquipmentOnly>().FirstOrDefault();
        }

        // ExperimentalEquipment's equipment instead of the event's own
        private EquipmentDef PickEquipment()
        {
            var experimental = RandomEvents.instance.m_activeEvents.OfType<EventExperimentalEquipment>().FirstOrDefault(e => e.Current != null);
            return experimental != null ? experimental.Current.EquipmentDef : commonEquipment;
        }
        public override string GetEventConfigName()
        {
            return "EquipmentOnly";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_EQUIPMENT_ONLY");
        }
        public override string GetDescription()
        {
            return "Disables all skills, but gives all players a random equipment with a short cooldown. Molotovs burst in the air next to flying monsters, dealing 800% damage to all monsters within 6 m. With an experimental equipment, the players get that one.";
        }
        public override string GetPopupDescription()
        {
            return "No skills, just a random equipment with a short cooldown.";
        }
        public override string GetConditionDescription()
        {
            return "Artifact of Enigma inactive, and the experimental equipment is not Foreign Fruit or Gnarled Woodsprite.";
        }
        protected override void AddConfig(ConfigFile config)
        {

        }

        public override void Preload()
        {
            FireEffects.Preload();
        }

        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.Projectile.ProjectileController.Start += ProjectileController_Start;
        }

        // The players' Molotovs, the thrown 6-Pack and its single cocktails, burst in the air next to flying monsters,
        // which their fire on the ground can't reach. The host moves the projectiles, so it alone decides.
        private void ProjectileController_Start(On.RoR2.Projectile.ProjectileController.orig_Start orig, ProjectileController self)
        {
            orig(self);
            if (NetworkServer.active && IsActive() && (self.name.StartsWith("MolotovClusterProjectile") || self.name.StartsWith("MolotovSingleProjectile"))
                && self.owner != null && self.owner.GetComponent<EquipmentOnlyComponent>() != null)
            {
                self.gameObject.AddComponent<MolotovAirburst>();
            }
        }

        public class MolotovAirburst : MonoBehaviour
        {
            private const float Radius = 6f;
            private const float DamageCoefficient = 8f;
            private ProjectileController controller;

            private void Awake()
            {
                controller = GetComponent<ProjectileController>();
            }

            private void FixedUpdate()
            {
                var owner = controller != null && controller.owner != null ? controller.owner.GetComponent<CharacterBody>() : null;
                if (!NetworkServer.active || owner == null)
                {
                    return;
                }
                var position = transform.position;
                foreach (var body in CharacterBody.readOnlyInstancesList)
                {
                    if (body != null && body.isFlying && body.mainHurtBox != null && body.healthComponent != null && body.healthComponent.alive
                        && body.teamComponent.teamIndex != owner.teamComponent.teamIndex && body.teamComponent.teamIndex != TeamIndex.Neutral
                        && (ClosestPoint(body.mainHurtBox.collider, position) - position).sqrMagnitude <= Radius * Radius)
                    {
                        Burst(owner, position);
                        return;
                    }
                }
            }

            // where the blast reaches the monster, which a non-convex mesh can't tell exactly
            private static Vector3 ClosestPoint(Collider collider, Vector3 position)
            {
                return collider is MeshCollider mesh && !mesh.convex ? collider.bounds.ClosestPoint(position) : collider.ClosestPoint(position);
            }

            // instead of its fire on the ground, or splitting into single cocktails
            private void Burst(CharacterBody owner, Vector3 position)
            {
                FireEffects.SpawnEffect(FireEffects.Ignition, position, Radius);
                new BlastAttack
                {
                    attacker = owner.gameObject,
                    inflictor = gameObject,
                    teamIndex = owner.teamComponent.teamIndex,
                    baseDamage = owner.damage * DamageCoefficient,
                    crit = TryGetComponent(out ProjectileDamage projectileDamage) ? projectileDamage.crit : owner.RollCrit(),
                    radius = Radius,
                    falloffModel = BlastAttack.FalloffModel.None,
                    position = position,
                    procCoefficient = 1f,
                    attackerFiltering = AttackerFiltering.NeverHitSelf,
                }.Fire();
                Destroy(gameObject);
            }
        }

        public override bool SuppressesHereticTransformation => true;

        // the equipment the event gives, also used by other events
        public static EquipmentDef[] EventEquipment => new[]
        {
            RoR2Content.Equipment.BFG, DLC1Content.Equipment.Molotov, RoR2Content.Equipment.CommandMissile,
            RoR2Content.Equipment.Lightning, RoR2Content.Equipment.Saw
        };

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.isPlayerControlled)
            {
                var equipment = body.gameObject.EnsureComponent<EquipmentOnlyComponent>();
                equipment.Setup(PickEquipment());
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            WeightedSelection<EquipmentDef> weightedSelection = new WeightedSelection<EquipmentDef>();
            weightedSelection.AddChoice(RoR2Content.Equipment.BFG, 3f);
            weightedSelection.AddChoice(DLC1Content.Equipment.Molotov, 2f);
            weightedSelection.AddChoice(RoR2Content.Equipment.CommandMissile, 1f);
            weightedSelection.AddChoice(RoR2Content.Equipment.Lightning, 1f);
            weightedSelection.AddChoice(RoR2Content.Equipment.Saw, 1f);
            weightedSelection.AddChoice(null, 3f);
            commonEquipment = weightedSelection.Evaluate(RoR2Application.rng.nextNormalizedFloat);

            // the players, on whichever team, e.g. their own in Deathmatch
            var teamMembers = CharacterBody.readOnlyInstancesList.Where(body => body != null && body.isPlayerControlled).Select(body => body.teamComponent).ToList();
            foreach (var teamMember in teamMembers)
            {
                if (teamMember.body != null && teamMember.body.isPlayerControlled)
                {
                    var equipment = teamMember.body.gameObject.EnsureComponent<EquipmentOnlyComponent>();
                    equipment.Setup(PickEquipment());
                }
            }
        }

        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    if (body.TryGetComponent(out EquipmentOnlyComponent casinoItem))
                    {
                        GameObject.Destroy(casinoItem);
                    }
                }
            }
        }

        public class EquipmentOnlyComponent : MonoBehaviour
        {
            TemporaryInventory inventory;
            TemporaryEquipment equipment;
            int timer = 0;
            // a body that spawns as the event starts is set up both by Start and by onBodyStartGlobal
            bool isSetUp = false;
            public void Setup(EquipmentDef equipmentDef)
            {
                if (isSetUp)
                {
                    return;
                }
                var body = GetComponent<CharacterBody>();
                if (body != null && body.inventory != null)
                {
                    isSetUp = true;
                    if (inventory == null)
                    {
                        inventory = gameObject.AddComponent<TemporaryInventory>();
                        equipment = gameObject.AddComponent<TemporaryEquipment>();
                    }
                    if(equipmentDef == null)
                    {
                        WeightedSelection<EquipmentDef> weightedSelection = new WeightedSelection<EquipmentDef>();
                        weightedSelection.AddChoice(RoR2Content.Equipment.BFG, 1.5f);
                        weightedSelection.AddChoice(DLC1Content.Equipment.Molotov, 1f);
                        weightedSelection.AddChoice(RoR2Content.Equipment.CommandMissile, 1f);
                        weightedSelection.AddChoice(RoR2Content.Equipment.Lightning, 1f);
                        weightedSelection.AddChoice(RoR2Content.Equipment.Saw, 1f);
                        equipmentDef = weightedSelection.Evaluate(RoR2Application.rng.nextNormalizedFloat);
                    }

                    if (equipmentDef == RoR2Content.Equipment.BFG) { 
                        equipment.cooldownScale = 6f / RoR2Content.Equipment.BFG.cooldown;
                    } else if(equipmentDef == DLC1Content.Equipment.Molotov) {
                        equipment.cooldownScale = 1.0f / DLC1Content.Equipment.Molotov.cooldown;
                    } else if (equipmentDef == RoR2Content.Equipment.Lightning) {
                        equipment.cooldownScale = 0.5f / RoR2Content.Equipment.Lightning.cooldown;
                    } else { 
                        equipment.cooldownScale = 2f / equipmentDef.cooldown;
                    }

                    equipment.doNotDisableEquipment = true;
                    equipment.SetTemporaryEquipment(equipmentDef.equipmentIndex);
                    body.AddBuff(DLC2Content.Buffs.DisableAllSkills);
                    //inventory.GiveTemporaryItem(RoR2Content.Items.LunarPrimaryReplacement.itemIndex, 499);
                    //inventory.GiveTemporaryItem(RoR2Content.Items.LunarSecondaryReplacement.itemIndex, 199);
                    //inventory.GiveTemporaryItem(RoR2Content.Items.LunarUtilityReplacement.itemIndex, 199);
                    //inventory.GiveTemporaryItem(RoR2Content.Items.LunarSpecialReplacement.itemIndex, 124);
                    inventory.GiveTemporaryItem(RoR2Content.Items.Feather.itemIndex, 3);
                    //PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.LunarPrimaryReplacement.itemIndex);
                    //PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.LunarSecondaryReplacement.itemIndex);
                    //PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.LunarUtilityReplacement.itemIndex);
                    //PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.LunarSpecialReplacement.itemIndex);
                    ItemOrb.Fly(body.transform.position, body.gameObject, RoR2Content.Items.Feather.itemIndex);
                    body.inventory.RestockEquipmentCharges(body.inventory.activeEquipmentLocation, 1);
                }
            }

            private void OnDisable()
            {
                Destroy(inventory);
                Destroy(equipment);
                var body = GetComponent<CharacterBody>();
                if (body != null && body.inventory != null)
                {
                    body.RemoveBuff(DLC2Content.Buffs.DisableAllSkills);
                }
            }

            private void FixedUpdate()
            {
                timer++;
                if (timer > 50)
                {
                    var body = GetComponent<CharacterBody>();
                    if (body != null && body.skillLocator != null)
                    {
                        // body.skillLocator.DeductCooldownFromAllSkillsServer(-2f);
                    }
                    timer = 0;
                }
            }
        }
    }
}
