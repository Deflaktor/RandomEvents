using BepInEx.Configuration;
using HG;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Items;
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
        // a Molotov's fire stays on the ground, useless against mostly flying monsters
        private static bool molotovUseful = true;
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            var enigmaArtifactActive = RunArtifactManager.enabledArtifactsEnumerable.Any(e => e == RoR2Content.Artifacts.Enigma)
                || activeOtherEvents.Any(e => e is EventRandomArtifact randomArtifact && randomArtifact.TurnsOn(RoR2Content.Artifacts.Enigma));
            if (enigmaArtifactActive)
            {
                return false;
            }
            return !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("SkillsOnly", StringComparison.InvariantCultureIgnoreCase)
                                            || e.GetEventConfigName().Equals("FuelArray", StringComparison.InvariantCultureIgnoreCase));
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
            return "Disables all skills, but gives all players a random equipment with a short cooldown, or a Crowdfunder that fires nonstop for free.";
        }
        public override string GetConditionDescription()
        {
            return "Events \"SkillsOnly\", \"FuelArray\" inactive. Artifact of Enigma inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {

        }

        public override void Preload()
        {
        }

        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.EntityStates.GoldGat.GoldGatFire.FireBullet += GoldGatFire_FireBullet;
        }

        // the Crowdfunder costs no gold: the host takes it for each bullet, and gives it back
        private void GoldGatFire_FireBullet(On.EntityStates.GoldGat.GoldGatFire.orig_FireBullet orig, EntityStates.GoldGat.GoldGatFire self)
        {
            var master = self.bodyMaster;
            var body = master != null ? master.GetBody() : null;
            if (!NetworkServer.active || !IsActive() || body == null || body.GetComponent<EquipmentOnlyComponent>() == null)
            {
                orig(self);
                return;
            }
            var money = master.money;
            orig(self);
            master.money = money;
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
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.teamComponent.teamIndex == TeamIndex.Player && body.isPlayerControlled)
            {
                var equipment = body.gameObject.EnsureComponent<EquipmentOnlyComponent>();
                equipment.Setup(commonEquipment);
            }
        }

        // whether flying monsters make up most of the spawn pool's weight
        private static bool MostlyFlyingMonsters()
        {
            var choices = MonsterPool.GetChoices().Where(choice => choice.value != null && choice.value.spawnCard != null).ToList();
            var flying = choices.Where(choice => choice.value.spawnCard.nodeGraphType == RoR2.Navigation.MapNodeGroup.GraphType.Air).Sum(choice => choice.weight);
            return flying > choices.Sum(choice => choice.weight) / 2f;
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            molotovUseful = !MostlyFlyingMonsters() && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("DroneUprising", StringComparison.InvariantCultureIgnoreCase));
            WeightedSelection<EquipmentDef> weightedSelection = new WeightedSelection<EquipmentDef>();
            weightedSelection.AddChoice(RoR2Content.Equipment.BFG, 3f);
            weightedSelection.AddChoice(DLC1Content.Equipment.Molotov, molotovUseful ? 2f : 0f);
            weightedSelection.AddChoice(RoR2Content.Equipment.CommandMissile, 1f);
            weightedSelection.AddChoice(RoR2Content.Equipment.Lightning, 1f);
            weightedSelection.AddChoice(RoR2Content.Equipment.Saw, 1f);
            weightedSelection.AddChoice(RoR2Content.Equipment.GoldGat, 1f);
            weightedSelection.AddChoice(null, 3f);
            commonEquipment = weightedSelection.Evaluate(RoR2Application.rng.nextNormalizedFloat);

            ReadOnlyCollection<TeamComponent> teamMembers = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var teamMember in teamMembers)
            {
                if (teamMember.body != null && teamMember.body.isPlayerControlled)
                {
                    var equipment = teamMember.body.gameObject.EnsureComponent<EquipmentOnlyComponent>();
                    equipment.Setup(commonEquipment);
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
            bool crowdfunder = false;
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
                        weightedSelection.AddChoice(DLC1Content.Equipment.Molotov, molotovUseful ? 1f : 0f);
                        weightedSelection.AddChoice(RoR2Content.Equipment.CommandMissile, 1f);
                        weightedSelection.AddChoice(RoR2Content.Equipment.Lightning, 1f);
                        weightedSelection.AddChoice(RoR2Content.Equipment.Saw, 1f);
                        weightedSelection.AddChoice(RoR2Content.Equipment.GoldGat, 1f);
                        equipmentDef = weightedSelection.Evaluate(RoR2Application.rng.nextNormalizedFloat);
                    }

                    if (equipmentDef == RoR2Content.Equipment.BFG) { 
                        equipment.cooldownScale = 6f / RoR2Content.Equipment.BFG.cooldown;
                    } else if(equipmentDef == DLC1Content.Equipment.Molotov) {
                        equipment.cooldownScale = 1.0f / DLC1Content.Equipment.Molotov.cooldown;
                    } else if (equipmentDef == RoR2Content.Equipment.Lightning) {
                        equipment.cooldownScale = 0.5f / RoR2Content.Equipment.Lightning.cooldown;
                    } else if (equipmentDef == RoR2Content.Equipment.GoldGat) {
                        // fired nonstop by the player's own game while they hold a Gesture of the Drowned
                        crowdfunder = true;
                        inventory.GiveTemporaryItem(RoR2Content.Items.AutoCastEquipment.itemIndex);
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
                    PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.Feather.itemIndex);
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
                // the Crowdfunder stops without gold
                var master = crowdfunder && NetworkServer.active ? GetComponent<CharacterBody>()?.master : null;
                if (master != null && master.money == 0)
                {
                    master.money = 1;
                }
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
