using BepInEx.Configuration;
using EntityStates.InfiniteTowerSafeWard;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RandomEvents
{
    public class EventRandomArtifact : AbstractEvent
    {
        List<ArtifactDef> artifacts = new List<ArtifactDef>();
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return GetArtifactCandidates(activeOtherEvents).Count > 0;
        }
        // artifacts the event may turn on: not on yet and not blacklisted
        private static List<ArtifactDef> GetArtifactCandidates(List<AbstractEvent> otherEvents)
        {
            var enabledArtifacts = RunArtifactManager.enabledArtifactsEnumerable.ToList();
            bool atLeastOneCharacterHasAutoCastEquipment = false;
            foreach (PlayerCharacterMasterController pc in PlayerCharacterMasterController.instances)
            {
                if (pc.isConnected)
                {
                    if (pc.master?.inventory.GetItemCountEffective(RoR2Content.Items.AutoCastEquipment) > 0) {
                        atLeastOneCharacterHasAutoCastEquipment = true;
                        break;
                    }
                }
            }
            var artifactCandidates = new List<ArtifactDef>(ArtifactCatalog.artifactDefs);
            var equipmentOnlyActive = otherEvents.Any(e => e.GetEventConfigName().Equals("EquipmentOnly", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("ExperimentalEquipment", StringComparison.InvariantCultureIgnoreCase));
            if (atLeastOneCharacterHasAutoCastEquipment || equipmentOnlyActive)
            {
                artifactCandidates.Remove(RoR2Content.Artifacts.Enigma);
            }
            // Artifact of Kin and Artifact of Dissonance rebuild the stage's monster pool when turned on or off, which drops
            // the changes of the events that change which monsters spawn
            if (otherEvents.Any(MonsterPool.IsChangingEvent))
            {
                artifactCandidates.Remove(RoR2Content.Artifacts.SingleMonsterType);
                artifactCandidates.Remove(RoR2Content.Artifacts.MixEnemy);
            }
            artifactCandidates.Remove(RoR2Content.Artifacts.TeamDeath);
            artifactCandidates.Remove(RoR2Content.Artifacts.ShadowClone);
            artifactCandidates.Remove(RoR2Content.Artifacts.Sacrifice);
            artifactCandidates.Remove(RoR2Content.Artifacts.RandomSurvivorOnRespawn);
            artifactCandidates.Remove(RoR2Content.Artifacts.MonsterTeamGainsItems);
            artifactCandidates.Remove(DLC2Content.Artifacts.Rebirth);
            artifactCandidates.Remove(CU8Content.Artifacts.Devotion);
            artifactCandidates.Remove(CU8Content.Artifacts.Delusion);
            artifactCandidates.Remove(DLC3Content.Artifacts.Prestige);
            return artifactCandidates.Where(def => !enabledArtifacts.Any(enabled => enabled.artifactIndex == def.artifactIndex)).ToList();
        }
        public override string GetEventConfigName()
        {
            return "RandomArtifact";
        }
        public override string GetAnnouncement()
        {
            if (artifacts.Count == 1)
                return Language.GetStringFormatted("ANNOUNCE_EVENT_RANDOM_ARTIFACT", Language.GetString(artifacts[0].nameToken));
            if (artifacts.Count == 2)
                return Language.GetStringFormatted("ANNOUNCE_EVENT_RANDOM_DOUBLE_ARTIFACT", Language.GetString(artifacts[0].nameToken), Language.GetString(artifacts[1].nameToken));
            if (artifacts.Count == 3)
                return Language.GetStringFormatted("ANNOUNCE_EVENT_RANDOM_TRIPLE_ARTIFACT", Language.GetString(artifacts[0].nameToken), Language.GetString(artifacts[1].nameToken), Language.GetString(artifacts[2].nameToken));
            return null;
        }
        public override string GetDescription()
        {
            return "Turns on up to three random artifacts until the event ends.";
        }
        public override string GetPopupDescription()
        {
            return "Activates these artifacts temporarily.";
        }
        public override string GetConditionDescription()
        {
            return "There is at least one artifact which can be enabled and is not blacklisted.";
        }
        protected override void AddConfig(ConfigFile config)
        {

        }

        public override void Preload()
        {

        }

        public override void Hook()
        {
            On.RoR2.InfiniteTowerRun.AdvanceWave += InfiniteTowerRun_AdvanceWave;
        }

        private void InfiniteTowerRun_AdvanceWave(On.RoR2.InfiniteTowerRun.orig_AdvanceWave orig, RoR2.InfiniteTowerRun self)
        {
            orig(self);
        }

        public override void Prepare()
        {
            var notEnabledArtifacts = GetArtifactCandidates(RandomEvents.instance.m_upcomingEvents);

            var shuffled = notEnabledArtifacts.OrderBy(x => UnityEngine.Random.Range(0f, 1f)).ToList();
            artifacts = shuffled.Take(UnityEngine.Random.RandomRangeInt(1, 4)).ToList();
        }

        public bool TurnsOn(ArtifactDef artifactDef)
        {
            return artifacts.Contains(artifactDef);
        }

        // nor is such an event picked after this one turns on one of them
        public override bool IsCompatibleWith(AbstractEvent other)
        {
            return !MonsterPool.IsChangingEvent(other) || !(TurnsOn(RoR2Content.Artifacts.SingleMonsterType) || TurnsOn(RoR2Content.Artifacts.MixEnemy));
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            // leave out artifacts turned on since Prepare, so Stop doesn't turn them off
            artifacts.RemoveAll(artifact => RunArtifactManager.instance.IsArtifactEnabled(artifact));
            artifacts.ForEach(artifact => {
                RunArtifactManager.instance.SetArtifactEnabled(artifact, true);
            });
        }

        public override void Stop()
        {
            // the run's artifact manager is gone once the run has ended
            if (RunArtifactManager.instance != null)
            {
                artifacts.ForEach(artifact => {
                    RunArtifactManager.instance.SetArtifactEnabled(artifact, false);
                });
            }
            artifacts.Clear();
        }
    }
}
