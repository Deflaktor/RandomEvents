using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections;
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
        // the bomb goes off at this share of the event's duration, after a countdown
        private const float DetonationShare = 0.75f;
        private const int CountdownSeconds = 5;
        private const float PassDistance = 3.5f;
        // so that two characters standing together don't pass the bomb back and forth every tick
        private const float PassCooldown = 1.5f;
        private ConfigEntry<float> NukeRadius;
        private ConfigEntry<float> PlayerDamage;
        private ConfigEntry<float> MonsterDamage;
        private ConfigEntry<float> BossDamage;
        private AsyncOperationHandle<GameObject> TickEffect;
        private readonly BuffTracker buffs = new BuffTracker();
        private CharacterBody holder;
        // the last player who had the bomb, who gets the kills
        private CharacterBody lastPlayerHolder;
        private Vector3 lastHolderPosition;
        private float detonationTime;
        private int countdown;
        private float passTimer;
        private float tickTimer;
        private bool detonated;

        public override bool LoadCondition()
        {
            return true;
        }
        // not while the teleporter or a Simulacrum boss wave has a boss to fight
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            var teleporter = TeleporterInteraction.instance;
            return Helper.GetLivingPlayerBodies().Count >= 1 && !Helper.IsSimulacrumBossWaveNext()
                && (teleporter == null || !(teleporter.isIdleToCharging || teleporter.isCharging))
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("TagTeam", StringComparison.InvariantCultureIgnoreCase));
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
            return "A player carries a bomb, which goes to any player or monster they touch. Near the end of the event it explodes like a mini nuke.";
        }
        public override string GetConditionDescription()
        {
            return "Not during the teleporter event or a Simulacrum boss wave, and event \"TagTeam\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            NukeRadius = config.Bind(GetEventConfigName(), "Nuke Radius", 40f, "How far the explosion reaches, in meters.");
            PlayerDamage = config.Bind(GetEventConfigName(), "Player Damage", 0.75f, "The share of their maximum health players lose at the center of the explosion; a quarter of it at its edge.");
            MonsterDamage = config.Bind(GetEventConfigName(), "Monster Damage", 2f, "The share of their maximum health monsters lose at the center of the explosion; a quarter of it at its edge.");
            BossDamage = config.Bind(GetEventConfigName(), "Boss Damage", 0.5f, "The share of their maximum health bosses lose at the center of the explosion; a quarter of it at its edge.");
        }
        public override void Preload()
        {
            TickEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFXQuick.prefab");
            MiniNuke.Preload();
        }
        public override void Hook()
        {
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive() || detonated)
            {
                return;
            }
            if (holder == null || !holder.healthComponent.alive)
            {
                // the bomb of a carrier who died goes to whoever is nearest
                var receiver = GetCharacters(null).OrderBy(body => (body.corePosition - lastHolderPosition).sqrMagnitude).FirstOrDefault();
                if (receiver == null)
                {
                    return;
                }
                if (holder != null)
                {
                    ChatHelper.SendEventMessage("EVENT_HOT_POTATO_PASSED", ChatHelper.GetColoredPlayerName(holder), ChatHelper.GetColoredPlayerName(receiver));
                }
                GiveBomb(receiver);
            }
            lastHolderPosition = holder.corePosition;
            var timeLeft = detonationTime - Time.fixedTime;
            if (timeLeft <= 0f)
            {
                Detonate();
                return;
            }
            if (Mathf.CeilToInt(timeLeft) < countdown)
            {
                countdown = Mathf.CeilToInt(timeLeft);
                if (countdown == CountdownSeconds)
                {
                    ChatHelper.SendEventMessage("EVENT_HOT_POTATO_WARNING", ChatHelper.GetColoredPlayerName(holder), countdown);
                }
                else
                {
                    ChatHelper.SendEventMessage("EVENT_HOT_POTATO_COUNTDOWN", countdown);
                }
            }
            Tick(timeLeft);
            passTimer -= Time.fixedDeltaTime;
            if (passTimer <= 0f)
            {
                // players pass it to anyone, monsters only to players
                var receiver = GetCharacters(holder)
                    .Where(body => holder.isPlayerControlled || body.isPlayerControlled)
                    .Where(body => (body.corePosition - holder.corePosition).magnitude <= PassDistance + body.radius + holder.radius)
                    .OrderBy(body => (body.corePosition - holder.corePosition).sqrMagnitude)
                    .FirstOrDefault();
                if (receiver != null)
                {
                    ChatHelper.SendEventMessage("EVENT_HOT_POTATO_PASSED", ChatHelper.GetColoredPlayerName(holder), ChatHelper.GetColoredPlayerName(receiver));
                    GiveBomb(receiver);
                }
            }
        }

        // the living players and monsters, other than the one given
        private static IEnumerable<CharacterBody> GetCharacters(CharacterBody except)
        {
            return CharacterBody.readOnlyInstancesList.Where(body => body != null && body != except && body.healthComponent != null && body.healthComponent.alive
                && body.teamComponent.teamIndex != TeamIndex.Neutral && body.teamComponent.teamIndex != TeamIndex.None
                && (body.isPlayerControlled || body.teamComponent.teamIndex != TeamIndex.Player) && body.master != null);
        }

        // a small blast at the carrier, faster and faster as the detonation nears
        private void Tick(float timeLeft)
        {
            tickTimer -= Time.fixedDeltaTime;
            if (tickTimer > 0f)
            {
                return;
            }
            tickTimer = Mathf.Lerp(0.15f, 1f, timeLeft / 20f);
            EffectManager.SpawnEffect(TickEffect.WaitForCompletion(), new EffectData
            {
                origin = holder.corePosition,
                scale = holder.radius * 2f
            }, transmit: true);
        }

        private void GiveBomb(CharacterBody body)
        {
            buffs.RemoveAll();
            holder = body;
            if (body.isPlayerControlled)
            {
                lastPlayerHolder = body;
            }
            tickTimer = 0f;
            // Essence of Heresy's marks show everyone who carries the bomb; only Hooks of Heresy could detonate them,
            // unlike Death Mark's skull, which adds 50% damage taken
            buffs.Add(holder, RoR2Content.Buffs.LunarDetonationCharge);
            passTimer = PassCooldown;
        }

        private void Detonate()
        {
            detonated = true;
            var victim = holder;
            buffs.RemoveAll();
            holder = null;
            ChatHelper.SendEventMessage("EVENT_HOT_POTATO_EXPLODED", ChatHelper.GetColoredPlayerName(victim));
            // ground zero is on the floor below the carrier
            var groundZero = Physics.Raycast(victim.corePosition, Vector3.down, out var hit, 50f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : victim.footPosition;
            var attacker = lastPlayerHolder != null && lastPlayerHolder.healthComponent.alive ? lastPlayerHolder.gameObject : null;
            Nuke(groundZero, Mathf.Max(1f, NukeRadius.Value), PlayerDamage.Value, MonsterDamage.Value, BossDamage.Value, attacker);
        }

        // The mushroom cloud, and everyone within the radius losing a share of their health: all of it at the center, a
        // quarter of it at the edge. The attacker gets the kills of monsters; players with no share to lose are spared.
        public static void Nuke(Vector3 groundZero, float radius, float playerShare, float monsterShare, float bossShare, GameObject attacker)
        {
            new GameObject("RandomEventsMiniNuke").AddComponent<MiniNuke>().groundZero = groundZero;
            foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
            {
                if (body == null || body.healthComponent == null || !body.healthComponent.alive || body.teamComponent.teamIndex == TeamIndex.Neutral)
                {
                    continue;
                }
                var offset = body.corePosition - groundZero;
                var distance = offset.magnitude;
                if (distance > radius)
                {
                    continue;
                }
                var falloff = 1f - 0.75f * distance / radius;
                var isPlayerTeam = body.teamComponent.teamIndex == TeamIndex.Player;
                var share = isPlayerTeam ? playerShare : body.isBoss ? bossShare : monsterShare;
                if (share <= 0f)
                {
                    continue;
                }
                body.healthComponent.TakeDamage(new DamageInfo
                {
                    damage = body.healthComponent.fullCombinedHealth * share * falloff,
                    attacker = isPlayerTeam ? null : attacker,
                    position = body.corePosition,
                    force = (offset.sqrMagnitude > 0.01f ? offset.normalized : Vector3.up) * 4000f * falloff + Vector3.up * 1000f * falloff,
                    damageType = DamageType.BypassArmor | DamageType.BypassBlock,
                    procCoefficient = 0f,
                    damageColorIndex = DamageColorIndex.Item
                });
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            holder = null;
            lastPlayerHolder = null;
            detonated = false;
            detonationTime = Time.fixedTime + Mathf.Max(CountdownSeconds, ModConfig.EventDuration.Value * DetonationShare);
            countdown = CountdownSeconds + 1;
            var candidates = Helper.GetLivingPlayerBodies();
            if (candidates.Count > 0)
            {
                var first = candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
                GiveBomb(first);
                lastHolderPosition = first.corePosition;
                ChatHelper.SendEventMessage("EVENT_HOT_POTATO_NEW_BOMB", ChatHelper.GetColoredPlayerName(first), Mathf.RoundToInt(detonationTime - Time.fixedTime));
            }
        }

        public override void Stop()
        {
            buffs.RemoveAll();
            holder = null;
            lastPlayerHolder = null;
        }

        // The explosions, all at ground zero and in quick succession: the flash and fireball, a burst for each shockwave
        // ring, the stem and the rolling cap of the mushroom cloud, which go on after the event has ended
        public class MiniNuke : MonoBehaviour
        {
            // the Diablo Strike's flash this far down, out of sight, for its screen tint, shake and boom
            private const float FlashDepth = 15f;
            private static AsyncOperationHandle<GameObject> Flash;
            private static AsyncOperationHandle<GameObject> Nova;
            private static AsyncOperationHandle<GameObject> Fireball;
            private static AsyncOperationHandle<GameObject> Breach;
            private static AsyncOperationHandle<GameObject> Omni;
            private static AsyncOperationHandle<GameObject> Behemoth;
            private static AsyncOperationHandle<GameObject> Meteor;
            public Vector3 groundZero;

            public static void Preload()
            {
                if (Flash.IsValid())
                {
                    return;
                }
                // the Diablo Strike's flash and the Wandering Vagrant's nova tint the screen and shake it far away
                Flash = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Captain/CaptainAirstrikeAltImpact.prefab");
                Nova = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Vagrant/VagrantNovaExplosion.prefab");
                Fireball = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/FusionCellDestructible/FusionCellExplosion.prefab");
                // the explosion of ExplodingCorpses' monsters
                Breach = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Scorchling/VFXScorchlingBreachExplosion.prefab");
                Omni = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/OmniExplosionVFX.prefab");
                Behemoth = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/StickyBomb/BehemothVFX.prefab");
                // a rumble with a shake
                Meteor = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Meteor/MeteorStrikeImpact.prefab");
            }

            private void Start()
            {
                StartCoroutine(Flashes());
                StartCoroutine(FireballAndShockwave());
                StartCoroutine(Stem());
                StartCoroutine(Cap());
                Destroy(gameObject, 6f);
            }

            private void Spawn(AsyncOperationHandle<GameObject> effect, float scale)
            {
                Spawn(effect, groundZero, scale);
            }

            private static void Spawn(AsyncOperationHandle<GameObject> effect, Vector3 position, float scale)
            {
                EffectManager.SpawnEffect(effect.WaitForCompletion(), new EffectData
                {
                    origin = position,
                    scale = scale,
                    rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)
                }, transmit: true);
            }

            // 0 s: the flash; 1.5 s: a second nova as the cap forms
            private IEnumerator Flashes()
            {
                Spawn(Flash, groundZero + Vector3.down * FlashDepth, 1f);
                Spawn(Nova, 40f);
                Spawn(Fireball, 30f);
                yield return new WaitForSeconds(1.5f);
                Spawn(Nova, 20f);
            }

            // 0-0.4 s: the fireball, and a burst of the shockwave's explosions with each of its six rings
            private IEnumerator FireballAndShockwave()
            {
                for (int i = 0; i < 7; i++)
                {
                    Spawn(i % 2 == 0 ? Fireball : Behemoth, UnityEngine.Random.Range(10f, 15f));
                    if (i < 6)
                    {
                        for (int j = 0; j < 6 + 2 * i; j++)
                        {
                            Spawn(Breach, 4f);
                        }
                    }
                    yield return new WaitForSeconds(0.1f);
                }
            }

            // 0.3-2 s: the stem
            private IEnumerator Stem()
            {
                yield return new WaitForSeconds(0.3f);
                const float duration = 1.7f;
                const float step = 0.075f;
                for (float time = 0f; time < duration; time += step)
                {
                    Spawn(Omni, 6f);
                    Spawn(time % 0.15f < step ? Fireball : Behemoth, 5f);
                    yield return new WaitForSeconds(step);
                }
            }

            // 1.5-5 s: the cap, thinning out at the end
            private IEnumerator Cap()
            {
                yield return new WaitForSeconds(1.5f);
                const float duration = 3.5f;
                const float step = 0.06f;
                float rumbleTimer = 0f;
                for (float time = 0f; time < duration; time += step)
                {
                    var progress = time / duration;
                    var count = progress < 0.7f ? 3 : 1;
                    for (int i = 0; i < count; i++)
                    {
                        switch (UnityEngine.Random.Range(0, 3))
                        {
                            case 0: Spawn(Fireball, UnityEngine.Random.Range(8f, 12f)); break;
                            case 1: Spawn(Breach, 5f); break;
                            default: Spawn(Omni, 8f); break;
                        }
                    }
                    if (UnityEngine.Random.value < 0.3f)
                    {
                        Spawn(Behemoth, 8f);
                    }
                    rumbleTimer -= step;
                    if (rumbleTimer <= 0f && progress < 0.8f)
                    {
                        rumbleTimer = 0.5f;
                        Spawn(Meteor, 1f);
                    }
                    yield return new WaitForSeconds(step);
                }
            }
        }
    }
}
