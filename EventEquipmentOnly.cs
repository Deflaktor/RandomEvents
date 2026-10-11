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
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            var enigmaArtifactActive = RunArtifactManager.enabledArtifactsEnumerable.Any(e => e == RoR2Content.Artifacts.Enigma);
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
            return "Gives all players a random equipment temporarily with reduced cooldown but disables all skills.";
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
            IL.RoR2.CharacterMaster.OnInventoryChanged += CharacterMaster_OnInventoryChanged;
        }

        private void CharacterMaster_OnInventoryChanged(MonoMod.Cil.ILContext il)
        {
            // Skip the whole Heretic transformation while the event is active, not just TransformBody:
            // since 1.5.0 the block also changes the original body and body handling before that call.
            ILCursor c = new ILCursor(il);
            var skipHeretic = c.DefineLabel();
            c.GotoNext(
            x => x.MatchLdstr("HereticBody"),
            x => x.MatchCall(typeof(BodyCatalog), nameof(BodyCatalog.FindBodyPrefab))
            );
            var end = c.Clone();
            end.GotoNext(
            x => x.MatchLdarg(0),
            x => x.MatchCall<CharacterMaster>("UpdateMoneyBasedItems")
            );
            end.MarkLabel(skipHeretic);
            c.EmitDelegate<Func<bool>>(() => ModConfig.Enabled.Value && NetworkServer.active && IsActive());
            c.Emit(OpCodes.Brtrue, skipHeretic);
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.teamComponent.teamIndex == TeamIndex.Player && body.isPlayerControlled)
            {
                var equipment = body.gameObject.EnsureComponent<EquipmentOnlyComponent>();
                equipment.Setup(commonEquipment);
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
            commonEquipment = weightedSelection.Evaluate(UnityEngine.Random.value);

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
            public void Setup(EquipmentDef equipmentDef)
            {
                var body = GetComponent<CharacterBody>();
                if (body != null && body.inventory != null)
                {
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
                        equipmentDef = weightedSelection.Evaluate(UnityEngine.Random.value);
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
