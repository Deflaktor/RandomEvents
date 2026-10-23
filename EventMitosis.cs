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
        public override EventTags Tags => EventTags.Monsters;
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
        private ConfigEntry<int> MaxSplits;
        private const float SplitImmunity = 3f;
        // the strong monsters of the pool and the cards of their weaker relatives
        private readonly Dictionary<BodyIndex, SpawnCard> splits = new Dictionary<BodyIndex, SpawnCard>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // they would pick other monsters from the pool
            return !Helper.IsSimulacrumBossWaveNext() && FindSplits().Count > 0
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("StrongEnemies", StringComparison.InvariantCultureIgnoreCase)
                    || e.GetEventConfigName().Equals("WeakEnemies", StringComparison.InvariantCultureIgnoreCase)
                    || e.GetEventConfigName().Equals("DroneUprising", StringComparison.InvariantCultureIgnoreCase)
                    || e.GetEventConfigName().Equals("LynxPups", StringComparison.InvariantCultureIgnoreCase)
                    || e.GetEventConfigName().Equals("BlightedCorpses", StringComparison.InvariantCultureIgnoreCase)
                    || e.GetEventConfigName().Equals("BulletHell", StringComparison.InvariantCultureIgnoreCase));
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
            return "Only monsters with a weaker relative spawn, and they split into several of them when they die.";
        }
        public override string GetConditionDescription()
        {
            return "Not a Simulacrum boss wave, the stage's monsters include such a pair, and events \"StrongEnemies\", \"WeakEnemies\", \"DroneUprising\", \"LynxPups\", \"BlightedCorpses\" and \"BulletHell\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            MaxSplits = config.Bind(GetEventConfigName(), "Max Splits", 10, "The most weaker monsters a monster can split into.");
        }

        // As many weaker monsters as the strong one's health is worth, from their base health at the same level,
        // without items or elite boosts, which the halves get as well
        private int GetSplitCount(CharacterBody body, SpawnCard weakCard)
        {
            var weakMaster = weakCard.prefab != null ? weakCard.prefab.GetComponent<CharacterMaster>() : null;
            var weakBody = weakMaster != null && weakMaster.bodyPrefab != null ? weakMaster.bodyPrefab.GetComponent<CharacterBody>() : null;
            var level = Mathf.Max(0f, body.level - 1f);
            var weakHealth = weakBody != null ? weakBody.baseMaxHealth + weakBody.levelMaxHealth * level : 0f;
            var strongHealth = body.baseMaxHealth + body.levelMaxHealth * level;
            var count = weakHealth > 0f ? Mathf.RoundToInt(strongHealth / weakHealth) : 2;
            return Mathf.Clamp(count, 2, Mathf.Max(2, MaxSplits.Value));
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
            var splitCount = GetSplitCount(body, weakCard);
            for (int i = 0; i < splitCount; i++)
            {
                var offset = Quaternion.AngleAxis(360f / splitCount * i, Vector3.up) * Vector3.forward * (body.radius * 0.5f);
                var split = Helper.SpawnMonster(weakCard, position + offset, eliteDef);
                if (split != null)
                {
                    split.gameObject.AddComponent<MitosisSplit>();
                }
                // briefly invincible, as after Dio's Best Friend, so a blast that killed the monster doesn't kill them too
                var splitBody = split != null ? split.GetBody() : null;
                if (splitBody != null)
                {
                    splitBody.AddTimedBuff(RoR2Content.Buffs.Immune, SplitImmunity);
                }
            }
        }

        // marks the weaker monsters a monster split into, e.g. so they don't become ghosts
        public class MitosisSplit : MonoBehaviour
        {
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
