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
    // Instead of its totem, a Primordial Cube's black hole that swallows the monsters it pulls in. Once it has swallowed
    // none for 5 seconds, it bursts: the bigger the health it swallowed, the bigger the blast, the harder it hurts, and
    // the further the swallowed monsters fly out of it.
    public class ExperimentalTotemOfSacrifice : ExperimentalEquipment
    {
        private const float PlaceRange = 50f;
        private const float HoverHeight = 2f;
        private static AsyncOperationHandle<GameObject> BlackHole;
        private static AsyncOperationHandle<GameObject> Swallow;
        private static AsyncOperationHandle<GameObject> Nova;
        private static AsyncOperationHandle<GameObject> Fireball;
        private static AsyncOperationHandle<GameObject> BurstEffect;

        public override EquipmentDef EquipmentDef => DLC4Content.Equipment.SpawnAltar;
        public override string Description => "the Totem of Sacrifice opens a black hole that swallows monsters and bursts, the harder the more it swallowed";
        public override string PopupDescription => "Opens a black hole that swallows the monsters it pulls in, then bursts, throwing them out. The more it swallowed, the bigger the burst.";

        public override void Preload()
        {
            BlackHole = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Blackhole/GravSphere.prefab");
            Swallow = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC4/SpawnAltar/ChargeUpEffect.prefab");
            // Genesis Loop's nova, which is the Wandering Vagrant's
            Nova = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Vagrant/VagrantNovaExplosion.prefab");
            Fireball = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/FusionCellDestructible/FusionCellExplosion.prefab");
            BurstEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Jellyfish/JellyfishNova.prefab");
        }

        // where the player aims, on the ground, or in front of them
        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            var aim = slot.GetAimRay();
            var point = Physics.Raycast(aim, out var hit, PlaceRange, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : body.footPosition + body.transform.forward * 5f;
            var center = point + Vector3.up * HoverHeight;
            var hole = Object.Instantiate(BlackHole.WaitForCompletion(), center, Quaternion.identity);
            if (hole.TryGetComponent(out ProjectileSimple simple))
            {
                // it stays where it is until it bursts
                simple.desiredForwardSpeed = 0f;
                simple.lifetime = 600f;
            }
            if (hole.TryGetComponent(out ProjectileController controller))
            {
                controller.owner = body.gameObject;
            }
            if (hole.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = body.teamComponent.teamIndex;
            }
            if (hole.TryGetComponent(out ProjectileDamage projectileDamage))
            {
                projectileDamage.damage = 0f;
            }
            NetworkServer.Spawn(hole);
            var altar = hole.AddComponent<Altar>();
            altar.owner = body;
            altar.center = center;
            return true;
        }

        public class Altar : MonoBehaviour
        {
            private const float SwallowRadius = 3f;
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

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (CanSwallow(body) && (body.corePosition - center).sqrMagnitude <= (SwallowRadius + body.radius) * (SwallowRadius + body.radius))
                    {
                        Eat(body);
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

            // if the black hole goes before it bursts, e.g. with the stage, the swallowed come out unharmed
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
