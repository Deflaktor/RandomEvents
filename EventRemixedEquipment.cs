using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // Gives all players one of the equipment in RemixedEquipment/, which does something else and has a shorter cooldown
    public class EventRemixedEquipment : AbstractEvent
    {
        private readonly List<RemixedEquipment> remixes = new List<RemixedEquipment>
        {
            new RemixRoyalCapacitor(), new RemixDisposableMissileLauncher(), new RemixRadarScanner(), new RemixSuperMassiveLeech(),
            new RemixForgiveMePlease(), new RemixCrowdfunder(), new RemixMolotov(), new RemixSawmerang()
        };

        // the equipment the event gives this time
        public RemixedEquipment Current { get; private set; }

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Artifact of Enigma would swap the equipment on every use, and Equipment Only gives its own
            var enigmaArtifactActive = RunArtifactManager.enabledArtifactsEnumerable.Any(e => e == RoR2Content.Artifacts.Enigma)
                || activeOtherEvents.Any(e => e is EventRandomArtifact randomArtifact && randomArtifact.TurnsOn(RoR2Content.Artifacts.Enigma));
            return !enigmaArtifactActive && !Helper.IsSimulacrumBossWaveNext()
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("EquipmentOnly", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "RemixedEquipment";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_REMIXED_EQUIPMENT", Language.GetString((Current ?? remixes[0]).EquipmentDef.nameToken));
        }
        public override string GetDescription()
        {
            var parts = remixes.Select(remix => remix.Description).ToList();
            return "Gives all players a remixed equipment with a shorter cooldown. " + string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts.Last() + ".";
        }
        public override string GetPopupDescription()
        {
            return (Current ?? remixes[0]).PopupDescription;
        }
        public override string GetConditionDescription()
        {
            return "Not a Simulacrum boss wave, event \"EquipmentOnly\" inactive and Artifact of Enigma inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
            foreach (var remix in remixes)
            {
                remix.Event = this;
                remix.Preload();
            }
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.EquipmentSlot.PerformEquipmentAction += EquipmentSlot_PerformEquipmentAction;
            foreach (var remix in remixes)
            {
                remix.Hook();
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
            if (body.inventory == null || Current == null || body.GetComponent<RemixComponent>() != null)
            {
                return;
            }
            body.gameObject.AddComponent<RemixComponent>().Setup(Current);
        }

        private bool IsRemixed(EquipmentSlot slot, EquipmentDef def)
        {
            return ModConfig.Enabled.Value && NetworkServer.active && IsActive() && Current != null && def == Current.EquipmentDef && Current.ReplacesAction
                && slot.characterBody != null && slot.characterBody.GetComponent<RemixComponent>() != null;
        }

        // returning false fires nothing and keeps the charge
        private bool EquipmentSlot_PerformEquipmentAction(On.RoR2.EquipmentSlot.orig_PerformEquipmentAction orig, EquipmentSlot self, EquipmentDef def)
        {
            if (!IsRemixed(self, def) || self.equipmentDisabled)
            {
                return orig(self, def);
            }
            return Current.Fire(self);
        }

        public override void Prepare()
        {
            // only those of the run's expansions, e.g. the Molotov (6-Pack) with Survivors of the Void
            var available = remixes.Where(remix => remix.EquipmentDef != null && (remix.EquipmentDef.requiredExpansion == null || Run.instance == null
                || Run.instance.IsExpansionEnabled(remix.EquipmentDef.requiredExpansion))).ToList();
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
                if (body != null && body.TryGetComponent(out RemixComponent remix))
                {
                    UnityEngine.Object.Destroy(remix);
                }
            }
            foreach (var remix in remixes)
            {
                remix.Stop();
            }
            Current = null;
        }

        public class RemixComponent : MonoBehaviour
        {
            private TemporaryEquipment equipment;
            private RemixedEquipment remix;

            public void Setup(RemixedEquipment remixedEquipment)
            {
                remix = remixedEquipment;
                equipment = gameObject.AddComponent<TemporaryEquipment>();
                equipment.cooldownScale = remix.CooldownScale;
                equipment.SetTemporaryEquipment(remix.EquipmentDef.equipmentIndex);
            }

            private void FixedUpdate()
            {
                if (NetworkServer.active && remix != null && TryGetComponent(out CharacterBody body))
                {
                    remix.FixedUpdatePlayer(body);
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
