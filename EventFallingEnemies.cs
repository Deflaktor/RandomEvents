using BepInEx.Configuration;
using MonoMod.Cil;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using static RoR2.GenericPickupController;
using static RoR2.SpawnCard;

namespace RandomEvents
{
    public class EventFallingEnemies : AbstractEvent
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
            return "FallingEnemies";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_FALLING_ENEMIES");
        }
        public override string GetDescription()
        {
            return "Enemies fall from the sky. Only walking enemies spawn.";
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
            //IL.RoR2.DirectorCore.TrySpawnObject += DirectorCore_TrySpawnObject;
            //On.RoR2.DirectorCore.TrySpawnObject += DirectorCore_TrySpawnObject;
            On.RoR2.SpawnCard.DoSpawn += SpawnCard_DoSpawn;
        }

        private SpawnResult SpawnCard_DoSpawn(On.RoR2.SpawnCard.orig_DoSpawn orig, SpawnCard self, Vector3 position, Quaternion rotation, DirectorSpawnRequest spawnRequest)
        {
            if (ModConfig.Enabled.Value && 
                NetworkServer.active && 
                IsActive() && 
                self.nodeGraphType == RoR2.Navigation.MapNodeGroup.GraphType.Ground && 
                self.prefab.GetComponent<CharacterMaster>() != null && 
                self.prefab.GetComponent<CharacterMaster>().bodyPrefab != null && 
                self.prefab.GetComponent<CharacterMaster>().bodyPrefab.GetComponent<CharacterBody>() != null && 
                self.prefab.GetComponent<CharacterMaster>().bodyPrefab.GetComponent<CharacterBody>().baseMoveSpeed > 0
               )
            {
                position.y += 5f;
                var newPosition = Helper.RaycastToCeiling(position, 70f, true);
                position.y -= 5f;
                if (newPosition.HasValue)
                {
                    if(newPosition.Value.y - 10f > position.y)
                    {
                        position.y = newPosition.Value.y - 10f;
                    }
                }
                else
                {
                    position.y += 65f;
                }
            }
            return orig(self, position, rotation, spawnRequest);
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            var atLeastOneChoice = false;
            var filteredWeightedSelection = new WeightedSelection<DirectorCard>();
            foreach(var choice in ClassicStageInfo.instance.monsterSelection.choices)
            {
                if (choice.weight > 0 && choice.value != null)
                {
                    var prefab = choice.value.spawnCard.prefab;
                    if (prefab != null && prefab.GetComponent<CharacterMaster>() != null)
                    {
                        var master = prefab.GetComponent<CharacterMaster>();
                        var body = master.bodyPrefab;
                        if (body != null && body.GetComponent<CharacterBody>() != null)
                        {
                            CharacterBody b = body.GetComponent<CharacterBody>();
                            if (b.baseMoveSpeed > 0 && choice.value.spawnCard.nodeGraphType == RoR2.Navigation.MapNodeGroup.GraphType.Ground)
                            {
                                filteredWeightedSelection.AddChoice(choice);
                                atLeastOneChoice = true;
                            }
                        }
                    }
                }
            }
            if (atLeastOneChoice)
            {
                ClassicStageInfo.instance.monsterSelection = filteredWeightedSelection;
            }
        }


        public override void Stop()
        {
            if (ClassicStageInfo.instance != null)
            {
                ClassicStageInfo.instance.RebuildCards();
            }
        }
    }
}
