using BepInEx.Configuration;
using HG;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventStealthMission : AbstractEvent
    {
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Hunted makes the monsters chase a player, whom they couldn't see
            return !activeOtherEvents.Any(e => e.GetEventConfigName().Equals("Hunted", StringComparison.InvariantCultureIgnoreCase));
        }
        public override string GetEventConfigName()
        {
            return "StealthMission";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_STEALTH_MISSION");
        }
        public override string GetDescription()
        {
            return "Players are cloaked. Attacking reveals them for 2 seconds.";
        }
        public override string GetConditionDescription()
        {
            return "Event \"Hunted\" inactive.";
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
            On.RoR2.CharacterBody.OnSkillActivated += CharacterBody_OnSkillActivated;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.isPlayerControlled)
            {
                body.gameObject.EnsureComponent<StealthComponent>();
            }
        }

        // the host also runs this for the skills of clients' bodies
        private void CharacterBody_OnSkillActivated(On.RoR2.CharacterBody.orig_OnSkillActivated orig, CharacterBody self, RoR2.GenericSkill skill)
        {
            orig(self, skill);
            if (NetworkServer.active && skill != null && skill.isCombatSkill && self.TryGetComponent(out StealthComponent stealth))
            {
                stealth.Reveal();
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                body.gameObject.EnsureComponent<StealthComponent>();
            }
        }

        public override void Stop()
        {
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body.TryGetComponent(out StealthComponent stealth))
                {
                    GameObject.Destroy(stealth);
                }
            }
        }

        public class StealthComponent : MonoBehaviour
        {
            private const float revealDuration = 2f;
            private CharacterBody body;
            private bool cloaked = false;
            private float revealTimer = 0f;

            private void Awake()
            {
                body = GetComponent<CharacterBody>();
            }

            public void Reveal()
            {
                revealTimer = revealDuration;
                SetCloaked(false);
            }

            private void FixedUpdate()
            {
                if (revealTimer > 0f)
                {
                    revealTimer -= Time.fixedDeltaTime;
                }
                if (revealTimer <= 0f)
                {
                    SetCloaked(true);
                }
            }

            private void SetCloaked(bool value)
            {
                if (body == null || cloaked == value)
                {
                    return;
                }
                cloaked = value;
                if (value)
                {
                    body.AddBuff(RoR2Content.Buffs.Cloak);
                }
                else
                {
                    body.RemoveBuff(RoR2Content.Buffs.Cloak);
                }
            }

            private void OnDisable()
            {
                SetCloaked(false);
            }
        }
    }
}
