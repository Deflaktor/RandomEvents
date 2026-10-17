using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventRedLightGreenLight : AbstractEvent
    {
        // time to react to a red light, which also covers the delay until the host knows where a client's player is
        private const float GracePeriod = 0.75f;
        private const float CheckInterval = 0.25f;
        // horizontal, so that falling doesn't count
        private const float AllowedMovementPerCheck = 0.75f;
        private const float DamageFractionPerCheck = 0.1f;
        private readonly BuffTracker roots = new BuffTracker();
        private readonly Dictionary<CharacterBody, Vector3> lastPositions = new Dictionary<CharacterBody, Vector3>();
        private readonly HashSet<CharacterBody> caughtThisPhase = new HashSet<CharacterBody>();
        private bool redLight;
        private float phaseTimer;
        private float graceTimer;
        private float checkTimer;

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
            return "RedLightGreenLight";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_RED_LIGHT_GREEN_LIGHT");
        }
        public override string GetDescription()
        {
            return "Alternates between green and red light, announced in chat. During red light, all AI-controlled characters are rooted, and players who move lose 10% of their health every quarter second.";
        }
        public override string GetConditionDescription()
        {
            return "";
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
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && redLight)
            {
                Root(body);
            }
        }

        private void Root(CharacterBody body)
        {
            if (!body.isPlayerControlled && !roots.Has(body, RoR2Content.Buffs.LunarSecondaryRoot))
            {
                roots.Add(body, RoR2Content.Buffs.LunarSecondaryRoot);
            }
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive())
            {
                return;
            }
            phaseTimer -= Time.fixedDeltaTime;
            if (phaseTimer <= 0f)
            {
                if (redLight)
                {
                    GreenLight();
                }
                else
                {
                    RedLight();
                }
                return;
            }
            if (!redLight)
            {
                return;
            }
            if (graceTimer > 0f)
            {
                graceTimer -= Time.fixedDeltaTime;
                if (graceTimer <= 0f)
                {
                    RememberPositions();
                }
                return;
            }
            checkTimer -= Time.fixedDeltaTime;
            if (checkTimer > 0f)
            {
                return;
            }
            checkTimer = CheckInterval;
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                if (lastPositions.TryGetValue(body, out var lastPosition))
                {
                    var movement = body.transform.position - lastPosition;
                    movement.y = 0f;
                    if (movement.sqrMagnitude > AllowedMovementPerCheck * AllowedMovementPerCheck)
                    {
                        Punish(body);
                    }
                }
            }
            RememberPositions();
        }

        private void RememberPositions()
        {
            lastPositions.Clear();
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                lastPositions[body] = body.transform.position;
            }
        }

        private void Punish(CharacterBody body)
        {
            if (caughtThisPhase.Add(body))
            {
                ChatHelper.SendEventMessage("EVENT_RED_LIGHT_CAUGHT", ChatHelper.GetColoredPlayerName(body));
            }
            body.healthComponent.TakeDamage(new DamageInfo
            {
                damage = body.healthComponent.fullCombinedHealth * DamageFractionPerCheck,
                position = body.corePosition,
                damageType = DamageType.BypassArmor | DamageType.BypassBlock,
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Bleed
            });
        }

        private void RedLight()
        {
            redLight = true;
            phaseTimer = RoR2Application.rng.RangeFloat(3f, 5f);
            graceTimer = GracePeriod;
            checkTimer = 0f;
            caughtThisPhase.Clear();
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.healthComponent.alive)
                {
                    Root(body);
                }
            }
            ChatHelper.SendEventMessage("EVENT_RED_LIGHT");
            // the sound of being rooted by a Void Reaver
            var sound = CharacterBody.CommonAssets.nullifiedBuffAppliedSound;
            if (sound != null)
            {
                foreach (var body in Helper.GetLivingPlayerBodies())
                {
                    EffectManager.SimpleSoundEffect(sound.index, body.corePosition, transmit: true);
                }
            }
        }

        private void GreenLight()
        {
            redLight = false;
            phaseTimer = RoR2Application.rng.RangeFloat(4f, 8f);
            roots.RemoveAll();
            lastPositions.Clear();
            ChatHelper.SendEventMessage("EVENT_GREEN_LIGHT");
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            GreenLight();
        }

        public override void Stop()
        {
            redLight = false;
            roots.RemoveAll();
            lastPositions.Clear();
            caughtThisPhase.Clear();
        }
    }
}
