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

        public override EventTags Tags => EventTags.PlayerMarker;
        public override EventTags ExcludedTags => EventTags.PlayerMarker;
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
            return "TagTeam";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_TAG_TEAM");
        }
        public override string GetDescription()
        {
            return "Only one player deals damage, as much as all players together, e.g. 4 times as much with 4 players, and so do their drones and other allies; nothing else on the players' side does. Every 15 seconds another player is tagged in.";
        }
        public override string GetPopupDescription()
        {
            return "Only the tagged player and their drones deal damage, as much as the whole team. The tag passes every 15 seconds.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            TagInterval = config.Bind(GetEventConfigName(), "Tag Interval", 15f, "Seconds until the next player is tagged in.");
            DamageMultiplierPerPlayer = config.Bind(GetEventConfigName(), "Damage Multiplier Per Player", 1f, "The tagged player's damage is multiplied by this times the number of players.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // Of everything on the players' side, only the tagged player and what belongs to them deal damage, the extra
        // damage too: their drones, turrets and other allies. Other players, theirs, and allies of no one deal none.
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && damageInfo != null && damageInfo.attacker != null && self.body != null
                && TeamComponent.GetObjectTeam(damageInfo.attacker) == TeamIndex.Player && self.body.teamComponent.teamIndex != TeamIndex.Player)
            {
                if (BelongsToTagged(damageInfo.attacker))
                {
                    damageInfo.damage *= Mathf.Max(0f, DamageMultiplierPerPlayer.Value) * GetPlayerCount();
                }
                else
                {
                    // rejected hits set off no items either
                    damageInfo.rejected = true;
                }
            }
            orig(self, damageInfo);
        }

        // the tagged player, or a minion of theirs, also one a minion of theirs summoned
        private bool BelongsToTagged(GameObject attacker)
        {
            var master = tagged != null && attacker.TryGetComponent(out CharacterBody body) ? body.master : null;
            for (int depth = 0; master != null && depth < 10; depth++)
            {
                if (master == tagged.master)
                {
                    return true;
                }
                master = master.minionOwnership != null ? master.minionOwnership.ownerMaster : null;
            }
            return false;
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
