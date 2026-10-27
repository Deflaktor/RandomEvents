using BepInEx.Configuration;
using EntityStates;
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
    public class EventBlightedCorpses : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters | EventTags.MonsterPool;
        // it needs the stage's own monsters
        public override EventTags ExcludedTags => EventTags.MonsterPool;
        private const float TickInterval = 1f;
        private const float BlightDuration = 5f;
        private ConfigEntry<float> Radius;
        private ConfigEntry<int> MaxStacksPerSecond;
        private ConfigEntry<int> MaxCorpses;
        private ConfigEntry<float> DamageMultiplier;
        private AsyncOperationHandle<GameObject> AcidPool;

        // Where a monster died, marked by a pool of acid that blights players. Its body stays as the corpse where its
        // death allows, which some monsters' doesn't, e.g. an Imp's.
        private class Corpse
        {
            public CharacterBody body;
            public Vector3 position;
            public float radius;
            // the monster's damage when it died, which sets how much its blight hurts
            public float damage;
            public GameObject pool;
        }
        // oldest first
        private readonly List<Corpse> corpses = new List<Corpse>();
        // corpses let go of again, which their death state then removes as usual
        private readonly HashSet<CharacterBody> released = new HashSet<CharacterBody>();
        private float tickTimer;

        public override bool LoadCondition()
        {
            return true;
        }
        // only monsters that leave a corpse spawn, so most of the stage's should
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return MonsterTraits.WeightShare(MonsterPool.GetChoices(), MonsterTraits.LeavesCorpse) > 0.5f;
        }
        public override string GetEventConfigName()
        {
            return "BlightedCorpses";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_BLIGHTED_CORPSES");
        }
        public override string GetDescription()
        {
            return "Only monsters that leave a corpse spawn. Their corpses lie in a pool of acid until the event ends, and being near them blights players.";
        }
        public override string GetPopupDescription()
        {
            return "Only monsters that leave a corpse spawn. Their corpses lie in pools of acid, and being near them blights you.";
        }
        public override string GetConditionDescription()
        {
            return "Most of the stage's monsters leave a corpse.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            Radius = config.Bind(GetEventConfigName(), "Radius", 6f, "How near in meters players must be to a corpse's pool to be blighted.");
            MaxStacksPerSecond = config.Bind(GetEventConfigName(), "Max Stacks Per Second", 3, "Players get a Blight stack per second for each corpse near them, up to this many.");
            DamageMultiplier = config.Bind(GetEventConfigName(), "Damage Multiplier", 0.2f, "The damage of each Blight stack, relative to normal.");
            MaxCorpses = config.Bind(GetEventConfigName(), "Max Corpses", 40, "How many corpses stay at most; the oldest go first.");
        }
        public override void Preload()
        {
            AcidPool = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/BeetleQueen/BeetleQueenAcid.prefab");
        }
        public override void Hook()
        {
            GlobalEventManager.onCharacterDeathGlobal += GlobalEventManager_onCharacterDeathGlobal;
            On.EntityStates.GenericCharacterDeath.DestroyBodyAsapServer += GenericCharacterDeath_DestroyBodyAsapServer;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // on the ground below where the monster died; none for one that fell out of the map
        private void GlobalEventManager_onCharacterDeathGlobal(DamageReport report)
        {
            var body = report.victimBody;
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive() || body == null || body.teamComponent.teamIndex == TeamIndex.Player
                || body.teamComponent.teamIndex == TeamIndex.Neutral || corpses.Any(corpse => corpse.body == body)
                || !Physics.Raycast(body.corePosition, Vector3.down, out var hit, 20f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                return;
            }
            corpses.Add(new Corpse
            {
                body = body,
                position = hit.point,
                radius = body.radius,
                damage = body.damage,
                pool = SpawnPool(hit, body.teamComponent.teamIndex)
            });
            while (corpses.Count > Mathf.Max(0, MaxCorpses.Value))
            {
                Remove(corpses[0]);
                corpses.RemoveAt(0);
            }
        }

        // Beetle Queen's acid, for show: its damage zone only runs on the host, so with it turned off the pool hurts no
        // one and stays until it's destroyed
        private GameObject SpawnPool(RaycastHit hit, TeamIndex teamIndex)
        {
            var rotation = Util.QuaternionSafeLookRotation(Vector3.ProjectOnPlane(Vector3.forward, hit.normal), hit.normal);
            var pool = Object.Instantiate(AcidPool.WaitForCompletion(), hit.point, rotation);
            if (pool.TryGetComponent(out ProjectileDotZone dotZone))
            {
                dotZone.enabled = false;
            }
            if (pool.TryGetComponent(out ProjectileDamage projectileDamage))
            {
                projectileDamage.damage = 0f;
            }
            if (pool.TryGetComponent(out TeamFilter teamFilter))
            {
                teamFilter.teamIndex = teamIndex;
            }
            NetworkServer.Spawn(pool);
            return pool;
        }

        private void Remove(Corpse corpse)
        {
            if (corpse.body != null)
            {
                released.Add(corpse.body);
            }
            if (corpse.pool != null)
            {
                Object.Destroy(corpse.pool);
            }
        }

        // The game removes a dead body a second after it comes to rest, and only then each player's game keeps its own
        // corpse, up to their own limit. Kept, the body stays a corpse for everyone; it goes with its pool.
        private void GenericCharacterDeath_DestroyBodyAsapServer(On.EntityStates.GenericCharacterDeath.orig_DestroyBodyAsapServer orig, GenericCharacterDeath self)
        {
            var body = self.characterBody;
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive() || body == null || self.isPlayerDeath || self.isVoidDeath
                || self.destroyModelOnDeath || self.cachedModelTransform == null || self.fallingStopwatch >= GenericCharacterDeath.maxFallDuration
                || released.Contains(body) || !corpses.Any(corpse => corpse.body == body))
            {
                orig(self);
            }
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            tickTimer -= Time.fixedDeltaTime;
            if (tickTimer > 0f)
            {
                return;
            }
            tickTimer = TickInterval;
            released.RemoveWhere(body => body == null);
            var radius = Mathf.Max(0f, Radius.Value);
            foreach (var player in Helper.GetLivingPlayerBodies())
            {
                var near = corpses.Where(corpse => (corpse.position - player.footPosition).sqrMagnitude <= (radius + corpse.radius) * (radius + corpse.radius))
                    .Take(Mathf.Max(0, MaxStacksPerSecond.Value)).ToList();
                foreach (var corpse in near)
                {
                    Blight(player, corpse);
                }
            }
        }

        // Blight draws its damage from its attacker: the corpse if it's still there, otherwise the player, scaled to the
        // dead monster's damage, which grows with the level
        private void Blight(CharacterBody player, Corpse corpse)
        {
            var multiplier = Mathf.Max(0f, DamageMultiplier.Value);
            var attacker = corpse.body != null ? corpse.body.gameObject : player.gameObject;
            if (corpse.body == null)
            {
                multiplier *= corpse.damage / Mathf.Max(1f, player.damage);
            }
            DotController.InflictDot(player.gameObject, attacker, DotController.DotIndex.Blight, BlightDuration, multiplier, null);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            corpses.Clear();
            released.Clear();
            tickTimer = TickInterval;
            MonsterPool.Filter(this, choice => MonsterTraits.LeavesCorpse(choice.value));
        }

        // the bodies kept go now, as the death states remove them again; then each player's game keeps their corpses as usual
        public override void Stop()
        {
            MonsterPool.Restore(this);
            foreach (var corpse in corpses)
            {
                if (corpse.pool != null)
                {
                    Object.Destroy(corpse.pool);
                }
            }
            corpses.Clear();
            released.Clear();
        }
    }
}
