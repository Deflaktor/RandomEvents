using BepInEx.Configuration;
using RoR2;
using RoR2.CharacterAI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // One player has a role, which passes on to the player who touches them, or whom they touch. The events decide what
    // the role does, which players are invincible, whose hits count and whom the monsters attack.
    public abstract class TagEvent : AbstractEvent
    {
        private const float RetargetInterval = 0.5f;
        private ConfigEntry<float> TouchDistance;
        private ConfigEntry<float> TagBackDelay;
        private readonly BuffTracker buffs = new BuffTracker();
        protected CharacterBody tagged;
        private float tagBackTimer;
        private float retargetTimer;

        public override EventTags Tags => EventTags.PlayerMarker | EventTags.MonsterTargets | EventTags.PlayerDamage;
        // the monsters must see the players to go after the right ones
        public override EventTags ExcludedTags => EventTags.PlayerMarker | EventTags.MonsterTargets | EventTags.PlayerDamage | EventTags.HiddenPlayers;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return Helper.GetLivingPlayerBodies().Count >= 2;
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            TouchDistance = config.Bind(GetEventConfigName(), "Touch Distance", 1.5f, "How close two players must get to touch, in meters between their edges.");
            TagBackDelay = config.Bind(GetEventConfigName(), "Tag Back Delay", 2f, "Seconds after the role passed on before it can pass on again.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        protected abstract string TaggedMessageToken { get; }
        protected abstract bool IsInvincible(CharacterBody player);
        // the factor for the hits on monsters of a player or their drones and other allies; 0 for none
        protected abstract float DamageFactor(CharacterBody player, int playerCount);
        // whom a monster attacks instead of the player it is after, or null to leave it be
        protected abstract CharacterBody RedirectMonster(CharacterBody current, List<CharacterBody> players);

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && damageInfo != null && self.body != null)
            {
                if (self.body.isPlayerControlled && IsInvincible(self.body))
                {
                    damageInfo.rejected = true;
                }
                else if (damageInfo.attacker != null && TeamComponent.GetObjectTeam(damageInfo.attacker) == TeamIndex.Player
                    && self.body.teamComponent.teamIndex != TeamIndex.Player)
                {
                    var owner = GetOwningPlayer(damageInfo.attacker);
                    var factor = owner != null ? DamageFactor(owner, Helper.GetLivingPlayerBodies().Count) : 0f;
                    if (factor > 0f)
                    {
                        damageInfo.damage *= factor;
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

        // the player, also of a minion of theirs or one a minion of theirs summoned
        private static CharacterBody GetOwningPlayer(GameObject attacker)
        {
            var master = attacker.TryGetComponent(out CharacterBody body) ? body.master : null;
            for (int depth = 0; master != null && depth < 10; depth++)
            {
                if (master.playerCharacterMasterController != null)
                {
                    return master.GetBody();
                }
                master = master.minionOwnership != null ? master.minionOwnership.ownerMaster : null;
            }
            return null;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            var players = Helper.GetLivingPlayerBodies();
            if (tagged == null || !tagged.healthComponent.alive)
            {
                Tag(players.Count > 0 ? players[RoR2Application.rng.RangeInt(0, players.Count)] : null);
            }
            tagBackTimer -= Time.fixedDeltaTime;
            if (tagged != null && tagBackTimer <= 0f)
            {
                var touched = players.FirstOrDefault(player => player != tagged && AreTouching(player, tagged));
                if (touched != null)
                {
                    Tag(touched);
                }
            }
            retargetTimer -= Time.fixedDeltaTime;
            if (retargetTimer <= 0f)
            {
                retargetTimer = RetargetInterval;
                RetargetMonsters(players);
            }
        }

        private bool AreTouching(CharacterBody a, CharacterBody b)
        {
            return Vector3.Distance(a.corePosition, b.corePosition) <= a.radius + b.radius + Mathf.Max(0f, TouchDistance.Value);
        }

        private void Tag(CharacterBody body)
        {
            buffs.RemoveAll();
            tagged = body;
            tagBackTimer = Mathf.Max(0f, TagBackDelay.Value);
            if (tagged == null)
            {
                return;
            }
            // Essence of Heresy's marks show everyone who has the role
            buffs.Add(tagged, RoR2Content.Buffs.LunarDetonationCharge);
            ChatHelper.SendEventMessage(TaggedMessageToken, ChatHelper.GetColoredPlayerName(tagged));
        }

        // the monster AI runs on the host
        private void RetargetMonsters(List<CharacterBody> players)
        {
            foreach (var master in CharacterMaster.readOnlyInstancesList)
            {
                if (master == null || master.teamIndex == TeamIndex.Player || master.teamIndex == TeamIndex.Neutral)
                {
                    continue;
                }
                foreach (var ai in master.AiComponents)
                {
                    if (ai == null || ai.currentEnemy == null || ai.body == null)
                    {
                        continue;
                    }
                    var current = ai.currentEnemy.gameObject != null ? ai.currentEnemy.gameObject.GetComponent<CharacterBody>() : null;
                    var target = RedirectMonster(current, players.OrderBy(player => Vector3.Distance(player.corePosition, ai.body.corePosition)).ToList());
                    if (target != null && target != current)
                    {
                        ai.currentEnemy.gameObject = target.gameObject;
                        ai.currentEnemy.bestHurtBox = target.mainHurtBox;
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
            tagged = null;
            retargetTimer = 0f;
        }
        public override void Stop()
        {
            buffs.RemoveAll();
            tagged = null;
        }
    }
}
