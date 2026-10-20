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
        public override string Description => "the Molotov (6-Pack) lights a blazing fire that burns everything nearby in its sight harder and harder for 5 seconds";
        public override string PopupDescription => "One bottle that lights a blazing fire, burning everything nearby in its sight harder and harder for 5 seconds.";

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

        // The experimental Molotov: the Molotov's and Gasoline's bursts where it landed, then for 5 seconds the ground ablaze
        // with Molotov fire, Kjaro's Band's fire tornado in the middle, and a huge Elder Lemurian's fireball bursting over it
        // again and again. Meanwhile
        // everything hostile nearby that the blaze can see burns, with stacks that pile up faster and faster to 5000% of the
        // thrower's damage in all, which the Grandparent's heat shows, and is scorched every half second with Helfire
        // Tincture's flash.
        public class Inferno : MonoBehaviour
        {
            private const float Duration = 5f;
            private const float Radius = 8f;
            private const float TickInterval = 0.1f;
            private const float TotalDamage = 50f;
            private const int MaxHeatStacks = 40;
            // where the blaze sees from
            private const float BlazeHeight = 1.5f;
            // an Elder Lemurian's fireball bursting over the middle again and again
            private const float FireballInterval = 0.5f;
            private const float FireballHeight = 4f;
            private const float FireballScale = 10f;
            private const int FirePools = 6;
            // Kjaro's Band's fire tornado, whose flames die down after a while, so a new one comes
            private const float TornadoInterval = 2.5f;
            private const float ScorchDamagePerSecond = 1.5f;
            private const float ScorchInterval = 0.5f;
            private static AsyncOperationHandle<GameObject> Gasoline;
            private static AsyncOperationHandle<GameObject> Fireball;
            private static AsyncOperationHandle<GameObject> Tornado;
            private static AsyncOperationHandle<GameObject> ScorchEffect;
            public CharacterBody owner;
            public Vector3 center;
            private readonly Dictionary<CharacterBody, int> heat = new Dictionary<CharacterBody, int>();
            private readonly List<GameObject> fires = new List<GameObject>();
            private float fireballTimer;
            private float tornadoTimer;
            private float age;
            private int tick;
            private int ticks;
            private float weightSum;
            private float scorchTimer;

            public static void Preload()
            {
                FireEffects.Preload();
                Gasoline = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/IgniteOnKill/IgniteExplosionVFX.prefab");
                Fireball = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LemurianBruiser/OmniExplosionVFXLemurianBruiserFireballImpact.prefab");
                Tornado = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ElementalRings/FireTornado.prefab");
                ScorchEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/BurnNearby/HelfireIgniteEffect.prefab");
            }

            private void Start()
            {
                ticks = Mathf.RoundToInt(Duration / TickInterval);
                for (int i = 1; i <= ticks; i++)
                {
                    weightSum += i * i;
                }
                FireEffects.SpawnEffect(FireEffects.Ignition, center, Radius);
                FireEffects.SpawnEffect(Gasoline, center + Vector3.up, Radius);
                var team = owner != null && owner.teamComponent != null ? owner.teamComponent.teamIndex : TeamIndex.Neutral;
                fires.Add(FireEffects.SpawnFire(FireEffects.FirePool, center, null, team));
                for (int i = 0; i < FirePools; i++)
                {
                    fires.Add(FireEffects.SpawnFire(FireEffects.FirePool, FireEffects.OnGround(center + FireEffects.RandomHorizontal(Radius * 0.8f)), null, team));
                }
            }

            private Vector3 BlazePosition => center + Vector3.up * BlazeHeight;

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                fireballTimer -= Time.fixedDeltaTime;
                if (fireballTimer <= 0f && age < Duration)
                {
                    fireballTimer = FireballInterval;
                    FireEffects.SpawnEffect(Fireball, center + Vector3.up * FireballHeight, FireballScale);
                }
                tornadoTimer -= Time.fixedDeltaTime;
                if (tornadoTimer <= 0f && age < Duration)
                {
                    tornadoTimer = TornadoInterval;
                    SpawnTornado();
                }
                scorchTimer -= Time.fixedDeltaTime;
                if (scorchTimer <= 0f && age < Duration)
                {
                    scorchTimer = ScorchInterval;
                    Scorch();
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

            private void SpawnTornado()
            {
                var team = owner != null && owner.teamComponent != null ? owner.teamComponent.teamIndex : TeamIndex.Neutral;
                fires.Add(FireEffects.SpawnFire(Tornado, center, null, team));
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
                foreach (var body in ShoneOn())
                {
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

            // only what the blaze can see, like the Grandparent's sun
            private List<CharacterBody> ShoneOn()
            {
                return CharacterBody.readOnlyInstancesList.Where(body => IsTarget(body, Radius)
                    && !Physics.Linecast(BlazePosition, body.corePosition, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)).ToList();
            }

            // 150% of the thrower's damage per second to everything the blaze can see, with Helfire Tincture's flash at each
            private void Scorch()
            {
                if (owner == null || owner.teamComponent == null)
                {
                    return;
                }
                foreach (var body in ShoneOn())
                {
                    EffectManager.SpawnEffect(ScorchEffect.WaitForCompletion(), new EffectData { origin = body.corePosition }, transmit: true);
                    body.healthComponent.TakeDamage(new DamageInfo
                    {
                        attacker = owner.gameObject,
                        inflictor = gameObject,
                        damage = owner.damage * ScorchDamagePerSecond * ScorchInterval,
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
                foreach (var fire in fires)
                {
                    if (fire != null)
                    {
                        Destroy(fire);
                    }
                }
                fires.Clear();
            }
        }
    }
}
