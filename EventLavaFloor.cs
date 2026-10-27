using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventLavaFloor : AbstractEvent
    {
        private const float UpdateInterval = 0.25f;
        // a Molotov's fire on the ground under a burning player, which belongs to the monsters and burns the players in it:
        // a new one this far from their last, each burning for a while, three times a second for this much of a
        // Commando's base damage at the monsters' level
        private const float FireSpacing = 3f;
        private const float FireDuration = 3f;
        private const float FireDamage = 0.5f;
        private ConfigEntry<float> GraceTime;
        private ConfigEntry<float> DamagePerSecond;
        private ConfigEntry<int> Feathers;
        // how long each player has been on the ground
        private readonly Dictionary<CharacterBody, float> groundedTimes = new Dictionary<CharacterBody, float>();
        private readonly Dictionary<CharacterBody, (Vector3 position, GameObject fire)> lastFires = new Dictionary<CharacterBody, (Vector3 position, GameObject fire)>();
        private readonly List<(GameObject fire, float end)> fires = new List<(GameObject fire, float end)>();
        private float updateTimer;

        public override EventTags Tags => EventTags.EventItems;
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
            return "LavaFloor";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_LAVA_FLOOR");
        }
        public override string GetDescription()
        {
            return "Players burn while on the ground for more than half a second, which sets the ground beneath them on fire, burning the players in it. All players get 2 Hopoo Feathers.";
        }
        public override string GetPopupDescription()
        {
            return "The floor is lava! Don't stay on the ground. You get 2 Hopoo Feathers.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            GraceTime = config.Bind(GetEventConfigName(), "Grace Time", 0.5f, "Seconds players can be on the ground before they burn.");
            DamagePerSecond = config.Bind(GetEventConfigName(), "Damage Per Second", 0.05f, "The share of their maximum health players lose per second while burning.");
            Feathers = config.Bind(GetEventConfigName(), "Hopoo Feathers", 2, "How many Hopoo Feathers players get during the event.");
        }
        public override void Preload()
        {
            FireEffects.Preload();
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.isPlayerControlled)
            {
                GiveFeathers(body);
            }
        }

        private void GiveFeathers(CharacterBody body)
        {
            if (body.inventory != null && Feathers.Value > 0)
            {
                EventLoot.GetInventory(this, body).GiveTemporaryItem(RoR2Content.Items.Feather.itemIndex, Feathers.Value);
                ItemOrb.Fly(body.corePosition + Vector3.up * 5f, body.gameObject, RoR2Content.Items.Feather.itemIndex);
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
            PutOutFires(false);
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                if (body.characterMotor == null || !body.characterMotor.isGrounded)
                {
                    groundedTimes[body] = 0f;
                    continue;
                }
                var groundedTime = groundedTimes.TryGetValue(body, out var time) ? time + UpdateInterval : UpdateInterval;
                groundedTimes[body] = groundedTime;
                if (groundedTime > GraceTime.Value)
                {
                    Burn(body);
                    SetGroundOnFire(body);
                }
            }
        }

        private void SetGroundOnFire(CharacterBody body)
        {
            var position = body.footPosition;
            // still standing in their last one
            if (lastFires.TryGetValue(body, out var last) && last.fire != null && (last.position - position).sqrMagnitude < FireSpacing * FireSpacing)
            {
                return;
            }
            var fire = UnityEngine.Object.Instantiate(FireEffects.FirePool.WaitForCompletion(), position, Quaternion.identity);
            if (fire.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = TeamIndex.Monster;
            }
            // the floor itself, which Defensive Microbots don't shoot down; they only do on the host
            if (fire.TryGetComponent(out RoR2.Projectile.ProjectileController projectileController))
            {
                projectileController.cannotBeDeleted = true;
            }
            // whatever the players' survivors, items or levels
            var commando = BodyCatalog.FindBodyPrefab("CommandoBody")?.GetComponent<CharacterBody>();
            if (fire.TryGetComponent(out RoR2.Projectile.ProjectileDamage projectileDamage) && commando != null)
            {
                projectileDamage.damage = (commando.baseDamage + commando.levelDamage * (Run.instance.ambientLevel - 1f)) * FireDamage;
                projectileDamage.crit = false;
            }
            NetworkServer.Spawn(fire);
            lastFires[body] = (position, fire);
            fires.Add((fire, Time.fixedTime + FireDuration));
        }

        // those burnt out, or all of them
        private void PutOutFires(bool all)
        {
            fires.RemoveAll(entry =>
            {
                if (!all && entry.fire != null && Time.fixedTime < entry.end)
                {
                    return false;
                }
                if (entry.fire != null)
                {
                    UnityEngine.Object.Destroy(entry.fire);
                }
                return true;
            });
            if (all)
            {
                lastFires.Clear();
            }
        }

        // burning also stops health regeneration
        private void Burn(CharacterBody body)
        {
            body.AddTimedBuff(RoR2Content.Buffs.OnFire, UpdateInterval * 2f);
            body.healthComponent.TakeDamage(new DamageInfo
            {
                damage = body.healthComponent.fullCombinedHealth * Mathf.Max(0f, DamagePerSecond.Value) * UpdateInterval,
                position = body.footPosition,
                // like the void fog's, irrespective of defenses
                damageType = Helper.HazardDamageType,
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Item
            });
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            groundedTimes.Clear();
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                GiveFeathers(body);
            }
        }

        public override void Stop()
        {
            groundedTimes.Clear();
            PutOutFires(true);
            EventLoot.Clear(this);
        }
    }
}
