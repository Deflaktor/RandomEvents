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
        // a player who keeps moving gets a Tonic Affliction every second, and loses one every 5 seconds again once they
        // stand still or the light is green
        private const float AfflictionInterval = 1f;
        private const float AfflictionDecayInterval = 5f;
        private readonly BuffTracker roots = new BuffTracker();
        private readonly Dictionary<CharacterBody, Vector3> lastPositions = new Dictionary<CharacterBody, Vector3>();
        private readonly HashSet<CharacterBody> caughtThisPhase = new HashSet<CharacterBody>();
        private class Affliction
        {
            public int count;
            public float nextAffliction;
            public float nextDecay;
        }
        private readonly Dictionary<CharacterBody, Affliction> afflictions = new Dictionary<CharacterBody, Affliction>();
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
            return "Alternates between green and red light, announced in chat. During red light, all AI-controlled characters are rooted, and players who move get a Tonic Affliction every second. Once they stop moving, they lose one of them every 5 seconds, and the rest when the event ends.";
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
            DecayAfflictions();
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
            var now = Run.instance.fixedTime;
            if (!afflictions.TryGetValue(body, out var affliction))
            {
                affliction = new Affliction();
                afflictions[body] = affliction;
            }
            // no decay while moving
            affliction.nextDecay = now + AfflictionDecayInterval;
            if (now < affliction.nextAffliction)
            {
                return;
            }
            affliction.nextAffliction = now + AfflictionInterval;
            affliction.count++;
            EventLoot.GetInventory(this, body).GiveTemporaryItem(RoR2Content.Items.TonicAffliction.itemIndex);
        }

        private void DecayAfflictions()
        {
            var now = Run.instance.fixedTime;
            foreach (var (body, affliction) in afflictions)
            {
                if (body == null || affliction.count <= 0 || now < affliction.nextDecay)
                {
                    continue;
                }
                affliction.count--;
                affliction.nextDecay = now + AfflictionDecayInterval;
                EventLoot.GetInventory(this, body).RemoveTemporaryItem(RoR2Content.Items.TonicAffliction.itemIndex);
            }
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
            EventLoot.Clear(this);
            afflictions.Clear();
        }
    }
}
