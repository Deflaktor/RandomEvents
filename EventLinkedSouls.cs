using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventLinkedSouls : AbstractEvent
    {
        // the shares of a hit are damage too, which must not be split again
        private bool sharing = false;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return Helper.GetLivingPlayerBodies().Count >= 2;
        }
        public override string GetEventConfigName()
        {
            return "LinkedSouls";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_LINKED_SOULS");
        }
        public override string GetDescription()
        {
            return "Damage a player takes is split evenly among all living players. Each share is reduced by that player's own armor.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (sharing || !NetworkServer.active || damageInfo == null || damageInfo.damage <= 0f || !ModConfig.Enabled.Value || !IsActive())
            {
                orig(self, damageInfo);
                return;
            }
            var victim = self.body;
            // nothing to share while invincible, and players don't pass on what they do to themselves
            if (victim == null || !victim.isPlayerControlled || !self.alive || damageInfo.attacker == victim.gameObject || IsInvincible(self))
            {
                orig(self, damageInfo);
                return;
            }
            var others = Helper.GetLivingPlayerBodies().Where(body => body != victim).ToList();
            if (others.Count == 0)
            {
                orig(self, damageInfo);
                return;
            }
            float share = damageInfo.damage / (others.Count + 1);
            damageInfo.damage = share;
            orig(self, damageInfo);
            sharing = true;
            try
            {
                foreach (var other in others)
                {
                    if (other == null || !other.healthComponent.alive)
                    {
                        continue;
                    }
                    other.healthComponent.TakeDamage(new DamageInfo
                    {
                        damage = share,
                        attacker = damageInfo.attacker,
                        inflictor = damageInfo.inflictor,
                        position = other.corePosition,
                        crit = damageInfo.crit,
                        damageColorIndex = damageInfo.damageColorIndex,
                        procCoefficient = 0f,
                        force = Vector3.zero
                    });
                }
            }
            finally
            {
                sharing = false;
            }
        }

        private static bool IsInvincible(HealthComponent healthComponent)
        {
            return healthComponent.godMode || healthComponent.body.HasBuff(RoR2Content.Buffs.Immune) || healthComponent.body.HasBuff(RoR2Content.Buffs.HiddenInvincibility);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
        }

        public override void Stop()
        {
        }
    }
}
