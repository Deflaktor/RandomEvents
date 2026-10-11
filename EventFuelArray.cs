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
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("EquipmentOnly", StringComparison.InvariantCultureIgnoreCase));
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
            return "All players get a Fuel Array equipment temporarily but take reduced damage from all sources.";
        }
        public override string GetConditionDescription()
        {
            return "Event \"FuelArray\" inactive.";
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
                body.gameObject.EnsureComponent<FuelArrayComponent>();
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
                    teamMember.body.gameObject.EnsureComponent<FuelArrayComponent>();
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
