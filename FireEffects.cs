using RoR2;
using RoR2.Projectile;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    // Fire to look at, for the experimental Molotov and the mini nuke: projectiles whose every part that would hurt or spread
    // runs on the host alone, and is off, and a Grandparent's sun that burns nothing
    public static class FireEffects
    {
        public static AsyncOperationHandle<GameObject> FirePool;
        public static AsyncOperationHandle<GameObject> BurningOil;
        public static AsyncOperationHandle<GameObject> Ignition;
        public static AsyncOperationHandle<GameObject> Fireball;
        public static AsyncOperationHandle<GameObject> Sun;

        public static void Preload()
        {
            if (FirePool.IsValid())
            {
                return;
            }
            FirePool = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Molotov/MolotovProjectileDotZone.prefab");
            BurningOil = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Chef/ChefImpactOilBoosted.prefab");
            Ignition = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Molotov/MolotovClusterIgniteExplosionVFX.prefab");
            Fireball = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Chef/BoostedSearFireballProjectileExplosionVFX.prefab");
            Sun = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Grandparent/GrandParentSun.prefab");
        }

        public static void SpawnEffect(AsyncOperationHandle<GameObject> effect, Vector3 position, float scale)
        {
            EffectManager.SpawnEffect(effect.WaitForCompletion(), new EffectData { origin = position, scale = scale }, transmit: true);
        }

        public static GameObject SpawnFire(AsyncOperationHandle<GameObject> prefab, Vector3 position, GameObject owner, TeamIndex teamIndex)
        {
            var fire = Object.Instantiate(prefab.WaitForCompletion(), position, Quaternion.identity);
            if (fire.TryGetComponent(out OilController oil))
            {
                // it sets itself on fire as it starts, which turns its damage zone on again
                oil.enabled = false;
            }
            foreach (var behaviour in new Behaviour[] { fire.GetComponent<ProjectileDotZone>(), fire.GetComponent<ProjectileOverlapAttack>() })
            {
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }
            }
            if (fire.TryGetComponent(out ProjectileDamage projectileDamage))
            {
                projectileDamage.damage = 0f;
            }
            if (fire.TryGetComponent(out ProjectileController controller) && owner != null)
            {
                controller.owner = owner;
            }
            if (fire.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = teamIndex;
            }
            NetworkServer.Spawn(fire);
            return fire;
        }

        // It shines while it has an owner, which can be any object on the host, then fades out by itself
        public static GenericOwnership SpawnSun(Vector3 position, GameObject owner, TeamIndex teamIndex)
        {
            var sunObject = Object.Instantiate(Sun.WaitForCompletion(), position, Quaternion.identity);
            if (sunObject.TryGetComponent(out GrandParentSunController controller))
            {
                controller.enabled = false;
            }
            if (sunObject.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = teamIndex;
            }
            var ownership = sunObject.GetComponent<GenericOwnership>();
            if (ownership != null)
            {
                ownership.ownerObject = owner;
            }
            NetworkServer.Spawn(sunObject);
            return ownership;
        }

        public static Vector3 RandomHorizontal(float radius)
        {
            var circle = Random.insideUnitCircle * radius;
            return new Vector3(circle.x, 0f, circle.y);
        }

        public static Vector3 OnGround(Vector3 position)
        {
            return Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out var hit, 15f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? hit.point : position;
        }
    }

    // Ground covered in Molotov fire and CHEF's burning oil, with fireballs bursting now and then, under a Grandparent's
    // sun, for a while; few of the parts with their own sound, which add up quickly
    public class GroundFire : MonoBehaviour
    {
        private const float FireballInterval = 2f;
        private const int FirePools = 10;
        private const int OilPuddles = 2;
        public Vector3 center;
        public float radius = 12f;
        public float duration = 10f;
        public float sunHeight = 4f;
        public float sunDuration = 5f;
        public TeamIndex teamIndex = TeamIndex.Neutral;
        private readonly List<GameObject> fires = new List<GameObject>();
        private GenericOwnership sun;
        private float age;
        private float fireballTimer;

        private void Start()
        {
            FireEffects.SpawnEffect(FireEffects.Ignition, center, radius);
            FireEffects.SpawnEffect(FireEffects.Fireball, center, radius * 0.5f);
            sun = FireEffects.SpawnSun(center + Vector3.up * sunHeight, gameObject, teamIndex);
            fires.Add(FireEffects.SpawnFire(FireEffects.FirePool, center, null, teamIndex));
            for (int i = 0; i < FirePools; i++)
            {
                fires.Add(FireEffects.SpawnFire(FireEffects.FirePool, FireEffects.OnGround(center + FireEffects.RandomHorizontal(radius * 0.85f)), null, teamIndex));
            }
            for (int i = 0; i < OilPuddles; i++)
            {
                fires.Add(FireEffects.SpawnFire(FireEffects.BurningOil, FireEffects.OnGround(center + FireEffects.RandomHorizontal(radius * 0.7f)), null, teamIndex));
            }
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active)
            {
                return;
            }
            age += Time.fixedDeltaTime;
            if (age >= sunDuration)
            {
                PutOutSun();
            }
            fireballTimer -= Time.fixedDeltaTime;
            if (fireballTimer <= 0f)
            {
                fireballTimer = FireballInterval;
                FireEffects.SpawnEffect(FireEffects.Fireball, FireEffects.OnGround(center + FireEffects.RandomHorizontal(radius)), 4f);
            }
            if (age >= duration)
            {
                Destroy(gameObject);
            }
        }

        private void PutOutSun()
        {
            if (sun != null)
            {
                sun.ownerObject = null;
            }
            sun = null;
        }

        private void OnDestroy()
        {
            PutOutSun();
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
