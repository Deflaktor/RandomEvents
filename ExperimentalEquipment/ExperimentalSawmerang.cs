using RoR2;
using RoR2.Projectile;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class ExperimentalSawmerang : ExperimentalEquipment
    {
        private static AsyncOperationHandle<GameObject> Sawmerang;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.Saw;
        public override string Description => "the Sawmerang's blades hunt monsters, cutting through them back and forth for 20 seconds";
        public override string PopupDescription => "The blades hunt monsters for 20 seconds.";

        public override void Preload()
        {
            Sawmerang = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Saw/Sawmerang.prefab");
        }

        // three blades, fanned out like the vanilla ones, which then hunt on their own
        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            var aim = slot.GetAimRay();
            foreach (var angle in new[] { -15f, 0f, 15f })
            {
                var rotation = Quaternion.LookRotation(aim.direction) * Quaternion.Euler(0f, angle, 0f);
                var saw = UnityEngine.Object.Instantiate(Sawmerang.WaitForCompletion(), aim.origin, rotation);
                if (saw.TryGetComponent(out BoomerangProjectile boomerang))
                {
                    boomerang.enabled = false;
                }
                if (saw.TryGetComponent(out Rigidbody rigidbody))
                {
                    rigidbody.isKinematic = true;
                }
                if (saw.TryGetComponent(out ProjectileController controller))
                {
                    controller.owner = body.gameObject;
                }
                if (saw.TryGetComponent(out TeamFilter teamFilter))
                {
                    teamFilter.teamIndex = body.teamComponent.teamIndex;
                }
                if (saw.TryGetComponent(out ProjectileDamage projectileDamage))
                {
                    projectileDamage.damage = body.damage;
                    projectileDamage.damageType.damageSource = DamageSource.Equipment;
                    projectileDamage.crit = body.RollCrit();
                    projectileDamage.damageColorIndex = DamageColorIndex.Item;
                }
                var hunter = saw.AddComponent<HuntingSaw>();
                hunter.owner = body;
                hunter.direction = rotation * Vector3.forward;
                NetworkServer.Spawn(saw);
            }
            return true;
        }

        // The blade flies at the nearest monster and through it, a little past it, then turns back for another pass, so
        // it cuts it again and again; once the monster dies, the next nearest. The host moves it, which its projectile
        // transform passes on to everyone.
        public class HuntingSaw : MonoBehaviour
        {
            private const float Lifetime = 20f;
            private const float Speed = 40f;
            private const float TurnRate = 720f;
            private const float Overshoot = 0.25f;
            private const float SearchRadius = 120f;
            public CharacterBody owner;
            public Vector3 direction;
            private CharacterBody target;
            private ProjectileOverlapAttack overlapAttack;
            private float age;
            // how long it keeps going straight after passing through its target
            private float passTimer;

            private void Start()
            {
                overlapAttack = GetComponent<ProjectileOverlapAttack>();
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                if (age >= Lifetime)
                {
                    Destroy(gameObject);
                    return;
                }
                if (target == null || target.healthComponent == null || !target.healthComponent.alive)
                {
                    target = FindTarget();
                    passTimer = 0f;
                }
                var position = transform.position;
                if (target != null)
                {
                    var toTarget = target.corePosition - position;
                    if (passTimer > 0f)
                    {
                        passTimer -= Time.fixedDeltaTime;
                        if (passTimer <= 0f && overlapAttack != null)
                        {
                            // a new pass hits it again
                            overlapAttack.ResetOverlapAttack();
                        }
                    }
                    else if (toTarget.sqrMagnitude <= target.radius * target.radius + 1f || Vector3.Dot(toTarget, direction) < 0f && toTarget.sqrMagnitude < 25f)
                    {
                        passTimer = Overshoot;
                    }
                    else
                    {
                        direction = Vector3.RotateTowards(direction, toTarget.normalized, TurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
                    }
                }
                else if (owner != null)
                {
                    // nothing to hunt: circle back to its owner
                    var toOwner = owner.corePosition + Vector3.up * 3f - position;
                    if (toOwner.sqrMagnitude > 9f)
                    {
                        direction = Vector3.RotateTowards(direction, toOwner.normalized, TurnRate * 0.5f * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
                    }
                }
                transform.SetPositionAndRotation(position + direction * Speed * Time.fixedDeltaTime, Util.QuaternionSafeLookRotation(direction));
            }

            private CharacterBody FindTarget()
            {
                if (owner == null || owner.teamComponent == null)
                {
                    return null;
                }
                var position = transform.position;
                return CharacterBody.readOnlyInstancesList
                    .Where(body => body != null && body.healthComponent != null && body.healthComponent.alive && body.teamComponent.teamIndex != owner.teamComponent.teamIndex
                        && body.teamComponent.teamIndex != TeamIndex.Neutral && (body.corePosition - position).sqrMagnitude <= SearchRadius * SearchRadius)
                    .OrderBy(body => (body.corePosition - position).sqrMagnitude)
                    .FirstOrDefault();
            }
        }
    }
}
