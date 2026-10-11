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
    public class EventHaste : AbstractEvent
    {
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
            return "Haste";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_HASTE");
        }
        public override string GetDescription()
        {
            return "All beings become faster.";
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
            //SpawnCard.onSpawnedServerGlobal += SpawnCard_onSpawnedServerGlobal;
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive())
            {
                body.gameObject.EnsureComponent<HasteComponent>();
            }
        }

        //private void SpawnCard_onSpawnedServerGlobal(SpawnCard.SpawnResult spawnResult)
        //{
        //    if (ModConfig.Enabled.Value && IsActive() && NetworkServer.active)
        //    {
        //        var obj = spawnResult.spawnedInstance;
        //        if(obj != null && obj.TryGetComponent(out CharacterBody body))
        //        {
        //            body.gameObject.AddComponent<HasteComponent>();
        //        }
        //    }
        //}

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    body.gameObject.EnsureComponent<HasteComponent>();
                }
            }
        }


        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    if (body.TryGetComponent(out HasteComponent component))
                    {
                        GameObject.Destroy(component);
                    }
                }
            }
        }

        public class HasteComponent : MonoBehaviour
        {
            private void OnEnable()
            {
                if (TryGetComponent(out CharacterBody body))
                {
                    body.AddBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.AddBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.AddBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.AddBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.AddBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.AddBuff(DLC1Content.Buffs.KillMoveSpeed);
                }
            }

            private void OnDisable()
            {
                if (TryGetComponent(out CharacterBody body))
                {
                    body.RemoveBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.RemoveBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.RemoveBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.RemoveBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.RemoveBuff(DLC1Content.Buffs.KillMoveSpeed);
                    body.RemoveBuff(DLC1Content.Buffs.KillMoveSpeed);
                }
            }
        }
    }
}
