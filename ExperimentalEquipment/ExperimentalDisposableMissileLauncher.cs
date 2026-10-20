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
    public class ExperimentalDisposableMissileLauncher : ExperimentalEquipment
    {
        // per orb, as a multiple of the player's damage
        private const float OrbDamage = 10f;
        private const float LaunchInterval = 0.5f;
        // how many times each monster is hit
        private const int Rounds = 3;
        private static AsyncOperationHandle<GameObject> SunOrb;

        // only monsters within this many meters
        private const float Range = 200f;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.CommandMissile;
        public override float CooldownScale => 0.25f;
        public override string Description => "Disposable Missile Launcher sends out Egocentrism's orbs, one every half second, until every monster nearby is hit three times";
        public override string PopupDescription => "Sends out Egocentrism's orbs, one every half second, until every monster nearby is hit three times.";

        public override void Preload()
        {
            SunOrb = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/LunarSun/LunarSunProjectile.prefab");
        }

        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            if (!Launcher.Targets(body).Any())
            {
                return false;
            }
            var launcher = new GameObject("RandomEventsSunOrbLauncher").AddComponent<Launcher>();
            launcher.owner = body;
            return true;
        }

        // Each round, an orb for every monster alive then, one at a time; then the next round
        public class Launcher : MonoBehaviour
        {
            public CharacterBody owner;
            private readonly Queue<CharacterBody> queue = new Queue<CharacterBody>();
            private int round;
            private float timer;

            public static IEnumerable<CharacterBody> Targets(CharacterBody owner)
            {
                return CharacterBody.readOnlyInstancesList.Where(body => IsTarget(owner, body));
            }

            private static bool IsTarget(CharacterBody owner, CharacterBody body)
            {
                return body != null && body.mainHurtBox != null && body.healthComponent != null && body.healthComponent.alive
                    && body.teamComponent.teamIndex != owner.teamComponent.teamIndex && body.teamComponent.teamIndex != TeamIndex.Neutral
                    && (body.corePosition - owner.corePosition).sqrMagnitude <= Range * Range;
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || owner == null || !owner.healthComponent.alive)
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
                // those that died or got out of reach in the meantime are left out
                while (queue.Count > 0 && !IsTarget(owner, queue.Peek()))
                {
                    queue.Dequeue();
                }
                if (queue.Count == 0)
                {
                    if (round >= Rounds)
                    {
                        Destroy(gameObject);
                        return;
                    }
                    round++;
                    foreach (var target in Targets(owner).OrderBy(target => (target.corePosition - owner.corePosition).sqrMagnitude).ToList())
                    {
                        queue.Enqueue(target);
                    }
                    if (queue.Count == 0)
                    {
                        Destroy(gameObject);
                        return;
                    }
                }
                Launch(queue.Dequeue());
            }

            // Egocentrism's orb as it leaves its orbit for a target, which it does on the host: no orbit and no target
            // search of its own, but flying and steering at the given monster, exploding when it hits
            private void Launch(CharacterBody target)
            {
                var position = owner.corePosition + Vector3.up * 2f;
                var orb = Instantiate(SunOrb.WaitForCompletion(), position, Util.QuaternionSafeLookRotation(target.corePosition - position));
                foreach (var behaviour in new Behaviour[] { orb.GetComponent<LunarSunProjectileController>(), orb.GetComponent<ProjectileOwnerOrbiter>(),
                    orb.GetComponent<ProjectileSphereTargetFinder>(), orb.GetComponent<ProjectileDeployToOwner>() })
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = false;
                    }
                }
                foreach (var behaviour in new Behaviour[] { orb.GetComponent<ProjectileSimple>(), orb.GetComponent<ProjectileSteerTowardTarget>(), orb.GetComponent<ProjectileImpactExplosion>() })
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = true;
                    }
                }
                if (orb.TryGetComponent(out Rigidbody rigidbody))
                {
                    rigidbody.isKinematic = false;
                }
                if (orb.TryGetComponent(out ProjectileTargetComponent targetComponent))
                {
                    targetComponent.target = target.mainHurtBox.transform;
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
                    projectileDamage.damage = owner.damage * OrbDamage;
                    projectileDamage.crit = owner.RollCrit();
                    projectileDamage.damageColorIndex = DamageColorIndex.Item;
                }
                NetworkServer.Spawn(orb);
            }
        }
    }
}
