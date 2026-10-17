using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventMitosis : AbstractEvent
    {
        // a monster and the weaker relative it splits into
        private static readonly (string strong, string weak)[] Pairs =
        {
            ("LemurianBruiserBody", "LemurianBody"),
            ("GreaterWispBody", "WispBody"),
            ("BeetleQueen2Body", "BeetleGuardBody"),
            ("BeetleGuardBody", "BeetleBody"),
            ("TitanBody", "GolemBody"),
            ("ClayBossBody", "ClayBruiserBody"),
            ("ImpBossBody", "ImpBody"),
            ("VagrantBody", "JellyfishBody"),
            ("MegaConstructBody", "MinorConstructBody"),
            ("RoboBallBossBody", "RoboBallMiniBody"),
            ("SuperRoboBallBossBody", "RoboBallMiniBody"),
            ("LunarGolemBody", "LunarExploderBody"),
            ("LunarWispBody", "LunarExploderBody"),
            ("VoidMegaCrabBody", "NullifierBody"),
        };
        private const int SplitCount = 2;
        // the strong monsters of the pool and the cards of their weaker relatives
        private readonly Dictionary<BodyIndex, SpawnCard> splits = new Dictionary<BodyIndex, SpawnCard>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // they would pick other monsters from the pool
            return FindSplits().Count > 0
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("StrongEnemies", StringComparison.InvariantCultureIgnoreCase)
                    || e.GetEventConfigName().Equals("WeakEnemies", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "Mitosis";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_MITOSIS");
        }
        public override string GetDescription()
        {
            return "Only monsters with a weaker relative on the stage spawn, e.g. Elder Lemurians when there are also Lemurians. When they die, they split into two of the weaker monster.";
        }
        public override string GetConditionDescription()
        {
            return "The stage's monsters include such a pair, and events \"StrongEnemies\" and \"WeakEnemies\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            GlobalEventManager.onCharacterDeathServer += GlobalEventManager_onCharacterDeathServer;
        }

        private static BodyIndex GetBodyIndex(DirectorCard card)
        {
            var master = card?.spawnCard?.prefab != null ? card.spawnCard.prefab.GetComponent<CharacterMaster>() : null;
            var body = master != null && master.bodyPrefab != null ? master.bodyPrefab.GetComponent<CharacterBody>() : null;
            return body != null ? body.bodyIndex : BodyIndex.None;
        }

        // the pairs of which the stage's monsters include both
        private static Dictionary<BodyIndex, SpawnCard> FindSplits()
        {
            var cards = new Dictionary<BodyIndex, DirectorCard>();
            foreach (var choice in MonsterPool.GetChoices())
            {
                var bodyIndex = GetBodyIndex(choice.value);
                if (choice.weight > 0f && bodyIndex != BodyIndex.None)
                {
                    cards[bodyIndex] = choice.value;
                }
            }
            var result = new Dictionary<BodyIndex, SpawnCard>();
            foreach (var (strong, weak) in Pairs)
            {
                var strongIndex = BodyCatalog.FindBodyIndex(strong);
                var weakIndex = BodyCatalog.FindBodyIndex(weak);
                if (cards.ContainsKey(strongIndex) && cards.TryGetValue(weakIndex, out var weakCard))
                {
                    result[strongIndex] = weakCard.spawnCard;
                }
            }
            return result;
        }

        private void GlobalEventManager_onCharacterDeathServer(DamageReport damageReport)
        {
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive())
            {
                return;
            }
            var body = damageReport.victimBody;
            var master = damageReport.victimMaster;
            if (body == null || master == null || body.teamComponent.teamIndex == TeamIndex.Player || !splits.TryGetValue(body.bodyIndex, out var weakCard))
            {
                return;
            }
            // the halves keep an elite's aspect
            var eliteDef = EliteCatalog.GetEliteDefFromEquipmentIndex(master.inventory.currentEquipmentIndex);
            var position = body.isFlying ? body.corePosition : body.footPosition;
            for (int i = 0; i < SplitCount; i++)
            {
                var offset = Quaternion.AngleAxis(360f / SplitCount * i, Vector3.up) * Vector3.forward * (body.radius * 0.5f);
                Helper.SpawnMonster(weakCard, position + offset, eliteDef);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            splits.Clear();
            foreach (var (strongIndex, weakCard) in FindSplits())
            {
                splits[strongIndex] = weakCard;
            }
            // only the monsters that split
            MonsterPool.Filter(this, choice => choice.value != null && splits.ContainsKey(GetBodyIndex(choice.value)));
        }

        public override void Stop()
        {
            MonsterPool.Restore(this);
            splits.Clear();
        }
    }
}
