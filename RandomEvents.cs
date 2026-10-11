using BepInEx;
using R2API.Utils;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    [BepInDependency("com.KingEnderBrine.InLobbyConfig", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync, VersionStrictness.DifferentModVersionsAreOk)]
    public class RandomEvents : BaseUnityPlugin
    {
        public static PluginInfo PInfo { get; private set; }
        public static RandomEvents instance;

        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Def";
        public const string PluginName = "RandomEvents";
        public const string PluginVersion = "1.1.3";

        public List<AbstractEvent> m_allEventsIncludingNotLoadedOnes = new List<AbstractEvent>();
        public List<AbstractEvent> m_loadedEvents = new List<AbstractEvent>();
        public List<AbstractEvent> m_activeEvents = new List<AbstractEvent>();
        public List<AbstractEvent> m_upcomingEvents = new List<AbstractEvent>();
        public float currentEventProbability = 0f;
        public AsyncOperationHandle<CharacterSpawnCard> someMonster;
        public AsyncOperationHandle<CharacterSpawnCard> someMonster2;
        public bool m_announcementGiven = false;
        public int m_eventStartTime = 0;

        public void Awake()
        {
            PInfo = Info;
            instance = this;
            Log.Init(Logger);

            BodyCatalog.availability.CallWhenAvailable(() =>
            {
                TemporaryInventory.Hook();
                TemporaryEquipment.Hook();

                ModConfig.Init();
                InitAndHookEvent(new EventBulletHell());
                InitAndHookEvent(new EventCasinoItem());
                InitAndHookEvent(new EventEquipmentOnly());
                InitAndHookEvent(new EventExplodingCorpses());
                InitAndHookEvent(new EventFallingEnemies());
                InitAndHookEvent(new EventFuelArray());
                InitAndHookEvent(new EventGhosts());
                InitAndHookEvent(new EventHaste());
                InitAndHookEvent(new EventItemZone());
                InitAndHookEvent(new EventMonsterTransform());
                InitAndHookEvent(new EventRandomArtifact());
                InitAndHookEvent(new EventRandomTeams());
                InitAndHookEvent(new EventSkillsOnly());
                InitAndHookEvent(new EventSmallArena());
                InitAndHookEvent(new EventStrongEnemies());
                InitAndHookEvent(new EventWeakEnemies());
                InitAndHookEvent(new EventZombies());
                ModConfig.InLobbyConfig();
            });

            someMonster = Addressables.LoadAssetAsync<CharacterSpawnCard>("RoR2/DLC1/MajorAndMinorConstruct/cscMinorConstruct.asset");
            someMonster2 = Addressables.LoadAssetAsync<CharacterSpawnCard>("RoR2/Base/Wisp/cscLesserWisp.asset");
        }
        private AbstractEvent InitAndHookEvent(AbstractEvent ev)
        {
            if (ev.LoadCondition())
            {
                ev.SetupConfig(instance.Config);
                ev.Preload();
                ev.Hook();
                m_loadedEvents.Add(ev);
            }
            m_allEventsIncludingNotLoadedOnes.Add(ev);
            return ev;
        }

        private AbstractEvent ChooseRandomEvent(List<AbstractEvent> eventCandidates, List<AbstractEvent> activeEvents)
        {
            WeightedSelection<AbstractEvent> weightedSelection = new WeightedSelection<AbstractEvent>();
            foreach (var ev in eventCandidates)
            {
                if (!activeEvents.Contains(ev) && ev.IsEnabled() && ev.Condition(activeEvents))
                {
                    weightedSelection.AddChoice(ev, ev.GetWeight());
                }
            }
            if (weightedSelection.Count == 0)
                return null;
            return weightedSelection.Evaluate(UnityEngine.Random.value);
        }

        private void PrepareAndAnnounceUpcomingEvents(float announceDelay)
        {
#if DEBUG
            Config.Reload();
#endif
            if (ModConfig.Enabled.Value && NetworkServer.active && SceneCatalog.GetSceneDefForCurrentScene().sceneType == SceneType.Stage || SceneCatalog.GetSceneDefForCurrentScene().cachedName == "arena")
            {
                if (currentEventProbability >= 1.0f || RoR2Application.rng.nextNormalizedFloat < currentEventProbability)
                {
                    currentEventProbability = ModConfig.EventProbability.Value;
                    WeightedSelection<int> weightedSelection = new WeightedSelection<int>();
                    weightedSelection.AddChoice(1, ModConfig.SingleEventWeight.Value);
                    weightedSelection.AddChoice(2, ModConfig.DoubleEventWeight.Value);
                    weightedSelection.AddChoice(3, ModConfig.TripleEventWeight.Value);
                    var eventAmount = weightedSelection.Evaluate(UnityEngine.Random.value);

                    for (int i = 0; i < eventAmount; i++)
                    {
                        var ev = ChooseRandomEvent(m_loadedEvents, m_upcomingEvents);
                        if (ev != null)
                        {
                            m_upcomingEvents.Add(ev);
                            ev.Prepare();
                        }
                    }

                    instance.StartCoroutine(DelayChatSendAnnouncement(announceDelay, new List<AbstractEvent>(m_upcomingEvents)));
                } 
                else
                {
                    currentEventProbability += ModConfig.EventProbabilityIncrease.Value;
                }
            }
        }
        private void OnEnable()
        {
            On.RoR2.InfiniteTowerRun.Start += InfiniteTowerRun_Start;
            On.EntityStates.InfiniteTowerSafeWard.AwaitingActivation.OnEnter += AwaitingActivation_OnEnter;
            On.RoR2.InfiniteTowerRun.CleanUpCurrentWave += InfiniteTowerRun_CleanUpCurrentWave;
            On.RoR2.InfiniteTowerRun.OnWaveAllEnemiesDefeatedServer += InfiniteTowerRun_OnWaveAllEnemiesDefeatedServer;
            On.RoR2.InfiniteTowerRun.InitializeWaveController += InfiniteTowerRun_InitializeWaveController;
            On.RoR2.SceneDirector.Start += SceneDirector_Start;
            On.RoR2.Run.Start += Run_Start;
            On.RoR2.Run.OnFixedUpdate += Run_OnFixedUpdate;
        }

        private void OnDisable()
        {
            On.RoR2.InfiniteTowerRun.Start -= InfiniteTowerRun_Start;
            On.EntityStates.InfiniteTowerSafeWard.AwaitingActivation.OnEnter -= AwaitingActivation_OnEnter;
            On.RoR2.InfiniteTowerRun.CleanUpCurrentWave -= InfiniteTowerRun_CleanUpCurrentWave;
            On.RoR2.InfiniteTowerRun.OnWaveAllEnemiesDefeatedServer -= InfiniteTowerRun_OnWaveAllEnemiesDefeatedServer;
            On.RoR2.InfiniteTowerRun.InitializeWaveController -= InfiniteTowerRun_InitializeWaveController;
            On.RoR2.SceneDirector.Start -= SceneDirector_Start;
            On.RoR2.Run.Start -= Run_Start;
            On.RoR2.Run.OnFixedUpdate -= Run_OnFixedUpdate;
        }

        private void Run_OnFixedUpdate(On.RoR2.Run.orig_OnFixedUpdate orig, Run self)
        {
            orig(self);
            if(!ModConfig.Enabled.Value || Run.instance is InfiniteTowerRun infiniteTowerRun || !NetworkServer.active)
            {
                return;
            }
            int eventDuration = Mathf.RoundToInt(60 * ModConfig.EventDuration.Value);
            int eventFrequency = Mathf.RoundToInt(60 * ModConfig.EventFrequency.Value);
            int stopWatch = Mathf.RoundToInt(self.GetRunStopwatch() * 60);
            int oneSecond = 60 * 1;
            int sixSeconds = 60 * 6;
            if(m_activeEvents.Count > 0)
            {
                var timer = (stopWatch - m_eventStartTime) % eventFrequency;
                if(timer >= eventDuration - oneSecond)
                {
                    m_announcementGiven = false;
                    ChatHelper.AnnounceEventConclusion();
                    StopAllActiveEvents();
                }
            }
            else
            {
                var timer = stopWatch % eventFrequency;
                if (timer >= eventFrequency - sixSeconds && !m_announcementGiven)
                {
                    m_announcementGiven = true;
                    PrepareAndAnnounceUpcomingEvents(0f);
                }
                if (timer >= eventFrequency - oneSecond)
                {
                    m_announcementGiven = false;
                    m_eventStartTime = stopWatch;
                    StartUpcomingEvents();
                }
            }
        }
        private void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            m_eventStartTime = 0;
            m_announcementGiven = false;
            currentEventProbability = ModConfig.EventProbability.Value;
            orig(self);
        }


        private void SceneDirector_Start(On.RoR2.SceneDirector.orig_Start orig, SceneDirector self)
        {
            orig(self);
            if (!ModConfig.Enabled.Value || Run.instance is InfiniteTowerRun infiniteTowerRun || !NetworkServer.active)
            {
                return;
            }
            StopAllActiveEvents();
            m_eventStartTime = 0;
            m_announcementGiven = false;
        }

        private void InfiniteTowerRun_Start(On.RoR2.InfiniteTowerRun.orig_Start orig, InfiniteTowerRun self)
        {
            orig(self);
            WriteRandomEventsMarkdownFile();
            // remove incompatible wave categories
            foreach (var waveCategory in self.waveCategories)
            {
                List<InfiniteTowerWaveCategory.WeightedWave> wavePrefabs = new List<InfiniteTowerWaveCategory.WeightedWave>();
                foreach (var wavePrefab in waveCategory.wavePrefabs)
                {
                    if(wavePrefab.wavePrefab.GetComponent<ArtifactEnabler>() == null)
                    {
                        wavePrefabs.Add(wavePrefab);
                    }
                }
                waveCategory.wavePrefabs = wavePrefabs.ToArray();
            }
            currentEventProbability = ModConfig.EventProbability.Value;
        }

        private void InfiniteTowerRun_CleanUpCurrentWave(On.RoR2.InfiniteTowerRun.orig_CleanUpCurrentWave orig, InfiniteTowerRun self)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active && m_activeEvents.Count > 0)
            {
                ChatHelper.AnnounceEventConclusion();
                StopAllActiveEvents();
            }
            orig(self);
        }

        private void StopAllActiveEvents()
        {
            m_activeEvents.ForEach(ev => ev.Stop());
            m_activeEvents.Clear();
        }

        private void StartUpcomingEvents()
        {
            m_activeEvents.AddRange(m_upcomingEvents);
            m_activeEvents.ForEach(e => e.Start(m_activeEvents));
            m_upcomingEvents.Clear();
        }

        private void AwaitingActivation_OnEnter(On.EntityStates.InfiniteTowerSafeWard.AwaitingActivation.orig_OnEnter orig, EntityStates.InfiniteTowerSafeWard.AwaitingActivation self)
        {
            orig(self);
            PrepareAndAnnounceUpcomingEvents(1.0f);
        }

        private void InfiniteTowerRun_OnWaveAllEnemiesDefeatedServer(On.RoR2.InfiniteTowerRun.orig_OnWaveAllEnemiesDefeatedServer orig, InfiniteTowerRun self, InfiniteTowerWaveController wc)
        {
            orig(self, wc);
            if (ModConfig.Enabled.Value && NetworkServer.active)
            {
                StopAllActiveEvents();

                IEnumerator DelayPrepareEvents()
                {
                    yield return new WaitForSeconds(1.0f);
                    if (Run.instance is InfiniteTowerRun instance)
                    {
                        if (instance.safeWardController.wardStateMachine.state is EntityStates.InfiniteTowerSafeWard.Active)
                        {
                            PrepareAndAnnounceUpcomingEvents(0f);
                        }
                    }
                }
                instance.StartCoroutine(DelayPrepareEvents());
            }
        }

        private void InfiniteTowerRun_InitializeWaveController(On.RoR2.InfiniteTowerRun.orig_InitializeWaveController orig, InfiniteTowerRun self)
        {
            orig(self);
            if (ModConfig.Enabled.Value && NetworkServer.active && m_upcomingEvents.Count > 0)
            {
                StartUpcomingEvents();
            }
        }

        private CharacterMaster SpawnHiddenMonsterWithInvadingDoppelgangerItem()
        {
            bool foundPosition = false;
            Vector3 lowestFloor = new Vector3(0f, float.MaxValue, 0f);
            ReadOnlyCollection<TeamComponent> teamMembers = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var teamMember in teamMembers)
            {
                if (teamMember.body != null && teamMember.body.isPlayerControlled)
                {
                    var floor = Helper.RaycastToFloor(teamMember.body.corePosition, 50f, false);
                    var lowestPosition = floor.HasValue ? floor.Value : teamMember.body.footPosition;
                    if (lowestPosition.y < lowestFloor.y)
                    {
                        lowestFloor.x = lowestPosition.x;
                        lowestFloor.y = lowestPosition.y;
                        lowestFloor.z = lowestPosition.z;
                        foundPosition = true;
                    }
                }
            }
            if (!foundPosition)
            {
                return null;
            }
            lowestFloor.y -= 15f;
            DirectorPlacementRule directorPlacementRule = new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Direct,
                position = lowestFloor
            };
            SpawnCard spawnCard = someMonster2.WaitForCompletion();
            DirectorSpawnRequest directorSpawnRequest = new DirectorSpawnRequest(spawnCard, directorPlacementRule, Run.instance.runRNG);
            directorSpawnRequest.teamIndexOverride = TeamIndex.None;
            directorSpawnRequest.ignoreTeamMemberLimit = true;
            directorSpawnRequest.onSpawnedServer = (Action<SpawnCard.SpawnResult>)Delegate.Combine(directorSpawnRequest.onSpawnedServer, (Action<SpawnCard.SpawnResult>)delegate (SpawnCard.SpawnResult result)
            {
                var spawnResult = result.spawnedInstance.GetComponent<CharacterMaster>();
                spawnResult.inventory.GiveItemPermanent(RoR2Content.Items.InvadingDoppelganger);
                spawnResult.inventory.GiveItemPermanent(RoR2Content.Items.HealthDecay);
            });
            var result = spawnCard.DoSpawn(directorSpawnRequest.placementRule.position, UnityEngine.Quaternion.identity, directorSpawnRequest).spawnedInstance;
            return result.GetComponent<CharacterMaster>();
        }

        IEnumerator DelayChatSendAnnouncement(float time, List<AbstractEvent> events)
        {
            yield return new WaitForSeconds(time);
            if (events.Count == 1)
            {
                ChatHelper.AnnounceEvent();
            }
            else if (events.Count == 2)
            {
                ChatHelper.AnnounceDoubleEvent();
            }
            else
            {
                ChatHelper.AnnounceTripleEvent();
            }
            var gameObject = SpawnHiddenMonsterWithInvadingDoppelgangerItem();
            yield return new WaitForSeconds(0.5f);
            gameObject.TrueKill();
            yield return new WaitForSeconds(0.5f);
            events[0].GetAnnouncement();
            string announcement = "";
            List<string> eventAnnouncements = new List<string>();
            var conjunction = Language.GetString("ANNOUNCE_EVENTS_CONJUNCTION");
            for (int i = 0; i < events.Count; i++)
            {
                announcement += $"<style=cWorldEvent>{events[i].GetAnnouncement()}</style>";
                if (i < events.Count - 1)
                {
                    announcement += $"<style=cEvent>{conjunction}</style>";
                }
            }
            ChatHelper.Send($"<size=26px>{announcement}</size>");
            if (events.Count >= 2)
            {
                gameObject = SpawnHiddenMonsterWithInvadingDoppelgangerItem();
                yield return new WaitForSeconds(0.5f);
                gameObject.TrueKill();
                if (events.Count >= 3)
                {
                    yield return new WaitForSeconds(0.5f);
                    gameObject = SpawnHiddenMonsterWithInvadingDoppelgangerItem();
                    yield return new WaitForSeconds(0.5f);
                    gameObject.TrueKill();
                }
            }
        }

        [ConCommand(commandName = "start_event", flags = ConVarFlags.ExecuteOnServer, helpText = "Starts the given event. Provide internal event name.")]
        private static void Command_StartEvent(ConCommandArgs args)
        {
            if (!NetworkServer.active) {
                Log.LogError($"Events can only be started by the host.");
                return;
            }
            string name = args.GetArgString(0);
            var ev = instance.FindEventByName(name);
            if (ev != null)
            {
                ev.Prepare();
                instance.m_activeEvents.Add(ev);
                ev.Start(instance.m_activeEvents);
                Log.LogInfo($"Event {ev.GetEventConfigName()} started.");
                ChatHelper.Send($"<size=28px><style=cWorldEvent>{ev.GetAnnouncement()}</style></size>");
            }
            else
            {
                Log.LogError($"Can not find event named {name}.");
            }
        }

        [ConCommand(commandName = "queue_event", flags = ConVarFlags.ExecuteOnServer, helpText = "Queues the given event. Provide internal event name.")]
        private static void Command_QueueEvent(ConCommandArgs args)
        {
            if (!NetworkServer.active)
            {
                Log.LogError($"Events can only be started by the host.");
                return;
            }
            string name = args.GetArgString(0);
            var ev = instance.FindEventByName(name);
            if (ev != null)
            {
                if(instance.m_upcomingEvents.Contains(ev))
                {
                    Log.LogInfo($"Event {ev.GetEventConfigName()} is already in queue.");
                } else { 
                    ev.Prepare();
                    instance.m_upcomingEvents.Add(ev);
                    Log.LogInfo($"Event {ev.GetEventConfigName()} queued.");
                }
            }
            else
            {
                Log.LogError($"Can not find event named {name}.");
            }
        }

        [ConCommand(commandName = "stop_event", flags = ConVarFlags.ExecuteOnServer, helpText = "Stop the given event. Keyword all for all currently active events.")]
        private static void Command_StopEvent(ConCommandArgs args)
        {
            if (!NetworkServer.active)
            {
                Log.LogError($"Events can only be stopped by the host.");
                return;
            }
            string name = args.GetArgString(0);
            if(name.Equals("all", StringComparison.InvariantCultureIgnoreCase))
            {
                instance.StopAllActiveEvents();
            } 
            else
            {
                var ev = instance.FindEventByName(name);
                if (ev != null)
                {
                    if (instance.m_activeEvents.Contains(ev))
                    {
                        ev.Stop();
                        instance.m_activeEvents.Remove(ev);
                        Log.LogInfo($"Event {ev.GetEventConfigName()} stopped.");
                    }
                    else
                    {
                        Log.LogInfo($"Event {ev.GetEventConfigName()} is not active.");
                    }
                }
                else
                {
                    Log.LogError($"Can not find event named {name}.");
                }
            }
        }


        public static void WriteRandomEventsMarkdownFile()
        {
#if DEBUG
            string filePath = $"{RandomEvents.PluginName}.md";

            // This will write it next to the RiskOfRain2.exe file
            using (System.IO.StreamWriter writer = new System.IO.StreamWriter(filePath))
            {
                writer.WriteLine("## Random Events");
                writer.WriteLine("| Event Name | Announcement | Description | Condition |");
                writer.WriteLine("|------------|--------------|-------------|-----------|");
                foreach (var ev in RandomEvents.instance.m_allEventsIncludingNotLoadedOnes)
                {
                    writer.WriteLine($"| {ev.GetEventConfigName()} | {ev.GetAnnouncement()} | {ev.GetDescription()} | {ev.GetConditionDescription()} |");
                }
            }
#endif
        }

        private AbstractEvent FindEventByName(string eventName)
        {
            foreach (var ev in m_loadedEvents)
            {
                if (ev.GetEventConfigName().Equals(eventName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return ev;
                }
            }
            return null;
        }


        private static int currentMonsterIndex = 0;
        [ConCommand(commandName = "transform_next_monster", flags = ConVarFlags.ExecuteOnServer, helpText = "Transforms into the next body from the body list.")]
        private static void Command_TransformNextMonster(ConCommandArgs args)
        {
            if (!NetworkServer.active)
            {
                Log.LogError($"Only the host can transform players into monsters.");
                return;
            }
            if(args.Count == 1) { 
                string index = args.GetArgString(0);
                if (index != null)
                {
                    currentMonsterIndex = int.Parse(index);
                }
            }

            var ev = instance.FindEventByName("MonsterTransform");
            if (ev != null)
            {
                var monsterTransformEvent = (EventMonsterTransform)ev;
                if (currentMonsterIndex >= monsterTransformEvent.validBodyPrefabs.Count)
                {
                    currentMonsterIndex = 0;
                }
                var target = monsterTransformEvent.validBodyPrefabs[currentMonsterIndex];
                Log.LogInfo($"Transform into {currentMonsterIndex}: {target.name}");
                foreach(var pc in PlayerCharacterMasterController.instances)
                {
                    if(pc.isConnected)
                    {
                        monsterTransformEvent.TransformInto(pc, target);
                    }
                }
            }
            currentMonsterIndex++;
        }
    }
}
