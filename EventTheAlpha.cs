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
        public override EventTags Tags => EventTags.Monsters;
        private const float UpdateInterval = 0.5f;
        private AsyncOperationHandle<GameObject> BossCombatSquad;
        private readonly BuffTracker buffs = new BuffTracker();
        private CharacterMaster alpha;
        private int addedBoostHp;
        // half again as big, so the alpha stands out; each stack adds 1% of its size
        // +1% size per stack, more for smaller monsters, e.g. survivors, so every alpha stands out
        private static int GetAddedScale(CharacterBody body)
        {
            switch (body.hullClassification)
            {
                case HullClassification.Human: return 150;
                case HullClassification.Golem: return 50;
                default: return 25;
            }
        }
        private int addedScale;
        public enum AlphaDrop { None, White, Green, Red }
        private ConfigEntry<AlphaDrop> Drop;
        private ConfigEntry<float> SpawnRate;
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
            return "A random monster becomes a big alpha with ten times its health. While it lives, all other monsters are in a frenzy and have an Alien Head. It drops an item when killed. A quarter fewer monsters spawn.";
        }
        public override string GetPopupDescription()
        {
            return "A big, tough alpha drives the other monsters into a frenzy and gives them an Alien Head. Kill it for an item!";
        }
        public override string GetConditionDescription()
        {
            return "";
        }
        protected override void AddConfig(ConfigFile config)
        {
            Drop = config.Bind(GetEventConfigName(), "Alpha Drop", AlphaDrop.White, "The item of this tier the alpha drops when killed, which players keep. None for no item.");
            SpawnRate = config.Bind(GetEventConfigName(), "Spawn Rate", 0.75f, "How many monsters spawn during the event, relative to normal.");
        }
        public override void Preload()
        {
            BossCombatSquad = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Core/BossCombatSquad.prefab");
        }
        public override void Hook()
        {
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
            GlobalEventManager.onCharacterDeathServer += GlobalEventManager_onCharacterDeathServer;
            On.RoR2.CombatDirector.DirectorMoneyWave.Update += DirectorMoneyWave_Update;
        }

        // fewer monsters, since the alpha makes them stronger
        private float DirectorMoneyWave_Update(On.RoR2.CombatDirector.DirectorMoneyWave.orig_Update orig, CombatDirector.DirectorMoneyWave self, float deltaTime, float difficultyCoefficient)
        {
            var credits = orig(self, deltaTime, difficultyCoefficient);
            return NetworkServer.active && IsActive() ? credits * Mathf.Max(0f, SpawnRate.Value) : credits;
        }

        // a killed alpha drops a normal item, not one of the event's
        private void GlobalEventManager_onCharacterDeathServer(DamageReport damageReport)
        {
            if (!NetworkServer.active || !IsActive() || alpha == null || damageReport.victimMaster != alpha || damageReport.victimBody == null || Run.instance == null)
            {
                return;
            }
            List<PickupIndex> dropList;
            switch (Drop.Value)
            {
                case AlphaDrop.White: dropList = Run.instance.availableTier1DropList; break;
                case AlphaDrop.Green: dropList = Run.instance.availableTier2DropList; break;
                case AlphaDrop.Red: dropList = Run.instance.availableTier3DropList; break;
                default: return;
            }
            if (dropList != null && dropList.Count > 0)
            {
                var pickupIndex = dropList[RoR2Application.rng.RangeInt(0, dropList.Count)];
                PickupDropletController.CreatePickupDroplet(new UniquePickup(pickupIndex), damageReport.victimBody.corePosition, Vector3.up * 20f, false);
            }
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
                if (body == null || body == alphaBody || body.teamComponent.teamIndex != alphaBody.teamComponent.teamIndex || !body.healthComponent.alive)
                {
                    continue;
                }
                if (!buffs.Has(body, RoR2Content.Buffs.TeamWarCry))
                {
                    buffs.Add(body, RoR2Content.Buffs.TeamWarCry);
                }
                if (body.inventory != null)
                {
                    var inventory = EventLoot.GetInventory(this, body);
                    if (!inventory.temporaryItems.ContainsKey(RoR2Content.Items.AlienHead.itemIndex))
                    {
                        inventory.GiveTemporaryItem(RoR2Content.Items.AlienHead.itemIndex);
                    }
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
            addedScale = GetAddedScale(alphaBody);
            alpha.inventory.GiveItemPermanent(DLC4Content.Items.BoostCharacterScale, addedScale);
            // a boss health bar for every player; the alpha drops no boss loot
            squad = GameObject.Instantiate(BossCombatSquad.WaitForCompletion());
            squad.GetComponent<BossGroup>().dropPosition = null;
            // never defeated, which would count as a teleporter boss, e.g. for ReadyUp's vote; it's destroyed instead
            squad.GetComponent<CombatSquad>().delayDefeat = true;
            NetworkServer.Spawn(squad);
            squad.GetComponent<CombatSquad>().AddMember(alpha);
            ChatHelper.SendEventMessage("EVENT_THE_ALPHA_CHOSEN", Util.GetBestBodyName(alphaBody.gameObject));
        }

        private void AlphaFallen()
        {
            // a zombie that comes back from it would keep them
            RemoveBoosts();
            buffs.RemoveAll();
            EventLoot.Clear(this);
            if (squad != null)
            {
                GameObject.Destroy(squad);
                squad = null;
            }
            alpha = null;
            alphaFallen = true;
            ChatHelper.SendEventMessage("EVENT_THE_ALPHA_FALLEN");
        }

        // the alpha's extra health and size
        private void RemoveBoosts()
        {
            if (alpha != null && alpha.inventory != null)
            {
                alpha.inventory.RemoveItemPermanent(RoR2Content.Items.BoostHp, addedBoostHp);
                alpha.inventory.RemoveItemPermanent(DLC4Content.Items.BoostCharacterScale, addedScale);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            alpha = null;
            alphaFallen = false;
            updateTimer = 0f;
            // Simulacrum's waves get their credits from the wave instead
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.waveController != null)
            {
                var spawnRate = Mathf.Max(0f, SpawnRate.Value);
                infiniteTowerRun.waveController.totalWaveCredits *= spawnRate;
                infiniteTowerRun.waveController.creditsPerSecond *= spawnRate;
            }
        }

        public override void Stop()
        {
            buffs.RemoveAll();
            EventLoot.Clear(this);
            RemoveBoosts();
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
