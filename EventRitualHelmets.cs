using BepInEx.Configuration;
using RoR2;
using RoR2.CharacterAI;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventRitualHelmets : AbstractEvent
    {
        private ConfigEntry<float> DamageReduction;
        private ConfigEntry<bool> ForceSprint;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the Ritual Helmet comes with that expansion
            return DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4);
        }
        public override string GetEventConfigName()
        {
            return "RitualHelmets";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_RITUAL_HELMETS");
        }
        public override string GetDescription()
        {
            return "All monsters get a Ritual Helmet and sprint wherever they can, so that it charges. Players take 66% less damage.";
        }
        public override string GetPopupDescription()
        {
            return "All monsters wear a Ritual Helmet. You take 66% less damage.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            DamageReduction = config.Bind(GetEventConfigName(), "Damage Reduction", 0.66f, "How much less damage players take during the event.");
            ForceSprint = config.Bind(GetEventConfigName(), "Force Sprint", true, "Makes monsters with the helmet sprint wherever they can. The helmet only charges while sprinting, which most monsters never do on their own.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.CharacterAI.BaseAI.UpdateBodyInputs += BaseAI_UpdateBodyInputs;
        }

        // monsters spawning during the event get a helmet too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveHelmet(body);
            }
        }

        private void GiveHelmet(CharacterBody body)
        {
            if (body != null && body.inventory != null && body.teamComponent.teamIndex != TeamIndex.Player && body.teamComponent.teamIndex != TeamIndex.Neutral
                && body.inventory.GetItemCountEffective(DLC4Content.Items.SprintDamage) <= 0)
            {
                EventLoot.GetInventory(this, body).GiveTemporaryItem(DLC4Content.Items.SprintDamage.itemIndex);
            }
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && IsActive() && damageInfo != null && self.body != null && self.body.isPlayerControlled)
            {
                damageInfo.damage *= Mathf.Clamp01(1f - DamageReduction.Value);
            }
            orig(self, damageInfo);
        }

        private void BaseAI_UpdateBodyInputs(On.RoR2.CharacterAI.BaseAI.orig_UpdateBodyInputs orig, BaseAI self)
        {
            orig(self);
            if (IsActive() && ForceSprint.Value && self.body != null && self.bodyInputBank != null && self.body.inventory != null
                && self.body.inventory.GetItemCountEffective(DLC4Content.Items.SprintDamage) > 0)
            {
                // the body only sprints when it moves towards where it aims
                self.bodyInputBank.sprint.down = true;
                self.bodyInputBank.rawSprintInput.down = true;
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveHelmet(body);
            }
        }

        public override void Stop()
        {
            EventLoot.Clear(this);
        }
    }
}
