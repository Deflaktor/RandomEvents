using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventReducedGravity : AbstractEvent
    {
        // Shadow Cloak's buff halves the gravity of a player, whose game moves them. It also gives 200 armor, which 100
        // stacks of Symbiotic Scorpion's debuff take away exactly, and 84% movement speed, which the slows can only
        // nearly take away: 8 stacks of the stacking slow make up 80%.
        private const int ArmorDebuffStacks = 100;
        private const int SlowStacks = 8;
        // the host moves the other characters, whose gravity it halves directly
        private const float GravityScale = 0.5f;
        private readonly HashSet<CharacterBody> cloaked = new HashSet<CharacterBody>();
        private readonly Dictionary<CharacterMotor, float> gravityScales = new Dictionary<CharacterMotor, float>();

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // Shadow Cloak comes with Hallowed Concepts, but as a buff it works in any run
            return true;
        }
        public override string GetEventConfigName()
        {
            return "ReducedGravity";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_REDUCED_GRAVITY");
        }
        public override string GetDescription()
        {
            return "Everyone falls at half the gravity until the event ends.";
        }
        public override string GetPopupDescription()
        {
            return "Gravity is low: everyone falls slowly!";
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

        // also those who spawn or respawn during the event
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                LowerGravity(body);
            }
        }

        private void LowerGravity(CharacterBody body)
        {
            var motor = body != null ? body.characterMotor : null;
            if (motor == null || body.teamComponent.teamIndex == TeamIndex.Neutral)
            {
                return;
            }
            if (body.isPlayerControlled)
            {
                if (cloaked.Add(body))
                {
                    body.AddBuff(DLC4Content.Buffs.TeamInvisibility);
                    body.SetBuffCount(DLC1Content.Buffs.PermanentDebuff.buffIndex, body.GetBuffCount(DLC1Content.Buffs.PermanentDebuff) + ArmorDebuffStacks);
                    body.SetBuffCount(DLC3Content.Buffs.Slow10Stacking.buffIndex, body.GetBuffCount(DLC3Content.Buffs.Slow10Stacking) + SlowStacks);
                }
            }
            else if (!gravityScales.ContainsKey(motor))
            {
                gravityScales[motor] = motor.gravityScale;
                motor.gravityScale *= GravityScale;
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            cloaked.Clear();
            gravityScales.Clear();
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                LowerGravity(body);
            }
        }

        public override void Stop()
        {
            foreach (var body in cloaked)
            {
                if (body == null)
                {
                    continue;
                }
                if (body.HasBuff(DLC4Content.Buffs.TeamInvisibility))
                {
                    body.RemoveBuff(DLC4Content.Buffs.TeamInvisibility);
                }
                body.SetBuffCount(DLC1Content.Buffs.PermanentDebuff.buffIndex, Mathf.Max(0, body.GetBuffCount(DLC1Content.Buffs.PermanentDebuff) - ArmorDebuffStacks));
                body.SetBuffCount(DLC3Content.Buffs.Slow10Stacking.buffIndex, Mathf.Max(0, body.GetBuffCount(DLC3Content.Buffs.Slow10Stacking) - SlowStacks));
            }
            cloaked.Clear();
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
