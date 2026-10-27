using BepInEx.Configuration;
using HG;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.CharacterAI;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventStealthMission : AbstractEvent
    {
        public override EventTags Tags => EventTags.HiddenPlayers;
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
            return "StealthMission";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_STEALTH_MISSION");
        }
        public override string GetDescription()
        {
            return "Players and monsters are cloaked, and each side only fires at where it last saw the other. Attacking reveals a player or monster for 2 seconds.";
        }
        public override string GetPopupDescription()
        {
            return "Everyone is cloaked until they attack.";
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
            On.RoR2.CharacterBody.OnSkillActivated += CharacterBody_OnSkillActivated;
            IL.RoR2.CharacterAI.BaseAI.Target.GetBullseyePosition += Target_GetBullseyePosition;
        }

        // A monster keeps a cloaked player as its target and fires at where it last saw them, but the game gives it
        // their new position every 2 seconds. A stealthed player's stays where they were until they are revealed, and so
        // does a stealthed monster's for the players' drones and other allies. The AI runs on the host.
        private void Target_GetBullseyePosition(ILContext il)
        {
            var c = new ILCursor(il);
            if (!c.TryGotoNext(MoveType.After, x => x.MatchLdcR4(2f)))
            {
                Log.LogError("Could not find the cloaked target's update interval in BaseAI.Target.GetBullseyePosition.");
                return;
            }
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<float, BaseAI.Target, float>>((interval, target) =>
                ModConfig.Enabled.Value && IsActive() && target.characterBody != null && target.characterBody.GetComponent<StealthComponent>() != null
                    ? float.PositiveInfinity : interval);
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && IsCloaked(body))
            {
                body.gameObject.EnsureComponent<StealthComponent>();
            }
        }

        // the players and the monsters, not the players' allies
        private static bool IsCloaked(CharacterBody body)
        {
            return body != null && body.teamComponent != null && body.teamComponent.teamIndex != TeamIndex.Neutral
                && (body.isPlayerControlled || !Helper.IsPlayersSide(body));
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
            foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
            {
                if (body.healthComponent != null && body.healthComponent.alive && IsCloaked(body))
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
