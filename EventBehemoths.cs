using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventBehemoths : AbstractEvent
    {
        private ConfigEntry<int> PlayerBehemoths;
        private ConfigEntry<int> MonsterBehemoths;
        private ConfigEntry<float> MonsterDamageMultiplier;
        private readonly HashSet<CharacterBody> armed = new HashSet<CharacterBody>();

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
            return "Behemoths";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_BEHEMOTHS");
        }
        public override string GetDescription()
        {
            return "All players get 3 Brilliant Behemoths and every monster gets one, but monsters deal half damage.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            PlayerBehemoths = config.Bind(GetEventConfigName(), "Player Behemoths", 3, "How many Brilliant Behemoths each player gets.");
            MonsterBehemoths = config.Bind(GetEventConfigName(), "Monster Behemoths", 1, "How many Brilliant Behemoths each monster gets.");
            MonsterDamageMultiplier = config.Bind(GetEventConfigName(), "Monster Damage Multiplier", 0.5f, "The damage monsters deal during the event, relative to normal.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        // also those who spawn or respawn during the event
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveBehemoths(body);
            }
        }

        private void GiveBehemoths(CharacterBody body)
        {
            if (body == null || body.inventory == null || armed.Contains(body) || body.teamComponent.teamIndex == TeamIndex.Neutral)
            {
                return;
            }
            var count = body.isPlayerControlled ? PlayerBehemoths.Value : body.teamComponent.teamIndex != TeamIndex.Player ? MonsterBehemoths.Value : 0;
            if (count <= 0)
            {
                return;
            }
            armed.Add(body);
            EventLoot.GetInventory(this, body).GiveTemporaryItem(RoR2Content.Items.Behemoth.itemIndex, count);
            if (body.isPlayerControlled)
            {
                PurchaseInteraction.CreateItemTakenOrb(body.corePosition, body.gameObject, RoR2Content.Items.Behemoth.itemIndex);
            }
        }

        // all of the monsters' damage, also their blasts
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && IsActive() && damageInfo != null && damageInfo.attacker != null && damageInfo.attacker.TryGetComponent(out CharacterBody attackerBody)
                && !attackerBody.isPlayerControlled && attackerBody.teamComponent.teamIndex != TeamIndex.Player && attackerBody.teamComponent.teamIndex != TeamIndex.Neutral)
            {
                damageInfo.damage *= Mathf.Max(0f, MonsterDamageMultiplier.Value);
            }
            orig(self, damageInfo);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            armed.Clear();
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveBehemoths(body);
            }
        }

        public override void Stop()
        {
            EventLoot.Clear(this);
            armed.Clear();
        }
    }
}
