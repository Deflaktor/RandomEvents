using RoR2;
using RoR2.CharacterAI;
using RoR2.Projectile;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    // Instead of its black hole, a crackling spot where the player aims that pulls everything in and swallows the
    // monsters that reach it: they shrink away, then grow back unseen. Once it has swallowed none for 5 seconds and all
    // have grown back, it bursts: the bigger the health it swallowed, the bigger the blast, the harder it hurts, and the
    // further the swallowed monsters fly out of it.
    public class ExperimentalPrimordialCube : ExperimentalEquipment
    {
        private const float AimRange = 200f;
        private const float HoverHeight = 2f;
        private static AsyncOperationHandle<GameObject> Swallow;
        private static AsyncOperationHandle<GameObject> Nova;
        private static AsyncOperationHandle<GameObject> Fireball;
        private static AsyncOperationHandle<GameObject> BurstEffect;
        // the spot: the Captain's shocking beacon's nova and his Tazer's, small
        private static AsyncOperationHandle<GameObject> ShockNova;
        private static AsyncOperationHandle<GameObject> ShockRing;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Blackhole;
        // its size items come with Hallowed Concepts, and without it nobody's inventory takes them
        public override bool IsAvailable => DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4);
        public override string Description => "the Primordial Cube opens a crackling spot where you aim that swallows monsters and bursts, the harder the more it swallowed";
        public override string PopupDescription => "Opens a rift where you aim that swallows monsters, then bursts. The more it swallowed, the bigger the burst.";

        public override void Preload()
        {
            FireEffects.Preload();
            Swallow = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC4/SpawnAltar/ChargeUpEffect.prefab");
            // Genesis Loop's nova, which is the Wandering Vagrant's
            Nova = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Vagrant/VagrantNovaExplosion.prefab");
            Fireball = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/FusionCellDestructible/FusionCellExplosion.prefab");
            BurstEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Jellyfish/JellyfishNova.prefab");
            ShockNova = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Captain/CaptainTazerSupplyDropNova.prefab");
            ShockRing = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Captain/CaptainTazerNova.prefab");
        }

        // on the ground where the player aims, at once; with nothing to aim at, nothing happens and it keeps its charge
        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            if (!Aim(slot.GetAimRay(), body, out var point))
            {
                return false;
            }
            var spot = new GameObject("RandomEventsPullSpot").AddComponent<Altar>();
            spot.owner = body;
            spot.team = body.teamComponent.teamIndex;
            spot.center = point + Vector3.up * HoverHeight;
            return true;
        }

        // the first thing in its way: the ground, a wall, a prop or a character, but not the player
        private static bool Aim(Ray ray, CharacterBody body, out Vector3 point)
        {
            point = Vector3.zero;
            var closest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(ray, AimRange, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= closest || (hit.collider.TryGetComponent(out HurtBox hurtBox) && hurtBox.healthComponent == body.healthComponent))
                {
                    continue;
                }
                closest = hit.distance;
                point = hit.point;
            }
            return closest < float.PositiveInfinity;
        }

        public class Altar : MonoBehaviour
        {
            private const float SwallowRadius = 3f;
            // the Primordial Cube's pull, over a wider area
            private const float PullRadius = 50f;
            private const float PullForce = 1500f;
            private const float PullDamping = 0.5f;
            private const float SpotInterval = 0.4f;
            private const float QuietTime = 5f;
            // nearly nothing of their size left, whatever size items they already have
            private const float SmallScale = 0.05f;
            // the longest it waits for them to grow back after the quiet time, in case something else changes their size
            private const float MaxGrowWait = 5f;
            // of the swallowed health, what each monster in the blast loses, all of it at the center and half at the edge
            private const float DamageShare = 0.3f;
            public CharacterBody owner;
            // the user's team, spared even once its body is gone, e.g. after dying
            public TeamIndex team;
            public Vector3 center;
            private readonly List<CharacterBody> swallowed = new List<CharacterBody>();
            // the swallowed that have shrunk away and now grow back unseen
            private readonly HashSet<CharacterBody> hidden = new HashSet<CharacterBody>();
            // the shrink items each was given, and its size before
            private readonly Dictionary<CharacterBody, (int stacks, float size)> shrinks = new Dictionary<CharacterBody, (int stacks, float size)>();
            private bool spotRing;
            private float power;
            private float quietTimer = QuietTime;
            private float spotTimer;

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                // the spot, crackling again and again
                spotTimer -= Time.fixedDeltaTime;
                if (spotTimer <= 0f)
                {
                    spotTimer = SpotInterval;
                    spotRing = !spotRing;
                    FireEffects.SpawnEffect(spotRing ? ShockRing : ShockNova, center, spotRing ? 6f : 4f);
                }
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (!CanSwallow(body))
                    {
                        continue;
                    }
                    var distanceSqr = (body.corePosition - center).sqrMagnitude;
                    if (distanceSqr <= (SwallowRadius + body.radius) * (SwallowRadius + body.radius))
                    {
                        Eat(body);
                    }
                    else if (distanceSqr <= PullRadius * PullRadius)
                    {
                        Pull(body);
                    }
                }
                foreach (var body in swallowed)
                {
                    Hold(body);
                    // its target size is only lowered once its stats are worked out again, after the shrink items
                    if (body != null && !hidden.Contains(body) && IsSmall(body))
                    {
                        Hide(body);
                    }
                }
                quietTimer -= Time.fixedDeltaTime;
                // once all of them have grown back
                if (quietTimer <= -MaxGrowWait || quietTimer <= 0f && swallowed.All(body => body == null || !body.healthComponent.alive || hidden.Contains(body) && IsGrownBack(body)))
                {
                    Burst();
                }
            }

            private bool IsSmall(CharacterBody body)
            {
                var small = shrinks[body].size - shrinks[body].stacks * 0.01f;
                return body.desiredScale <= small + 0.005f && body.currentScale <= body.desiredScale + 0.01f;
            }

            private bool IsGrownBack(CharacterBody body)
            {
                return body.desiredScale >= shrinks[body].size - 0.005f && body.currentScale >= body.desiredScale - 0.01f;
            }

            private bool CanSwallow(CharacterBody body)
            {
                return body != null && body.healthComponent != null && body.healthComponent.alive && body.master != null && !body.isBoss
                    && body.teamComponent.teamIndex != TeamIndex.Neutral && body.teamComponent.teamIndex != TeamIndex.None
                    && body.teamComponent.teamIndex != team && !swallowed.Contains(body);
            }

            // as the Primordial Cube's black hole pulls: harder further in, against the speed it already has
            private void Pull(CharacterBody body)
            {
                if (body.healthComponent.IsImmuneToPhysicsForces())
                {
                    return;
                }
                var offset = body.corePosition - center;
                var closeness = 1f - Mathf.Clamp(offset.magnitude / PullRadius, 0f, 1f);
                var velocity = body.characterMotor != null ? body.characterMotor.velocity : body.rigidbody != null ? body.rigidbody.velocity : Vector3.zero;
                var mass = body.characterMotor != null ? body.characterMotor.mass : body.rigidbody != null ? body.rigidbody.mass : 0f;
                velocity.y += Physics.gravity.y * Time.fixedDeltaTime;
                var force = -offset.normalized * PullForce * (1f - closeness) - velocity * (PullDamping * mass * closeness);
                var info = PhysForceInfo.Create();
                info.force = force;
                if (body.TryGetComponent(out IPhysMotor motor))
                {
                    motor.ApplyForceImpulse(in info);
                }
                else if (body.rigidbody != null)
                {
                    body.rigidbody.AddForceWithInfo(info);
                }
            }

            // Shrunk to nearly nothing, which every player's game sees from the size item, frozen and unhurt
            private void Eat(CharacterBody body)
            {
                swallowed.Add(body);
                power += body.healthComponent.fullCombinedHealth;
                quietTimer = QuietTime;
                // each shrink item takes away 1% of the normal size, so one already bigger needs more
                var size = body.desiredScale;
                var stacks = Mathf.Max(0, Mathf.RoundToInt((size - SmallScale) * 100f));
                shrinks[body] = (stacks, size);
                body.inventory.GiveItemPermanent(DLC4Content.Items.ReduceCharacterScale, stacks);
                body.AddBuff(RoR2Content.Buffs.Immune);
                if (body.master.TryGetComponent(out BaseAI ai))
                {
                    ai.enabled = false;
                }
                if (body.characterMotor != null)
                {
                    body.characterMotor.velocity = Vector3.zero;
                    body.characterMotor.enabled = false;
                }
                if (body.rigidbody != null)
                {
                    body.rigidbody.velocity = Vector3.zero;
                    body.rigidbody.isKinematic = true;
                }
                TeleportHelper.TeleportBody(body, center);
                EffectManager.SpawnEffect(Swallow.WaitForCompletion(), new EffectData { origin = center, scale = 3f }, transmit: true);
            }

            // Shrunk away: cloaked, with no hit boxes on any player's screen, it grows back to its size over the game's 2 seconds
            private void Hide(CharacterBody body)
            {
                hidden.Add(body);
                body.AddBuff(RoR2Content.Buffs.Cloak);
                body.AddBuff(RoR2Content.Buffs.Intangible);
                body.inventory.RemoveItemPermanent(DLC4Content.Items.ReduceCharacterScale, shrinks[body].stacks);
            }

            // its AI is off, so nothing presses its buttons any more
            private static void Hold(CharacterBody body)
            {
                if (body == null || body.inputBank == null)
                {
                    return;
                }
                body.inputBank.moveVector = Vector3.zero;
                body.inputBank.skill1.PushState(false);
                body.inputBank.skill2.PushState(false);
                body.inputBank.skill3.PushState(false);
                body.inputBank.skill4.PushState(false);
            }

            private void Burst()
            {
                var strength = Mathf.Sqrt(power);
                var radius = Mathf.Clamp(10f + strength * 0.5f, 10f, 100f);
                var speed = Mathf.Clamp(20f + strength * 0.3f, 20f, 80f);
                if (power > 0f)
                {
                    EffectManager.SpawnEffect(Nova.WaitForCompletion(), new EffectData { origin = center, scale = radius }, transmit: true);
                    EffectManager.SpawnEffect(Fireball.WaitForCompletion(), new EffectData { origin = center, scale = radius * 0.6f }, transmit: true);
                    EffectManager.SpawnEffect(BurstEffect.WaitForCompletion(), new EffectData { origin = center, scale = radius }, transmit: true);
                }
                foreach (var body in swallowed)
                {
                    Release(body);
                }
                if (power > 0f)
                {
                    foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                    {
                        if (body == null || body.healthComponent == null || !body.healthComponent.alive || body.teamComponent.teamIndex == TeamIndex.Neutral
                            || body.teamComponent.teamIndex == team)
                        {
                            continue;
                        }
                        var offset = body.corePosition - center;
                        var distance = offset.magnitude;
                        if (distance > radius + body.radius)
                        {
                            continue;
                        }
                        var falloff = 1f - 0.5f * Mathf.Clamp01(distance / radius);
                        // the swallowed ones fly out in every direction, the others away from it
                        var direction = swallowed.Contains(body) || distance < 0.5f ? (Random.onUnitSphere + Vector3.up).normalized : offset.normalized;
                        var mass = body.characterMotor != null ? body.characterMotor.mass : body.rigidbody != null ? body.rigidbody.mass : 100f;
                        body.healthComponent.TakeDamage(new DamageInfo
                        {
                            attacker = owner != null ? owner.gameObject : null,
                            inflictor = gameObject,
                            damage = power * DamageShare * falloff,
                            crit = owner != null && owner.RollCrit(),
                            position = body.corePosition,
                            force = direction * speed * falloff * Mathf.Min(mass, 1000f),
                            damageType = DamageType.BypassArmor,
                            procCoefficient = 0f,
                            damageColorIndex = DamageColorIndex.Item
                        });
                    }
                }
                swallowed.Clear();
                hidden.Clear();
                shrinks.Clear();
                Destroy(gameObject);
            }

            // seen again at once, moving and fighting again; one still shrinking grows back too
            private void Release(CharacterBody body)
            {
                if (body == null)
                {
                    return;
                }
                if (hidden.Contains(body))
                {
                    body.RemoveBuff(RoR2Content.Buffs.Cloak);
                    body.RemoveBuff(RoR2Content.Buffs.Intangible);
                }
                else
                {
                    body.inventory?.RemoveItemPermanent(DLC4Content.Items.ReduceCharacterScale, shrinks[body].stacks);
                }
                if (body.HasBuff(RoR2Content.Buffs.Immune))
                {
                    body.RemoveBuff(RoR2Content.Buffs.Immune);
                }
                if (body.characterMotor != null)
                {
                    body.characterMotor.enabled = true;
                }
                if (body.rigidbody != null && body.characterMotor == null)
                {
                    body.rigidbody.isKinematic = false;
                }
                if (body.master != null && body.master.TryGetComponent(out BaseAI ai))
                {
                    ai.enabled = true;
                }
            }

            // if the spot goes before it bursts, e.g. with the stage, the swallowed come out unharmed
            private void OnDestroy()
            {
                foreach (var body in swallowed)
                {
                    Release(body);
                }
                swallowed.Clear();
            }
        }
    }
}
