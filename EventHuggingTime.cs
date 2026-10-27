using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventHuggingTime : AbstractEvent
    {
        // a force that changes the victim's speed by less counts as no knockback, e.g. that of most bullets
        private const float KnockbackSpeed = 3f;
        // how fast the victim flies to the attacker, within the shortest and longest time in the air
        private const float FlightSpeed = 25f;
        private const float MinFlightTime = 0.3f;
        private const float MaxFlightTime = 1f;
        // the gap the victim lands at, next to the attacker
        private const float Gap = 0.5f;
        // the tether shows at most this often for each victim, e.g. once for all of a shotgun's pellets, as each one
        // lingers for half a second
        private const float TetherInterval = 0.25f;
        // how long the tether takes to reach the victim
        private const float TetherTravelTime = 0.1f;
        private ConfigEntry<float> PullDistance;
        private AsyncOperationHandle<GameObject> TetherEffect;
        // when each victim's tether last showed
        private readonly Dictionary<HealthComponent, float> lastTethers = new Dictionary<HealthComponent, float>();

        // Bouncy's launches would end with every hit, and Red Light, Green Light would punish players for being pulled
        public override EventTags ExcludedTags => EventTags.PlayerMovement;
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
            return "HuggingTime";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_HUGGING_TIME");
        }
        public override string GetDescription()
        {
            return "Knockback sends whoever is hit flying to the attacker, and hits without knockback pull them 3 m closer, each with a tether between them. Damage over time is unaffected.";
        }
        public override string GetPopupDescription()
        {
            return "Knockback sends whoever is hit flying to the attacker, and every other hit pulls them closer.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            PullDistance = config.Bind(GetEventConfigName(), "Pull Distance", 3f, "How many meters a hit without knockback pulls whoever is hit closer to the attacker.");
        }
        public override void Preload()
        {
            // Polylute's void lightning, with its crackle. It reaches the victim, which it follows, and stays there while
            // it fades. Tracers, e.g. a Void Jailer's capture, end as soon as they arrive, which a pull's short distance
            // makes a single frame.
            TetherEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/ChainLightningVoid/VoidLightningOrbEffect.prefab");
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
        }

        // The game's force goes, so that the hit's own pull is the only one. It comes after the hit, only if the hit landed,
        // and resets the victim's speed, so that quick hits don't add up.
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive() || damageInfo == null || damageInfo.attacker == null || self.body == null
                || damageInfo.attacker == self.gameObject || !damageInfo.attacker.TryGetComponent(out CharacterBody attacker) || attacker.teamComponent == null
                || IsDamageOverTime(damageInfo) || EventLinkedSouls.Sharing || IsBlast(damageInfo) || !FriendlyFireManager.ShouldDirectHitProceed(self, attacker.teamComponent.teamIndex))
            {
                orig(self, damageInfo);
                return;
            }
            var force = damageInfo.force;
            damageInfo.force = Vector3.zero;
            orig(self, damageInfo);
            damageInfo.force = force;
            if (!damageInfo.rejected && self.alive && attacker != null)
            {
                ShowTether(self, attacker);
                Pull(self, attacker, KnockbackSpeedOf(self, force, damageInfo.physForceFlags) >= KnockbackSpeed);
            }
        }

        private static bool IsDamageOverTime(DamageInfo damageInfo)
        {
            return damageInfo.dotIndex != DotController.DotIndex.None || (damageInfo.damageType.damageType & DamageType.DoT) != 0;
        }

        // The mini nuke's and the Fuel Array's blasts blow everything away from them, though they name whoever set them off
        // as the attacker. The Fuel Array's comes from its attachment, during the countdown it ends.
        private static bool IsBlast(DamageInfo damageInfo)
        {
            var inflictor = damageInfo.inflictor;
            return inflictor != null && (inflictor.GetComponent<EventHotPotato.MiniNuke>() != null
                || (inflictor.TryGetComponent(out EntityStateMachine machine) && machine.state is EntityStates.QuestVolatileBattery.CountDown));
        }

        // how much the game's force would change the victim's speed
        private static float KnockbackSpeedOf(HealthComponent victim, Vector3 force, PhysForceFlags flags)
        {
            if ((flags & PhysForceFlags.massIsOne) != 0)
            {
                return force.magnitude;
            }
            float mass = victim.TryGetComponent(out IPhysMotor motor) ? motor.mass : victim.TryGetComponent(out Rigidbody rigidbody) ? rigidbody.mass : 0f;
            if ((flags & PhysForceFlags.simplifiedMass) != 0)
            {
                mass = PhysForceInfo.GetSimplifiedMassCoefficient(mass);
            }
            return mass > 0f ? force.magnitude / mass : 0f;
        }

        // Throws the victim in an arc that lands next to the attacker, or the pull distance along it
        private void Pull(HealthComponent victim, CharacterBody attacker, bool knockback)
        {
            var body = victim.body;
            Vector3 toVictim = body.footPosition - attacker.footPosition;
            Vector3 away = new Vector3(toVictim.x, 0f, toVictim.z);
            away = away.sqrMagnitude > 0.0001f ? away.normalized : -attacker.transform.forward;
            Vector3 target = attacker.footPosition + away * (attacker.radius + body.radius + Gap);
            Vector3 path = target - body.footPosition;
            if (Vector3.Dot(path, away) >= 0f)
            {
                // already closer than the gap
                return;
            }
            float distance = path.magnitude;
            if (!knockback && distance > PullDistance.Value)
            {
                path *= Mathf.Max(0f, PullDistance.Value) / distance;
                distance = path.magnitude;
            }
            if (distance < 0.01f)
            {
                return;
            }
            Throw.ToPoint(victim, body.footPosition + path, FlightSpeed, MinFlightTime, MaxFlightTime);
        }

        // a tether from the attacker to the victim, which fades within about half a second
        private void ShowTether(HealthComponent victim, CharacterBody attacker)
        {
            float now = Time.fixedTime;
            if (lastTethers.TryGetValue(victim, out var last) && now - last < TetherInterval)
            {
                return;
            }
            lastTethers[victim] = now;
            var effectData = new EffectData
            {
                origin = attacker.corePosition,
                genericFloat = TetherTravelTime
            };
            if (victim.body.mainHurtBox != null)
            {
                effectData.SetHurtBoxReference(victim.body.mainHurtBox);
            }
            else
            {
                effectData.SetNetworkedObjectReference(victim.gameObject);
            }
            EffectManager.SpawnEffect(TetherEffect.WaitForCompletion(), effectData, transmit: true);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            lastTethers.Clear();
        }

        public override void Stop()
        {
            lastTethers.Clear();
        }
    }
}
