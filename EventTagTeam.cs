using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventTagTeam : AbstractEvent
    {
        private ConfigEntry<float> TagInterval;
        private ConfigEntry<float> DamageMultiplierPerPlayer;
        private readonly BuffTracker buffs = new BuffTracker();
        private CharacterBody tagged;
        private float tagTimer;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Hot Potato marks its bomb carrier the same way
            return Helper.GetLivingPlayerBodies().Count >= 2
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("HotPotato", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "TagTeam";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_TAG_TEAM");
        }
        public override string GetDescription()
        {
            return "Only one player deals damage, 1.5 times as much for each player. Every 15 seconds another player is tagged in.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive and event \"HotPotato\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            TagInterval = config.Bind(GetEventConfigName(), "Tag Interval", 15f, "Seconds until the next player is tagged in.");
            DamageMultiplierPerPlayer = config.Bind(GetEventConfigName(), "Damage Multiplier Per Player", 1.5f, "The tagged player's damage is multiplied by this times the number of players.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // the tagged player's minions deal the extra damage too, other players' minions none
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && damageInfo != null && damageInfo.attacker != null && self.body != null
                && damageInfo.attacker.TryGetComponent(out CharacterBody attackerBody) && attackerBody.teamComponent.teamIndex != self.body.teamComponent.teamIndex)
            {
                var master = attackerBody.master;
                if (master != null && master.minionOwnership.ownerMaster != null)
                {
                    master = master.minionOwnership.ownerMaster;
                }
                if (master != null && master.playerCharacterMasterController != null)
                {
                    if (tagged != null && master == tagged.master)
                    {
                        damageInfo.damage *= Mathf.Max(0f, DamageMultiplierPerPlayer.Value) * GetPlayerCount();
                    }
                    else
                    {
                        // rejected hits set off no items either
                        damageInfo.rejected = true;
                    }
                }
            }
            orig(self, damageInfo);
        }

        private static int GetPlayerCount()
        {
            return PlayerCharacterMasterController.instances.Count(pc => pc.isConnected);
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            tagTimer -= Time.fixedDeltaTime;
            if (tagTimer <= 0f || tagged == null || !tagged.healthComponent.alive)
            {
                TagNext();
            }
        }

        // another living player than the current one, if there is one
        private void TagNext()
        {
            var candidates = Helper.GetLivingPlayerBodies();
            var others = candidates.Where(body => body != tagged).ToList();
            if (others.Count > 0)
            {
                candidates = others;
            }
            tagTimer = Mathf.Max(1f, TagInterval.Value);
            if (candidates.Count == 0 || (candidates.Count == 1 && candidates[0] == tagged))
            {
                return;
            }
            buffs.RemoveAll();
            tagged = candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
            // Essence of Heresy's marks show everyone who is tagged in
            buffs.Add(tagged, RoR2Content.Buffs.LunarDetonationCharge);
            ChatHelper.SendEventMessage("EVENT_TAG_TEAM_TAGGED", ChatHelper.GetColoredPlayerName(tagged), Mathf.RoundToInt(tagTimer));
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            tagged = null;
            TagNext();
        }

        public override void Stop()
        {
            buffs.RemoveAll();
            tagged = null;
        }
    }
}
