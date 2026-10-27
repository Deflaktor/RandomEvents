using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventVoidCore : AbstractEvent
    {
        private const float TickPeriod = 0.5f;
        private ConfigEntry<float> GrowDuration;
        private ConfigEntry<float> MaxRadiusShare;
        private ConfigEntry<float> HealthFractionPerSecond;
        private ConfigEntry<float> RampPerSecond;
        private AsyncOperationHandle<GameObject> zonePrefab;
        private GameObject zone;
        private SphereZone sphere;
        private float age;
        private float tickTimer;
        // how long each body has been in the fog
        private readonly Dictionary<CharacterBody, float> exposure = new Dictionary<CharacterBody, float>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the sphere that shows the fog comes with that expansion
            return DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4) && Helper.TryGetArena(out _, out _);
        }
        public override string GetEventConfigName()
        {
            return "VoidCore";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_VOID_CORE");
        }
        public override string GetDescription()
        {
            return "Void fog spreads from the middle of the Simulacrum's safe zone or the charging teleporter's zone until it covers half of its radius, so only the ring around it is safe.";
        }
        public override string GetPopupDescription()
        {
            return "Void fog is spreading from the center. Stay in the outer ring!";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled, and in the Simulacrum or while the teleporter charges.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            GrowDuration = config.Bind(GetEventConfigName(), "Grow Duration", 30f, "Seconds the fog takes to reach its full size.");
            MaxRadiusShare = config.Bind(GetEventConfigName(), "Max Radius Share", 0.5f, "The fog's full radius, as a share of the safe zone's or the teleporter zone's radius.");
            HealthFractionPerSecond = config.Bind(GetEventConfigName(), "Health Fraction Per Second", 0.05f, "The share of their full health that players and their allies lose per second in the fog, at first.");
            RampPerSecond = config.Bind(GetEventConfigName(), "Ramp Per Second", 0.1f, "How much of that the damage grows by for each second spent in the fog.");
        }
        public override void Preload()
        {
            zonePrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC4/DreadFogSafeZone.prefab");
        }
        public override void Hook()
        {
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            if (!Helper.TryGetArena(out var center, out var arenaRadius))
            {
                RemoveZone();
                return;
            }
            age += Time.fixedDeltaTime;
            var radius = Mathf.Clamp01(age / Mathf.Max(0.1f, GrowDuration.Value)) * Mathf.Max(0f, MaxRadiusShare.Value) * arenaRadius;
            UpdateZone(center, radius);
            tickTimer -= Time.fixedDeltaTime;
            if (tickTimer <= 0f)
            {
                tickTimer = TickPeriod;
                DamageInFog(center, radius);
            }
        }

        // The sphere shows the fog to everyone: clients follow its radius but not its position, so it is spawned anew
        // when the safe zone moves.
        private void UpdateZone(Vector3 center, float radius)
        {
            if (zone != null && Vector3.Distance(zone.transform.position, center) > 1f)
            {
                RemoveZone();
            }
            if (zone == null)
            {
                var prefab = zonePrefab.WaitForCompletion();
                if (prefab == null)
                {
                    return;
                }
                zone = Object.Instantiate(prefab, center, Quaternion.identity);
                sphere = zone.GetComponent<SphereZone>();
                if (sphere != null)
                {
                    sphere.Networkradius = radius;
                }
                NetworkServer.Spawn(zone);
            }
            if (sphere != null && Mathf.Abs(sphere.radius - radius) > 0.2f)
            {
                sphere.Networkradius = radius;
            }
        }

        private void RemoveZone()
        {
            if (zone != null)
            {
                Object.Destroy(zone);
            }
            zone = null;
            sphere = null;
        }

        // like the game's void fog: a share of the full health, growing the longer one stays in it, and half for drones
        private void DamageInFog(Vector3 center, float radius)
        {
            var inFog = new HashSet<CharacterBody>();
            foreach (var member in TeamComponent.GetTeamMembers(TeamIndex.Player))
            {
                var body = member != null ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive
                    || (body.corePosition - center).sqrMagnitude > radius * radius)
                {
                    continue;
                }
                inFog.Add(body);
                exposure[body] = exposure.TryGetValue(body, out var seconds) ? seconds + TickPeriod : 0f;
                var damage = Mathf.Max(0f, HealthFractionPerSecond.Value) * (1f + exposure[body] * Mathf.Max(0f, RampPerSecond.Value)) * TickPeriod
                    * body.healthComponent.fullCombinedHealth;
                if (body.IsDrone)
                {
                    damage *= 0.5f;
                }
                body.healthComponent.TakeDamage(new DamageInfo
                {
                    damage = damage,
                    position = body.corePosition,
                    damageType = Helper.HazardDamageType,
                    damageColorIndex = DamageColorIndex.Void
                });
                body.AddTimedBuff(RoR2Content.Buffs.VoidFogMild, TickPeriod + 0.1f);
            }
            // leaving the fog starts the count anew
            foreach (var body in new List<CharacterBody>(exposure.Keys))
            {
                if (!inFog.Contains(body))
                {
                    exposure.Remove(body);
                }
            }
        }

        public override void Prepare()
        {
        }
        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            age = 0f;
            tickTimer = TickPeriod;
            exposure.Clear();
        }
        public override void Stop()
        {
            RemoveZone();
            exposure.Clear();
        }
    }
}
