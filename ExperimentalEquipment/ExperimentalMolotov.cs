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
        public override string Description => "the Molotov (6-Pack) flares up like a sun, blasting everything nearby and setting what it sees ablaze";
        public override string PopupDescription => "One bottle that flares up like a sun where it lands, blasting everything nearby and setting what it sees ablaze.";

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

        // The experimental Molotov: where it lands, a Grandparent's sun grows for half a second and is gone. In that moment
        // it blasts every monster within 20 meters for 2000% of the thrower's damage, all of it within 6 meters and a quarter
        // at the edge, and sets every one it sees within 100 meters ablaze with the Ignition Tank's stronger burn, 2000% in all
        // up close, less the further away.
        public class Flare : MonoBehaviour
        {
            // the sun grows for 0.6 seconds, so it never gets to burn on its own
            private const float SunTime = 0.5f;
            private const float SunHeight = 4f;
            private const float Radius = 20f;
            private const float SweetSpot = 6f;
            private const float EdgeShare = 0.25f;
            private const float BlastDamage = 20f;
            private const float BurnDamage = 20f;
            // each stack of the burn, up close; one at the edge of its sight
            private const int BurnStacks = 10;
            private const float BurnRange = 100f;
            // what the Grandparent's sun shows on everything it heats
            private static AsyncOperationHandle<GameObject> HeatEffect;
            public CharacterBody owner;
            public Vector3 center;
            private GenericOwnership sun;

            public static void Preload()
            {
                FireEffects.Preload();
                HeatEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Grandparent/IgniteDirectionalExplosionVFX.prefab");
            }

            private Vector3 SunPosition => center + Vector3.up * SunHeight;

            private void Start()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                var team = owner != null && owner.teamComponent != null ? owner.teamComponent.teamIndex : TeamIndex.Neutral;
                FireEffects.SpawnEffect(FireEffects.Ignition, center, Radius * 0.5f);
                sun = FireEffects.SpawnSun(SunPosition, gameObject, team);
                if (owner != null && owner.teamComponent != null)
                {
                    Blast();
                }
                Destroy(gameObject, SunTime);
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
                        body.healthComponent.TakeDamage(new DamageInfo
                        {
                            attacker = owner.gameObject,
                            inflictor = gameObject,
                            damage = owner.damage * BlastDamage * falloff,
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
                    var stacks = Mathf.Max(1, Mathf.RoundToInt(BurnStacks * (1f - Mathf.Clamp01((distance - SweetSpot) / (BurnRange - SweetSpot)))));
                    EffectManager.SpawnEffect(HeatEffect.WaitForCompletion(), new EffectData
                    {
                        origin = body.corePosition,
                        rotation = Util.QuaternionSafeLookRotation(SunPosition - body.corePosition),
                        scale = body.bestFitRadius
                    }, transmit: true);
                    for (int i = 0; i < stacks; i++)
                    {
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
                }
            }

            private void OnDestroy()
            {
                if (sun != null)
                {
                    Destroy(sun.gameObject);
                }
            }
        }
    }
}
