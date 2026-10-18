using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventBreachingFins : AbstractEvent
    {
        private ConfigEntry<float> GroundedWeight;
        private ConfigEntry<float> LeashLength;
        // how strongly a stretched leash pulls a monster back, per meter beyond its length
        private const float LeashSpring = 15f;
        // where each walking monster last stood, which its leash is tied to
        private readonly Dictionary<CharacterMotor, Vector3> anchors = new Dictionary<CharacterMotor, Vector3>();

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
            return "All players get a Breaching Fin without a cooldown until the event ends. More walking monsters spawn, and they are on a leash.";
        }
        public override string GetConditionDescription()
        {
            return "Seekers of the Storm is enabled.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            GroundedWeight = config.Bind(GetEventConfigName(), "Grounded Monster Weight", 2f, "How much more often monsters that walk spawn during the event, since the fins launch them.");
            LeashLength = config.Bind(GetEventConfigName(), "Leash Length", 10f, "How far a launched monster gets from where it last stood before its leash pulls it back, in meters.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // Each walking monster is tied to where it last stood. Beyond the leash's length, it can't get any further away
        // and is pulled back like on a bungee cord. The host moves the monsters, so every player sees it.
        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive())
            {
                return;
            }
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                var motor = body != null ? body.characterMotor : null;
                if (motor == null || body.isPlayerControlled || body.teamComponent.teamIndex == TeamIndex.Player || !motor.hasEffectiveAuthority
                    || !body.healthComponent.alive)
                {
                    continue;
                }
                var position = body.transform.position;
                if (motor.isGrounded || !anchors.TryGetValue(motor, out var anchor))
                {
                    anchors[motor] = position;
                    continue;
                }
                var offset = position - anchor;
                var distance = offset.magnitude;
                var length = Mathf.Max(0f, LeashLength.Value);
                if (distance <= length || distance <= 0f)
                {
                    continue;
                }
                var direction = offset / distance;
                var velocity = motor.velocity;
                // the taut leash stops it from going further
                var outward = Vector3.Dot(velocity, direction);
                if (outward > 0f)
                {
                    velocity -= direction * outward;
                }
                velocity -= direction * LeashSpring * (distance - length) * Time.fixedDeltaTime;
                motor.velocity = velocity;
            }
        }

        // players who respawn during the event get it again
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveFin(body);
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

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            MonsterPool.Reweight(this, choice => choice.value != null && choice.value.spawnCard != null
                && choice.value.spawnCard.nodeGraphType == RoR2.Navigation.MapNodeGroup.GraphType.Ground ? Mathf.Max(0f, GroundedWeight.Value) : 1f);
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                GiveFin(body);
            }
        }

        public override void Stop()
        {
            MonsterPool.Restore(this);
            EventLoot.Clear(this);
            anchors.Clear();
        }
    }
}
