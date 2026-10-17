using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class EventTheAlpha : AbstractEvent
    {
        private const float UpdateInterval = 0.5f;
        private AsyncOperationHandle<GameObject> BossCombatSquad;
        private readonly BuffTracker buffs = new BuffTracker();
        private CharacterMaster alpha;
        private int addedBoostHp;
        // half again as big, so the alpha stands out; each stack adds 1% of its size
        private const int AddedScale = 50;
        private GameObject squad;
        private bool alphaFallen;
        private float updateTimer;

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
            return "TheAlpha";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_THE_ALPHA");
        }
        public override string GetDescription()
        {
            return "Makes a random monster the alpha, half again as big, with ten times its health and a boss health bar. While it lives, all other monsters are in a frenzy like from Gorag's Opus: +100% attack speed and +50% movement speed.";
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
            BossCombatSquad = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Core/BossCombatSquad.prefab");
        }
        public override void Hook()
        {
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive())
            {
                return;
            }
            updateTimer -= Time.fixedDeltaTime;
            if (updateTimer > 0f)
            {
                return;
            }
            updateTimer = UpdateInterval;
            if (alpha == null)
            {
                // until there is a monster to lead the others
                if (!alphaFallen)
                {
                    ChooseAlpha();
                }
                return;
            }
            var alphaBody = alpha.GetBody();
            if (alphaBody == null || !alphaBody.healthComponent.alive)
            {
                AlphaFallen();
                return;
            }
            foreach (var body in CharacterBody.instancesList)
            {
                if (body != null && body != alphaBody && body.teamComponent.teamIndex == alphaBody.teamComponent.teamIndex && body.healthComponent.alive && !buffs.Has(body, RoR2Content.Buffs.TeamWarCry))
                {
                    buffs.Add(body, RoR2Content.Buffs.TeamWarCry);
                }
            }
        }

        private void ChooseAlpha()
        {
            var candidates = CharacterBody.instancesList
                .Where(body => body != null && body.master != null && !body.master.isBoss && !body.isPlayerControlled && body.teamComponent.teamIndex == TeamIndex.Monster && body.healthComponent.alive)
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }
            var alphaBody = candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
            alpha = alphaBody.master;
            // ten times its health, including an elite's extra health: each Boost Hp adds 10% of the base health
            addedBoostHp = 90 + 9 * alpha.inventory.GetItemCountPermanent(RoR2Content.Items.BoostHp);
            alpha.inventory.GiveItemPermanent(RoR2Content.Items.BoostHp, addedBoostHp);
            alpha.inventory.GiveItemPermanent(DLC4Content.Items.BoostCharacterScale, AddedScale);
            // a boss health bar for every player; the alpha drops no boss loot
            squad = GameObject.Instantiate(BossCombatSquad.WaitForCompletion());
            squad.GetComponent<BossGroup>().dropPosition = null;
            NetworkServer.Spawn(squad);
            squad.GetComponent<CombatSquad>().AddMember(alpha);
            ChatHelper.SendEventMessage("EVENT_THE_ALPHA_CHOSEN", Util.GetBestBodyName(alphaBody.gameObject));
        }

        private void AlphaFallen()
        {
            buffs.RemoveAll();
            if (squad != null)
            {
                GameObject.Destroy(squad);
                squad = null;
            }
            alpha = null;
            alphaFallen = true;
            ChatHelper.SendEventMessage("EVENT_THE_ALPHA_FALLEN");
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            alpha = null;
            alphaFallen = false;
            updateTimer = 0f;
        }

        public override void Stop()
        {
            buffs.RemoveAll();
            if (alpha != null && alpha.inventory != null)
            {
                alpha.inventory.RemoveItemPermanent(RoR2Content.Items.BoostHp, addedBoostHp);
                alpha.inventory.RemoveItemPermanent(DLC4Content.Items.BoostCharacterScale, AddedScale);
            }
            // destroying the squad also removes the health bar and the alpha's boss status
            if (squad != null)
            {
                GameObject.Destroy(squad);
                squad = null;
            }
            alpha = null;
        }
    }
}
