using BepInEx.Configuration;
using EntityStates;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventBlightedCorpses : AbstractEvent
    {
        private const float TickInterval = 1f;
        private const float BlightDuration = 5f;
        private ConfigEntry<float> Radius;
        private ConfigEntry<int> MaxStacksPerSecond;
        private ConfigEntry<int> MaxCorpses;
        // the dead monsters kept as corpses, oldest first, with where they came to rest
        private readonly List<(CharacterBody body, Vector3 position)> corpses = new List<(CharacterBody body, Vector3 position)>();
        // corpses let go of again, which their death state then removes as usual
        private readonly HashSet<CharacterBody> released = new HashSet<CharacterBody>();
        private float tickTimer;

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
            return "BlightedCorpses";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_BLIGHTED_CORPSES");
        }
        public override string GetDescription()
        {
            return "Corpses of monsters stay until the event ends, and being near them blights players.";
        }
        public override string GetPopupDescription()
        {
            return "Corpses stay. Being near them blights you.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            Radius = config.Bind(GetEventConfigName(), "Radius", 6f, "How near in meters players must be to a corpse to be blighted.");
            MaxStacksPerSecond = config.Bind(GetEventConfigName(), "Max Stacks Per Second", 3, "Players get a Blight stack per second for each corpse near them, up to this many.");
            MaxCorpses = config.Bind(GetEventConfigName(), "Max Corpses", 40, "How many corpses stay at most; the oldest go first.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.EntityStates.GenericCharacterDeath.DestroyBodyAsapServer += GenericCharacterDeath_DestroyBodyAsapServer;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // The game removes a dead body a second after it comes to rest, and only then each player's game keeps its own
        // corpse, up to their own limit. Kept, the body stays a corpse for everyone; it goes once the event ends.
        private void GenericCharacterDeath_DestroyBodyAsapServer(On.EntityStates.GenericCharacterDeath.orig_DestroyBodyAsapServer orig, GenericCharacterDeath self)
        {
            var body = self.characterBody;
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive() || body == null || self.isPlayerDeath || self.isVoidDeath
                || self.destroyModelOnDeath || self.cachedModelTransform == null || self.fallingStopwatch >= GenericCharacterDeath.maxFallDuration
                || body.teamComponent.teamIndex == TeamIndex.Player || released.Contains(body))
            {
                orig(self);
                return;
            }
            if (!corpses.Any(corpse => corpse.body == body))
            {
                corpses.Add((body, body.corePosition));
                while (corpses.Count > Mathf.Max(0, MaxCorpses.Value))
                {
                    released.Add(corpses[0].body);
                    corpses.RemoveAt(0);
                }
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
            corpses.RemoveAll(corpse => corpse.body == null);
            released.RemoveWhere(body => body == null);
            var radius = Mathf.Max(0f, Radius.Value);
            foreach (var player in Helper.GetLivingPlayerBodies())
            {
                var near = corpses.Where(corpse => (corpse.position - player.corePosition).sqrMagnitude <= (radius + corpse.body.radius) * (radius + corpse.body.radius))
                    .Take(Mathf.Max(0, MaxStacksPerSecond.Value)).ToList();
                // the dead monster's damage, which grows with the level, sets how much it hurts
                foreach (var corpse in near)
                {
                    DotController.InflictDot(player.gameObject, corpse.body.gameObject, DotController.DotIndex.Blight, BlightDuration, 1f, null);
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            corpses.Clear();
            released.Clear();
            tickTimer = TickInterval;
        }

        // the bodies kept go now, as the death states remove them again; then each player's game keeps their corpses as usual
        public override void Stop()
        {
            corpses.Clear();
            released.Clear();
        }
    }
}
