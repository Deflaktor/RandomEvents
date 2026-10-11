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
            return "BulletHell";
        }
        public override string GetAnnouncement()
        {
            return Language.GetString("ANNOUNCE_EVENT_BULLET_HELL");
        }
        public override string GetDescription()
        {
            return "Increases the fire rate of enemies but lowers their damage.";
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
                    // originalGame:
                    // num97 = Mathf.Pow(0.95f, tonicAfflictions)
                    // attackSpeed *= num97;
                    // moveSpeed *= num97;
                    // damage *= num97;
                    // regen *= num97;
                    // cursePenalty = 1f + 0.1f * tonicAfflictions;
                    // maxHealth *= 1f + 0.1f * BoostHP
                    // maxHealth /= cursePenalty;
                    var attackSpeedFactor = 4f;
                    var damageFactor = 0.1f;
                    var alienHeadAmount = Mathf.RoundToInt(-Mathf.Log(attackSpeedFactor, 0.75f));
                    var tonicAfflictionAmount = Mathf.RoundToInt(Mathf.Log(damageFactor) / Mathf.Log(0.95f));
                    inventory.GiveTemporaryItem(RoR2Content.Items.TonicAffliction.itemIndex, tonicAfflictionAmount);
                    inventory.GiveTemporaryItem(RoR2Content.Items.BoostHp.itemIndex, Mathf.RoundToInt(10f * 0.10f * tonicAfflictionAmount));
                    inventory.GiveTemporaryItem(RoR2Content.Items.Hoof.itemIndex, Mathf.RoundToInt(10 * 0.14f * tonicAfflictionAmount));
                    inventory.GiveTemporaryItem(RoR2Content.Items.BoostAttackSpeed.itemIndex, Mathf.RoundToInt(attackSpeedFactor * 10 * 0.1f * tonicAfflictionAmount));
                    // VoidJailer perma stunning is not fun
                    if (!body.name.Equals("VoidJailerBody")) {
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
