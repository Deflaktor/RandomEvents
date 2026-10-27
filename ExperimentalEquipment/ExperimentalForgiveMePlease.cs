using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class ExperimentalForgiveMePlease : ExperimentalEquipment
    {
        private const float DollSpeed = 6f;
        private const float DollFuse = 10f;
        private const float DollNukeRadius = 40f;
        private static AsyncOperationHandle<GameObject> Doll;

        public override EquipmentDef EquipmentDef => RoR2Content.Equipment.DeathProjectile;
        public override string PopupDescription => "The doll walks straight ahead and explodes like a mini nuke after 10 seconds.";

        public override void Preload()
        {
            Doll = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/DeathProjectile/DeathProjectile.prefab");
            EventHotPotato.MiniNuke.Preload();
        }

        // The doll, without its own behavior: no on-kill effects, no sticking, no physics. The host moves it, which its
        // projectile transform passes on to everyone. Each player can have 3 out at once, as with the original doll.
        public override bool Fire(EquipmentSlot slot)
        {
            var body = slot.characterBody;
            if (body.master == null || body.master.IsDeployableLimited(DeployableSlot.DeathProjectile))
            {
                return false;
            }
            var aim = slot.GetAimRay().direction;
            var direction = new Vector3(aim.x, 0f, aim.z);
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : body.transform.forward;
            var doll = UnityEngine.Object.Instantiate(Doll.WaitForCompletion(), body.footPosition + direction * 1.5f + Vector3.up, Quaternion.LookRotation(direction));
            foreach (var behaviour in new Behaviour[] { doll.GetComponent<DeathProjectile>(), doll.GetComponent<ProjectileStickOnImpact>(), doll.GetComponent<ProjectileSimple>(),
                doll.GetComponent<ApplyTorqueOnStart>(), doll.GetComponent<DestroyOnTimer>() })
            {
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }
            }
            if (doll.TryGetComponent(out Rigidbody rigidbody))
            {
                rigidbody.isKinematic = true;
            }
            if (doll.TryGetComponent(out ProjectileController controller))
            {
                controller.owner = body.gameObject;
            }
            if (doll.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = body.teamComponent.teamIndex;
            }
            if (doll.TryGetComponent(out TeamComponent teamComponent))
            {
                teamComponent.teamIndex = body.teamComponent.teamIndex;
            }
            var walker = doll.AddComponent<WalkingDoll>();
            walker.owner = body;
            walker.direction = direction;
            NetworkServer.Spawn(doll);
            return true;
        }

        // Walks along the ground, bouncing off walls, then goes off like Hot Potato's bomb, sparing players
        public class WalkingDoll : MonoBehaviour
        {
            public CharacterBody owner;
            public Vector3 direction;
            private const float StepRate = 12f;
            private float age;

            private void FixedUpdate()
            {
                if (!NetworkServer.active)
                {
                    return;
                }
                age += Time.fixedDeltaTime;
                var position = transform.position;
                var step = direction * DollSpeed * Time.fixedDeltaTime;
                // a ramp it walks up, a wall it bounces off
                if (Physics.Raycast(position, direction, out var wall, step.magnitude + 0.5f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) && wall.normal.y < 0.5f)
                {
                    // reflected along the ground; straight back if that leaves no direction
                    var reflected = Vector3.Reflect(direction, wall.normal);
                    reflected.y = 0f;
                    direction = reflected.sqrMagnitude > 0.01f ? reflected.normalized : -direction;
                }
                else
                {
                    position += step;
                }
                // on the ground below, or falling if there is none
                // with a little hop for each step, highest between leaning to one side and the other
                var sway = Mathf.Sin(age * StepRate);
                if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out var hit, 6f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    position.y = hit.point.y + 0.5f + (1f - Mathf.Abs(sway)) * 0.25f;
                }
                else
                {
                    position.y -= 10f * Time.fixedDeltaTime;
                }
                // Upright: the doll's model lies along its forward axis, head first and face down, so that axis points up
                // and its down axis ahead. Then a waddle from side to side, and a nod of the head with every step.
                var nod = Mathf.Sin(age * StepRate * 2f) * 10f;
                var upright = Quaternion.LookRotation(Vector3.up, -direction);
                var side = Vector3.Cross(Vector3.up, direction);
                transform.SetPositionAndRotation(position, Quaternion.AngleAxis(sway * 15f, direction) * Quaternion.AngleAxis(nod, side) * upright);
                if (age >= DollFuse)
                {
                    var groundZero = Physics.Raycast(position, Vector3.down, out var floor, 50f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? floor.point : position;
                    EventHotPotato.Nuke(groundZero, DollNukeRadius, 0f, owner != null ? owner.gameObject : null, DamageSource.Equipment);
                    Destroy(gameObject);
                }
            }
        }
    }
}
