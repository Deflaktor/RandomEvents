using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
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
        // effects that tint everyone's screen
        private AsyncOperationHandle<GameObject> RedLightFlash;
        private AsyncOperationHandle<GameObject> GreenLightFlash;
        private readonly Dictionary<CharacterBody, Vector3> lastPositions = new Dictionary<CharacterBody, Vector3>();
        private readonly HashSet<CharacterBody> caughtThisPhase = new HashSet<CharacterBody>();
        // how many red lights in a row each player stood still through, an Irradiant Pearl for every second one
        private const int CleanPhasesPerPearl = 2;
        private readonly Dictionary<CharacterMaster, int> cleanPhases = new Dictionary<CharacterMaster, int>();
        private readonly Dictionary<CharacterBody, float> nextAfflictions = new Dictionary<CharacterBody, float>();
        private bool redLight;
        private float phaseTimer;
        private float graceTimer;
        // players who stood still stay invincible this long into green light
        private const float ImmunityAfterRedLight = 1f;
        private float immunityTimer;
        // the orbs of the afflictions and pearls fly into the player from this high above
        private const float OrbHeight = 10f;
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
            return "Red and green light take turns. During red light, monsters freeze, players who stand still can't be hurt, and players who move get Tonic Afflictions until the event ends. Players get an Irradiant Pearl until the event ends for every two red lights in a row they stay still through.";
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
            // the flash and chime of a Solus portal appearing
            RedLightFlash = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/SolusWebPortalSpawnEffect.prefab");
            // the flash of a rift's activation
            GreenLightFlash = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC4/survival/common/RiftActivation.prefab");
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
            if (immunityTimer > 0f)
            {
                immunityTimer -= Time.fixedDeltaTime;
                if (immunityTimer <= 0f)
                {
                    immunities.RemoveAll();
                }
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
            // the affliction flies into the player from above with the whoosh of an item being handed over, e.g. by a
            // printer; the orb itself gives nothing
            if (body.mainHurtBox != null)
            {
                OrbManager.instance.AddOrb(new ItemTransferOrb
                {
                    origin = body.corePosition + Vector3.up * OrbHeight,
                    target = body.mainHurtBox,
                    itemIndex = RoR2Content.Items.TonicAffliction.itemIndex,
                    onArrival = orb => { }
                });
            }
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
            Flash(RedLightFlash);
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

        // an Irradiant Pearl until the event ends for every second red light in a row a player didn't move during; being
        // caught starts the count over
        private void RewardStillPlayers()
        {
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                if (body.master == null)
                {
                    continue;
                }
                if (caughtThisPhase.Contains(body))
                {
                    cleanPhases.Remove(body.master);
                    continue;
                }
                var count = cleanPhases.GetValueOrDefault(body.master) + 1;
                cleanPhases[body.master] = count;
                if (count % CleanPhasesPerPearl == 0)
                {
                    EventLoot.GetInventory(this, body).GiveTemporaryItem(RoR2Content.Items.ShinyPearl.itemIndex);
                    PurchaseInteraction.CreateItemTakenOrb(body.corePosition + Vector3.up * OrbHeight, body.gameObject, RoR2Content.Items.ShinyPearl.itemIndex);
                }
            }
        }

        private void GreenLight()
        {
            if (redLight)
            {
                RewardStillPlayers();
            }
            redLight = false;
            phaseTimer = RoR2Application.rng.RangeFloat(4f, 8f);
            roots.RemoveAll();
            immunityTimer = ImmunityAfterRedLight;
            lastPositions.Clear();
            ChatHelper.SendEventMessage("EVENT_GREEN_LIGHT");
            Flash(GreenLightFlash);
        }

        // on each player, so everyone sees and hears it
        private static void Flash(AsyncOperationHandle<GameObject> effect)
        {
            var prefab = effect.WaitForCompletion();
            if (prefab == null)
            {
                return;
            }
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                EffectManager.SpawnEffect(prefab, new EffectData { origin = body.corePosition }, transmit: true);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            // starts with green light, which rewards no one
            redLight = false;
            cleanPhases.Clear();
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
