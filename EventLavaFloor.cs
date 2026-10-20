using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventLavaFloor : AbstractEvent
    {
        private const float UpdateInterval = 0.25f;
        private ConfigEntry<float> GraceTime;
        private ConfigEntry<float> DamagePerSecond;
        private ConfigEntry<int> Feathers;
        // how long each player has been on the ground
        private readonly Dictionary<CharacterBody, float> groundedTimes = new Dictionary<CharacterBody, float>();
        private float updateTimer;

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
            return "Players burn while on the ground for more than 1 second. All players get 2 Hopoo Feathers.";
        }
        public override string GetPopupDescription()
        {
            return "You burn while on the ground for more than 1 second. You get 2 Hopoo Feathers temporarily.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            GraceTime = config.Bind(GetEventConfigName(), "Grace Time", 1f, "Seconds players can be on the ground before they burn.");
            DamagePerSecond = config.Bind(GetEventConfigName(), "Damage Per Second", 0.05f, "The share of their maximum health players lose per second while burning.");
            Feathers = config.Bind(GetEventConfigName(), "Hopoo Feathers", 2, "How many Hopoo Feathers players get during the event.");
        }
        public override void Preload()
        {
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
                }
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
                damageType = DamageType.BypassArmor | DamageType.BypassBlock,
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
            EventLoot.Clear(this);
        }
    }
}
