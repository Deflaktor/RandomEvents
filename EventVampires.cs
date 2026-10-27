using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventVampires : AbstractEvent
    {
        private const float UpdateInterval = 0.25f;
        private ConfigEntry<float> Lifesteal;
        private ConfigEntry<float> HealthLossPerSecond;
        private ConfigEntry<float> EnemyRadius;
        private float updateTimer;

        public override EventTags Tags => EventTags.PlayerHealth;
        public override EventTags ExcludedTags => EventTags.PlayerHealth;
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
            return "Vampires";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_VAMPIRES");
        }
        public override string GetDescription()
        {
            return "Players heal by a fifth of the damage they deal, but lose health while monsters are near.";
        }
        public override string GetPopupDescription()
        {
            return "You heal by a fifth of the damage you deal, but lose health while monsters are near.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            Lifesteal = config.Bind(GetEventConfigName(), "Lifesteal", 0.2f, "The share of the damage players deal that heals them.");
            HealthLossPerSecond = config.Bind(GetEventConfigName(), "Health Loss Per Second", 0.05f, "The share of their maximum health players lose per second while a monster is near. It doesn't kill them.");
            EnemyRadius = config.Bind(GetEventConfigName(), "Enemy Radius", 30f, "How near in meters a monster must be for players to lose health.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            GlobalEventManager.onServerDamageDealt += GlobalEventManager_onServerDamageDealt;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void GlobalEventManager_onServerDamageDealt(DamageReport report)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && report.attackerBody != null && report.attackerBody.isPlayerControlled
                && report.attackerTeamIndex != report.victimTeamIndex && report.attackerBody.healthComponent != null)
            {
                report.attackerBody.healthComponent.Heal(report.damageDealt * Mathf.Max(0f, Lifesteal.Value), report.damageInfo.procChainMask);
            }
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            updateTimer -= Time.fixedDeltaTime;
            if (updateTimer > 0f)
            {
                return;
            }
            updateTimer = UpdateInterval;
            var radiusSqr = EnemyRadius.Value * EnemyRadius.Value;
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                var healthComponent = body.healthComponent;
                if (healthComponent == null || healthComponent.health <= 1f || !IsEnemyNear(body, radiusSqr))
                {
                    continue;
                }
                // health only, without a hit, so it sets off no items and never kills
                var loss = healthComponent.fullHealth * Mathf.Max(0f, HealthLossPerSecond.Value) * UpdateInterval;
                healthComponent.Networkhealth = Mathf.Max(1f, healthComponent.health - loss);
            }
        }

        private static bool IsEnemyNear(CharacterBody body, float radiusSqr)
        {
            for (var teamIndex = TeamIndex.Neutral + 1; teamIndex < TeamIndex.Count; teamIndex++)
            {
                if (teamIndex == body.teamComponent.teamIndex)
                {
                    continue;
                }
                foreach (var member in TeamComponent.GetTeamMembers(teamIndex))
                {
                    if (member.body != null && member.body.healthComponent != null && member.body.healthComponent.alive
                        && (member.body.corePosition - body.corePosition).sqrMagnitude <= radiusSqr)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            updateTimer = 0f;
        }

        public override void Stop()
        {
        }
    }
}
