using BepInEx;
using R2API.Utils;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    [BepInDependency("com.KingEnderBrine.InLobbyConfig", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.funkfrog_sipondo.sharesuite", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    [BepInDependency(PerClientSync.PerClientSyncPlugin.PluginGUID)]
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
        private readonly Dictionary<InfiniteTowerWaveCategory, InfiniteTowerWaveCategory.WeightedWave[]> originalWavePrefabs = new Dictionary<InfiniteTowerWaveCategory, InfiniteTowerWaveCategory.WeightedWave[]>();

        public void Awake()
        {
            PInfo = Info;
            instance = this;
            Log.Init(Logger);

            On.RoR2.Console.AutoComplete.ctor += AutoComplete_ctor;

            BodyCatalog.availability.CallWhenAvailable(() =>
            {
                TemporaryInventory.Hook();
                TemporaryEquipment.Hook();
                HereticTransformation.Hook();
                EventLoot.Hook();
                Slowdown.Hook();
                MonsterPool.Hook();
                ScaledMonsterAi.Hook();
                OpponentMarks.Hook();

                ModConfig.Init();
                // all of them exist before their configs, whose descriptions name the events they rule out
                var events = new List<AbstractEvent>
                {
                    new EventBlightedCorpses(),
                    new EventBouncy(),
                    new EventBulletHell(),
                    new EventCasinoItem(),
                    new EventChestMimics(),
                    new EventCursedSpoils(),
                    new EventDeathmatch(),
                    new EventDroneEscort(),
                    new EventDroneUprising(),
                    new EventEliteSurge(),
                    new EventEquipmentOnly(),
                    new EventExperimentalEquipment(),
                    new EventExplodingCorpses(),
                    new EventFallingEnemies(),
                    new EventFocusFire(),
                    new EventFuelArray(),
                    new EventGhosts(),
                    new EventGiantBalloons(),
                    new EventHaste(),
                    new EventHotPotato(),
                    new EventHunted(),
                    new EventHuggingTime(),
                    new EventIndirectDamage(),
                    new EventInventoryRoulette(),
                    new EventItemKits(),
                    new EventItemRush(),
                    new EventItemZone(),
                    new EventLavaFloor(),
                    new EventLinkedSouls(),
                    new EventLynxPups(),
                    new EventMirroredEnemies(),
                    new EventMitosis(),
                    new EventMonsterTransform(),
                    new EventNeutroniumWeights(),
                    new EventOssification(),
                    new EventRandomArtifact(),
                    new EventRandomTeams(),
                    new EventRedLightGreenLight(),
                    new EventReducedGravity(),
                    new EventRitualHelmets(),
                    new EventSecondWind(),
                    new EventShielded(),
                    new EventSkillsOnly(),
                    new EventSlippery(),
                    new EventSmallArena(),
                    new EventStealthMission(),
                    new EventStrongEnemies(),
                    new EventSupplyDrop(),
                    new EventSurvivorSwap(),
                    new EventSwarmQueen(),
                    new EventTagKing(),
                    new EventTagYoureIt(),
                    new EventTheAlpha(),
                    new EventThieves(),
                    new EventTimeWarp(),
                    new EventUmbraAmbush(),
                    new EventUnitedWeStand(),
                    new EventVampires(),
                    new EventVengefulSpirits(),
                    new EventWeakEnemies(),
                    new EventZombies(),
                };
                foreach (var ev in events)
                {
                    InitAndHookEvent(ev, events);
                }
                ModConfig.InLobbyConfig();
                DebugToolkitAutoComplete.Register(m_loadedEvents);
            });

            someMonster = Addressables.LoadAssetAsync<CharacterSpawnCard>("RoR2/DLC1/MajorAndMinorConstruct/cscMinorConstruct.asset");
            someMonster2 = Addressables.LoadAssetAsync<CharacterSpawnCard>("RoR2/Base/Wisp/cscLesserWisp.asset");
        }
        private AbstractEvent InitAndHookEvent(AbstractEvent ev, List<AbstractEvent> allEvents)
        {
            ev.Id = m_allEventsIncludingNotLoadedOnes.Count + 1;
            // an event that fails to load leaves out only itself
            try
            {
                if (ev.LoadCondition())
                {
                    ev.SetupConfig(instance.Config, allEvents);
                    ev.Preload();
                    ev.Hook();
                    m_loadedEvents.Add(ev);
                }
            }
            catch (Exception e)
            {
                Log.LogError($"Event {ev.GetEventConfigName()} failed to load: {e}");
            }
            m_allEventsIncludingNotLoadedOnes.Add(ev);
            return ev;
        }

        private AbstractEvent ChooseRandomEvent(List<AbstractEvent> eventCandidates, List<AbstractEvent> activeEvents)
        {
            WeightedSelection<AbstractEvent> weightedSelection = new WeightedSelection<AbstractEvent>();
            foreach (var ev in eventCandidates)
            {
                // a weighted selection can't pick from weights of 0
                if (!activeEvents.Contains(ev) && ev.IsEnabled() && ev.GetWeight() > 0f && ev.Condition(activeEvents)
                    && activeEvents.All(other => ev.IsCompatibleWith(other) && other.IsCompatibleWith(ev)))
                {
                    weightedSelection.AddChoice(ev, ev.GetWeight());
                }
            }
            if (weightedSelection.Count == 0)
                return null;
            // below 1, unlike UnityEngine.Random.value, which the selection can't evaluate
            return weightedSelection.Evaluate(RoR2Application.rng.nextNormalizedFloat);
        }

        private void PrepareAndAnnounceUpcomingEvents(float announceDelay)
        {
#if DEBUG
            Config.Reload();
#endif
            if (ModConfig.Enabled.Value && NetworkServer.active && (SceneCatalog.GetSceneDefForCurrentScene().sceneType == SceneType.Stage || SceneCatalog.GetSceneDefForCurrentScene().cachedName == "arena"))
            {
                if (currentEventProbability >= 1.0f || RoR2Application.rng.nextNormalizedFloat < currentEventProbability)
                {
                    currentEventProbability = ModConfig.EventProbability.Value;
                    WeightedSelection<int> weightedSelection = new WeightedSelection<int>();
                    weightedSelection.AddChoice(1, Mathf.Max(0f, ModConfig.SingleEventWeight.Value));
                    weightedSelection.AddChoice(2, Mathf.Max(0f, ModConfig.DoubleEventWeight.Value));
                    weightedSelection.AddChoice(3, Mathf.Max(0f, ModConfig.TripleEventWeight.Value));
                    // no events if all three weights are 0
                    var eventAmount = weightedSelection.GetTotalWeight() > 0f ? weightedSelection.Evaluate(RoR2Application.rng.nextNormalizedFloat) : 0;

                    for (int i = 0; i < eventAmount; i++)
                    {
                        var ev = ChooseRandomEvent(m_loadedEvents, m_upcomingEvents);
                        if (ev != null)
                        {
                            m_upcomingEvents.Add(ev);
                            ev.Prepare();
                        }
                    }

                    // none of the events may qualify
                    if (m_upcomingEvents.Count > 0)
                    {
                        instance.StartCoroutine(DelayChatSendAnnouncement(announceDelay, new List<AbstractEvent>(m_upcomingEvents)));
                    }
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
            Stage.onServerStageComplete += Stage_onServerStageComplete;
            Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
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
            Stage.onServerStageComplete -= Stage_onServerStageComplete;
            Run.onRunDestroyGlobal -= Run_onRunDestroyGlobal;
        }

        private void Run_OnFixedUpdate(On.RoR2.Run.orig_OnFixedUpdate orig, Run self)
        {
            orig(self);
            if(!ModConfig.Enabled.Value || Run.instance is InfiniteTowerRun infiniteTowerRun || !NetworkServer.active)
            {
                return;
            }
            // no events between the end of a stage and the start of the next one
            if (Stage.instance == null || Stage.instance.completed)
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
                // the time since the events started, which can be longer than the Event Timer
                var timer = stopWatch - m_eventStartTime;
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
            // events still going when the last run ended were undone with it
            m_activeEvents.Clear();
            m_upcomingEvents.Clear();
            TemporaryEquipment.ClearSnapshots();
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

        private void Stage_onServerStageComplete(Stage stage)
        {
            // events end with their stage, so nothing they changed (e.g. artifacts or a player's body) carries over into the next stage or ProperSave's save
            StopAllActiveEvents();
            m_upcomingEvents.Clear();
        }

        // leaving the run, e.g. for the lobby, where the host is still the server and the events would keep going
        private void Run_onRunDestroyGlobal(Run run)
        {
            StopAllActiveEvents();
            m_upcomingEvents.Clear();
        }

        private void InfiniteTowerRun_Start(On.RoR2.InfiniteTowerRun.orig_Start orig, InfiniteTowerRun self)
        {
            orig(self);
            WriteRandomEventsMarkdownFile();
            // the wave categories are assets, so waves removed for one run are put back before the next
            foreach (var (waveCategory, wavePrefabs) in originalWavePrefabs)
            {
                waveCategory.wavePrefabs = wavePrefabs;
            }
            originalWavePrefabs.Clear();
            // remove artifact waves, which would clash with RandomArtifact's artifacts
            var randomArtifact = m_loadedEvents.Find(ev => ev is EventRandomArtifact);
            if (ModConfig.Enabled.Value && randomArtifact != null && randomArtifact.IsEnabled())
            {
                foreach (var waveCategory in self.waveCategories)
                {
                    if (originalWavePrefabs.ContainsKey(waveCategory))
                    {
                        continue;
                    }
                    originalWavePrefabs.Add(waveCategory, waveCategory.wavePrefabs);
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

        // for an event that keeps its own time, e.g. one that stops the run timer the events go by
        public void EndActiveEvents()
        {
            if (m_activeEvents.Count > 0)
            {
                m_announcementGiven = false;
                ChatHelper.AnnounceEventConclusion();
                StopAllActiveEvents();
            }
        }

        private void StopAllActiveEvents()
        {
            // an event that fails to stop must not keep the others running or leave the list filled
            foreach (var ev in m_activeEvents)
            {
                try
                {
                    ev.Stop();
                }
                catch (Exception e)
                {
                    Log.LogError($"Event {ev.GetEventConfigName()} failed to stop: {e}");
                }
            }
            m_activeEvents.Clear();
        }

        private void StartUpcomingEvents()
        {
            var events = new List<AbstractEvent>(m_upcomingEvents);
            m_upcomingEvents.Clear();
            m_activeEvents.AddRange(events);
            // an event that fails to start must not keep the others from starting or leave the queue filled
            foreach (var ev in events)
            {
                try
                {
                    ev.Start(m_activeEvents);
                }
                catch (Exception e)
                {
                    Log.LogError($"Event {ev.GetEventConfigName()} failed to start: {e}");
                    try
                    {
                        ev.Stop();
                    }
                    catch (Exception stopException)
                    {
                        Log.LogError($"Event {ev.GetEventConfigName()} failed to stop: {stopException}");
                    }
                    m_activeEvents.Remove(ev);
                }
            }
        }

        private void AwaitingActivation_OnEnter(On.EntityStates.InfiniteTowerSafeWard.AwaitingActivation.orig_OnEnter orig, EntityStates.InfiniteTowerSafeWard.AwaitingActivation self)
        {
            orig(self);
            PrepareAndAnnounceUpcomingEvents(1.0f);
        }

        private void InfiniteTowerRun_OnWaveAllEnemiesDefeatedServer(On.RoR2.InfiniteTowerRun.orig_OnWaveAllEnemiesDefeatedServer orig, InfiniteTowerRun self, InfiniteTowerWaveController wc)
        {
            if (ModConfig.Enabled.Value && NetworkServer.active)
            {
                // before the wave's end revives dead players, so they come back in their own body
                StopAllActiveEvents();
            }
            orig(self, wc);
            if (ModConfig.Enabled.Value && NetworkServer.active)
            {
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

        // The sound of the umbra effect, which players don't hear from its carrier far away
        private const string UmbraSound = "Play_elite_haunt_spawn";

        // The umbra effect on every player's screen belongs to bodies that carry the umbra item, so a hidden body far
        // above the players carries it for a moment. What the effect does to the screen reaches every player, wherever it is.
        private CharacterMaster SpawnUmbraEffectCarrier()
        {
            var position = new Vector3(0f, 1000f, 0f);
            var bodies = Helper.GetLivingPlayerBodies();
            if (bodies.Count > 0)
            {
                position = bodies[0].corePosition;
                position.y = bodies.Max(body => body.corePosition.y) + 1000f;
            }
            DirectorPlacementRule directorPlacementRule = new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Direct,
                position = position
            };
            SpawnCard spawnCard = someMonster2.WaitForCompletion();
            // the application's generator, so the run's seeded one gives the same results as without the mod
            DirectorSpawnRequest directorSpawnRequest = new DirectorSpawnRequest(spawnCard, directorPlacementRule, RoR2Application.rng);
            directorSpawnRequest.teamIndexOverride = TeamIndex.None;
            directorSpawnRequest.ignoreTeamMemberLimit = true;
            directorSpawnRequest.onSpawnedServer = (Action<SpawnCard.SpawnResult>)Delegate.Combine(directorSpawnRequest.onSpawnedServer, (Action<SpawnCard.SpawnResult>)delegate (SpawnCard.SpawnResult result)
            {
                var carrier = result.spawnedInstance.GetComponent<CharacterMaster>();
                carrier.inventory.GiveItemPermanent(RoR2Content.Items.InvadingDoppelganger);
                // far above the map, where the game would teleport a body with that item back near the players;
                // looked up by name, since the item is newer than the game libraries the mod is built against
                var ignoreMapZones = ItemCatalog.FindItemIndex("IgnoreMapZonesUntilRemoval");
                if (ignoreMapZones != ItemIndex.None)
                {
                    carrier.inventory.GiveItemPermanent(ignoreMapZones);
                }
                // it only carries the item; its AI is switched on when its body starts, so it's switched off after that
                carrier.onBodyStart += body =>
                {
                    foreach (var ai in carrier.AiComponents)
                    {
                        ai.enabled = false;
                    }
                };
            });
            var result = spawnCard.DoSpawn(directorSpawnRequest.placementRule.position, UnityEngine.Quaternion.identity, directorSpawnRequest).spawnedInstance;
            return result != null ? result.GetComponent<CharacterMaster>() : null;
        }

        // One pulse of the umbra effect. The carrier is removed without dying: an umbra that dies drops an item from
        // Artifact of Vengeance's drop table, which the game handles in every run.
        IEnumerator ShowUmbraEffect()
        {
            var carrier = SpawnUmbraEffectCarrier();
            yield return new WaitForSeconds(0.5f);
            if (carrier != null)
            {
                // without the item the effect fades out, as it does in the game
                carrier.inventory.RemoveItemPermanent(RoR2Content.Items.InvadingDoppelganger);
                var body = carrier.GetBodyObject();
                if (body != null)
                {
                    GameObject.Destroy(body, 0.3f);
                }
                GameObject.Destroy(carrier.gameObject, 0.3f);
            }
        }

        IEnumerator DelayChatSendAnnouncement(float time, List<AbstractEvent> events)
        {
            yield return new WaitForSeconds(time);
            if (events.Count == 1)
            {
                ChatHelper.AnnounceEvent(UmbraSound);
            }
            else if (events.Count == 2)
            {
                ChatHelper.AnnounceDoubleEvent(UmbraSound);
            }
            else
            {
                ChatHelper.AnnounceTripleEvent(UmbraSound);
            }
            yield return ShowUmbraEffect();
            yield return new WaitForSeconds(0.5f);
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
            foreach (var ev in events)
            {
                ChatHelper.SendPopup($"<style=cWorldEvent>{ev.GetAnnouncement()}</style>", ev.GetPopupDescription());
            }
            if (events.Count >= 2)
            {
                // the next umbra effect starts with the events' names
                ChatHelper.SendWithSound($"<size=26px>{announcement}</size>", UmbraSound);
                yield return ShowUmbraEffect();
                if (events.Count >= 3)
                {
                    yield return new WaitForSeconds(0.5f);
                    yield return ShowUmbraEffect();
                }
            }
            else
            {
                ChatHelper.Send($"<size=26px>{announcement}</size>");
            }
        }

        [ConCommand(commandName = "start_event", flags = ConVarFlags.ExecuteOnServer, helpText = "Starts the given event, over again if it's already going. Provide its internal name or its number from list_events.")]
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
                // One already going starts over, a frame later: what its end removes, e.g. the components on players,
                // only goes at the end of the frame, and starting before would find them still there.
                if (instance.m_activeEvents.Contains(ev))
                {
                    ev.Stop();
                    instance.m_activeEvents.Remove(ev);
                    Log.LogInfo($"Event {ev.GetEventConfigName()} stopped.");
                    instance.StartCoroutine(StartNextFrame(ev));
                    return;
                }
                StartFromCommand(ev);
            }
            else
            {
                Log.LogError($"Can not find event named {name}.");
            }
        }

        private static IEnumerator StartNextFrame(AbstractEvent ev)
        {
            yield return null;
            if (NetworkServer.active && !instance.m_activeEvents.Contains(ev))
            {
                StartFromCommand(ev);
            }
        }

        private static void StartFromCommand(AbstractEvent ev)
        {
            ev.Prepare();
            instance.m_activeEvents.Add(ev);
            ev.Start(instance.m_activeEvents);
            Log.LogInfo($"Event {ev.GetEventConfigName()} started.");
            ChatHelper.Send($"<size=28px><style=cWorldEvent>{ev.GetAnnouncement()}</style></size>");
        }

        [ConCommand(commandName = "queue_event", flags = ConVarFlags.ExecuteOnServer, helpText = "Queues the given event. Provide its internal name or its number from list_events.")]
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

        [ConCommand(commandName = "stop_event", flags = ConVarFlags.ExecuteOnServer, helpText = "Stop the given event, by its internal name or its number from list_events. Keyword all for all currently active events.")]
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
                writer.WriteLine("| Announcement | Description | Condition | Internal Name |");
                writer.WriteLine("|--------------|-------------|-----------|---------------|");
                foreach (var ev in RandomEvents.instance.m_allEventsIncludingNotLoadedOnes)
                {
                    writer.WriteLine($"| {ev.GetAnnouncement()} | {ev.GetDescription()} | {ev.GetFullConditionDescription(RandomEvents.instance.m_allEventsIncludingNotLoadedOnes)} | {ev.GetEventConfigName()} |");
                }
            }
#endif
        }

        // by its internal name or its number
        private AbstractEvent FindEventByName(string eventName)
        {
            if (int.TryParse(eventName, out var id))
            {
                return m_loadedEvents.FirstOrDefault(ev => ev.Id == id);
            }
            foreach (var ev in m_loadedEvents)
            {
                if (ev.GetEventConfigName().Equals(eventName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return ev;
                }
            }
            return null;
        }

        [ConCommand(commandName = "list_events", flags = ConVarFlags.None, helpText = "Lists the events with their numbers, and which are active or queued.")]
        private static void Command_ListEvents(ConCommandArgs args)
        {
            foreach (var ev in instance.m_loadedEvents)
            {
                var state = instance.m_activeEvents.Contains(ev) ? " (active)" : instance.m_upcomingEvents.Contains(ev) ? " (queued)" : "";
                Debug.Log($"{ev.Id}: {ev.GetEventConfigName()}{state}");
            }
        }

        // the console suggests the event commands with every event's name
        private static void AutoComplete_ctor(On.RoR2.Console.AutoComplete.orig_ctor orig, RoR2.Console.AutoComplete self, RoR2.Console console)
        {
            orig(self, console);
            if (instance == null)
            {
                return;
            }
            var strings = self.GetSearchableStrings;
            foreach (var command in new[] { "start_event", "queue_event", "stop_event" })
            {
                foreach (var ev in instance.m_loadedEvents)
                {
                    strings.Add($"{command} {ev.GetEventConfigName()}");
                }
            }
            strings.Add("stop_event all");
            strings.Sort();
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
