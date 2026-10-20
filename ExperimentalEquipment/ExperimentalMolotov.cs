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
        public override string Description => "the Molotov (6-Pack) lights a blazing light that burns everything nearby it shines on harder and harder for 5 seconds";
        public override string PopupDescription => "One bottle that lights a blazing light, burning everything nearby it shines on harder and harder for 5 seconds.";

        public override void Preload()
        {
            MolotovBottle = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Molotov/MolotovSingleProjectile.prefab");
            Inferno.Preload();
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

        // the experimental Molotov's bottle sets its sea of fire instead of the bottle's own fire
        private void ProjectileExplosion_DetonateServer(On.RoR2.Projectile.ProjectileExplosion.orig_DetonateServer orig, ProjectileExplosion self)
        {
            var owner = self.projectileController != null && self.projectileController.owner != null ? self.projectileController.owner.GetComponent<CharacterBody>() : null;
            if (!IsExperimental(owner) || !self.name.StartsWith("MolotovSingleProjectile"))
            {
                orig(self);
                return;
            }
            var center = Physics.Raycast(self.transform.position + Vector3.up, Vector3.down, out var hit, 30f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : self.transform.position;
            var inferno = new GameObject("RandomEventsInferno").AddComponent<Inferno>();
            inferno.owner = owner;
            inferno.center = center;
        }

        // The experimental Molotov: a bright white spot where it landed, then a blazing light there for 5 seconds, from a
        // small Genesis Loop nova again and again. Meanwhile everything hostile nearby that the light shines on burns, with
        // stacks that pile up faster and faster to 5000% of the thrower's damage in all, which the Grandparent's heat
        // shows, and its middle burns continuously.
        public class Inferno : MonoBehaviour
        {
            private const float Duration = 5f;
            private const float Radius = 8f;
            private const float TickInterval = 0.1f;
            private const float TotalDamage = 50f;
            private const int MaxHeatStacks = 20;
            // a small Genesis Loop nova over the middle again and again, for its light
            private const float LightInterval = 0.5f;
            private const float LightHeight = 1.5f;
            private const float LightScale = 3f;
            private const float CoreRadius = 4f;
            private const float CoreDamagePerSecond = 1.5f;
            private const float CoreTickInterval = 0.25f;
            public CharacterBody owner;
            public Vector3 center;
            private readonly Dictionary<CharacterBody, int> heat = new Dictionary<CharacterBody, int>();
            private float lightTimer;
            private float age;
            private int tick;
            private int ticks;
            private float weightSum;
            private float coreTimer;

            public static void Preload()
            {
                FireEffects.Preload();
            }

            private void Start()
            {
                ticks = Mathf.RoundToInt(Duration / TickInterval);
                for (int i = 1; i <= ticks; i++)
                {
                    weightSum += i * i;
                }
                FireEffects.SpawnEffect(FireEffects.WhiteGlint, center + Vector3.up, 2f);
                FireEffects.SpawnEffect(FireEffects.LightBurst, center + Vector3.up, 2f);
            }

            private Vector3 LightPosition => center + Vector3.up * LightHeight;

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                lightTimer -= Time.fixedDeltaTime;
                if (lightTimer <= 0f && age < Duration)
                {
                    lightTimer = LightInterval;
                    FireEffects.SpawnEffect(FireEffects.Nova, LightPosition, LightScale);
                }
                coreTimer -= Time.fixedDeltaTime;
                if (coreTimer <= 0f && age < Duration)
                {
                    coreTimer = CoreTickInterval;
                    BurnCore();
                }
                while (tick < ticks && age >= (tick + 1) * TickInterval)
                {
                    tick++;
                    Burn(tick);
                }
                if (age >= Duration)
                {
                    Destroy(gameObject);
                }
            }

            private bool IsTarget(CharacterBody body, float radius)
            {
                return body != null && body.healthComponent != null && body.healthComponent.alive && body.teamComponent.teamIndex != owner.teamComponent.teamIndex
                    && body.teamComponent.teamIndex != TeamIndex.Neutral && (body.corePosition - center).sqrMagnitude <= (radius + body.radius) * (radius + body.radius);
            }

            // the share of the burn for this tick grows with its square, so the stacks pile up faster and faster
            private void Burn(int tickNumber)
            {
                if (owner == null || owner.teamComponent == null)
                {
                    return;
                }
                var damage = TotalDamage * owner.damage * tickNumber * tickNumber / weightSum;
                var heatStacks = Mathf.Min(MaxHeatStacks, Mathf.CeilToInt(MaxHeatStacks * (float)tickNumber / ticks));
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    // only what the light shines on, like the Grandparent's sun
                    if (!IsTarget(body, Radius) || Physics.Linecast(LightPosition, body.corePosition, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                    var info = new InflictDotInfo
                    {
                        attackerObject = owner.gameObject,
                        victimObject = body.gameObject,
                        dotIndex = DotController.DotIndex.Burn,
                        damageMultiplier = 1f,
                        totalDamage = damage,
                        hitHurtBox = body.mainHurtBox
                    };
                    DotController.InflictDot(ref info);
                    var added = heat.GetValueOrDefault(body);
                    if (heatStacks > added)
                    {
                        body.SetBuffCount(RoR2Content.Buffs.Overheat.buffIndex, body.GetBuffCount(RoR2Content.Buffs.Overheat) + heatStacks - added);
                        heat[body] = heatStacks;
                    }
                }
            }

            // 150% of the thrower's damage per second to everything hostile in the middle
            private void BurnCore()
            {
                if (owner == null || owner.teamComponent == null)
                {
                    return;
                }
                foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
                {
                    if (!IsTarget(body, CoreRadius))
                    {
                        continue;
                    }
                    body.healthComponent.TakeDamage(new DamageInfo
                    {
                        attacker = owner.gameObject,
                        inflictor = gameObject,
                        damage = owner.damage * CoreDamagePerSecond * CoreTickInterval,
                        crit = owner.RollCrit(),
                        position = body.corePosition,
                        procCoefficient = 0f,
                        damageColorIndex = DamageColorIndex.Item
                    });
                }
            }

            private void OnDestroy()
            {
                foreach (var (body, added) in heat)
                {
                    if (body != null)
                    {
                        body.SetBuffCount(RoR2Content.Buffs.Overheat.buffIndex, Mathf.Max(0, body.GetBuffCount(RoR2Content.Buffs.Overheat) - added));
                    }
                }
                heat.Clear();
            }
        }
    }
}
