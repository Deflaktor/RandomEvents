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
    // Instead of its black hole, a bright white spot where the player aims that pulls everything in and swallows the
    // monsters that reach it. Once it has swallowed none for 5 seconds, it bursts: the bigger the health it swallowed,
    // the bigger the blast, the harder it hurts, and the further the swallowed monsters fly out of it.
    public class ExperimentalPrimordialCube : ExperimentalEquipment
    {
        private const float AimRange = 200f;
        private const float HoverHeight = 2f;
        private static AsyncOperationHandle<GameObject> Swallow;
        private static AsyncOperationHandle<GameObject> Nova;
        private static AsyncOperationHandle<GameObject> Fireball;
        private static AsyncOperationHandle<GameObject> BurstEffect;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Blackhole;
        public override string Description => "the Primordial Cube opens a bright spot where you aim that swallows monsters and bursts, the harder the more it swallowed";
        public override string PopupDescription => "Opens a bright spot where you aim that pulls in and swallows monsters, then bursts, throwing them out. The more it swallowed, the bigger the burst.";

        public override void Preload()
        {
            FireEffects.Preload();
            Swallow = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC4/SpawnAltar/ChargeUpEffect.prefab");
            // Genesis Loop's nova, which is the Wandering Vagrant's
            Nova = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Vagrant/VagrantNovaExplosion.prefab");
            Fireball = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/FusionCellDestructible/FusionCellExplosion.prefab");
            BurstEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Jellyfish/JellyfishNova.prefab");
        }

        // on the ground where the player aims, at once; with nothing to aim at, nothing happens and it keeps its charge
        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            if (!Physics.Raycast(slot.GetAimRay(), out var hit, AimRange, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            var spot = new GameObject("RandomEventsPullSpot").AddComponent<Altar>();
            spot.owner = body;
            spot.center = hit.point + Vector3.up * HoverHeight;
            return true;
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
            // nearly nothing of their size left
            private const int ShrinkStacks = 95;
            // of the swallowed health, what each monster in the blast loses, all of it at the center and half at the edge
            private const float DamageShare = 0.3f;
            public CharacterBody owner;
            public Vector3 center;
            private readonly List<CharacterBody> swallowed = new List<CharacterBody>();
            private float power;
            private float quietTimer = QuietTime;
            private float spotTimer;

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                // the bright white spot, flashing again and again
                spotTimer -= Time.fixedDeltaTime;
                if (spotTimer <= 0f)
                {
                    spotTimer = SpotInterval;
                    FireEffects.SpawnEffect(FireEffects.WhiteGlint, center, 3f);
                    FireEffects.SpawnEffect(FireEffects.LightBurst, center, 3f);
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
                }
                quietTimer -= Time.fixedDeltaTime;
                if (quietTimer <= 0f)
                {
                    Burst();
                }
            }

            private bool CanSwallow(CharacterBody body)
            {
                return body != null && body.healthComponent != null && body.healthComponent.alive && body.master != null && !body.isBoss
                    && body.teamComponent.teamIndex != TeamIndex.Neutral && body.teamComponent.teamIndex != TeamIndex.None
                    && (owner == null || body.teamComponent.teamIndex != owner.teamComponent.teamIndex) && !swallowed.Contains(body);
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
                body.inventory.GiveItemPermanent(DLC4Content.Items.ReduceCharacterScale, ShrinkStacks);
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
                            || (owner != null && body.teamComponent.teamIndex == owner.teamComponent.teamIndex))
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
                Destroy(gameObject);
            }

            // back to its size, over the 2 seconds the game takes, moving and fighting again
            private static void Release(CharacterBody body)
            {
                if (body == null)
                {
                    return;
                }
                body.inventory?.RemoveItemPermanent(DLC4Content.Items.ReduceCharacterScale, ShrinkStacks);
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
