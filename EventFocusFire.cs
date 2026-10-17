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
        private ConfigEntry<float> DamagePerStack;
        // damage toward each monster's next stack, as a share of its health
        private readonly Dictionary<CharacterBody, float> progress = new Dictionary<CharacterBody, float>();
        private ConfigEntry<float> DecayDelay;
        private ConfigEntry<float> DecayInterval;
        // when each monster last took damage from players, and when it next loses a stack
        private readonly Dictionary<CharacterBody, float> lastDamage = new Dictionary<CharacterBody, float>();
        private readonly Dictionary<CharacterBody, float> nextDecay = new Dictionary<CharacterBody, float>();
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
            return "All monsters have ten times their health, but for every 1% of its health that players deal, a monster gets a stack of Symbiotic Scorpion's debuff, and each stack makes it take 10% more damage, compounding. Few strong hits count as much as many weak ones. After 3 seconds without damage from players, a monster loses a stack every second.";
        }
        public override string GetPopupDescription()
        {
            return "Monsters are very tough, but the more damage they take, the more damage they take. Focus your fire!";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            HealthMultiplier = config.Bind(GetEventConfigName(), "Health Multiplier", 10f, "The monsters' health, relative to normal.");
            DecayDelay = config.Bind(GetEventConfigName(), "Decay Delay", 3f, "Seconds without damage from players after which a monster starts losing stacks.");
            DecayInterval = config.Bind(GetEventConfigName(), "Decay Interval", 1f, "Seconds between two stacks a monster loses.");
            DamagePerStack = config.Bind(GetEventConfigName(), "Damage Per Stack", 0.01f, "Damage that gives a monster a stack, as a share of its health, before the stacks multiply it.");
            DamageGrowth = config.Bind(GetEventConfigName(), "Damage Growth", 0.1f, "How much more damage each stack makes a monster take, compounding: 0.1 means 1.1 to the power of the stacks.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // monsters that players leave alone lose their stacks again
        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive() || Run.instance == null)
            {
                return;
            }
            var now = Run.instance.fixedTime;
            foreach (var body in new List<CharacterBody>(stacks.Keys))
            {
                var count = stacks[body];
                if (body == null || count <= 0 || now < lastDamage.GetValueOrDefault(body) + DecayDelay.Value || now < nextDecay.GetValueOrDefault(body))
                {
                    continue;
                }
                nextDecay[body] = now + Mathf.Max(0.05f, DecayInterval.Value);
                stacks[body] = count - 1;
                body.RemoveUnmanagedBuff(DLC1Content.Buffs.PermanentDebuff);
            }
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
            // the damage before the stacks multiply it, so that they don't feed themselves
            var baseDamage = damageInfo.damage;
            damageInfo.damage *= Mathf.Pow(1f + Mathf.Max(0f, DamageGrowth.Value), count);
            orig(self, damageInfo);
            // Stacks by damage dealt rather than by hits, so survivors that hit rarely but hard aren't at a disadvantage.
            // From players or their allies, also over time.
            var attacker = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (damageInfo.rejected || attacker == null || attacker.teamComponent.teamIndex != TeamIndex.Player || !self.alive || self.fullCombinedHealth <= 0f)
            {
                return;
            }
            lastDamage[body] = Run.instance.fixedTime;
            var share = baseDamage / self.fullCombinedHealth + progress.GetValueOrDefault(body);
            var perStack = Mathf.Max(0.0001f, DamagePerStack.Value);
            var added = Mathf.FloorToInt(share / perStack);
            progress[body] = share - added * perStack;
            if (added > 0)
            {
                stacks[body] = count + added;
                body.AddUnmanagedBuff(DLC1Content.Buffs.PermanentDebuff, added);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            stacks.Clear();
            progress.Clear();
            lastDamage.Clear();
            nextDecay.Clear();
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
            progress.Clear();
            lastDamage.Clear();
            nextDecay.Clear();
            EventLoot.Clear(this);
        }
    }
}
