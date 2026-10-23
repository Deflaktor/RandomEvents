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
    public class EventBulletHell : AbstractEvent
    {
        public override EventTags Tags => EventTags.Monsters;
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // only monsters whose primary fires projectiles spawn, so most of the stage's should; the faster attacks would
            // undo UmbraAmbush's hunters' slowdown
            return MonsterTraits.WeightShare(MonsterPool.GetChoices(), MonsterTraits.PrimaryFiresProjectiles) > 0.5f && !MonsterPool.OtherChangingEventActive(this, activeOtherEvents)
                && !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("UmbraAmbush", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "BulletHell";
        }
        public override string GetAnnouncement()
        {
            return Language.GetString("ANNOUNCE_EVENT_BULLET_HELL");
        }
        public override string GetDescription()
        {
            return "Only monsters whose primary attack fires projectiles spawn. Increases the fire rate of enemies but lowers their damage.";
        }
        public override string GetPopupDescription()
        {
            return "Only monsters that shoot projectiles spawn. They fire much faster, but each shot hurts less.";
        }
        public override string GetConditionDescription()
        {
            return "Most of the stage's monsters' primary attacks fire projectiles, and events \"WeakEnemies\", \"StrongEnemies\", \"Mitosis\", \"DroneUprising\", \"FallingEnemies\", \"RitualHelmets\", \"Bouncy\", \"LynxPups\", \"BlightedCorpses\" and \"UmbraAmbush\" inactive.";
        }
        protected override void AddConfig(ConfigFile config)
        {

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
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.teamComponent.teamIndex != TeamIndex.Player)
            {
                body.gameObject.EnsureComponent<BulletHellComponent>();
            }
        }

        public override void Prepare()
        {

        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            MonsterPool.Filter(this, choice => MonsterTraits.PrimaryFiresProjectiles(choice.value));
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.teamComponent.teamIndex != TeamIndex.Player)
                {
                    body.gameObject.EnsureComponent<BulletHellComponent>();
                }
            }
        }


        public override void Stop()
        {
            MonsterPool.Restore(this);
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null)
                {
                    if (body.TryGetComponent(out BulletHellComponent component))
                    {
                        GameObject.Destroy(component);
                    }
                }
            }
        }


        public class BulletHellComponent : MonoBehaviour
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
                    // Not with Tonic Afflictions, whose curse would show as a mostly cursed health bar: a hidden item lowers
                    // the damage by 1% per stack, another raises the attack speed by 10% per stack. Shorter cooldowns come
                    // from Alien Heads, each taking off 25%.
                    var cooldownFactor = 4f;
                    var attackSpeedFactor = 1.9f;
                    var damageFactor = 0.1f;
                    var alienHeadAmount = Mathf.RoundToInt(-Mathf.Log(cooldownFactor, 0.75f));
                    inventory.GiveTemporaryItem(DLC4Content.Items.ReduceDamage.itemIndex, Mathf.RoundToInt((1f - damageFactor) * 100f));
                    inventory.GiveTemporaryItem(RoR2Content.Items.BoostAttackSpeed.itemIndex, Mathf.RoundToInt((attackSpeedFactor - 1f) * 10f));
                    // VoidJailer perma stunning is not fun; spawned bodies are named "VoidJailerBody(Clone)", so compare the body index
                    if (body.bodyIndex != BodyCatalog.FindBodyIndex("VoidJailerBody")) {
                        inventory.GiveTemporaryItem(RoR2Content.Items.AlienHead.itemIndex, alienHeadAmount);
                    }
                }
            }

            private void OnDisable()
            {
                Destroy(inventory);
            }
        }
    }
}
