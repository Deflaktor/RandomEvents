using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventRedLightGreenLight : AbstractEvent
    {
        // time to react to a red light, which also covers the delay until the host knows where a client's player is
        private const float GracePeriod = 1f;
        private const float CheckInterval = 0.25f;
        // horizontal, so that falling doesn't count
        private const float AllowedMovementPerCheck = 0.75f;
        // a player who keeps moving gets a Tonic Affliction every second, until the event ends
        private const float AfflictionInterval = 1f;
        private readonly BuffTracker roots = new BuffTracker();
        // players who stand still during red light can't be hurt
        private readonly BuffTracker immunities = new BuffTracker();
        private AsyncOperationHandle<GameObject> AfflictionEffect;
        private AsyncOperationHandle<GameObject> RedLightEffect;
        private readonly Dictionary<CharacterBody, Vector3> lastPositions = new Dictionary<CharacterBody, Vector3>();
        private readonly HashSet<CharacterBody> caughtThisPhase = new HashSet<CharacterBody>();
        private readonly Dictionary<CharacterBody, float> nextAfflictions = new Dictionary<CharacterBody, float>();
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
            return "Alternates between green and red light, announced in chat. During red light, all AI-controlled characters are rooted, and after a second to react, players who move get a Tonic Affliction every second until the event ends. Players who stand still can't be hurt.";
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
            // Symbiotic Scorpion's effect with the sound of its debuff hitting
            AfflictionEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/PermanentDebuffOnHit/PermanentDebuffEffect.prefab");
            // the red burst of a monster leveling up
            RedLightEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/LevelUpEffectEnemy.prefab");
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
                        immunities.Remove(body, RoR2Content.Buffs.Immune);
                        Punish(body);
                    }
                    else if (!immunities.Has(body, RoR2Content.Buffs.Immune))
                    {
                        immunities.Add(body, RoR2Content.Buffs.Immune);
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
            if (nextAfflictions.TryGetValue(body, out var next) && now < next)
            {
                return;
            }
            nextAfflictions[body] = now + AfflictionInterval;
            EventLoot.GetInventory(this, body).GiveTemporaryItem(RoR2Content.Items.TonicAffliction.itemIndex);
            // the affliction flies into the player from above, like a picked up item
            PurchaseInteraction.CreateItemTakenOrb(body.corePosition + Vector3.up * 4f, body.gameObject, RoR2Content.Items.TonicAffliction.itemIndex);
            EffectManager.SpawnEffect(AfflictionEffect.WaitForCompletion(), new EffectData
            {
                origin = body.corePosition,
                scale = body.radius
            }, transmit: true);
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
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                var effectData = new EffectData
                {
                    origin = body.corePosition,
                    scale = body.radius
                };
                effectData.SetNetworkedObjectReference(body.gameObject);
                EffectManager.SpawnEffect(RedLightEffect.WaitForCompletion(), effectData, transmit: true);
            }
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
            immunities.RemoveAll();
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
            immunities.RemoveAll();
            EventLoot.Clear(this);
            nextAfflictions.Clear();
        }
    }
}
