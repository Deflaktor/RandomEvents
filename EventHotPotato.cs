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
    public class EventHotPotato : AbstractEvent
    {
        private const float FuseTime = 20f;
        private const float WarningTime = 5f;
        private const float PassDistance = 3.5f;
        // so that two players standing together don't pass the bomb back and forth every tick
        private const float PassCooldown = 1.5f;
        private const float ExplosionRadius = 15f;
        private const float HolderDamageFraction = 0.5f;
        private AsyncOperationHandle<GameObject> FusionCellExplosion;
        private readonly BuffTracker buffs = new BuffTracker();
        private CharacterBody holder;
        private float fuseTimer;
        private float passTimer;
        private bool warned;

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
            return "HotPotato";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_HOT_POTATO");
        }
        public override string GetDescription()
        {
            return "A random player carries a bomb, marked with a skull, and passes it on by touching another player. After 20 seconds it explodes, dealing half of the carrier's health to them and hurting monsters nearby. Then another player gets a new bomb.";
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
            FusionCellExplosion = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/FusionCellDestructible/FusionCellExplosion.prefab");
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
            if (holder == null || !holder.healthComponent.alive)
            {
                // the bomb of a carrier who died goes on ticking with someone else
                var candidates = Helper.GetLivingPlayerBodies();
                if (candidates.Count == 0)
                {
                    return;
                }
                var receiver = candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
                if (holder != null)
                {
                    ChatHelper.SendEventMessage("EVENT_HOT_POTATO_PASSED", ChatHelper.GetColoredPlayerName(holder), ChatHelper.GetColoredPlayerName(receiver));
                }
                GiveBomb(receiver, keepFuse: holder != null);
                return;
            }
            fuseTimer -= Time.fixedDeltaTime;
            passTimer -= Time.fixedDeltaTime;
            if (!warned && fuseTimer <= WarningTime)
            {
                warned = true;
                ChatHelper.SendEventMessage("EVENT_HOT_POTATO_WARNING", ChatHelper.GetColoredPlayerName(holder), Mathf.RoundToInt(WarningTime));
            }
            if (fuseTimer <= 0f)
            {
                Explode();
                return;
            }
            if (passTimer <= 0f)
            {
                var receiver = Helper.GetLivingPlayerBodies()
                    .Where(body => body != holder && (body.corePosition - holder.corePosition).sqrMagnitude <= PassDistance * PassDistance)
                    .OrderBy(body => (body.corePosition - holder.corePosition).sqrMagnitude)
                    .FirstOrDefault();
                if (receiver != null)
                {
                    ChatHelper.SendEventMessage("EVENT_HOT_POTATO_PASSED", ChatHelper.GetColoredPlayerName(holder), ChatHelper.GetColoredPlayerName(receiver));
                    GiveBomb(receiver, keepFuse: true);
                }
            }
        }

        private void GiveBomb(CharacterBody body, bool keepFuse)
        {
            buffs.RemoveAll();
            holder = body;
            // Death Mark's skull shows everyone who carries the bomb
            buffs.Add(holder, RoR2Content.Buffs.DeathMark);
            passTimer = PassCooldown;
            if (!keepFuse)
            {
                fuseTimer = FuseTime;
                warned = false;
                ChatHelper.SendEventMessage("EVENT_HOT_POTATO_NEW_BOMB", ChatHelper.GetColoredPlayerName(holder), Mathf.RoundToInt(FuseTime));
            }
        }

        private void Explode()
        {
            var victim = holder;
            buffs.RemoveAll();
            holder = null;
            var position = victim.corePosition;
            EffectManager.SpawnEffect(FusionCellExplosion.WaitForCompletion(), new EffectData
            {
                origin = position,
                scale = ExplosionRadius
            }, transmit: true);
            new BlastAttack
            {
                attacker = victim.gameObject,
                inflictor = victim.gameObject,
                teamIndex = victim.teamComponent.teamIndex,
                position = position,
                radius = ExplosionRadius,
                baseDamage = victim.damage * 10f,
                baseForce = 2000f,
                falloffModel = BlastAttack.FalloffModel.SweetSpot,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
                crit = victim.RollCrit(),
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Item
            }.Fire();
            victim.healthComponent.TakeDamage(new DamageInfo
            {
                damage = victim.healthComponent.fullCombinedHealth * HolderDamageFraction,
                position = position,
                damageType = DamageType.BypassArmor | DamageType.BypassBlock,
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Bleed
            });
            ChatHelper.SendEventMessage("EVENT_HOT_POTATO_EXPLODED", ChatHelper.GetColoredPlayerName(victim));
            // a new bomb for someone else, if possible
            var candidates = Helper.GetLivingPlayerBodies();
            var others = candidates.Where(body => body != victim).ToList();
            if (others.Count > 0)
            {
                candidates = others;
            }
            if (candidates.Count > 0)
            {
                GiveBomb(candidates[RoR2Application.rng.RangeInt(0, candidates.Count)], keepFuse: false);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            holder = null;
            var candidates = Helper.GetLivingPlayerBodies();
            if (candidates.Count > 0)
            {
                GiveBomb(candidates[RoR2Application.rng.RangeInt(0, candidates.Count)], keepFuse: false);
            }
        }

        public override void Stop()
        {
            buffs.RemoveAll();
            holder = null;
        }
    }
}
