using BepInEx.Configuration;
using RoR2;
using RoR2.Navigation;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventChestMimics : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters | EventTags.MonsterPool;
        private ConfigEntry<float> MimicShare;
        private CharacterSpawnCard spawnCard;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the Mimic comes with that expansion
            return !Helper.IsSimulacrumBossWaveNext() && DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4)
                && MonsterPool.GetChoices().Count > 0 && GetSpawnCard() != null;
        }
        public override string GetEventConfigName()
        {
            return "ChestMimics";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_CHEST_MIMICS");
        }
        public override string GetDescription()
        {
            return "Mimics join the monsters that spawn, about a quarter of them, and hunt the players right away instead of waiting as a chest.";
        }
        public override string GetPopupDescription()
        {
            return "Mimics are on the hunt! Don't let them swallow you.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled, and not a Simulacrum boss wave.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            MimicShare = config.Bind(GetEventConfigName(), "Mimic Share", 0.25f, "The share of the spawned monsters that are Mimics, between 0 and 1.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.EntityStates.Mimic.Chest.Idle.OnEnter += Idle_OnEnter;
        }

        // the game only summons Mimics, so the event makes its own card for them
        private CharacterSpawnCard GetSpawnCard()
        {
            if (spawnCard == null)
            {
                var master = MasterCatalog.FindMasterPrefab("MimicMaster");
                if (master == null)
                {
                    return null;
                }
                spawnCard = ScriptableObject.CreateInstance<CharacterSpawnCard>();
                spawnCard.name = "cscMimic";
                spawnCard.prefab = master;
                spawnCard.sendOverNetwork = true;
                spawnCard.hullSize = HullClassification.Golem;
                spawnCard.nodeGraphType = MapNodeGroup.GraphType.Ground;
                spawnCard.forbiddenFlags = NodeFlags.NoCharacterSpawn;
                // about twice a Stone Golem's health
                spawnCard.directorCreditCost = 60;
            }
            return spawnCard;
        }

        // A monster Mimic turns into a chest once it spawned; during the event it opens up right away, as when a
        // player touches it, and goes after the players.
        private void Idle_OnEnter(On.EntityStates.Mimic.Chest.Idle.orig_OnEnter orig, EntityStates.Mimic.Chest.Idle self)
        {
            orig(self);
            if (NetworkServer.active && IsActive() && self.characterBody != null && self.characterBody.teamComponent.teamIndex != TeamIndex.Player
                && self.characterBody.TryGetComponent(out MimicController mimic))
            {
                mimic.ForceUndisguiseStateServer();
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            // the Artifact of Kin, e.g. on its Simulacrum wave, keeps to one kind of monster
            if (RunArtifactManager.instance != null && RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.SingleMonsterType))
            {
                return;
            }
            var choices = MonsterPool.GetChoices();
            var share = Mathf.Clamp(MimicShare.Value, 0f, 0.95f);
            var otherWeight = choices.Sum(choice => choice.weight);
            if (otherWeight <= 0f || share <= 0f || GetSpawnCard() == null)
            {
                return;
            }
            var selection = new WeightedSelection<DirectorCard>();
            foreach (var choice in choices)
            {
                selection.AddChoice(choice);
            }
            selection.AddChoice(new DirectorCard
            {
                spawnCard = GetSpawnCard(),
                selectionWeight = 1,
                spawnDistance = DirectorCore.MonsterSpawnDistance.Standard
            }, otherWeight * share / (1f - share));
            MonsterPool.Replace(this, selection);
        }

        public override void Stop()
        {
            MonsterPool.Restore(this);
        }
    }
}
