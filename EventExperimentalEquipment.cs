using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // Gives all players one of the equipment in ExperimentalEquipment/, which does something else and has a shorter cooldown
    public class EventExperimentalEquipment : AbstractEvent
    {
        private readonly List<ExperimentalEquipment> equipments = new List<ExperimentalEquipment>
        {
            new ExperimentalRoyalCapacitor(), new ExperimentalDisposableMissileLauncher(), new ExperimentalRadarScanner(), new ExperimentalSuperMassiveLeech(),
            new ExperimentalForgiveMePlease(), new ExperimentalCrowdfunder(), new ExperimentalMolotov(), new ExperimentalSawmerang(), new ExperimentalForeignFruit(),
            new ExperimentalPrimordialCube(), new ExperimentalPreonAccumulator(), new ExperimentalGnarledWoodsprite()
        };

        // the equipment the event gives this time
        public ExperimentalEquipment Current { get; private set; }

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Artifact of Enigma would swap the equipment on every use, EquipmentOnly and FuelArray give their own, and
            // SkillsOnly takes it away
            var enigmaArtifactActive = RunArtifactManager.enabledArtifactsEnumerable.Any(e => e == RoR2Content.Artifacts.Enigma)
                || activeOtherEvents.Any(e => e is EventRandomArtifact randomArtifact && randomArtifact.TurnsOn(RoR2Content.Artifacts.Enigma));
            return !enigmaArtifactActive && !Helper.IsSimulacrumBossWaveNext()
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("EquipmentOnly", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("FuelArray", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("SkillsOnly", StringComparison.InvariantCultureIgnoreCase));
        }
        protected override float DefaultWeight => 2f;

        public override string GetEventConfigName()
        {
            return "ExperimentalEquipment";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_EXPERIMENTAL_EQUIPMENT", Language.GetString((Current ?? equipments[0]).EquipmentDef.nameToken));
        }
        public override string GetDescription()
        {
            var parts = equipments.Select(experimental => experimental.Description).ToList();
            return "Gives all players an experimental equipment with a shorter cooldown. " + string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts.Last() + ".";
        }
        public override string GetPopupDescription()
        {
            return (Current ?? equipments[0]).PopupDescription;
        }
        public override string GetConditionDescription()
        {
            return "Not a Simulacrum boss wave, events \"EquipmentOnly\", \"FuelArray\" and \"SkillsOnly\" inactive and Artifact of Enigma inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
            foreach (var experimental in equipments)
            {
                experimental.Event = this;
                experimental.Preload();
            }
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.EquipmentSlot.PerformEquipmentAction += EquipmentSlot_PerformEquipmentAction;
            foreach (var experimental in equipments)
            {
                experimental.Hook();
            }
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.isPlayerControlled)
            {
                Give(body);
            }
        }

        private void Give(CharacterBody body)
        {
            if (body.inventory == null || Current == null || body.GetComponent<ExperimentalComponent>() != null)
            {
                return;
            }
            body.gameObject.AddComponent<ExperimentalComponent>().Setup(Current);
        }

        private bool IsExperimental(EquipmentSlot slot, EquipmentDef def)
        {
            return ModConfig.Enabled.Value && NetworkServer.active && IsActive() && Current != null && def == Current.EquipmentDef && Current.ReplacesAction
                && slot.characterBody != null && slot.characterBody.GetComponent<ExperimentalComponent>() != null;
        }

        // returning false fires nothing and keeps the charge
        private bool EquipmentSlot_PerformEquipmentAction(On.RoR2.EquipmentSlot.orig_PerformEquipmentAction orig, EquipmentSlot self, EquipmentDef def)
        {
            if (!IsExperimental(self, def) || self.equipmentDisabled)
            {
                return orig(self, def);
            }
            return Current.Fire(self);
        }

        public override void Prepare()
        {
            // only those of the run's expansions, e.g. the Molotov (6-Pack) with Survivors of the Void
            var available = equipments.Where(experimental => experimental.EquipmentDef != null && experimental.IsAvailable && (experimental.EquipmentDef.requiredExpansion == null
                || Run.instance == null || Run.instance.IsExpansionEnabled(experimental.EquipmentDef.requiredExpansion))).ToList();
            Current = available[RoR2Application.rng.RangeInt(0, available.Count)];
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            if (Current == null)
            {
                Prepare();
            }
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                Give(body);
            }
        }

        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.TryGetComponent(out ExperimentalComponent experimental))
                {
                    UnityEngine.Object.Destroy(experimental);
                }
            }
            foreach (var experimental in equipments)
            {
                experimental.Stop();
            }
            Current = null;
        }

        public class ExperimentalComponent : MonoBehaviour
        {
            private TemporaryEquipment equipment;
            private ExperimentalEquipment experimental;

            public void Setup(ExperimentalEquipment experimentalEquipment)
            {
                experimental = experimentalEquipment;
                equipment = gameObject.AddComponent<TemporaryEquipment>();
                equipment.cooldownScale = experimental.CooldownScale;
                equipment.SetTemporaryEquipment(experimental.EquipmentDef.equipmentIndex);
            }

            private void FixedUpdate()
            {
                if (NetworkServer.active && experimental != null && TryGetComponent(out CharacterBody body))
                {
                    experimental.FixedUpdatePlayer(body);
                }
            }

            private void OnDisable()
            {
                if (equipment != null)
                {
                    Destroy(equipment);
                }
            }
        }
    }
}
