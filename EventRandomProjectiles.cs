using BepInEx.Configuration;
using RoR2;
using RoR2.Projectile;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // The host spawns the monsters' projectiles, so it can spawn another kind instead. Clients see the spawned one as
    // usual.
    public class EventRandomProjectiles : AbstractEvent
    {
        // projectiles that need a certain owner, carry a passenger, spawn characters or deployables, or move items
        private static readonly HashSet<string> UnsuitableComponents = new HashSet<string>
        {
            "BigMushProjectileSpawnCardOnImpact", "CleaverProjectile", "DeathProjectile", "GatewayProjectileController",
            "GhostBladeProjectileController", "GhostBladeThrownProjectileController", "HookProjectileImpact", "JunkCubeController",
            "PalmBlastProjectileController", "ProjectileDeployToOwner", "ProjectileGrappleController", "ProjectileHurtOwnerOnDeath",
            "ProjectileHurtOwnerOnDeathWithOrb", "ProjectileInstantiateDeployable", "ProjectileInteractOnImpact", "ProjectileOwnerRemoteDetonate",
            "ProjectileReduceCooldownOnDestruction", "ProjectileSetHealthToOwnerHealth", "ProjectileSpawnCardOnImpact",
            "ProjectileSpawnDeployableImpact", "ProjectileSpawnMaster", "ProjectileTransferItem", "ProjectileTransferOwnershipOnBodyChanged",
            "ProvidenceMineController", "RolyPolyBoostedProjectileTimer", "SolusWingGravPulseProjectile", "SoulSearchController",
            "ThrownObjectProjectileController", "VehicleSeat", "YesChefProjectileTimer"
        };
        private List<GameObject> pool;
        private readonly Dictionary<GameObject, GameObject> swaps = new Dictionary<GameObject, GameObject>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return true;
        }
        public override string GetEventConfigName()
        {
            return "RandomProjectiles";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_RANDOM_PROJECTILES");
        }
        public override string GetDescription()
        {
            return "Each kind of projectile is swapped for another random one, that the monsters fire. The damage stays that of the attack.";
        }
        public override string GetPopupDescription()
        {
            return "Every monster projectile turns into a different one!";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.Projectile.ProjectileManager.FireProjectileServer += ProjectileManager_FireProjectileServer;
        }

        private void ProjectileManager_FireProjectileServer(On.RoR2.Projectile.ProjectileManager.orig_FireProjectileServer orig, ProjectileManager self,
            FireProjectileInfo fireProjectileInfo, NetworkConnection clientAuthorityOwner, ushort predictionId, double fastForwardTime)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && fireProjectileInfo.projectilePrefab != null
                && IsMonster(fireProjectileInfo.owner) && swaps.TryGetValue(fireProjectileInfo.projectilePrefab, out var swapped))
            {
                fireProjectileInfo.projectilePrefab = swapped;
            }
            orig(self, fireProjectileInfo, clientAuthorityOwner, predictionId, fastForwardTime);
        }

        // not the players and their allies, nor hazards
        private static bool IsMonster(GameObject owner)
        {
            var team = owner != null ? TeamComponent.GetObjectTeam(owner) : TeamIndex.None;
            return team != TeamIndex.None && team != TeamIndex.Neutral && team != TeamIndex.Player;
        }

        // The projectiles that others spawn are left out, so that one can't keep spawning itself through the swaps. So
        // are those that kill outright, like the Void Reaver's death bomb.
        private static List<GameObject> GetPool()
        {
            var prefabs = Enumerable.Range(0, ProjectileCatalog.projectilePrefabCount).Select(ProjectileCatalog.GetProjectilePrefab)
                .Where(prefab => prefab != null).ToList();
            var children = new HashSet<GameObject>(prefabs.SelectMany(prefab => ReferencedObjects(prefab)
                .Where(child => child != prefab && ProjectileCatalog.GetProjectileIndex(child) >= 0)));
            return prefabs.Where(prefab => !children.Contains(prefab)
                && prefab.TryGetComponent(out ProjectileDamage damage) && (damage.damageType.damageType & DamageType.VoidDeath) == 0
                && !prefab.GetComponentsInChildren<Component>(true).Any(component => component != null && UnsuitableComponents.Contains(component.GetType().Name)))
                .ToList();
        }

        // the game objects the prefab's components refer to, also in lists of them
        private static IEnumerable<GameObject> ReferencedObjects(GameObject prefab)
        {
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                for (var type = component != null ? component.GetType() : null; type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                {
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        foreach (var gameObject in GameObjectsIn(field.GetValue(component), 1))
                        {
                            yield return gameObject;
                        }
                    }
                }
            }
        }

        private static IEnumerable<GameObject> GameObjectsIn(object value, int depth)
        {
            if (value is GameObject gameObject)
            {
                if (gameObject != null)
                {
                    yield return gameObject;
                }
            }
            else if (value is IEnumerable list && !(value is string) && !(value is UnityEngine.Object))
            {
                foreach (var element in list)
                {
                    foreach (var found in GameObjectsIn(element, depth))
                    {
                        yield return found;
                    }
                }
            }
            else if (depth > 0 && value != null && !(value is UnityEngine.Object) && !value.GetType().IsPrimitive && value.GetType().IsSerializable)
            {
                foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (var found in GameObjectsIn(field.GetValue(value), depth - 1))
                    {
                        yield return found;
                    }
                }
            }
        }

        public override void Prepare()
        {
        }
        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            pool ??= GetPool();
            // in one shuffled loop, each swaps for the next, so none stays itself
            var shuffled = pool.OrderBy(_ => RoR2Application.rng.nextNormalizedFloat).ToList();
            swaps.Clear();
            for (int i = 0; i < shuffled.Count && shuffled.Count > 1; i++)
            {
                swaps[shuffled[i]] = shuffled[(i + 1) % shuffled.Count];
            }
        }
        public override void Stop()
        {
            swaps.Clear();
        }
    }
}
