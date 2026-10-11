using BepInEx.Configuration;
using HG;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using static RoR2.SpawnCard;

namespace RandomEvents
{
    public class EventRandomTeams : AbstractEvent
    {
        Dictionary<GameObject, TeamIndex> originalTeam = new Dictionary<GameObject, TeamIndex>();

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
            return "RandomTeams";
        }
        public override string GetAnnouncement()
        {
            return Language.GetString("ANNOUNCE_EVENT_RANDOM_TEAMS");
        }
        public override string GetDescription()
        {
            return "Makes some enemies fight each other as well as the players.";
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
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.teamComponent.teamIndex != TeamIndex.Player)
            {
                List<TeamIndex> candidates = new List<TeamIndex>();
                //candidates.Add(TeamIndex.Player);
                candidates.Add(TeamIndex.Void);
                // candidates.Add(TeamIndex.Neutral);
                candidates.Add(TeamIndex.Monster);
                // candidates.Add(TeamIndex.None);
                originalTeam.Add(body.gameObject, body.teamComponent.teamIndex);
                body.teamComponent.teamIndex = candidates.OrderBy(x => UnityEngine.Random.Range(0f, 1f)).ToList().First();
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            originalTeam.Clear();
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                infiniteTowerRun.waveController.totalWaveCredits *= 1.5f;
                infiniteTowerRun.waveController.creditsPerSecond *= 1.5f;
            }
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.teamComponent.teamIndex != TeamIndex.Player)
                {
                    List<TeamIndex> candidates = new List<TeamIndex>();
                    //candidates.Add(TeamIndex.Player);
                    candidates.Add(TeamIndex.Void);
                    //candidates.Add(TeamIndex.Neutral);
                    candidates.Add(TeamIndex.Monster);
                    //candidates.Add(TeamIndex.None);
                    originalTeam.Add(body.gameObject, body.teamComponent.teamIndex);
                    body.teamComponent.teamIndex = candidates.OrderBy(x => UnityEngine.Random.Range(0f, 1f)).ToList().First();
                }
            }
        }


        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && originalTeam.ContainsKey(body.gameObject))
                {
                    body.teamComponent.teamIndex = originalTeam[body.gameObject];
                }
            }
        }
    }
}
