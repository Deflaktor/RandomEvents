using BepInEx.Configuration;
using HG;
using RoR2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using static RandomEvents.EventSkillsOnly;
using static RoR2.SpawnCard;

namespace RandomEvents
{
    public class EventFuelArray : AbstractEvent
    {
        public override EventTags Tags => EventTags.Equipment | EventTags.PlayerHealth;
        public override EventTags ExcludedTags => EventTags.Equipment | EventTags.PlayerHealth;
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
            return "FuelArray";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_FUEL_ARRAY");
        }
        public override string GetDescription()
        {
            return "Heals all players and gives them a Fuel Array until the event ends, but they take less damage.";
        }
        public override string GetPopupDescription()
        {
            return "You're healed and carry a Fuel Array, which explodes if your health drops below half. You're much tougher meanwhile.";
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
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.isPlayerControlled)
            {
                body.gameObject.EnsureComponent<FuelArrayComponent>();
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            // the players, on whichever team, e.g. their own in Deathmatch
            var teamMembers = CharacterBody.readOnlyInstancesList.Where(body => body != null && body.isPlayerControlled).Select(body => body.teamComponent).ToList();
            foreach (var teamMember in teamMembers)
            {
                if (teamMember.body != null && teamMember.body.isPlayerControlled)
                {
                    teamMember.body.gameObject.EnsureComponent<FuelArrayComponent>();
                    // everyone starts far from the Fuel Array's threshold; set directly, since healing rules such as
                    // Eclipse's reduced healing would leave some players short of full health
                    var healthComponent = teamMember.body.healthComponent;
                    if (healthComponent != null && healthComponent.alive)
                    {
                        healthComponent.Networkhealth = healthComponent.fullHealth;
                        healthComponent.RechargeShieldFull();
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
                    if (body.TryGetComponent(out FuelArrayComponent component))
                    {
                        GameObject.Destroy(component);
                    }
                }
            }
        }

        public class FuelArrayComponent : MonoBehaviour
        {
            TemporaryEquipment equipment;
            private void OnEnable()
            {
                if (TryGetComponent(out CharacterBody body))
                {
                    body.AddBuff(RoR2Content.Buffs.ArmorBoost);
                    equipment = gameObject.AddComponent<TemporaryEquipment>();
                    equipment.SetTemporaryEquipment(RoR2Content.Equipment.QuestVolatileBattery.equipmentIndex);
                }
            }

            private void OnDisable()
            {
                if (TryGetComponent(out CharacterBody body))
                {
                    body.RemoveBuff(RoR2Content.Buffs.ArmorBoost);
                }
                if(equipment != null)
                    Destroy(equipment);
            }
        }
    }
}
