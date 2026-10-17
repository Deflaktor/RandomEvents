using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventSecondWind : AbstractEvent
    {
        private const float FragileHealthFraction = 0.1f;
        private const float ReviveDelay = 2f;
        // the players alive at the start, who are fragile until they die or the event ends; also their new bodies, e.g. after Dio's Best Friend
        private readonly HashSet<CharacterMaster> fragile = new HashSet<CharacterMaster>();
        private readonly HashSet<CharacterMaster> revived = new HashSet<CharacterMaster>();
        private readonly Dictionary<CharacterBody, int> curses = new Dictionary<CharacterBody, int>();

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
            return "SecondWind";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SECOND_WIND");
        }
        public override string GetDescription()
        {
            return "Revives dead players. All other players drop to 10% of their health until the event ends, but anyone who dies during the event is revived once with full health.";
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
            On.RoR2.CharacterMaster.OnBodyDeath += CharacterMaster_OnBodyDeath;
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && body.master != null && fragile.Contains(body.master))
            {
                MakeFragile(body);
            }
        }

        private void CharacterMaster_OnBodyDeath(On.RoR2.CharacterMaster.orig_OnBodyDeath orig, CharacterMaster self, CharacterBody body)
        {
            orig(self, body);
            // only when no item or other effect revives them; the pending revive also keeps the run from ending
            if (ModConfig.Enabled.Value && NetworkServer.active && IsActive() && self.playerCharacterMasterController != null
                && self.playerCharacterMasterController.isConnected && !revived.Contains(self) && self.IsDeadAndOutOfLivesServer())
            {
                revived.Add(self);
                fragile.Remove(self);
                self.Invoke(nameof(CharacterMaster.RespawnExtraLifeShrine), ReviveDelay);
                ChatHelper.SendEventMessage("EVENT_SECOND_WIND_REVIVED", ChatHelper.GetColoredPlayerName(body));
            }
        }

        // curse lowers the maximum health, which the health bar shows, and is reduced again afterwards
        private void MakeFragile(CharacterBody body)
        {
            if (curses.ContainsKey(body))
            {
                return;
            }
            var stacks = Mathf.RoundToInt(body.cursePenalty * (1f / FragileHealthFraction - 1f) / 0.01f);
            body.SetBuffCount(RoR2Content.Buffs.PermanentCurse.buffIndex, body.GetBuffCount(RoR2Content.Buffs.PermanentCurse) + stacks);
            curses[body] = stacks;
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            fragile.Clear();
            revived.Clear();
            curses.Clear();
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                fragile.Add(body.master);
                MakeFragile(body);
            }
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (pc.isConnected && pc.master != null && !fragile.Contains(pc.master) && pc.master.IsDeadAndOutOfLivesServer())
                {
                    var name = pc.GetDisplayName();
                    pc.master.RespawnExtraLifeShrine();
                    var body = pc.master.GetBody();
                    ChatHelper.SendEventMessage("EVENT_SECOND_WIND_RETURNED", body != null ? ChatHelper.GetColoredPlayerName(body) : name);
                }
            }
        }

        public override void Stop()
        {
            foreach (var (body, stacks) in curses)
            {
                if (body != null)
                {
                    body.SetBuffCount(RoR2Content.Buffs.PermanentCurse.buffIndex, Math.Max(0, body.GetBuffCount(RoR2Content.Buffs.PermanentCurse) - stacks));
                }
            }
            curses.Clear();
            fragile.Clear();
            revived.Clear();
        }
    }
}
