using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventEgocentrism : AbstractEvent
    {
        private ConfigEntry<float> DamageTakenMultiplier;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Egocentrism comes with that expansion
            return DLC1Content.DLC1 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC1Content.DLC1);
        }
        public override string GetEventConfigName()
        {
            return "Egocentrism";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_EGOCENTRISM");
        }
        public override string GetDescription()
        {
            return "All monsters get an Egocentrism, but players take 75% less damage.";
        }
        public override string GetPopupDescription()
        {
            return "All monsters have an Egocentrism. You take 75% less damage.";
        }
        public override string GetConditionDescription()
        {
            return "Survivors of the Void is enabled.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            DamageTakenMultiplier = config.Bind(GetEventConfigName(), "Damage Taken Multiplier", 0.25f, "The damage players take during the event, relative to normal.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        // monsters spawning during the event get one too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveEgocentrism(body);
            }
        }

        // a temporary one, which doesn't turn the monster's items into more Egocentrism
        private void GiveEgocentrism(CharacterBody body)
        {
            if (body != null && body.inventory != null && body.teamComponent.teamIndex != TeamIndex.Player && body.teamComponent.teamIndex != TeamIndex.Neutral
                && body.inventory.GetItemCountEffective(DLC1Content.Items.LunarSun) <= 0)
            {
                EventLoot.GetInventory(this, body).GiveTemporaryItem(DLC1Content.Items.LunarSun.itemIndex);
            }
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && IsActive() && damageInfo != null && self.body != null && self.body.isPlayerControlled)
            {
                damageInfo.damage *= Mathf.Max(0f, DamageTakenMultiplier.Value);
            }
            orig(self, damageInfo);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveEgocentrism(body);
            }
        }

        public override void Stop()
        {
            EventLoot.Clear(this);
        }
    }
}
