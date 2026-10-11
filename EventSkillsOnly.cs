using BepInEx.Configuration;
using HG;
using RoR2;
using RoR2.Items;
using System;
using System.Collections;
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
    public class EventSkillsOnly : AbstractEvent
    {

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            var equipmentOnlyOrMonsterTransformActive = activeOtherEvents.Any(e => e.GetEventConfigName().Equals("EquipmentOnly", StringComparison.InvariantCultureIgnoreCase) ||
                                                                                   e.GetEventConfigName().Equals("MonsterTransform", StringComparison.InvariantCultureIgnoreCase));
            // none of the characters must have the Bandolier item, as it breaks the concept of this event
            var hasBandolier = false;
            foreach (PlayerCharacterMasterController pc in PlayerCharacterMasterController.instances)
            {
                if (pc.isConnected)
                {
                    if (pc.master?.inventory.GetItemCountEffective(RoR2Content.Items.Bandolier) >= 1)
                    {
                        hasBandolier = true;
                        break;
                    }
                }
            }

            return !equipmentOnlyOrMonsterTransformActive && !hasBandolier;
        }
        public override string GetEventConfigName()
        {
            return "SkillsOnly";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SKILLS_ONLY");
        }
        public override string GetDescription()
        {
            return "Makes players unable to use primary skills but other skills have reduced cooldown.";
        }
        public override string GetConditionDescription()
        {
            return "Events \"EquipmentOnly\", \"MonsterTransform\" inactive and none of the players have a bandolier in their inventories (workaround to make it work server-side only).";
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
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.teamComponent.teamIndex == TeamIndex.Player && body.isPlayerControlled)
            {
                body.gameObject.EnsureComponent<SkillsOnlyComponent>();
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            ReadOnlyCollection<TeamComponent> teamMembers = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var teamMember in teamMembers)
            {
                if (teamMember.body != null && teamMember.body.isPlayerControlled)
                {
                    var skillsOnlyComponent = teamMember.body.gameObject.EnsureComponent<SkillsOnlyComponent>();
                    var fuelArrayEventActive = activeOtherEvents.Any(e => e.GetEventConfigName().Equals("FuelArray", StringComparison.InvariantCultureIgnoreCase));
                    if (!fuelArrayEventActive)
                    {
                        skillsOnlyComponent.SetupEquipment();
                    }
                }
            }
        }

        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    if (body.TryGetComponent(out SkillsOnlyComponent casinoItem))
                    {
                        GameObject.Destroy(casinoItem);
                    }
                }
            }
        }

        public class SkillsOnlyComponent : MonoBehaviour
        {
            TemporaryInventory inventory;
            TemporaryEquipment equipment;
            int timer;
            private void OnEnable()
            {
                var body = GetComponent<CharacterBody>();
                if (body != null && body.inventory != null)
                {
                    if (inventory == null)
                    {
                        inventory = gameObject.AddComponent<TemporaryInventory>();
                    }
                    inventory.GiveTemporaryItem(RoR2Content.Items.SecondarySkillMagazine.itemIndex, 3);
                    PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.SecondarySkillMagazine.itemIndex);
                    inventory.GiveTemporaryItem(RoR2Content.Items.UtilitySkillMagazine.itemIndex, 1);
                    PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.UtilitySkillMagazine.itemIndex);
                    inventory.GiveTemporaryItem(RoR2Content.Items.AlienHead.itemIndex, 10);
                    PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.AlienHead.itemIndex);
                    RandomEvents.instance.StartCoroutine(DelayGivePrimaryReplacement(body));
                }
            }
            IEnumerator DelayGivePrimaryReplacement(CharacterBody body)
            {
                yield return new WaitForSeconds(2.0f);
                inventory.GiveTemporaryItem(RoR2Content.Items.LunarPrimaryReplacement.itemIndex, 9000);
                PurchaseInteraction.CreateItemTakenOrb(body.transform.position, body.gameObject, RoR2Content.Items.LunarPrimaryReplacement.itemIndex);
            }

            public void SetupEquipment()
            {
                var body = GetComponent<CharacterBody>();
                if (body != null && body.inventory != null && equipment == null)
                {
                    equipment = gameObject.AddComponent<TemporaryEquipment>();
                    equipment.SetTemporaryEquipment(EquipmentIndex.None);
                }
            }

            private void OnDisable()
            {
                Destroy(inventory);
                if(equipment != null)
                    Destroy(equipment);
            }

            private void FixedUpdate()
            {
                timer++;
                if (timer > 50)
                {
                    var body = GetComponent<CharacterBody>();
                    if (body != null && body.skillLocator != null)
                    {
                        body.skillLocator.DeductCooldownFromAllSkillsServer(1f);
                    }
                    timer = 0;
                }
            }
        }
    }
}
