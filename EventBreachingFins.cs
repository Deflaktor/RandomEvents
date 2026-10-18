using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventBreachingFins : AbstractEvent
    {
        private ConfigEntry<float> MonsterGravity;
        private ConfigEntry<float> GroundedWeight;
        // the monsters' own gravity, to put back when the event ends
        private readonly Dictionary<CharacterMotor, float> gravityScales = new Dictionary<CharacterMotor, float>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Breaching Fin comes with that expansion
            var fin = DLC2Content.Items.KnockBackHitEnemies;
            return fin != null && Run.instance != null && (fin.requiredExpansion == null || Run.instance.IsExpansionEnabled(fin.requiredExpansion));
        }
        public override string GetEventConfigName()
        {
            return "BreachingFins";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_BREACHING_FINS");
        }
        public override string GetDescription()
        {
            return "All players get a Breaching Fin without a cooldown until the event ends, and monsters fall three times as fast. More walking monsters spawn.";
        }
        public override string GetConditionDescription()
        {
            return "Seekers of the Storm is enabled.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            MonsterGravity = config.Bind(GetEventConfigName(), "Monster Gravity", 3f, "How fast monsters fall during the event, relative to normal, so launched ones come back down quickly.");
            GroundedWeight = config.Bind(GetEventConfigName(), "Grounded Monster Weight", 2f, "How much more often monsters that walk spawn during the event, since the fins launch them.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        // players who respawn during the event get it again
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveFin(body);
                MakeHeavy(body);
            }
        }

        private void GiveFin(CharacterBody body)
        {
            if (body == null || !body.isPlayerControlled || body.inventory == null)
            {
                return;
            }
            var inventory = EventLoot.GetInventory(this, body);
            if (inventory.temporaryItems.ContainsKey(DLC2Content.Items.KnockBackHitEnemies.itemIndex))
            {
                return;
            }
            inventory.GiveTemporaryItem(DLC2Content.Items.KnockBackHitEnemies.itemIndex);
            // an item without a name that takes away the fin's cooldown
            inventory.GiveTemporaryItem(DLC4Content.Items.KnockBackHitEnemiesNoCooldown.itemIndex);
            PurchaseInteraction.CreateItemTakenOrb(body.corePosition, body.gameObject, DLC2Content.Items.KnockBackHitEnemies.itemIndex);
        }

        // the host moves the monsters, so their faster fall reaches every player
        private void MakeHeavy(CharacterBody body)
        {
            var motor = body != null ? body.characterMotor : null;
            if (motor == null || body.isPlayerControlled || body.teamComponent.teamIndex == TeamIndex.Player || gravityScales.ContainsKey(motor))
            {
                return;
            }
            gravityScales[motor] = motor.gravityScale;
            motor.gravityScale *= Mathf.Max(0f, MonsterGravity.Value);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            gravityScales.Clear();
            MonsterPool.Reweight(this, choice => choice.value != null && choice.value.spawnCard != null
                && choice.value.spawnCard.nodeGraphType == RoR2.Navigation.MapNodeGroup.GraphType.Ground ? Mathf.Max(0f, GroundedWeight.Value) : 1f);
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                GiveFin(body);
            }
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                MakeHeavy(body);
            }
        }

        public override void Stop()
        {
            MonsterPool.Restore(this);
            EventLoot.Clear(this);
            foreach (var (motor, gravityScale) in gravityScales)
            {
                if (motor != null)
                {
                    motor.gravityScale = gravityScale;
                }
            }
            gravityScales.Clear();
        }
    }
}
