using BepInEx.Configuration;
using RoR2;
using RoR2.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventLynxPups : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters | EventTags.MonsterPool | EventTags.MonsterKinds;
        public override EventTags ExcludedTags => EventTags.MonsterKinds;
        private ConfigEntry<float> PupShare;
        private AsyncOperationHandle<GameObject> MiniLynxMaster;
        private CharacterSpawnCard spawnCard;
        private readonly HashSet<CharacterMaster> grown = new HashSet<CharacterMaster>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the Lynx Pup comes with that expansion
            return !Helper.IsSimulacrumBossWaveNext() && DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4)
                && MonsterPool.GetChoices().Count > 0;
        }
        public override string GetEventConfigName()
        {
            return "LynxPups";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_LYNX_PUPS");
        }
        public override string GetDescription()
        {
            return "Half of the monsters that spawn are Lynx Pups, one and a half times as big, with one and a half times the health and attacking half as often.";
        }
        public override string GetPopupDescription()
        {
            return "Half the monsters are big, tough Lynx Pups that attack slowly.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled, and not a Simulacrum boss wave.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            PupShare = config.Bind(GetEventConfigName(), "Pup Share", 0.5f, "The share of the spawned monsters that are Lynx Pups, between 0 and 1.");
        }
        public override void Preload()
        {
            MiniLynxMaster = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC4/MiniLynx/MiniLynxMaster.prefab");
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private CharacterSpawnCard GetSpawnCard()
        {
            if (spawnCard == null)
            {
                spawnCard = ScriptableObject.CreateInstance<CharacterSpawnCard>();
                spawnCard.name = "cscGiantMiniLynx";
                spawnCard.prefab = MiniLynxMaster.WaitForCompletion();
                spawnCard.sendOverNetwork = true;
                // one and a half times as big, so it needs more room than a pup
                spawnCard.hullSize = HullClassification.Golem;
                spawnCard.nodeGraphType = MapNodeGroup.GraphType.Ground;
                spawnCard.forbiddenFlags = NodeFlags.NoCharacterSpawn;
                // twice a Lynx Pup's cost, for its one and a half times the health and size, about a Lesser Wisp's
                spawnCard.directorCreditCost = 10;
            }
            return spawnCard;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                Grow(body);
            }
        }

        // every Lynx Pup during the event, also those the stage would spawn anyway and those already there
        private void Grow(CharacterBody body)
        {
            if (body == null || body.master == null || body.inventory == null || grown.Contains(body.master)
                || body.teamComponent.teamIndex == TeamIndex.Player || body.bodyIndex != BodyCatalog.FindBodyIndex("MiniLynxBody"))
            {
                return;
            }
            grown.Add(body.master);
            // each Boost Hp adds 10% of the base health, each stack of the scale item 1% of the size
            body.inventory.GiveItemPermanent(RoR2Content.Items.BoostHp, 5);
            body.inventory.GiveItemPermanent(DLC4Content.Items.BoostCharacterScale, 50);
            // and attacking half as often
            Slowdown.Apply(body, 0.5f);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            grown.Clear();
            foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
            {
                Grow(body);
            }
            // the Artifact of Kin, e.g. on its Simulacrum wave, keeps to one kind of monster
            if (RunArtifactManager.instance != null && RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.SingleMonsterType))
            {
                return;
            }
            var choices = MonsterPool.GetChoices();
            var share = Mathf.Clamp(PupShare.Value, 0f, 0.95f);
            var otherWeight = choices.Sum(choice => choice.weight);
            if (otherWeight <= 0f || share <= 0f)
            {
                return;
            }
            // the stage's monsters and the pups, which make up the share of all spawns
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
            grown.Clear();
        }
    }
}
