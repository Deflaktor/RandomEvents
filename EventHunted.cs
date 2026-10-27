using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventHunted : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters;
        public override EventTags ExcludedTags => EventTags.HiddenPlayers;
        private const float RetargetInterval = 0.5f;
        private CharacterBody hunted;
        private float retargetTimer;

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
            return "Hunted";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_HUNTED");
        }
        public override string GetDescription()
        {
            return "All monsters chase one random player.";
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
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive())
            {
                return;
            }
            retargetTimer -= Time.fixedDeltaTime;
            if (retargetTimer > 0f)
            {
                return;
            }
            retargetTimer = RetargetInterval;
            if (hunted == null || !hunted.healthComponent.alive)
            {
                ChooseHunted();
            }
            if (hunted != null)
            {
                TargetHunted();
            }
        }

        private void ChooseHunted()
        {
            var candidates = Helper.GetLivingPlayerBodies();
            hunted = candidates.Count > 0 ? candidates[RoR2Application.rng.RangeInt(0, candidates.Count)] : null;
            if (hunted != null)
            {
                ChatHelper.SendEventMessage("EVENT_HUNTED_TARGET", ChatHelper.GetColoredPlayerName(hunted));
            }
        }

        // the monster AI runs on the host
        private void TargetHunted()
        {
            foreach (var master in CharacterMaster.readOnlyInstancesList)
            {
                if (master == null || master.teamIndex == TeamIndex.Player || master.teamIndex == TeamIndex.Neutral)
                {
                    continue;
                }
                foreach (var ai in master.AiComponents)
                {
                    if (ai != null && ai.currentEnemy != null)
                    {
                        ai.currentEnemy.gameObject = hunted.gameObject;
                        ai.currentEnemy.bestHurtBox = hunted.mainHurtBox;
                        ai.enemyAttention = ai.enemyAttentionDuration;
                    }
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            retargetTimer = 0f;
            hunted = null;
        }

        public override void Stop()
        {
            hunted = null;
        }
    }
}
