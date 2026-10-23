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
    public class EventSurvivorSwap : AbstractEvent
    {
        private readonly ItemSwap itemSwap = new ItemSwap();
        private readonly BuffTracker buffs = new BuffTracker();
        // 20 armor each
        private const int ArmorBuffs = 2;
        // the player who is alone and became a random survivor, who has armor to make up for their unknown kit
        private CharacterMaster aloneMaster;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun)
            {
                // not for boss waves
                if ((infiniteTowerRun.waveIndex + 1) % 5 == 0)
                {
                    return false;
                }
            }
            // they change the players' bodies or items as well
            if (activeOtherEvents.Any(e => e.GetEventConfigName().Equals("MonsterTransform", StringComparison.InvariantCultureIgnoreCase)
                || e.GetEventConfigName().Equals("InventoryRoulette", StringComparison.InvariantCultureIgnoreCase)))
            {
                return false;
            }
            var bodies = Helper.GetLivingPlayerBodies();
            return bodies.Count >= 2 || (bodies.Count == 1 && GetSurvivorCandidates(bodies[0].master).Count > 0);
        }
        public override string GetEventConfigName()
        {
            return "SurvivorSwap";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_SURVIVOR_SWAP");
        }
        public override string GetDescription()
        {
            return "Players swap survivors and items with each other until the event ends. Alone, you become a random survivor with 40 armor.";
        }
        public override string GetPopupDescription()
        {
            return "Swap survivors and items with another player! Alone, you become a random survivor.";
        }
        public override string GetConditionDescription()
        {
            return "Events \"MonsterTransform\" and \"InventoryRoulette\" inactive and currently not a boss wave.";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.CharacterMaster.RestoreOriginalBodyPrefab += CharacterMaster_RestoreOriginalBodyPrefab;
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.Artifacts.EnigmaArtifactManager.OnPlayerCharacterBodyStartServer += EnigmaArtifactManager_OnPlayerCharacterBodyStartServer;
        }

        public override bool SuppressesHereticTransformation => true;

        // revives during the event, e.g. by Dio's Best Friend, bring back the swapped survivor, as with Monster Transform
        // also the bodies after the swap's respawn or a revive
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive() && aloneMaster != null && body.master == aloneMaster)
            {
                for (int i = 0; i < ArmorBuffs; i++)
                {
                    buffs.Add(body, DLC4Content.Buffs.MinorArmorBuff);
                }
            }
        }

        private void CharacterMaster_RestoreOriginalBodyPrefab(On.RoR2.CharacterMaster.orig_RestoreOriginalBodyPrefab orig, CharacterMaster self)
        {
            if (NetworkServer.active && self.TryGetComponent(out SurvivorSwapComponent survivorSwap) && survivorSwap.enabled)
            {
                return;
            }
            orig(self);
        }

        // the swap counts as spawning, for which Artifact of Enigma would hand out equipment
        private void EnigmaArtifactManager_OnPlayerCharacterBodyStartServer(On.RoR2.Artifacts.EnigmaArtifactManager.orig_OnPlayerCharacterBodyStartServer orig, CharacterBody characterBody)
        {
            if (!IsActive())
            {
                orig(characterBody);
            }
        }

        // the survivors other than the current one that the player has unlocked
        private static List<SurvivorDef> GetSurvivorCandidates(CharacterMaster master)
        {
            var networkUser = master.playerCharacterMasterController != null ? master.playerCharacterMasterController.networkUser : null;
            return SurvivorCatalog.orderedSurvivorDefs.Where(survivorDef =>
            {
                if (survivorDef == null || survivorDef.hidden || survivorDef.bodyPrefab == null || survivorDef.bodyPrefab == master.bodyPrefab
                    || (survivorDef.unlockableDef != null && networkUser != null && !networkUser.unlockables.Contains(survivorDef.unlockableDef)))
                {
                    return false;
                }
                var expansion = survivorDef.GetRequiredExpansion();
                return expansion == null || (Run.instance != null && Run.instance.IsExpansionEnabled(expansion));
            }).ToList();
        }

        private static string GetSurvivorName(GameObject bodyPrefab)
        {
            return Language.GetString(bodyPrefab.GetComponent<CharacterBody>().baseNameToken);
        }

        private static void Swap(CharacterMaster master, GameObject bodyPrefab, Loadout loadout)
        {
            master.gameObject.EnsureComponent<SurvivorSwapComponent>().SetSurvivor(bodyPrefab, loadout);
            var body = master.GetBody();
            if (body != null)
            {
                master.Respawn(body.transform.position, body.transform.rotation);
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            var bodies = Helper.GetLivingPlayerBodies().OrderBy(body => RoR2Application.rng.nextNormalizedFloat).ToList();
            if (bodies.Count >= 2)
            {
                // in a random order, each player takes over the survivor, loadout and items of the next one
                var masters = bodies.Select(body => body.master).ToList();
                // before the respawns replace the bodies
                var names = bodies.Select(ChatHelper.GetColoredPlayerName).ToList();
                var others = masters.Skip(1).Concat(masters.Take(1)).ToList();
                var bodyPrefabs = others.Select(other => other.bodyPrefab).ToList();
                var loadouts = others.Select(other =>
                {
                    var loadout = Loadout.RequestInstance();
                    other.loadout.Copy(loadout);
                    return loadout;
                }).ToList();
                itemSwap.Lend(masters, others);
                for (int i = 0; i < masters.Count; i++)
                {
                    Swap(masters[i], bodyPrefabs[i], loadouts[i]);
                    Loadout.ReturnInstance(loadouts[i]);
                    ChatHelper.SendEventMessage("EVENT_SURVIVOR_SWAP_TAKES", names[i], GetSurvivorName(bodyPrefabs[i]), names[(i + 1) % names.Count]);
                }
            }
            else if (bodies.Count == 1)
            {
                var master = bodies[0].master;
                var candidates = GetSurvivorCandidates(master);
                if (candidates.Count > 0)
                {
                    var survivorDef = candidates[RoR2Application.rng.RangeInt(0, candidates.Count)];
                    ChatHelper.SendEventMessage("EVENT_SURVIVOR_SWAP_RANDOM", ChatHelper.GetColoredPlayerName(bodies[0]), GetSurvivorName(survivorDef.bodyPrefab));
                    // with the player's own loadout for that survivor
                    aloneMaster = master;
                    Swap(master, survivorDef.bodyPrefab, null);
                }
            }
        }

        public override void Stop()
        {
            itemSwap.Return();
            aloneMaster = null;
            buffs.RemoveAll();
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (pc.master != null && pc.master.TryGetComponent(out SurvivorSwapComponent survivorSwap))
                {
                    // disabling restores the player's own survivor now, Destroy would do so only after the respawn
                    survivorSwap.enabled = false;
                    GameObject.Destroy(survivorSwap);
                    var body = pc.master.GetBody();
                    // after the stage has ended, the next stage spawns the player's own survivor
                    if (body != null && Stage.instance != null && !Stage.instance.completed)
                    {
                        pc.master.Respawn(body.transform.position, body.transform.rotation);
                    }
                }
            }
        }

        public class SurvivorSwapComponent : MonoBehaviour
        {
            private GameObject ownBodyPrefab = null;
            private GameObject survivorBodyPrefab = null;
            private Loadout ownLoadout = null;

            public void SetSurvivor(GameObject bodyPrefab, Loadout loadout)
            {
                var master = GetComponent<CharacterMaster>();
                if (ownBodyPrefab == null)
                {
                    ownBodyPrefab = master.bodyPrefab;
                    // as with Monster Transform: an earlier revive may have cleared the base body, which the swap would replace
                    if (!master.originalBodyPrefab)
                    {
                        master.SetBaseBodyHandlingConfig(null, ownBodyPrefab);
                    }
                }
                if (loadout != null && ownLoadout == null)
                {
                    ownLoadout = Loadout.RequestInstance();
                    master.loadout.Copy(ownLoadout);
                }
                survivorBodyPrefab = bodyPrefab;
                master.bodyPrefab = bodyPrefab;
                if (loadout != null)
                {
                    master.SetLoadoutServer(loadout);
                }
            }

            private void FixedUpdate()
            {
                var master = GetComponent<CharacterMaster>();
                if (survivorBodyPrefab != null && master.bodyPrefab != survivorBodyPrefab)
                {
                    master.bodyPrefab = survivorBodyPrefab;
                }
            }

            private void OnDisable()
            {
                var master = GetComponent<CharacterMaster>();
                if (master && ownBodyPrefab)
                {
                    // the respawns of the event put the swapped survivor's body config on the master; replace them with the player's own
                    var ownBodyConfig = new BodyHandlingConfig(ownBodyPrefab.GetComponent<CharacterBody>().bodyIndex);
                    master.SetBaseBodyHandlingConfig(ownBodyConfig, ownBodyPrefab);
                    master.PushBodyHandlingConfig(ownBodyConfig, ownBodyPrefab, clearHandlingConfigs: true);
                }
                if (master && ownLoadout != null)
                {
                    master.SetLoadoutServer(ownLoadout);
                }
                if (ownLoadout != null)
                {
                    Loadout.ReturnInstance(ownLoadout);
                    ownLoadout = null;
                }
                survivorBodyPrefab = null;
            }
        }
    }
}
