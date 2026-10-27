using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventSwarmQueen : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters | EventTags.ExtraMonsters | EventTags.MonsterSize;
        public override EventTags ExcludedTags => EventTags.MonsterSize;
        private const float SpreadRadius = 3f;
        private ConfigEntry<int> SwarmSize;
        private AsyncOperationHandle<CharacterSpawnCard> Beetle;
        private AsyncOperationHandle<CharacterSpawnCard> LesserWisp;

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
            return "SwarmQueen";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SWARM_QUEEN");
        }
        public override string GetDescription()
        {
            return "Every monster that spawns brings 3 Beetles along, or 3 Lesser Wisps if it flies.";
        }
        public override string GetPopupDescription()
        {
            return "Every monster brings 3 Beetles along, or 3 Lesser Wisps if it flies.";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            SwarmSize = config.Bind(GetEventConfigName(), "Swarm Size", 3, "How many Beetles or Lesser Wisps each monster brings along.");
        }
        public override void Preload()
        {
            Beetle = Addressables.LoadAssetAsync<CharacterSpawnCard>("RoR2/Base/Beetle/cscBeetle.asset");
            LesserWisp = Addressables.LoadAssetAsync<CharacterSpawnCard>("RoR2/Base/Wisp/cscLesserWisp.asset");
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        // not for bosses, minions or the swarm itself, nor for Mitosis' splits or zombies that come back, which would bring
        // ever more swarms
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            var master = body.master;
            if (!ModConfig.Enabled.Value || !NetworkServer.active || !IsActive() || master == null || body.isBoss || master.isBoss
                || body.teamComponent.teamIndex == TeamIndex.Player || body.teamComponent.teamIndex == TeamIndex.Neutral
                || master.minionOwnership.ownerMaster != null || master.GetComponent<SwarmMember>() != null || master.GetComponent<EventMitosis.MitosisSplit>() != null
                || (master.TryGetComponent(out EventZombies.ZombieComponent zombie) && zombie.revived))
            {
                return;
            }
            var flies = body.isFlying;
            var spawnCard = flies ? LesserWisp.WaitForCompletion() : Beetle.WaitForCompletion();
            var center = flies ? body.corePosition : body.footPosition;
            for (int i = 0; i < SwarmSize.Value; i++)
            {
                var offset = Quaternion.Euler(0f, 360f * i / Mathf.Max(1, SwarmSize.Value), 0f) * Vector3.forward * (body.radius + SpreadRadius);
                var member = Helper.SpawnMonster(spawnCard, center + offset);
                if (member != null)
                {
                    member.gameObject.AddComponent<SwarmMember>();
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
        }

        public override void Stop()
        {
        }

        // marks the swarms' own monsters, which bring no swarm along, e.g. so Shielded doesn't shield them
        public class SwarmMember : MonoBehaviour
        {
        }
    }
}
