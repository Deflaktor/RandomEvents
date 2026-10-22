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
    // The Preon Accumulator charges as usual, then fires a Malachite elite's poison orb straight ahead instead of its own. Where the orb
    // hits the ground, it implodes like a Void Jailer's death, if smaller, and Void Jailer death bombs fly out in every direction and each hunts down a monster, the strongest
    // first, imploding when it gets there.
    public class ExperimentalPreonAccumulator : ExperimentalEquipment
    {
        private const float OrbSpeed = 40f;
        private const int Bombs = 8;
        // per bomb, as a multiple of the player's damage
        private const float BombDamage = 10f;
        private const float BombSpeed = 30f;
        // one after another while the implosion is at its biggest
        private const float FirstBombDelay = 0.2f;
        private const float BombInterval = 0.1f;
        // the orb's own implosion, smaller than the bombs'
        private const float ImplosionDamage = 5f;
        private const float ImplosionRadius = 4f;
        // only monsters within this many meters
        private const float Range = 100f;
        private static AsyncOperationHandle<GameObject> PoisonOrb;
        private static AsyncOperationHandle<GameObject> Bomb;
        private static AsyncOperationHandle<GameObject> OrbImpact;
        private static AsyncOperationHandle<GameObject> Implosion;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.BFG;
        public override string Description => "the Preon Accumulator fires a poison orb that bursts on the ground into void bombs, each hunting down a monster, the strongest first";
        public override string PopupDescription => "Fires a poison orb that bursts on the ground into void bombs, each hunting down a monster, the strongest first.";

        // it charges as usual, which every player's game shows
        public override bool ReplacesAction => false;

        public override void Preload()
        {
            PoisonOrb = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ElitePoison/PoisonOrbProjectile.prefab");
            Bomb = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/VoidJailer/VoidJailerDeathBombProjectile.prefab");
            OrbImpact = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ElitePoison/OmniExplosionVFXUrchin.prefab");
            Implosion = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/VoidJailer/VoidJailerDeathBombExplosion.prefab");
        }

        public override void Hook()
        {
            On.RoR2.Projectile.ProjectileManager.FireProjectileWithoutDamageType += ProjectileManager_FireProjectileWithoutDamageType;
            On.RoR2.Projectile.ProjectileExplosion.DetonateServer += ProjectileExplosion_DetonateServer;
        }

        // the charged shot, which the host fires
        private void ProjectileManager_FireProjectileWithoutDamageType(On.RoR2.Projectile.ProjectileManager.orig_FireProjectileWithoutDamageType orig, ProjectileManager self,
            GameObject prefab, Vector3 position, Quaternion rotation, GameObject owner, float damage, float force, bool crit, DamageColorIndex damageColorIndex, GameObject target, float speedOverride)
        {
            var body = owner != null ? owner.GetComponent<CharacterBody>() : null;
            if (prefab == null || !prefab.name.StartsWith("BeamSphere") || !IsExperimental(body))
            {
                orig(self, prefab, position, rotation, owner, damage, force, crit, damageColorIndex, target, speedOverride);
                return;
            }
            // straight ahead: the orb would drop, but the host moves it and passes that on to everyone
            var orb = Object.Instantiate(PoisonOrb.WaitForCompletion(), position, rotation);
            if (orb.TryGetComponent(out Rigidbody rigidbody))
            {
                rigidbody.useGravity = false;
            }
            if (orb.TryGetComponent(out ProjectileSimple simple))
            {
                simple.desiredForwardSpeed = OrbSpeed;
            }
            if (orb.TryGetComponent(out ProjectileController controller))
            {
                controller.owner = owner;
            }
            if (orb.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = body.teamComponent.teamIndex;
            }
            if (orb.TryGetComponent(out ProjectileDamage projectileDamage))
            {
                projectileDamage.damage = body.damage;
                projectileDamage.crit = crit;
            }
            NetworkServer.Spawn(orb);
        }

        // the orb bursts into the bombs instead of its own blast and spike
        private void ProjectileExplosion_DetonateServer(On.RoR2.Projectile.ProjectileExplosion.orig_DetonateServer orig, ProjectileExplosion self)
        {
            var owner = self.projectileController != null && self.projectileController.owner != null ? self.projectileController.owner.GetComponent<CharacterBody>() : null;
            if (!self.name.StartsWith("PoisonOrbProjectile") || !IsExperimental(owner))
            {
                orig(self);
                return;
            }
            var position = self.transform.position + Vector3.up * 1.5f;
            EffectManager.SpawnEffect(OrbImpact.WaitForCompletion(), new EffectData { origin = position, scale = 7f }, transmit: true);
            Implode(owner, position);
            var burst = new GameObject("RandomEventsVoidBombBurst").AddComponent<Burst>();
            burst.owner = owner;
            burst.position = position;
            burst.targets = Targets(owner, position);
        }

        // The bombs, one by one in a circle
        public class Burst : MonoBehaviour
        {
            public CharacterBody owner;
            public Vector3 position;
            public List<CharacterBody> targets;
            private int launched;
            private float age;

            private void FixedUpdate()
            {
                if (!NetworkServer.active || owner == null || launched >= Bombs)
                {
                    Destroy(gameObject);
                    return;
                }
                age += Time.fixedDeltaTime;
                while (launched < Bombs && age >= FirstBombDelay + launched * BombInterval)
                {
                    var direction = Quaternion.Euler(-30f, 360f * launched / Bombs, 0f) * Vector3.forward;
                    LaunchBomb(owner, position, direction, targets.Count > 0 ? targets[launched % targets.Count] : null);
                    launched++;
                }
            }
        }

        // the Void Jailer's death implosion, which like the bombs kills outright what isn't immune to void deaths
        private static void Implode(CharacterBody owner, Vector3 position)
        {
            EffectManager.SpawnEffect(Implosion.WaitForCompletion(), new EffectData { origin = position, scale = ImplosionRadius }, transmit: true);
            new BlastAttack
            {
                position = position,
                radius = ImplosionRadius,
                falloffModel = BlastAttack.FalloffModel.None,
                attacker = owner.gameObject,
                baseDamage = owner.damage * ImplosionDamage,
                crit = owner.RollCrit(),
                procCoefficient = 1f,
                damageType = DamageType.VoidDeath | DamageType.BypassBlock,
                damageColorIndex = DamageColorIndex.Item,
                attackerFiltering = AttackerFiltering.NeverHitSelf,
                teamIndex = owner.teamComponent.teamIndex
            }.Fire();
        }

        // the strongest first: bosses, then champions, then by health
        private static List<CharacterBody> Targets(CharacterBody owner, Vector3 position)
        {
            return CharacterBody.readOnlyInstancesList.Where(body => IsTarget(owner, body) && (body.corePosition - position).sqrMagnitude <= Range * Range)
                .OrderByDescending(body => body.isBoss).ThenByDescending(body => body.isChampion).ThenByDescending(body => body.healthComponent.fullCombinedHealth)
                .Take(Bombs).ToList();
        }

        private static bool IsTarget(CharacterBody owner, CharacterBody body)
        {
            return body != null && body.mainHurtBox != null && body.healthComponent != null && body.healthComponent.alive
                && body.teamComponent.teamIndex != owner.teamComponent.teamIndex && body.teamComponent.teamIndex != TeamIndex.Neutral;
        }

        private static void LaunchBomb(CharacterBody owner, Vector3 position, Vector3 direction, CharacterBody target)
        {
            var bomb = Object.Instantiate(Bomb.WaitForCompletion(), position, Util.QuaternionSafeLookRotation(direction));
            // its own search would pick the nearest
            if (bomb.TryGetComponent(out ProjectileSphereTargetFinder targetFinder))
            {
                targetFinder.enabled = false;
            }
            if (bomb.TryGetComponent(out ProjectileSimple simple))
            {
                simple.desiredForwardSpeed = BombSpeed;
            }
            if (bomb.TryGetComponent(out ProjectileImpactExplosion explosion))
            {
                // long enough to reach anything in range, and never on the thrower
                explosion.lifetime = Range / BombSpeed + 2f;
                explosion.blastAttackerFiltering = AttackerFiltering.NeverHitSelf;
            }
            if (bomb.TryGetComponent(out ProjectileController controller))
            {
                controller.owner = owner.gameObject;
            }
            if (bomb.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = owner.teamComponent.teamIndex;
            }
            if (bomb.TryGetComponent(out ProjectileDamage projectileDamage))
            {
                projectileDamage.damage = owner.damage * BombDamage;
                projectileDamage.crit = owner.RollCrit();
                projectileDamage.damageColorIndex = DamageColorIndex.Item;
            }
            var hunter = bomb.AddComponent<Hunter>();
            hunter.owner = owner;
            hunter.SetTarget(target);
            NetworkServer.Spawn(bomb);
        }

        // Keeps the bomb on its monster, a new one if it dies first, and sets it off once it gets there
        public class Hunter : MonoBehaviour
        {
            private const float ReachDistance = 2f;
            // it flies out for a moment before it turns
            private const float StraightTime = 0.3f;
            public CharacterBody owner;
            private CharacterBody target;
            private ProjectileTargetComponent targetComponent;
            private ProjectileImpactExplosion explosion;
            private float age;

            private void Awake()
            {
                targetComponent = GetComponent<ProjectileTargetComponent>();
                explosion = GetComponent<ProjectileImpactExplosion>();
            }

            public void SetTarget(CharacterBody body)
            {
                target = body;
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || targetComponent == null)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                if (owner != null && (target == null || !IsTarget(owner, target)))
                {
                    target = Targets(owner, transform.position).FirstOrDefault();
                }
                targetComponent.target = age >= StraightTime && target != null ? target.mainHurtBox.transform : null;
                if (target != null && explosion != null && (target.corePosition - transform.position).sqrMagnitude <= (ReachDistance + target.radius) * (ReachDistance + target.radius))
                {
                    explosion.Detonate();
                }
            }
        }
    }
}
