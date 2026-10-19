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
    public class EventGhosts : AbstractEvent
    {
        private ConfigEntry<float> GhostProbability;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                // not for boss waves
                if((infiniteTowerRun.waveIndex + 1) % 5 == 0)
                {
                    return false;
                }
            }
            // ghosts' fading health and the monsters' barrier from Ossification don't mix well
            return !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("Ossification", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "Ghosts";
        }
        public override string GetAnnouncement()
        {
            return Language.GetString("ANNOUNCE_EVENT_GHOSTS");
        }
        public override string GetDescription()
        {
            return "Some monsters spawn as ghosts that fade after a while.";
        }
        public override string GetConditionDescription()
        {
            return "Currently not a boss wave, and event \"Ossification\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            GhostProbability = config.Bind(GetEventConfigName(), "Ghost Probability", 0.3f, $"Probability for a monster to become a ghost.");
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
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.teamComponent.teamIndex != TeamIndex.Player && !body.isBoss && body.master?.gameObject.GetComponent<EventZombies.ZombieComponent>() == null && body.master?.gameObject.GetComponent<EventMitosis.MitosisSplit>() == null && Run.instance.runRNG.nextNormalizedFloat < GhostProbability.Value)
            {
                body.gameObject.EnsureComponent<GhostComponent>();
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.teamComponent.teamIndex != TeamIndex.Player && !body.isBoss && body.master?.gameObject.GetComponent<EventZombies.ZombieComponent>() == null && body.master?.gameObject.GetComponent<EventMitosis.MitosisSplit>() == null && Run.instance.runRNG.nextNormalizedFloat < GhostProbability.Value)
                {
                    body.gameObject.EnsureComponent<GhostComponent>();
                }
            }
        }


        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    if (body.TryGetComponent(out GhostComponent component))
                    {
                        GameObject.Destroy(component);
                    }
                }
            }
        }

        public class GhostComponent : MonoBehaviour
        {
            TemporaryInventory inventory;
            private void OnEnable()
            {
                var body = GetComponent<CharacterBody>();
                if (body != null && body.inventory != null)
                {
                    if (inventory == null)
                    {
                        inventory = gameObject.AddComponent<TemporaryInventory>();
                    }
                    
                    inventory.GiveTemporaryItem(RoR2Content.Items.Ghost.itemIndex, 1);
                    inventory.GiveTemporaryItem(RoR2Content.Items.HealthDecay.itemIndex, Math.Max(10, Math.Min((int)Mathf.Sqrt(body.baseMaxHealth), 40)));
                }
            }

            private void OnDisable()
            {
                Destroy(inventory);
            }
        }
    }
}
