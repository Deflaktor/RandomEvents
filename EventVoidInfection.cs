using BepInEx.Configuration;
using RoR2;
using RoR2.Navigation;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventVoidInfection : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters | EventTags.MonsterPool;
        private ConfigEntry<float> InfestorShare;
        private CharacterSpawnCard spawnCard;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the Void Infestor comes with that expansion
            return !Helper.IsSimulacrumBossWaveNext() && DLC1Content.DLC1 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC1Content.DLC1)
                && MonsterPool.GetChoices().Count > 0 && GetSpawnCard() != null;
        }
        public override string GetEventConfigName()
        {
            return "VoidInfection";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_VOID_INFECTION");
        }
        public override string GetDescription()
        {
            return "Void Infestors join the monsters that spawn, about a third of them. As usual, they leap at players and monsters alike, and the monsters they hit turn into void elites that fight everyone.";
        }
        public override string GetPopupDescription()
        {
            return "Void Infestors are everywhere, turning monsters into void elites!";
        }
        public override string GetConditionDescription()
        {
            return "Survivors of the Void is enabled, and not a Simulacrum boss wave.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            InfestorShare = config.Bind(GetEventConfigName(), "Infestor Share", 0.33f, "The share of the director's picks that are Void Infestors, between 0 and 1.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        // the game only spawns Void Infestors from void elites, so the event makes its own card for them
        private CharacterSpawnCard GetSpawnCard()
        {
            if (spawnCard == null)
            {
                var master = MasterCatalog.FindMasterPrefab("VoidInfestorMaster");
                if (master == null)
                {
                    return null;
                }
                spawnCard = ScriptableObject.CreateInstance<CharacterSpawnCard>();
                spawnCard.name = "cscVoidInfestor";
                spawnCard.prefab = master;
                spawnCard.sendOverNetwork = true;
                spawnCard.hullSize = HullClassification.Human;
                spawnCard.nodeGraphType = MapNodeGroup.GraphType.Ground;
                spawnCard.forbiddenFlags = NodeFlags.NoCharacterSpawn;
                // with 45 health, cheaper than a Lesser Wisp
                spawnCard.directorCreditCost = 5;
            }
            return spawnCard;
        }

        // The stage's director spawns them on the monsters' team; like those of void elites, they belong to the void's,
        // the same way their infest puts the monsters they hit on it.
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive() && body != null && body.master != null && body.teamComponent != null
                && body.teamComponent.teamIndex == TeamIndex.Monster && body.bodyIndex == BodyCatalog.FindBodyIndex("VoidInfestorBody"))
            {
                body.master.teamIndex = TeamIndex.Void;
                body.teamComponent.teamIndex = TeamIndex.Void;
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
            var share = Mathf.Clamp(InfestorShare.Value, 0f, 0.95f);
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
