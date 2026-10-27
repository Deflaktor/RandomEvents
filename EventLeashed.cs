using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // The players are linked along the shortest tree that connects them all, which the tethers show. Each tether is a
    // leash: once one is longer than the leash, the side of the tree with fewer players on it strayed from the group and
    // is thrown back to the player at the tether's other end. With as many on both sides, both are thrown toward the
    // tether's middle.
    public class EventLeashed : AbstractEvent
    {
        private const float LinkUpdateInterval = 0.25f;
        // how fast the strays fly back, within the shortest and longest time in the air
        private const float FlightSpeed = 30f;
        private const float MinFlightTime = 0.4f;
        private const float MaxFlightTime = 1.2f;
        // a thrown player isn't thrown again this soon, so the throw isn't cut short mid-air
        private const float ThrowCooldown = 1.5f;
        private ConfigEntry<float> LeashLength;
        private ConfigEntry<float> LandingDistance;
        private ConfigEntry<float> DamageTakenMultiplier;
        private readonly PlayerTethers tethers = new PlayerTethers();
        private readonly Dictionary<CharacterBody, float> lastThrows = new Dictionary<CharacterBody, float>();
        private float linkTimer;

        public override EventTags Tags => EventTags.PlayerMovement | EventTags.PlayerDamage;
        // Bouncy's launches would snap the leash all the time, and Red Light, Green Light would punish being thrown back
        public override EventTags ExcludedTags => EventTags.PlayerMovement;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return Helper.GetLivingPlayerBodies().Count >= 2;
        }
        public override string GetEventConfigName()
        {
            return "Leashed";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_LEASHED");
        }
        public override string GetDescription()
        {
            return "Tethers link the players as leashes. A player who gets more than 20 m away from the others is thrown back to them, but the players take 50% less damage.";
        }
        public override string GetPopupDescription()
        {
            return "You're leashed together: stray too far from the others and you get thrown back. You take less damage.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            LeashLength = config.Bind(GetEventConfigName(), "Leash Length", 20f, "How far apart two players linked by a tether can get before the leash throws one of them back, in meters.");
            LandingDistance = config.Bind(GetEventConfigName(), "Landing Distance", 5f, "How far from the player at the other end of the tether a thrown player lands, in meters.");
            DamageTakenMultiplier = config.Bind(GetEventConfigName(), "Damage Taken Multiplier", 0.5f, "The damage players take during the event, relative to normal.");
        }
        public override void Preload()
        {
            PlayerTethers.Preload();
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // the players themselves, not their allies
        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && damageInfo != null && self.body != null && self.body.isPlayerControlled)
            {
                damageInfo.damage *= Mathf.Max(0f, DamageTakenMultiplier.Value);
            }
            orig(self, damageInfo);
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            linkTimer -= Time.fixedDeltaTime;
            if (linkTimer > 0f)
            {
                return;
            }
            linkTimer = LinkUpdateInterval;
            tethers.Update(Helper.GetLivingPlayerBodies());
            var links = tethers.Links.ToList();
            foreach (var (parent, child) in links)
            {
                if (Vector3.Distance(parent.footPosition, child.footPosition) > Mathf.Max(1f, LeashLength.Value))
                {
                    PullBack(links, parent, child);
                }
            }
        }

        private void PullBack(List<(CharacterBody parent, CharacterBody child)> links, CharacterBody parent, CharacterBody child)
        {
            var parentSide = Side(links, parent, child);
            var childSide = Side(links, child, parent);
            var landing = Mathf.Max(0f, LandingDistance.Value);
            if (parentSide.Count == childSide.Count)
            {
                var middle = (parent.footPosition + child.footPosition) * 0.5f;
                ThrowAll(parentSide, middle, landing * 0.5f);
                ThrowAll(childSide, middle, landing * 0.5f);
            }
            else if (childSide.Count < parentSide.Count)
            {
                ThrowAll(childSide, parent.footPosition, landing);
            }
            else
            {
                ThrowAll(parentSide, child.footPosition, landing);
            }
        }

        // the players on the start's side of the tether to the other player
        private static List<CharacterBody> Side(List<(CharacterBody parent, CharacterBody child)> links, CharacterBody start, CharacterBody other)
        {
            var side = new List<CharacterBody> { start };
            for (int i = 0; i < side.Count; i++)
            {
                foreach (var (parent, child) in links)
                {
                    var next = parent == side[i] ? child : child == side[i] ? parent : null;
                    if (next != null && !(side[i] == start && next == other) && !side.Contains(next))
                    {
                        side.Add(next);
                    }
                }
            }
            return side;
        }

        // each lands this far from the anchor, on the side they come from
        private void ThrowAll(List<CharacterBody> players, Vector3 anchor, float landing)
        {
            float now = Time.fixedTime;
            foreach (var body in players)
            {
                if (body == null || body.healthComponent == null || (lastThrows.TryGetValue(body, out var last) && now - last < ThrowCooldown))
                {
                    continue;
                }
                Vector3 fromAnchor = body.footPosition - anchor;
                Vector3 away = new Vector3(fromAnchor.x, 0f, fromAnchor.z);
                away = away.sqrMagnitude > 0.0001f ? away.normalized : body.transform.forward;
                lastThrows[body] = now;
                Throw.ToPoint(body.healthComponent, anchor + away * landing, FlightSpeed, MinFlightTime, MaxFlightTime);
            }
        }

        public override void Prepare()
        {
        }
        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            tethers.Clear();
            lastThrows.Clear();
            linkTimer = 0f;
        }
        public override void Stop()
        {
            tethers.Clear();
            lastThrows.Clear();
        }
    }
}
