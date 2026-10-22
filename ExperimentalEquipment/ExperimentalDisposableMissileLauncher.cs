using RoR2;
using RoR2.Orbs;
using RoR2.Projectile;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    // The Disposable Missile Launcher sends out an Overloading Worm's electric orb for each monster nearby, one every 0.2
    // seconds, at least 4 and up to 10. Each spirals its way slowly to its monster, sending out the Ukulele's chain lightning every second,
    // and calls down the Charged Perforator's lightning strike, three times as strong, on it when it gets there. With no
    // monster left it roams about in search of one, and when its time runs out the strike comes down where it is. All of it is electric, which
    // Faulty Conductor's energizing and conductive monsters work with.
    public class ExperimentalDisposableMissileLauncher : ExperimentalEquipment
    {
        private const int MinOrbs = 4;
        private const int MaxOrbs = 10;
        private const float LaunchInterval = 0.2f;
        // only monsters within this many meters
        private const float Range = 200f;
        private static AsyncOperationHandle<GameObject> ElectricOrb;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.CommandMissile;
        public override float CooldownScale => 0.25f;
        public override string Description => "Disposable Missile Launcher sends an electric orb spiraling at each monster nearby, at least 4 and up to 10, which zaps chain lightning on the way and calls down a lightning strike when it gets there";
        public override string PopupDescription => "Sends an electric orb spiraling at each monster nearby, at least 4 and up to 10. They zap chain lightning on the way and call down a lightning strike when they get there.";

        public override void Preload()
        {
            ElectricOrb = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ElectricWorm/ElectricOrbProjectile.prefab");
        }

        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            new GameObject("RandomEventsElectricOrbLauncher").AddComponent<Launcher>().owner = body;
            return true;
        }

        // the nearest first
        private static IEnumerable<CharacterBody> Targets(CharacterBody owner, Vector3 position)
        {
            return CharacterBody.readOnlyInstancesList.Where(body => IsTarget(owner, body) && (body.corePosition - position).sqrMagnitude <= Range * Range)
                .OrderBy(body => (body.corePosition - position).sqrMagnitude);
        }

        private static bool IsTarget(CharacterBody owner, CharacterBody body)
        {
            return body != null && body.mainHurtBox != null && body.healthComponent != null && body.healthComponent.alive
                && body.teamComponent.teamIndex != owner.teamComponent.teamIndex && body.teamComponent.teamIndex != TeamIndex.Neutral;
        }

        // One orb for each of the monsters nearby when it was used, one after another; the ones beyond the monsters look
        // for one themselves
        public class Launcher : MonoBehaviour
        {
            public CharacterBody owner;
            private readonly Queue<CharacterBody> queue = new Queue<CharacterBody>();
            private int launched;
            private float timer;

            private void Start()
            {
                if (owner != null)
                {
                    foreach (var target in Targets(owner, owner.corePosition).Take(MaxOrbs))
                    {
                        queue.Enqueue(target);
                    }
                    while (queue.Count < MinOrbs)
                    {
                        queue.Enqueue(null);
                    }
                }
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || owner == null || !owner.healthComponent.alive || queue.Count == 0)
                {
                    Destroy(gameObject);
                    return;
                }
                timer -= Time.fixedDeltaTime;
                if (timer > 0f)
                {
                    return;
                }
                timer = LaunchInterval;
                Launch(queue.Dequeue(), launched++);
            }

            private void Launch(CharacterBody target, int index)
            {
                var position = owner.corePosition + Vector3.up * 2f;
                var orb = Instantiate(ElectricOrb.WaitForCompletion(), position, Util.QuaternionSafeLookRotation(Vector3.up));
                // the host moves it and nothing sets it off but reaching its monster
                foreach (var behaviour in new Behaviour[] { orb.GetComponent<ProjectileSimple>(), orb.GetComponent<ProjectileImpactExplosion>() })
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = false;
                    }
                }
                foreach (var collider in orb.GetComponents<Collider>())
                {
                    collider.enabled = false;
                }
                if (orb.TryGetComponent(out Rigidbody rigidbody))
                {
                    rigidbody.useGravity = false;
                }
                if (orb.TryGetComponent(out ProjectileController controller))
                {
                    controller.owner = owner.gameObject;
                }
                if (orb.TryGetComponent(out TeamFilter teamFilter))
                {
                    teamFilter.teamIndex = owner.teamComponent.teamIndex;
                }
                if (orb.TryGetComponent(out ProjectileDamage projectileDamage))
                {
                    projectileDamage.damage = 0f;
                }
                var spiral = orb.AddComponent<Spiral>();
                spiral.owner = owner;
                spiral.target = target;
                // the chain lightning of each a little apart from the others'
                spiral.zapTimer = Spiral.ZapInterval + index * 0.1f;
                NetworkServer.Spawn(orb);
            }
        }

        // Inches towards its monster in a corkscrew, which the host moves and its projectile transform passes on to everyone
        public class Spiral : MonoBehaviour
        {
            public const float ZapInterval = 1f;
            private const float Speed = 8f;
            // how far from its path it circles, and how fast, in radians per second; closer than ConvergeDistance to its
            // monster, the circles shrink until it flies straight at it
            private const float SpiralRadius = 3f;
            private const float SpiralRate = 3f;
            private const float ConvergeDistance = 10f;
            // and a random 1 to 3 seconds more, so they don't all end at once
            private const float Lifetime = 30f;
            private const float ReachDistance = 1.5f;
            // how fast it turns while roaming, in degrees per second
            private const float RoamTurnRate = 90f;
            // three times the Charged Perforator's 500%, and the Ukulele's 80% to 3 monsters within 20 meters
            private const float StrikeDamage = 15f;
            private const float ZapDamage = 0.8f;
            private const int ZapBounces = 2;
            public CharacterBody owner;
            public CharacterBody target;
            public float zapTimer;
            private Rigidbody body;
            private float age;
            private float lifetime;
            private Vector3 roamDirection;
            private float roamTurn;

            private void Awake()
            {
                body = GetComponent<Rigidbody>();
                lifetime = Lifetime + Random.Range(1f, 3f);
                roamDirection = Random.onUnitSphere;
                roamDirection.y = 0f;
                roamDirection = roamDirection.sqrMagnitude > 0.01f ? roamDirection.normalized : Vector3.forward;
                roamTurn = Random.value < 0.5f ? -RoamTurnRate : RoamTurnRate;
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                if (owner == null)
                {
                    Destroy(gameObject);
                    return;
                }
                if (age >= lifetime)
                {
                    StrikeHere();
                    Destroy(gameObject);
                    return;
                }
                if (target == null || !IsTarget(owner, target))
                {
                    target = Targets(owner, transform.position).FirstOrDefault();
                }
                zapTimer -= Time.fixedDeltaTime;
                if (zapTimer <= 0f)
                {
                    zapTimer = ZapInterval;
                    Zap();
                }
                if (target == null)
                {
                    // round and round in wide circles, until a monster shows up
                    roamDirection = Quaternion.Euler(0f, roamTurn * Time.fixedDeltaTime, 0f) * roamDirection;
                    Move(roamDirection, SpiralRadius);
                    return;
                }
                var offset = target.corePosition - transform.position;
                if (offset.sqrMagnitude <= (ReachDistance + target.radius) * (ReachDistance + target.radius))
                {
                    Strike();
                    Destroy(gameObject);
                    return;
                }
                Move(offset.normalized, SpiralRadius * Mathf.Clamp01(offset.magnitude / ConvergeDistance));
            }

            // along the given direction, circling it
            private void Move(Vector3 forward, float radius)
            {
                var side = Vector3.Cross(Vector3.up, forward);
                side = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.right;
                var up = Vector3.Cross(forward, side);
                var angle = SpiralRate * age;
                SetVelocity(forward * Speed + (up * Mathf.Cos(angle) - side * Mathf.Sin(angle)) * (radius * SpiralRate));
            }

            private void SetVelocity(Vector3 velocity)
            {
                if (body != null)
                {
                    body.velocity = velocity;
                }
                if (velocity.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Util.QuaternionSafeLookRotation(velocity);
                }
            }

            // the Ukulele's chain lightning from where it is
            private void Zap()
            {
                var orb = new LightningOrb
                {
                    origin = transform.position,
                    damageValue = owner.damage * ZapDamage,
                    isCrit = owner.RollCrit(),
                    bouncesRemaining = ZapBounces,
                    teamIndex = owner.teamComponent.teamIndex,
                    attacker = owner.gameObject,
                    bouncedObjects = new List<HealthComponent>(),
                    procCoefficient = 0.2f,
                    lightningType = LightningOrb.LightningType.Ukulele,
                    damageColorIndex = DamageColorIndex.Item
                };
                orb.damageType.damageTypeExtended = DamageTypeExtended.Electrical;
                orb.procChainMask.AddProc(ProcType.ChainLightning);
                var hurtBox = orb.PickNextTarget(transform.position);
                if (hurtBox != null)
                {
                    orb.target = hurtBox;
                    OrbManager.instance.AddOrb(orb);
                }
            }

            // the Charged Perforator's lightning strike where it is, as if it had hit a monster there
            private void StrikeHere()
            {
                EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/SimpleLightningStrikeImpact"), new EffectData
                {
                    origin = transform.position
                }, transmit: true);
                var procChainMask = default(ProcChainMask);
                procChainMask.AddProc(ProcType.LightningStrikeOnHit);
                var blastAttack = new BlastAttack
                {
                    attacker = owner.gameObject,
                    baseDamage = owner.damage * StrikeDamage,
                    bonusForce = Vector3.down * 1500f,
                    crit = owner.RollCrit(),
                    damageColorIndex = DamageColorIndex.Item,
                    falloffModel = BlastAttack.FalloffModel.None,
                    position = transform.position,
                    procChainMask = procChainMask,
                    procCoefficient = 1f,
                    radius = 3f,
                    teamIndex = owner.teamComponent.teamIndex
                };
                blastAttack.damageType.damageTypeExtended = DamageTypeExtended.Electrical;
                blastAttack.Fire();
            }

            // the Charged Perforator's lightning strike on its monster
            private void Strike()
            {
                var hurtBox = target.mainHurtBox;
                if (target.hurtBoxGroup != null && target.hurtBoxGroup.hurtBoxes.Length > 0)
                {
                    hurtBox = target.hurtBoxGroup.hurtBoxes[Random.Range(0, target.hurtBoxGroup.hurtBoxes.Length)];
                }
                var procChainMask = default(ProcChainMask);
                procChainMask.AddProc(ProcType.LightningStrikeOnHit);
                OrbManager.instance.AddOrb(new SimpleLightningStrikeOrb
                {
                    attacker = owner.gameObject,
                    damageColorIndex = DamageColorIndex.Item,
                    damageValue = owner.damage * StrikeDamage,
                    damageType = DamageTypeExtended.Electrical,
                    isCrit = owner.RollCrit(),
                    procChainMask = procChainMask,
                    procCoefficient = 1f,
                    target = hurtBox
                });
            }
        }
    }
}
