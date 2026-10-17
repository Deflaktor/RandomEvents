using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventFocusFire : AbstractEvent
    {
        private ConfigEntry<float> HealthMultiplier;
        private ConfigEntry<float> DamageGrowth;
        // the stacks of Symbiotic Scorpion's debuff this event gave each monster, which also show on its health bar
        private readonly Dictionary<CharacterBody, int> stacks = new Dictionary<CharacterBody, int>();

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
            return "FocusFire";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_FOCUS_FIRE");
        }
        public override string GetDescription()
        {
            return "All monsters have ten times their health, but each hit from a player leaves Symbiotic Scorpion's debuff on them, and each stack makes them take 10% more damage, compounding.";
        }
        public override string GetPopupDescription()
        {
            return "Monsters are very tough, but every hit makes them take more damage. Focus your fire!";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            HealthMultiplier = config.Bind(GetEventConfigName(), "Health Multiplier", 10f, "The monsters' health, relative to normal.");
            DamageGrowth = config.Bind(GetEventConfigName(), "Damage Growth", 0.1f, "How much more damage each stack makes a monster take, compounding: 0.1 means 1.1 to the power of the stacks.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        // monsters spawning during the event are tough too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                Toughen(body);
            }
        }

        private static bool IsMonster(CharacterBody body)
        {
            return body != null && body.teamComponent.teamIndex != TeamIndex.Player && body.teamComponent.teamIndex != TeamIndex.Neutral;
        }

        private void Toughen(CharacterBody body)
        {
            if (!IsMonster(body) || body.inventory == null || stacks.ContainsKey(body))
            {
                return;
            }
            stacks[body] = 0;
            // each Boost Hp adds 10% of the base health
            var boostHp = Mathf.RoundToInt((HealthMultiplier.Value - 1f) * 10f);
            if (boostHp > 0)
            {
                EventLoot.GetInventory(this, body).GiveTemporaryItem(RoR2Content.Items.BoostHp.itemIndex, boostHp);
            }
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            var body = self.body;
            if (!NetworkServer.active || !IsActive() || damageInfo == null || body == null || !stacks.TryGetValue(body, out var count))
            {
                orig(self, damageInfo);
                return;
            }
            damageInfo.damage *= Mathf.Pow(1f + Mathf.Max(0f, DamageGrowth.Value), count);
            orig(self, damageInfo);
            // a hit from a player or their allies, not damage over time
            var attacker = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (!damageInfo.rejected && damageInfo.procCoefficient > 0f && attacker != null && attacker.teamComponent.teamIndex == TeamIndex.Player && self.alive)
            {
                stacks[body] = count + 1;
                body.AddUnmanagedBuff(DLC1Content.Buffs.PermanentDebuff);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            stacks.Clear();
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                Toughen(body);
            }
        }

        public override void Stop()
        {
            foreach (var (body, count) in stacks)
            {
                if (body != null && count > 0)
                {
                    body.RemoveUnmanagedBuff(DLC1Content.Buffs.PermanentDebuff, count);
                }
            }
            stacks.Clear();
            EventLoot.Clear(this);
        }
    }
}
