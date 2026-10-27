using RoR2;
using RoR2.Projectile;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class ExperimentalMolotov : ExperimentalEquipment
    {
        private static AsyncOperationHandle<GameObject> MolotovBottle;

        public override EquipmentDef EquipmentDef => DLC1Content.Equipment.Molotov;
        public override string PopupDescription => "One bottle that flares up like a sun, blasting everything nearby and setting what it sees ablaze.";

        public override void Preload()
        {
            MolotovBottle = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Molotov/MolotovSingleProjectile.prefab");
            Flare.Preload();
        }

        public override void Hook()
        {
            On.RoR2.Projectile.ProjectileExplosion.DetonateServer += ProjectileExplosion_DetonateServer;
        }

        // one bottle instead of the cluster
        public override bool Fire(EquipmentSlot slot)
        {
            var aim = slot.GetAimRay();
            ProjectileManager.instance.FireProjectile(new FireProjectileInfo
            {
                projectilePrefab = MolotovBottle.WaitForCompletion(),
                owner = slot.characterBody.gameObject,
                position = aim.origin,
                rotation = Quaternion.LookRotation(aim.direction),
                damage = slot.characterBody.damage,
                crit = slot.characterBody.RollCrit(),
                useSpeedOverride = true,
                speedOverride = 40f
            });
            return true;
        }

        // the experimental Molotov's bottle flares up instead of the bottle's own fire
        private void ProjectileExplosion_DetonateServer(On.RoR2.Projectile.ProjectileExplosion.orig_DetonateServer orig, ProjectileExplosion self)
        {
            var owner = self.projectileController != null && self.projectileController.owner != null ? self.projectileController.owner.GetComponent<CharacterBody>() : null;
            if (!IsExperimental(owner) || !self.name.StartsWith("MolotovSingleProjectile"))
            {
                orig(self);
                return;
            }
            var center = Physics.Raycast(self.transform.position + Vector3.up, Vector3.down, out var hit, 30f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : self.transform.position;
            var flare = new GameObject("RandomEventsFlare").AddComponent<Flare>();
            flare.owner = owner;
            flare.center = center;
        }

        // The experimental Molotov: where it lands, a Grandparent's sun flares up and burns out at once. In that moment it
        // blasts every monster within 40 meters for 2000% of the thrower's damage and throws it back, all of it within 6
        // meters and a quarter at the edge, and sets every one it sees within 100 meters ablaze with the Ignition Tank's stronger burn, 2000% in all
        // up close, less the further away.
        public class Flare : MonoBehaviour
        {
            private const float SunHeight = 4f;
            private const float Radius = 40f;
            private const float SweetSpot = 6f;
            private const float EdgeShare = 0.25f;
            private const float BlastDamage = 20f;
            // as fast for every monster, for its weight
            private const float BlastSpeed = 30f;
            private const float BurnDamage = 20f;
            // each stack of the burn, up close; one at the edge of its sight
            private const int BurnStacks = 10;
            private const float BurnRange = 100f;
            // the stacks come one after another, this far apart
            private const float BurnInterval = 0.1f;
            // what the Grandparent's sun shows on everything it heats
            private static AsyncOperationHandle<GameObject> HeatEffect;
            // what the sun shows as it appears and as it burns out
            private static AsyncOperationHandle<GameObject> SunBurst;
            public CharacterBody owner;
            public Vector3 center;
            // the burn stacks still to come for each that the sun saw
            private readonly Dictionary<CharacterBody, int> pendingStacks = new Dictionary<CharacterBody, int>();
            private float burnTimer;

            public static void Preload()
            {
                FireEffects.Preload();
                HeatEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Grandparent/IgniteDirectionalExplosionVFX.prefab");
                SunBurst = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Grandparent/GrandParentSunSpawn.prefab");
            }

            private Vector3 SunPosition => center + Vector3.up * SunHeight;

            private void Start()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                FireEffects.SpawnEffect(FireEffects.Ignition, center, Radius * 0.5f);
                FireEffects.SpawnEffect(SunBurst, SunPosition, 1f);
                if (owner != null && owner.teamComponent != null)
                {
                    Blast();
                }
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || owner == null || pendingStacks.Count == 0)
                {
                    Destroy(gameObject);
                    return;
                }
                burnTimer -= Time.fixedDeltaTime;
                if (burnTimer > 0f)
                {
                    return;
                }
                burnTimer = BurnInterval;
                foreach (var (body, stacks) in pendingStacks.ToList())
                {
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive || stacks <= 0)
                    {
                        pendingStacks.Remove(body);
                        continue;
                    }
                    Burn(body);
                    pendingStacks[body] = stacks - 1;
                }
            }

            // a stack of the stronger burn, with the sun's heat flash
            private void Burn(CharacterBody body)
            {
                EffectManager.SpawnEffect(HeatEffect.WaitForCompletion(), new EffectData
                {
                    origin = body.corePosition,
                    rotation = Util.QuaternionSafeLookRotation(SunPosition - body.corePosition),
                    scale = body.bestFitRadius
                }, transmit: true);
                var info = new InflictDotInfo
                {
                    attackerObject = owner.gameObject,
                    victimObject = body.gameObject,
                    dotIndex = DotController.DotIndex.StrongerBurn,
                    damageMultiplier = 1f,
                    totalDamage = owner.damage * BurnDamage / BurnStacks,
                    hitHurtBox = body.mainHurtBox
                };
                DotController.InflictDot(ref info);
            }

            private void Blast()
            {
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive || body.teamComponent.teamIndex == owner.teamComponent.teamIndex
                        || body.teamComponent.teamIndex == TeamIndex.Neutral)
                    {
                        continue;
                    }
                    var distance = Mathf.Max(0f, (body.corePosition - center).magnitude - body.radius);
                    if (distance <= Radius)
                    {
                        var falloff = 1f - (1f - EdgeShare) * Mathf.Clamp01((distance - SweetSpot) / (Radius - SweetSpot));
                        var offset = body.corePosition - center;
                        var direction = (new Vector3(offset.x, 0f, offset.z).normalized + Vector3.up * 0.5f).normalized;
                        var mass = body.characterMotor != null ? body.characterMotor.mass : body.rigidbody != null ? body.rigidbody.mass : 100f;
                        body.healthComponent.TakeDamage(new DamageInfo
                        {
                            attacker = owner.gameObject,
                            inflictor = gameObject,
                            damage = owner.damage * BlastDamage * falloff,
                            damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Equipment),
                            force = direction * BlastSpeed * falloff * Mathf.Min(mass, 1000f),
                            crit = owner.RollCrit(),
                            position = body.corePosition,
                            procCoefficient = 1f,
                            damageColorIndex = DamageColorIndex.Item
                        });
                    }
                    // only what the sun sees
                    if (distance > BurnRange || !body.healthComponent.alive
                        || Physics.Linecast(SunPosition, body.corePosition, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                    pendingStacks[body] = Mathf.Max(1, Mathf.RoundToInt(BurnStacks * (1f - Mathf.Clamp01((distance - SweetSpot) / (BurnRange - SweetSpot)))));
                }
            }
        }
    }
}
