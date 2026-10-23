using BepInEx.Configuration;
using RoR2;
using RoR2.CharacterAI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventRitualHelmets : AbstractEvent
    {
        private ConfigEntry<float> DamageTakenMultiplier;
        private ConfigEntry<bool> ForceSprint;
        private const float ChargeInterval = 0.25f;
        private readonly HashSet<CharacterBody> helmeted = new HashSet<CharacterBody>();
        private float chargeTimer;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the Ritual Helmet comes with that expansion
            return !Helper.IsSimulacrumBossWaveNext() && DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4)
                && MonsterPool.GetChoices().Any(CanRun)
                // its drones can't sprint
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("DroneUprising", System.StringComparison.InvariantCultureIgnoreCase)
                    || e.GetEventConfigName().Equals("BlightedCorpses", System.StringComparison.InvariantCultureIgnoreCase)
                    || e.GetEventConfigName().Equals("BulletHell", System.StringComparison.InvariantCultureIgnoreCase));
        }

        // Monsters that can sprint, and so charge into players: walking ones whose body runs the character state that
        // handles sprinting. Fliers, worms, Hermit Crabs and immobile monsters can't.
        private static bool CanRun(WeightedSelection<DirectorCard>.ChoiceInfo choice)
        {
            if (choice.weight <= 0f || choice.value == null || choice.value.spawnCard == null
                || choice.value.spawnCard.nodeGraphType != RoR2.Navigation.MapNodeGroup.GraphType.Ground)
            {
                return false;
            }
            var master = choice.value.spawnCard.prefab != null ? choice.value.spawnCard.prefab.GetComponent<CharacterMaster>() : null;
            var body = master != null && master.bodyPrefab != null ? master.bodyPrefab.GetComponent<CharacterBody>() : null;
            if (body == null || body.baseMoveSpeed <= 0f || body.GetComponent<CharacterMotor>() == null
                // worms move by their segments, and Hermit Crabs stay burrowed to attack
                || body.GetComponent<WormBodyPositions2>() != null || body.name == "HermitCrabBody")
            {
                return false;
            }
            var stateMachine = EntityStateMachine.FindByCustomName(body.gameObject, "Body");
            var mainState = stateMachine != null ? stateMachine.mainStateType.stateType : null;
            return mainState != null && typeof(EntityStates.GenericCharacterMain).IsAssignableFrom(mainState);
        }
        public override string GetEventConfigName()
        {
            return "RitualHelmets";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_RITUAL_HELMETS");
        }
        public override string GetDescription()
        {
            return "Only monsters that can sprint spawn, all with an always charged Ritual Helmet, but players take 66% less damage.";
        }
        public override string GetPopupDescription()
        {
            return "All monsters wear a Ritual Helmet. You take 66% less damage.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled, not a Simulacrum boss wave, the stage has monsters that can sprint, and events \"DroneUprising\", \"BlightedCorpses\" and \"BulletHell\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            DamageTakenMultiplier = config.Bind(GetEventConfigName(), "Damage Taken Multiplier", 0.34f, "The damage players take during the event, relative to normal.");
            ForceSprint = config.Bind(GetEventConfigName(), "Force Sprint", true, "Makes monsters with the helmet sprint wherever they can. Its skull only hits while sprinting, which most monsters never do on their own.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.CharacterAI.BaseAI.UpdateBodyInputs += BaseAI_UpdateBodyInputs;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // the helmet's charges are always full, so its skull is always ready
        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive())
            {
                return;
            }
            chargeTimer -= Time.fixedDeltaTime;
            if (chargeTimer > 0f)
            {
                return;
            }
            chargeTimer = ChargeInterval;
            helmeted.RemoveWhere(body => body == null);
            foreach (var body in helmeted)
            {
                if (body.healthComponent.alive && body.GetBuffCount(DLC4Content.Buffs.SprintDamageReady) < EntityStates.SprintDamage.Inactive.maxCharges)
                {
                    body.SetBuffCount(DLC4Content.Buffs.SprintDamageReady.buffIndex, EntityStates.SprintDamage.Inactive.maxCharges);
                }
            }
        }

        // monsters spawning during the event get a helmet too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                GiveHelmet(body);
            }
        }

        private void GiveHelmet(CharacterBody body)
        {
            if (body != null && body.inventory != null && body.teamComponent.teamIndex != TeamIndex.Player && body.teamComponent.teamIndex != TeamIndex.Neutral
                && body.inventory.GetItemCountEffective(DLC4Content.Items.SprintDamage) <= 0)
            {
                EventLoot.GetInventory(this, body).GiveTemporaryItem(DLC4Content.Items.SprintDamage.itemIndex);
                helmeted.Add(body);
            }
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && IsActive() && damageInfo != null && self.body != null && self.body.isPlayerControlled && !Helper.IsHazard(damageInfo))
            {
                damageInfo.damage *= Mathf.Max(0f, DamageTakenMultiplier.Value);
            }
            orig(self, damageInfo);
        }

        private void BaseAI_UpdateBodyInputs(On.RoR2.CharacterAI.BaseAI.orig_UpdateBodyInputs orig, BaseAI self)
        {
            orig(self);
            if (IsActive() && ForceSprint.Value && self.body != null && self.bodyInputBank != null && self.body.inventory != null
                && self.body.inventory.GetItemCountEffective(DLC4Content.Items.SprintDamage) > 0)
            {
                // the body only sprints when it moves towards where it aims
                self.bodyInputBank.sprint.down = true;
                self.bodyInputBank.rawSprintInput.down = true;
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            helmeted.Clear();
            chargeTimer = 0f;
            // with the pool of other events that filter it, e.g. StrongEnemies; unless no monster would be left
            MonsterPool.Filter(this, CanRun);
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                GiveHelmet(body);
            }
        }

        public override void Stop()
        {
            MonsterPool.Restore(this);
            EventLoot.Clear(this);
            helmeted.Clear();
        }
    }
}
