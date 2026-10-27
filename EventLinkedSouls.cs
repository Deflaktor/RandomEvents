using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventLinkedSouls : AbstractEvent
    {
        // the shares of a hit are damage too, which must not be split again
        // while a player's damage is passed on to the others, as hits that keep its attacker
        public static bool Sharing { get; private set; }
        private const float LinkUpdateInterval = 0.25f;
        private readonly PlayerTethers tethers = new PlayerTethers();
        private float linkTimer;

        public override EventTags Tags => EventTags.PlayerDamage;
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
            return "Damage a player takes is split among all living players. Tethers link the players.";
        }
        public override string GetPopupDescription()
        {
            return "Your souls are linked: damage you take is split among all living players.";
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
            PlayerTethers.Preload();
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive())
            {
                return;
            }
            linkTimer -= Time.fixedDeltaTime;
            if (linkTimer <= 0f)
            {
                linkTimer = LinkUpdateInterval;
                tethers.Update(Helper.GetLivingPlayerBodies());
            }
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            // the environment's damage, e.g. the void fog, hits each player in full, and HotPotato's bomb its carrier
            if (Sharing || EventHotPotato.KillingCarrier || !NetworkServer.active || damageInfo == null || damageInfo.damage <= 0f || !ModConfig.Enabled.Value || !IsActive() || Helper.IsHazard(damageInfo))
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
            Sharing = true;
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
                Sharing = false;
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
            tethers.Clear();
            linkTimer = 0f;
        }

        public override void Stop()
        {
            tethers.Clear();
        }
    }
}
