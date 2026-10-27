using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    // The monsters are removed and everything waits: their directors, the run timer and, in Simulacrum, the wave and the
    // void fog. Meanwhile the players fight each other, reviving whenever they die, and score a point for each kill.
    //
    // Up to three players, each fights alone, on a team of their own; from four on, they fight in two teams. Each player
    // is on their team along with what they own, e.g. drones and turrets, so that everything in every player's game
    // takes the others for enemies: their attacks, their homing missiles, their allies' targets. Each player sees the
    // players they fight marked, see OpponentMarks.
    //
    // So that nobody dies in an instant, however strong their items, the damage players take from other players weakens
    // the more health they recently lost to them: it approaches a share of their health per second without reaching it,
    // also within one hit, measured on the health they actually lose, item effects included. What they lost drains at that
    // share per second, but can add up to a larger share, so that a rare big hit, e.g. a supercharged Railgunner shot,
    // which is hard to land, counts for a lot, while steady damage stays at the share per second. Healing, regeneration
    // too, and barrier gain weaken the same way, so that nobody outheals the others' damage. On top of that, players deal
    // each other only a share of their items' damage. So that nobody is stunned to death, stuns, freezes, shocks and roots
    // only slow players by 80% for as long. Items that break, e.g. a Delicate Watch, and the players' drones that are
    // destroyed are restored afterwards, and what the fight left behind, e.g. a burn or a Warped Echo's delayed damage,
    // doesn't hurt anyone anymore.
    public class EventDeathmatch : AbstractEvent
    {
        private const float ReviveDelay = 3f;
        // how long after the event what the fight left behind can't hurt anyone, e.g. a Warped Echo's delayed damage or
        // a lightning orb still on its way
        private const float LeftoverTime = 10f;
        private float leftoverEnd = float.NegativeInfinity;
        // a death without a killer, e.g. a fall after a knock back, counts for the last player who hit within this time
        private const float KillCreditTime = 5f;
        // how long an immobilize, which has no duration of its own, slows instead
        private const float ImmobilizeSlowDuration = 2f;
        private ConfigEntry<float> HealthLossLimit;
        private ConfigEntry<float> HealthLossBurstLimit;
        private ConfigEntry<float> ItemHealthLossLimit;
        private ConfigEntry<float> ItemHealthLossBurstLimit;
        private ConfigEntry<float> HealingLimit;
        private ConfigEntry<float> BarrierLimit;
        private ConfigEntry<float> ItemDamageMultiplier;
        private ConfigEntry<float> BigHitDamagePercentage;
        private ConfigEntry<float> BigHitBurstLimit;
        private ConfigEntry<float> ItemHealthShare;
        private readonly Dictionary<CharacterMaster, int> scores = new Dictionary<CharacterMaster, int>();
        private readonly Dictionary<CharacterMaster, (CharacterMaster attacker, float time)> lastHits = new Dictionary<CharacterMaster, (CharacterMaster attacker, float time)>();
        // the share of their health each player recently lost to other players, which drains at the limit's rate
        private readonly Dictionary<CharacterBody, (float time, float lost)> healthLost = new Dictionary<CharacterBody, (float time, float lost)>();
        // and the share they recently lost to the other players' items, all of them together
        private readonly Dictionary<CharacterBody, (float time, float lost)> itemHealthLost = new Dictionary<CharacterBody, (float time, float lost)>();
        // and the share they recently healed, and gained as barrier
        private readonly Dictionary<HealthComponent, (float time, float healed)> healthHealed = new Dictionary<HealthComponent, (float time, float healed)>();
        private readonly Dictionary<HealthComponent, (float time, float gained)> barrierGained = new Dictionary<HealthComponent, (float time, float gained)>();
        // the hits between players being dealt, with what the victim recently lost
        private readonly Dictionary<HealthComponent, (float lost, float itemLost, bool isItemDamage, float burst)> hits = new Dictionary<HealthComponent, (float lost, float itemLost, bool isItemDamage, float burst)>();
        // the players' items that broke, how many, and how many broken ones they became
        private readonly List<(Inventory inventory, ItemIndex item, int count, ItemIndex broken, int brokenCount)> brokenItems = new List<(Inventory inventory, ItemIndex item, int count, ItemIndex broken, int brokenCount)>();
        // the players' drones that were destroyed: whose they were, what they were and where
        private readonly List<LostDrone> lostDrones = new List<LostDrone>();
        private readonly HashSet<CharacterBody> lostDroneBodies = new HashSet<CharacterBody>();
        // who placed each Warbanner, which nothing else tells; also those from before the event
        private readonly Dictionary<TeamFilter, CharacterMaster> bannerOwners = new Dictionary<TeamFilter, CharacterMaster>();

        private class LostDrone
        {
            public CharacterMaster owner;
            public GameObject masterPrefab;
            // its items, the upgrades of the Alloyed Collective included
            public Dictionary<ItemIndex, int> items = new Dictionary<ItemIndex, int>();
            public EquipmentIndex equipment;
            public Vector3 position;
        }
        private float timeLeft;
        // the teams the players fight on, the players' own first; not the monsters', whose deaths set off artifacts, e.g.
        // Soul's wisps
        private static readonly TeamIndex[] Teams = { TeamIndex.Player, TeamIndex.Lunar, TeamIndex.Void };
        private const int MaxPlayersAlone = 3;
        // whether the players fight in two teams rather than each alone, chosen when the event is announced
        private bool teamMode;
        private bool modeChosen;
        private readonly Dictionary<CharacterMaster, TeamIndex> playerTeams = new Dictionary<CharacterMaster, TeamIndex>();
        // the team each player's character or ally was on before, and the level of each team the players borrowed
        private readonly Dictionary<CharacterMaster, TeamIndex> originalTeams = new Dictionary<CharacterMaster, TeamIndex>();
        private readonly Dictionary<TeamIndex, uint> originalTeamLevels = new Dictionary<TeamIndex, uint>();

        public override EventTags Tags => EventTags.PlayerMarker | EventTags.Revives;
        // The events about monsters alone do nothing while they are gone, and the others change how players hurt each other
        // or come back, or hide them from each other
        public override EventTags ExcludedTags => EventTags.Monsters | EventTags.PlayerMarker | EventTags.Revives | EventTags.PlayerDamage | EventTags.HiddenPlayers;
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            var teleporter = TeleporterInteraction.instance;
            return Helper.GetLivingPlayerBodies().Count >= 2 && !Helper.IsSimulacrumBossWaveNext()
                && (teleporter == null || !(teleporter.isIdleToCharging || teleporter.isCharging));
        }
        public override string GetEventConfigName()
        {
            return "Deathmatch";
        }
        public override string GetAnnouncement()
        {
            if (!modeChosen)
            {
                ChooseMode();
            }
            return Language.GetStringFormatted(teamMode ? "ANNOUNCE_EVENT_TEAM_DEATHMATCH" : "ANNOUNCE_EVENT_DEATHMATCH");
        }
        public override string GetDescription()
        {
            return "The monsters are removed and the run timer stops while the players fight each other: alone with up to 3 players, otherwise in two teams. Players revive when they die and score a point for each kill. Damage between players is reduced. Afterwards broken items and destroyed drones come back, the winners get a green item, everyone is healed and the monsters return.";
        }
        public override string GetPopupDescription()
        {
            return "Fight the other players, alone or in teams! You revive when you die, and each kill scores a point. Items deal other players 30% damage. The winner gets a green item.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive, and not during the teleporter event or a Simulacrum boss wave.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            HealthLossLimit = config.Bind(GetEventConfigName(), "Health Loss Limit Per Second", 0.2f, "The share of their health per second, their health from their survivor and level plus the Item Health Share of what items add, that the damage players take from other players approaches, the more slowly the closer it gets, but never reaches.");
            HealthLossBurstLimit = config.Bind(GetEventConfigName(), "Health Loss Burst Limit", 0.5f, "The share of their health, counted the same way, that what players recently lost to other players approaches, the more slowly the closer it gets, but never reaches. It drains by the limit per second, so a big hit on a player who wasn't hurt for a while takes up to this much, while steady damage stays below the limit per second.");
            ItemHealthLossLimit = config.Bind(GetEventConfigName(), "Item Health Loss Limit Per Second", 0.1f, "The share of their health per second, counted the same way, that the damage players take from other players' items, all of them together, approaches, the more slowly the closer it gets, but never reaches. It comes on top of the other limits.");
            ItemHealthLossBurstLimit = config.Bind(GetEventConfigName(), "Item Health Loss Burst Limit", 0.25f, "The share of their health, counted the same way, that what players recently lost to other players' items, all of them together, approaches, the more slowly the closer it gets, but never reaches. It drains by the item limit per second.");
            HealingLimit = config.Bind(GetEventConfigName(), "Healing Limit Per Second", 0.2f, "The share of their health per second that the players' healing, regeneration included, approaches, the more slowly the closer it gets, but never reaches.");
            BarrierLimit = config.Bind(GetEventConfigName(), "Barrier Limit Per Second", 0.2f, "The share of their health per second that the barrier players gain approaches, the more slowly the closer it gets, but never reaches.");
            ItemDamageMultiplier = config.Bind(GetEventConfigName(), "Item Damage Multiplier", 0.3f, "The share of their items' damage, e.g. procs and damage over time, players deal each other, before it weakens. Skills and equipment deal all of theirs.");
            ItemHealthShare = config.Bind(GetEventConfigName(), "Item Health Share", 0.5f, "How much of the health and shield players have from items counts toward the health the limits on health loss go by, on top of their health from their survivor and level. 0 = none, 1 = all.");
            BigHitDamagePercentage = config.Bind(GetEventConfigName(), "Big Hit Damage Percentage", 4000f, "A hit of a skill or equipment with at least this much of the attacker's damage, e.g. the Railgunner's supercharged shot or the mini nuke, goes by the Big Hit Burst Limit instead of the Health Loss Burst Limit. From a quarter of it on, the burst limit rises toward that evenly. Crits and other multipliers don't count toward it. 0 = off.");
            BigHitBurstLimit = config.Bind(GetEventConfigName(), "Big Hit Burst Limit", 1f, "The Health Loss Burst Limit for big hits, e.g. 1 = a big hit on a player who wasn't hurt for a while leaves them very little, but never kills them at once.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            IL.RoR2.HealthComponent.TakeDamageProcess += HealthComponent_TakeDamageProcess;
            On.RoR2.HealthComponent.Heal += HealthComponent_Heal;
            On.RoR2.HealthComponent.AddBarrier += HealthComponent_AddBarrier;
            On.RoR2.CharacterMaster.TryReviveOnBodyDeath += CharacterMaster_TryReviveOnBodyDeath;
            GlobalEventManager.onCharacterDeathServer += GlobalEventManager_onCharacterDeathServer;
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
            // everything that goes on with the monsters waits
            On.RoR2.CombatDirector.FixedUpdate += CombatDirector_FixedUpdate;
            On.RoR2.InfiniteTowerWaveController.FixedUpdate += InfiniteTowerWaveController_FixedUpdate;
            On.RoR2.FogDamageController.FixedUpdate += FogDamageController_FixedUpdate;
            On.RoR2.Run.ShouldUpdateRunStopwatch += Run_ShouldUpdateRunStopwatch;
            On.RoR2.InfiniteTowerRun.ShouldUpdateRunStopwatch += InfiniteTowerRun_ShouldUpdateRunStopwatch;
            On.RoR2.TeleporterInteraction.GetInteractability += TeleporterInteraction_GetInteractability;
            // what would lock a player in place slows them instead; the host decides them all
            On.RoR2.SetStateOnHurt.SetStun += SetStateOnHurt_SetStun;
            On.RoR2.SetStateOnHurt.OverrideStun += SetStateOnHurt_OverrideStun;
            On.RoR2.SetStateOnHurt.SetFrozen += SetStateOnHurt_SetFrozen;
            On.RoR2.SetStateOnHurt.SetShock += SetStateOnHurt_SetShock;
            On.RoR2.SetStateOnHurt.SetImmobilize += SetStateOnHurt_SetImmobilize;
            On.RoR2.CharacterBody.AddTimedBuff_BuffDef_float += CharacterBody_AddTimedBuff_BuffDef_float;
            On.RoR2.CharacterBody.AddTimedBuff_BuffDef_float_int += CharacterBody_AddTimedBuff_BuffDef_float_int;
            On.RoR2.Inventory.ItemTransformation.TryTransform += ItemTransformation_TryTransform;
            On.RoR2.Artifacts.SacrificeArtifactManager.OnServerCharacterDeath += SacrificeArtifactManager_OnServerCharacterDeath;
            On.EntityStates.Drone.DeathState.OnImpactServer += DeathState_OnImpactServer;
            On.RoR2.SetStateOnHurt.OnTakeDamageServer += SetStateOnHurt_OnTakeDamageServer;
            On.RoR2.Items.WardOnLevelManager.OnCharacterLevelUp += WardOnLevelManager_OnCharacterLevelUp;
        }

        // Survivors can't be stunned or shocked at all, so a stun or shock hitting a player, e.g. the Captain's shocking
        // beacon's, slows them like the others
        private void SetStateOnHurt_OnTakeDamageServer(On.RoR2.SetStateOnHurt.orig_OnTakeDamageServer orig, SetStateOnHurt self, DamageReport damageReport)
        {
            orig(self, damageReport);
            var damageInfo = damageReport.damageInfo;
            if (!Pausing || self.canBeStunned || damageInfo.procCoefficient < Mathf.Epsilon || !self.TryGetComponent(out CharacterBody body) || !body.isPlayerControlled)
            {
                return;
            }
            if ((ulong)(damageInfo.damageType & DamageType.Shock5s) != 0L)
            {
                body.AddTimedBuff(RoR2Content.Buffs.Slow80, 5f * damageInfo.procCoefficient);
            }
            else if ((ulong)(damageInfo.damageType & DamageType.Stun1s) != 0L)
            {
                body.AddTimedBuff(RoR2Content.Buffs.Slow80, damageInfo.procCoefficient);
            }
        }

        // The game places a Warbanner where the character that leveled up stands
        private void WardOnLevelManager_OnCharacterLevelUp(On.RoR2.Items.WardOnLevelManager.orig_OnCharacterLevelUp orig, CharacterBody characterBody)
        {
            orig(characterBody);
            if (!NetworkServer.active || characterBody == null || characterBody.master == null)
            {
                return;
            }
            foreach (var ward in Object.FindObjectsOfType<BuffWard>())
            {
                if (ward.buffDef == RoR2Content.Buffs.Warbanner && ward.transform.position == characterBody.transform.position
                    && ward.TryGetComponent(out TeamFilter teamFilter) && !bannerOwners.ContainsKey(teamFilter))
                {
                    bannerOwners[teamFilter] = characterBody.master;
                }
            }
        }

        // What players leave in the world fights for their team too, e.g. the Captain's beacons, the Engineer's mines and
        // Warbanners, so it follows its owner's team when the event starts and ends
        private void UpdateOwnedTeams()
        {
            foreach (var destroyed in bannerOwners.Keys.Where(teamFilter => !teamFilter).ToList())
            {
                bannerOwners.Remove(destroyed);
            }
            foreach (var teamFilter in Object.FindObjectsOfType<TeamFilter>())
            {
                var owner = GetOwnerMaster(teamFilter);
                if (owner == null || GetPlayer(owner) == null)
                {
                    continue;
                }
                SetTeam(teamFilter, owner.teamIndex);
                // e.g. Forgive Me Please's doll, which has a team of its own as well
                if (teamFilter.TryGetComponent(out TeamComponent teamComponent) && (!teamFilter.TryGetComponent(out CharacterBody body) || body.master == null)
                    && teamComponent.teamIndex != owner.teamIndex)
                {
                    teamComponent.teamIndex = owner.teamIndex;
                }
                // the healing beacon's zone is an object of its own
                if (teamFilter.TryGetComponent(out EntityStateMachine stateMachine) && stateMachine.state is EntityStates.CaptainSupplyDrop.HealZoneMainState healZone
                    && healZone.healZoneInstance && healZone.healZoneInstance.TryGetComponent(out TeamFilter zoneTeamFilter))
                {
                    SetTeam(zoneTeamFilter, owner.teamIndex);
                }
            }
        }

        private static void SetTeam(TeamFilter teamFilter, TeamIndex team)
        {
            if (teamFilter.teamIndex != team)
            {
                teamFilter.teamIndex = team;
            }
        }

        private CharacterMaster GetOwnerMaster(TeamFilter teamFilter)
        {
            var gameObject = teamFilter.gameObject;
            if (gameObject.TryGetComponent(out Deployable deployable) && deployable.ownerMaster != null)
            {
                return deployable.ownerMaster;
            }
            if (bannerOwners.TryGetValue(teamFilter, out var bannerOwner))
            {
                return bannerOwner;
            }
            if (gameObject.TryGetComponent(out NetworkedBodyAttachment attachment) && attachment.attachedBody != null)
            {
                return attachment.attachedBody.master;
            }
            var ownerObject = gameObject.TryGetComponent(out GenericOwnership ownership) ? ownership.ownerObject
                : gameObject.TryGetComponent(out RoR2.Projectile.ProjectileController projectile) ? projectile.owner : null;
            // otherwise e.g. a ward on a character
            var body = ownerObject != null ? ownerObject.GetComponent<CharacterBody>() : gameObject.GetComponentInParent<CharacterBody>();
            return body != null ? body.master : null;
        }

        // the players and their allies, now enemies, would drop items for each other
        private void SacrificeArtifactManager_OnServerCharacterDeath(On.RoR2.Artifacts.SacrificeArtifactManager.orig_OnServerCharacterDeath orig, DamageReport damageReport)
        {
            if (Pausing && damageReport.victimMaster != null && GetPlayer(damageReport.victimMaster) != null)
            {
                return;
            }
            orig(damageReport);
        }

        private static List<CharacterMaster> ConnectedPlayers()
        {
            return PlayerCharacterMasterController.instances.Where(pc => pc.isConnected && pc.master != null).Select(pc => pc.master).ToList();
        }

        // each player alone up to three players, in two teams from four on
        private void ChooseMode()
        {
            int players = ConnectedPlayers().Count;
            teamMode = players > MaxPlayersAlone;
            modeChosen = true;
        }

        // Each player and what they own on their team, also a revived body or an ally that spawns later. Masters and bodies
        // have their team synced, so every player's game sees it. The two teams are drawn at random.
        private void AssignTeams()
        {
            var players = ConnectedPlayers().OrderBy(player => RoR2Application.rng.nextNormalizedFloat).ToList();
            for (int i = 0; i < players.Count; i++)
            {
                playerTeams[players[i]] = Teams[i % (teamMode ? 2 : Teams.Length)];
            }
            // a body's level is its team's, which the players' games work their stats out from too
            uint level = TeamManager.instance.GetTeamLevel(TeamIndex.Player);
            foreach (var team in playerTeams.Values.Distinct().Where(team => team != TeamIndex.Player))
            {
                originalTeamLevels[team] = TeamManager.instance.GetTeamLevel(team);
                TeamManager.instance.SetTeamLevel(team, level);
            }
            foreach (var master in CharacterMaster.readOnlyInstancesList.ToList())
            {
                AssignTeam(master);
            }
        }

        private void AssignTeam(CharacterMaster master)
        {
            var player = GetPlayer(master);
            if (player == null || !playerTeams.TryGetValue(player, out var team))
            {
                return;
            }
            if (master.teamIndex != team)
            {
                if (!originalTeams.ContainsKey(master))
                {
                    originalTeams[master] = master.teamIndex;
                }
                master.teamIndex = team;
            }
            var body = master.GetBody();
            if (body != null && body.teamComponent.teamIndex != team)
            {
                body.teamComponent.teamIndex = team;
            }
        }

        private void RestoreTeams()
        {
            foreach (var (master, team) in originalTeams)
            {
                if (master == null)
                {
                    continue;
                }
                master.teamIndex = team;
                var body = master.GetBody();
                if (body != null)
                {
                    body.teamComponent.teamIndex = team;
                }
            }
            // what the players got during the event, e.g. the Engineer's turrets, started out on their team for it
            foreach (var master in CharacterMaster.readOnlyInstancesList)
            {
                var player = GetPlayer(master);
                if (player == null || player == master || master.teamIndex == player.teamIndex)
                {
                    continue;
                }
                master.teamIndex = player.teamIndex;
                var body = master.GetBody();
                if (body != null)
                {
                    body.teamComponent.teamIndex = player.teamIndex;
                }
            }
            if (TeamManager.instance != null)
            {
                foreach (var (team, level) in originalTeamLevels)
                {
                    TeamManager.instance.SetTeamLevel(team, level);
                }
            }
            originalTeams.Clear();
            originalTeamLevels.Clear();
            playerTeams.Clear();
            // the AI keeps its target until it dies or out of sight, so allies would go on attacking their former enemies
            foreach (var master in CharacterMaster.readOnlyInstancesList)
            {
                foreach (var ai in master.AiComponents ?? new RoR2.CharacterAI.BaseAI[0])
                {
                    var enemy = ai.currentEnemy?.characterBody;
                    if (enemy != null && enemy.teamComponent.teamIndex == master.teamIndex)
                    {
                        ai.currentEnemy.Reset();
                        ai.enemyAttention = 0f;
                    }
                }
            }
        }

        // An item breaks by turning into its consumed version, e.g. a Delicate Watch at low health. Regenerating Scrap is
        // consumed by printing with it rather than broken.
        private bool ItemTransformation_TryTransform(On.RoR2.Inventory.ItemTransformation.orig_TryTransform orig, ref Inventory.ItemTransformation self, Inventory inventory, out Inventory.ItemTransformation.TryTransformResult result)
        {
            bool transformed = orig(ref self, inventory, out result);
            if (transformed && Pausing && inventory != null && inventory.TryGetComponent(out CharacterMaster master) && master.playerCharacterMasterController != null
                && result.takenItem.stackValues.permanentStacks > 0 && result.takenItem.itemIndex != DLC1Content.Items.RegeneratingScrap.itemIndex)
            {
                var broken = ItemCatalog.GetItemDef(result.givenItem.itemIndex);
                if (broken != null && broken.isConsumed)
                {
                    brokenItems.Add((inventory, result.takenItem.itemIndex, result.takenItem.stackValues.permanentStacks, broken.itemIndex, result.givenItem.stackValues.permanentStacks));
                }
            }
            return transformed;
        }

        private void RestoreBrokenItems()
        {
            foreach (var (inventory, item, count, broken, brokenCount) in brokenItems)
            {
                if (inventory == null)
                {
                    continue;
                }
                int stillBroken = Mathf.Min(brokenCount, inventory.GetItemCountPermanent(broken));
                if (stillBroken > 0)
                {
                    inventory.RemoveItemPermanent(broken, stillBroken);
                }
                inventory.GiveItemPermanent(item, count);
            }
            if (brokenItems.Count > 0)
            {
                ChatHelper.SendEventMessage("EVENT_DEATHMATCH_ITEMS_RESTORED");
            }
            brokenItems.Clear();
        }

        private bool SlowsInstead(SetStateOnHurt setStateOnHurt, float duration)
        {
            if (Pausing && setStateOnHurt.TryGetComponent(out CharacterBody body) && body.isPlayerControlled)
            {
                body.AddTimedBuff(RoR2Content.Buffs.Slow80, duration);
                return true;
            }
            return false;
        }

        private void SetStateOnHurt_SetStun(On.RoR2.SetStateOnHurt.orig_SetStun orig, SetStateOnHurt self, float duration)
        {
            if (!SlowsInstead(self, duration))
            {
                orig(self, duration);
            }
        }

        private void SetStateOnHurt_OverrideStun(On.RoR2.SetStateOnHurt.orig_OverrideStun orig, SetStateOnHurt self, float duration)
        {
            if (!SlowsInstead(self, duration))
            {
                orig(self, duration);
            }
        }

        private void SetStateOnHurt_SetFrozen(On.RoR2.SetStateOnHurt.orig_SetFrozen orig, SetStateOnHurt self, float duration)
        {
            if (!SlowsInstead(self, duration))
            {
                orig(self, duration);
            }
        }

        private void SetStateOnHurt_SetShock(On.RoR2.SetStateOnHurt.orig_SetShock orig, SetStateOnHurt self, float duration)
        {
            if (!SlowsInstead(self, duration))
            {
                orig(self, duration);
            }
        }

        private void SetStateOnHurt_SetImmobilize(On.RoR2.SetStateOnHurt.orig_SetImmobilize orig, SetStateOnHurt self, GameObject attacker, GameObject inflictor)
        {
            if (!SlowsInstead(self, ImmobilizeSlowDuration))
            {
                orig(self, attacker, inflictor);
            }
        }

        // the roots of Tentabauble, a Void Reaver's nullifying and Visions of Heresy's ruin
        private BuffDef SlowInsteadOfRoot(CharacterBody body, BuffDef buffDef)
        {
            return Pausing && body.isPlayerControlled && (buffDef == RoR2Content.Buffs.Entangle || buffDef == RoR2Content.Buffs.Nullified || buffDef == RoR2Content.Buffs.LunarSecondaryRoot)
                ? RoR2Content.Buffs.Slow80 : buffDef;
        }

        private void CharacterBody_AddTimedBuff_BuffDef_float(On.RoR2.CharacterBody.orig_AddTimedBuff_BuffDef_float orig, CharacterBody self, BuffDef buffDef, float duration)
        {
            orig(self, SlowInsteadOfRoot(self, buffDef), duration);
        }

        private void CharacterBody_AddTimedBuff_BuffDef_float_int(On.RoR2.CharacterBody.orig_AddTimedBuff_BuffDef_float_int orig, CharacterBody self, BuffDef buffDef, float duration, int maxStacks)
        {
            orig(self, SlowInsteadOfRoot(self, buffDef), duration, maxStacks);
        }

        private bool Pausing => ModConfig.Enabled.Value && NetworkServer.active && IsActive();

        private void CombatDirector_FixedUpdate(On.RoR2.CombatDirector.orig_FixedUpdate orig, CombatDirector self)
        {
            if (!Pausing)
            {
                orig(self);
            }
        }

        private void InfiniteTowerWaveController_FixedUpdate(On.RoR2.InfiniteTowerWaveController.orig_FixedUpdate orig, InfiniteTowerWaveController self)
        {
            if (!Pausing)
            {
                orig(self);
            }
        }

        private void FogDamageController_FixedUpdate(On.RoR2.FogDamageController.orig_FixedUpdate orig, FogDamageController self)
        {
            if (!Pausing)
            {
                orig(self);
            }
        }

        // the game pauses the run timer by this every frame; the players' games show it paused too
        private bool Run_ShouldUpdateRunStopwatch(On.RoR2.Run.orig_ShouldUpdateRunStopwatch orig, Run self)
        {
            return !Pausing && orig(self);
        }

        private bool InfiniteTowerRun_ShouldUpdateRunStopwatch(On.RoR2.InfiniteTowerRun.orig_ShouldUpdateRunStopwatch orig, InfiniteTowerRun self)
        {
            return !Pausing && orig(self);
        }

        // its boss couldn't spawn meanwhile
        private Interactability TeleporterInteraction_GetInteractability(On.RoR2.TeleporterInteraction.orig_GetInteractability orig, TeleporterInteraction self, Interactor activator)
        {
            return Pausing ? Interactability.Disabled : orig(self, activator);
        }

        // the player a character fights for: the player, or the owner of a drone or other ally, also of one it summoned
        private static CharacterMaster GetPlayer(GameObject attacker)
        {
            return GetPlayer(attacker != null && attacker.TryGetComponent(out CharacterBody body) ? body.master : null);
        }

        private static CharacterMaster GetPlayer(CharacterMaster master)
        {
            for (int depth = 0; master != null && depth < 10; depth++)
            {
                if (master.playerCharacterMasterController != null)
                {
                    return master;
                }
                master = master.minionOwnership != null ? master.minionOwnership.ownerMaster : null;
            }
            return null;
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            var victim = self.body;
            // what the fight left behind, see LeftoverTime
            if (!Pausing && NetworkServer.active && Time.fixedTime < leftoverEnd && damageInfo != null && victim != null)
            {
                var hitter = GetPlayer(damageInfo.attacker);
                var target = GetPlayer(victim.master);
                if (hitter != null && target != null && hitter != target)
                {
                    damageInfo.rejected = true;
                }
            }
            var attacker = Pausing && damageInfo != null && victim != null && victim.isPlayerControlled ? GetPlayer(damageInfo.attacker) : null;
            if (attacker == null)
            {
                orig(self, damageInfo);
                return;
            }
            if (attacker == victim.master)
            {
                // a player's own drones and allies don't hurt them, what they do to themselves does as usual
                if (damageInfo.attacker != victim.gameObject)
                {
                    damageInfo.rejected = true;
                }
                orig(self, damageInfo);
                return;
            }
            float now = Time.fixedTime;
            float limit = Mathf.Max(0.001f, HealthLossLimit.Value);
            float limitHealth = LimitHealth(victim);
            if (limitHealth <= 0f)
            {
                orig(self, damageInfo);
                return;
            }
            // what they lost drains at the limit per second
            float lost = healthLost.TryGetValue(victim, out var recent) ? Mathf.Max(0f, recent.lost - limit * (now - recent.time)) : 0f;
            float itemLimit = Mathf.Max(0.001f, ItemHealthLossLimit.Value);
            float itemLost = itemHealthLost.TryGetValue(victim, out var recentItems) ? Mathf.Max(0f, recentItems.lost - itemLimit * (now - recentItems.time)) : 0f;
            lastHits[victim.master] = (attacker, now);
            float before = self.combinedHealth;
            bool isItemDamage = IsItemDamage(damageInfo);
            hits[self] = (lost, itemLost, isItemDamage, isItemDamage ? HealthLossBurstLimit.Value : BurstLimit(damageInfo));
            try
            {
                orig(self, damageInfo);
            }
            finally
            {
                hits.Remove(self);
            }
            float share = Mathf.Max(0f, before - self.combinedHealth) / limitHealth;
            healthLost[victim] = (now, lost + share);
            if (isItemDamage)
            {
                itemHealthLost[victim] = (now, itemLost + share);
            }
        }

        // The game multiplies the hit's damage on its way, e.g. for a critical strike, a weak point or Death Mark, so it
        // weakens right before the armor, where the game has all of it. The game works out the hit's item effects from
        // the damage it was dealt, e.g. missiles from it or a burn from half of it; they weaken when they hit, so they
        // start from the whole hit, not the weakened one.
        private void HealthComponent_TakeDamageProcess(ILContext il)
        {
            var c = new ILCursor(il);
            // Other mods change much of the code before the armor, e.g. SneedHooks, RiskyFixes and WolfoFixes, so it is found
            // by the armor step and the check whether the hit bypasses it: the last branch before the armor past all of it,
            // however a mod words its condition
            if (!c.TryGotoNext(x => x.MatchCallOrCallvirt<CharacterBody>("get_armor")))
            {
                LogNotFound(il, "the armor");
                return;
            }
            int armor = c.Index;
            int bypassCheck = -1;
            for (int i = armor - 1; i >= 0 && bypassCheck < 0; i--)
            {
                if ((il.Instrs[i].MatchBrtrue(out var target) || il.Instrs[i].MatchBrfalse(out target)) && target.Target != null && il.Instrs.IndexOf(target.Target) > armor)
                {
                    bypassCheck = i;
                }
            }
            if (bypassCheck < 0)
            {
                LogNotFound(il, "the check for bypassing armor");
                return;
            }
            // the damage: the armor step keeps at least 1 of it with Mathf.Max, reading and storing the damage around it,
            // which other mods' copies of the damage don't
            int damage = -1;
            for (int i = bypassCheck; i < il.Instrs.Count - 1 && damage < 0; i++)
            {
                if (il.Instrs[i].MatchCall(typeof(Mathf), nameof(Mathf.Max)) && il.Instrs[i + 1].MatchStloc(out int local) && IsFloat(il, local))
                {
                    for (int j = Mathf.Max(bypassCheck, i - 6); j < i; j++)
                    {
                        if (il.Instrs[j].MatchLdloc(out int loaded) && loaded == local)
                        {
                            damage = local;
                        }
                    }
                }
            }
            if (damage < 0)
            {
                LogNotFound(il, "the damage after the armor");
                return;
            }
            // right before the branch, its condition already worked out
            c.Goto(bypassCheck, MoveType.Before);
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldloc, damage);
            c.EmitDelegate<System.Func<HealthComponent, float, float>>(WeakenHit);
            c.Emit(OpCodes.Stloc, damage);
        }

        private static bool IsFloat(ILContext il, int local)
        {
            return local >= 0 && local < il.Body.Variables.Count && il.Body.Variables[local].VariableType.MetadataType == Mono.Cecil.MetadataType.Single;
        }

        // with the code around the armor, to see what another mod changed
        private static void LogNotFound(ILContext il, string what)
        {
            var instructions = il.Instrs;
            int armor = instructions.ToList().FindIndex(instruction => instruction.MatchCallOrCallvirt<CharacterBody>("get_armor"));
            var around = armor < 0 ? "" : "\n" + string.Join("\n", instructions.Skip(Mathf.Max(0, armor - 30)).Take(60));
            Log.LogError($"Could not find {what} in HealthComponent.TakeDamageProcess, so players hit each other at full strength in the Deathmatch event.{around}");
        }

        private float WeakenHit(HealthComponent self, float damage)
        {
            if (!hits.TryGetValue(self, out var hit))
            {
                return damage;
            }
            float limit = Mathf.Max(0.001f, HealthLossLimit.Value);
            float burst = Mathf.Max(limit, hit.burst);
            float multiplier = hit.isItemDamage ? Mathf.Max(0f, ItemDamageMultiplier.Value) : 1f;
            float limitHealth = LimitHealth(self.body);
            float share = Mathf.Max(0f, damage) * multiplier / limitHealth;
            // all items' damage together, before the limit for everything
            if (hit.isItemDamage)
            {
                share = Weaken(share, Mathf.Max(Mathf.Max(0.001f, ItemHealthLossLimit.Value), ItemHealthLossBurstLimit.Value), hit.itemLost);
            }
            return Weaken(share, burst, hit.lost) * limitHealth;
        }

        // Each bit of damage counts for less the closer the loss is to the limit: a hit of the share r takes away
        // (limit - lost) * (1 - e^(-r / limit)), a little less than r for small hits, never all that is left.
        private static float Weaken(float share, float limit, float lost)
        {
            return Mathf.Max(0f, limit - lost) * (1f - Mathf.Exp(-share / limit));
        }

        // A skill's or equipment's hit takes more at once the bigger it is. Measured against the attacker's damage, the hit is
        // the attack's percentage, which items and levels don't raise; crits and the like come later
        private float BurstLimit(DamageInfo damageInfo)
        {
            float burst = HealthLossBurstLimit.Value;
            float threshold = BigHitDamagePercentage.Value / 100f;
            var attacker = damageInfo.attacker != null ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (threshold <= 0f || attacker == null || attacker.damage <= 0f)
            {
                return burst;
            }
            // evenly from a quarter of the big hit's percentage to all of it
            float bigness = Mathf.InverseLerp(threshold * 0.25f, threshold, damageInfo.damage / attacker.damage);
            return Mathf.Lerp(burst, Mathf.Max(burst, BigHitBurstLimit.Value), bigness);
        }

        // The health the limits go by: the health a player has from their survivor and level, and the share of what items add
        // to their full health and shield
        private float LimitHealth(CharacterBody body)
        {
            float baseHealth = body.baseMaxHealth + body.levelMaxHealth * Mathf.Max(0f, body.level - 1f);
            return baseHealth + Mathf.Clamp01(ItemHealthShare.Value) * Mathf.Max(0f, body.healthComponent.fullCombinedHealth - baseHealth);
        }

        // Skills and equipment name themselves as the damage's source; items don't. Damage over time comes without a
        // source, so it counts as the items', but for that of survivor skills alone: Acrid's poison and blight, and
        // Bandit's hemorrhage.
        private static bool IsItemDamage(DamageInfo damageInfo)
        {
            if (damageInfo.dotIndex != DotController.DotIndex.None)
            {
                return damageInfo.dotIndex != DotController.DotIndex.Poison && damageInfo.dotIndex != DotController.DotIndex.Blight
                    && damageInfo.dotIndex != DotController.DotIndex.SuperBleed;
            }
            return (damageInfo.damageType.damageSource & (DamageSource.SkillMask | DamageSource.Equipment)) == 0;
        }

        // the same curve as the damage, on what the player recently healed
        private float HealthComponent_Heal(On.RoR2.HealthComponent.orig_Heal orig, HealthComponent self, float amount, ProcChainMask procChainMask, bool nonRegen)
        {
            if (!Pausing || amount <= 0f || self.body == null || !self.body.isPlayerControlled || self.fullHealth <= 0f)
            {
                return orig(self, amount, procChainMask, nonRegen);
            }
            float now = Time.fixedTime;
            float limit = Mathf.Max(0.001f, HealingLimit.Value);
            float healed = healthHealed.TryGetValue(self, out var recent) ? Mathf.Max(0f, recent.healed - limit * (now - recent.time)) : 0f;
            float share = amount / self.fullHealth;
            float result = orig(self, Mathf.Max(0f, limit - healed) * (1f - Mathf.Exp(-share / limit)) * self.fullHealth, procChainMask, nonRegen);
            healthHealed[self] = (now, healed + Mathf.Max(0f, result) / self.fullHealth);
            return result;
        }

        // the same curve again, on what the player recently gained as barrier, which also comes from the players' games
        private void HealthComponent_AddBarrier(On.RoR2.HealthComponent.orig_AddBarrier orig, HealthComponent self, float value)
        {
            float fullHealth = self.fullCombinedHealth;
            if (!Pausing || value <= 0f || self.body == null || !self.body.isPlayerControlled || fullHealth <= 0f)
            {
                orig(self, value);
                return;
            }
            float now = Time.fixedTime;
            float limit = Mathf.Max(0.001f, BarrierLimit.Value);
            float gained = barrierGained.TryGetValue(self, out var recent) ? Mathf.Max(0f, recent.gained - limit * (now - recent.time)) : 0f;
            float before = self.barrier;
            orig(self, Mathf.Max(0f, limit - gained) * (1f - Mathf.Exp(-value / fullHealth / limit)) * fullHealth);
            barrierGained[self] = (now, gained + Mathf.Max(0f, self.barrier - before) / fullHealth);
        }

        // set rather than healed, which the event's limit on healing would still weaken
        private static void HealAll()
        {
            foreach (var body in Helper.GetLivingPlayerBodies())
            {
                body.healthComponent.Networkhealth = body.healthComponent.fullHealth;
                body.healthComponent.RechargeShieldFull();
            }
        }

        // Like Blast Shower, the players and their allies lose their debuffs, damage over time and stuns, which only the
        // other players can have given them, as there were no monsters
        private void CleanseAll()
        {
            foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
            {
                if (body != null && GetPlayer(body.master) != null)
                {
                    Util.CleanseBody(body, removeDebuffs: true, removeBuffs: false, removeCooldownBuffs: false, removeDots: true, removeStun: true, removeNearbyProjectiles: false);
                }
            }
        }

        // A player's drone, which comes back when the event ends, along with its items and, for an equipment drone,
        // its equipment
        private void RememberLostDrone(DamageReport report)
        {
            var master = report.victimMaster;
            var body = report.victimBody;
            var owner = master != null && master.playerCharacterMasterController == null ? GetPlayer(master) : null;
            if (owner == null || body == null || DroneCatalog.GetDroneIndexFromBodyIndex(body.bodyIndex) == DroneIndex.None)
            {
                return;
            }
            // the Operator's companion drones keep their master when downed and are repaired by the game, and DroneEscort
            // only lends its drones for its own duration
            if (master.GetComponent<DroneRepairMaster>() || master.GetComponent<EventDroneEscort.EscortDrone>())
            {
                return;
            }
            var masterPrefab = MasterCatalog.GetMasterPrefab(master.masterIndex);
            if (masterPrefab == null || lostDroneBodies.Contains(body))
            {
                return;
            }
            lostDroneBodies.Add(body);
            var drone = new LostDrone
            {
                owner = owner,
                masterPrefab = masterPrefab,
                equipment = master.inventory != null ? master.inventory.currentEquipmentIndex : EquipmentIndex.None,
                position = body.footPosition,
            };
            if (master.inventory != null)
            {
                foreach (var item in ItemCatalog.allItems)
                {
                    int count = master.inventory.GetItemCountPermanent(item);
                    if (count > 0)
                    {
                        drone.items[item] = count;
                    }
                }
            }
            lostDrones.Add(drone);
        }

        // the wreck to repair a drone by, which would be there besides the drone that comes back
        private void DeathState_OnImpactServer(On.EntityStates.Drone.DeathState.orig_OnImpactServer orig, EntityStates.Drone.DeathState self, Vector3 contactPoint)
        {
            if (self.characterBody != null && lostDroneBodies.Contains(self.characterBody))
            {
                return;
            }
            orig(self, contactPoint);
        }

        // like buying one, next to its owner, or where it was destroyed if they are dead
        private void RestoreLostDrones()
        {
            foreach (var drone in lostDrones)
            {
                if (drone.owner == null)
                {
                    continue;
                }
                var ownerBody = drone.owner.GetBody();
                var master = new MasterSummon
                {
                    masterPrefab = drone.masterPrefab,
                    position = ownerBody != null ? ownerBody.footPosition + Vector3.up * 2f : drone.position,
                    rotation = Quaternion.identity,
                    summonerBodyObject = ownerBody != null ? ownerBody.gameObject : null,
                    teamIndexOverride = TeamIndex.Player,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true,
                }.Perform();
                if (master == null)
                {
                    continue;
                }
                if (ownerBody == null)
                {
                    master.minionOwnership.SetOwner(drone.owner);
                }
                // up to what it had, as it may get some again on its own, e.g. from its owner's items
                foreach (var (item, count) in drone.items)
                {
                    int missing = count - master.inventory.GetItemCountPermanent(item);
                    if (missing > 0)
                    {
                        master.inventory.GiveItemPermanent(item, missing);
                    }
                }
                if (drone.equipment != EquipmentIndex.None)
                {
                    master.inventory.SetEquipmentIndex(drone.equipment);
                }
            }
            lostDrones.Clear();
            lostDroneBodies.Clear();
        }

        private void GlobalEventManager_onCharacterDeathServer(DamageReport report)
        {
            if (Pausing)
            {
                RememberLostDrone(report);
            }
            var victim = report.victimMaster;
            if (!Pausing || victim == null || victim.playerCharacterMasterController == null)
            {
                return;
            }
            var killer = GetPlayer(report.attacker);
            if ((killer == null || killer == victim) && lastHits.TryGetValue(victim, out var lastHit) && Time.fixedTime - lastHit.time <= KillCreditTime)
            {
                killer = lastHit.attacker;
            }
            lastHits.Remove(victim);
            if (killer == null || killer == victim)
            {
                return;
            }
            scores[killer] = scores.GetValueOrDefault(killer) + 1;
            if (teamMode)
            {
                ChatHelper.SendEventMessage("EVENT_DEATHMATCH_TEAM_KILL", PlayerName(killer), PlayerName(victim), TeamScore(playerTeams.GetValueOrDefault(killer)));
            }
            else
            {
                ChatHelper.SendEventMessage("EVENT_DEATHMATCH_KILL", PlayerName(killer), PlayerName(victim), scores[killer]);
            }
        }

        private static string PlayerName(CharacterMaster master)
        {
            var body = master.GetBody();
            return body != null ? ChatHelper.GetColoredPlayerName(body) : master.playerCharacterMasterController.GetDisplayName();
        }

        // Every death revives, before Dio's Best Friend and other revives, which stay for later. The game counts the
        // pending revive as a life left, so the run doesn't end while everyone is dead.
        private bool CharacterMaster_TryReviveOnBodyDeath(On.RoR2.CharacterMaster.orig_TryReviveOnBodyDeath orig, CharacterMaster self, CharacterBody body)
        {
            if (Pausing && self.playerCharacterMasterController != null && self.playerCharacterMasterController.isConnected)
            {
                self.Invoke(nameof(CharacterMaster.RespawnExtraLifeShrine), ReviveDelay);
                return true;
            }
            return orig(self, body);
        }

        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (Pausing)
            {
                Remove(body);
                // a revived player's new body, or an ally that came along
                if (body.master != null)
                {
                    AssignTeam(body.master);
                }
                OpponentMarks.Refresh();
            }
        }

        // Players are marked for the players of the other teams
        private bool IsOpponent(CharacterMaster viewer, CharacterBody body)
        {
            var player = GetPlayer(body.master);
            return player != null && player == body.master && player != viewer && playerTeams.TryGetValue(viewer, out var viewerTeam)
                && playerTeams.TryGetValue(player, out var team) && team != viewerTeam;
        }

        private static bool IsMonster(CharacterBody body)
        {
            var team = body.teamComponent.teamIndex;
            return !Helper.IsPlayersSide(body) && (team == TeamIndex.Monster || team == TeamIndex.Void || team == TeamIndex.Lunar);
        }

        // Gone for good, without dying, so that it gives no gold or experience; destroyed on the host, it is gone for every
        // player
        private static void Remove(CharacterBody body)
        {
            if (body == null || !IsMonster(body))
            {
                return;
            }
            if (body.master != null)
            {
                Object.Destroy(body.master.gameObject);
            }
            Object.Destroy(body.gameObject);
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!Pausing)
            {
                return;
            }
            // its own clock, since the run timer the events go by stands still
            timeLeft -= Time.fixedDeltaTime;
            if (timeLeft <= 0f)
            {
                Reward();
                RandomEvents.instance.EndActiveEvents();
            }
        }

        private int TeamScore(TeamIndex team)
        {
            return playerTeams.Where(player => player.Value == team).Sum(player => scores.GetValueOrDefault(player.Key));
        }

        // A random green item for the one with the most kills, or each player of the team with the most; a random white
        // item each if several have the most
        private void Reward()
        {
            List<CharacterMaster> winners;
            bool draw;
            int best;
            if (teamMode)
            {
                var teamScores = playerTeams.Values.Distinct().ToDictionary(team => team, TeamScore);
                best = teamScores.Count > 0 ? teamScores.Values.Max() : 0;
                var winningTeams = teamScores.Where(team => team.Value == best).Select(team => team.Key).ToList();
                winners = playerTeams.Where(player => winningTeams.Contains(player.Value) && player.Key != null).Select(player => player.Key).ToList();
                draw = winningTeams.Count > 1;
            }
            else
            {
                best = scores.Count > 0 ? scores.Values.Max() : 0;
                winners = scores.Where(score => score.Value == best && score.Key != null).Select(score => score.Key).ToList();
                draw = winners.Count > 1;
            }
            if (best <= 0)
            {
                ChatHelper.SendEventMessage("EVENT_DEATHMATCH_NO_WINNER");
                return;
            }
            var dropList = draw ? Run.instance.availableTier1DropList : Run.instance.availableTier2DropList;
            var rewards = new List<(CharacterMaster winner, PickupIndex pickup)>();
            foreach (var winner in winners)
            {
                var pickupDef = dropList.Count > 0 ? PickupCatalog.GetPickupDef(dropList[RoR2Application.rng.RangeInt(0, dropList.Count)]) : null;
                if (pickupDef == null || pickupDef.itemIndex == ItemIndex.None || winner.inventory == null)
                {
                    continue;
                }
                winner.inventory.GiveItemPermanent(pickupDef.itemIndex);
                rewards.Add((winner, pickupDef.pickupIndex));
                var body = winner.GetBody();
                if (body != null)
                {
                    ItemOrb.Fly(body.corePosition + Vector3.up * 5f, body.gameObject, pickupDef.itemIndex);
                }
            }
            // the items in the colors of their tiers, which also tells everyone what they got
            var rewardList = string.Join(", ", rewards.Select(reward => $"{PlayerName(reward.winner)}: {ChatHelper.GetItemNames(new[] { reward.pickup })}"));
            var names = string.Join(", ", winners.Select(PlayerName));
            if (teamMode)
            {
                if (draw)
                {
                    ChatHelper.SendEventMessage("EVENT_DEATHMATCH_TEAM_DRAW", best, rewardList);
                }
                else
                {
                    ChatHelper.SendEventMessage("EVENT_DEATHMATCH_TEAM_WINNER", names, best, rewardList);
                }
            }
            else if (draw)
            {
                ChatHelper.SendEventMessage("EVENT_DEATHMATCH_DRAW", names, best, rewardList);
            }
            else
            {
                ChatHelper.SendEventMessage("EVENT_DEATHMATCH_WINNER", names, best, rewards.Count > 0 ? ChatHelper.GetItemNames(new[] { rewards[0].pickup }) : "");
            }
        }

        public override void Prepare()
        {
            ChooseMode();
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            scores.Clear();
            lastHits.Clear();
            healthLost.Clear();
            itemHealthLost.Clear();
            healthHealed.Clear();
            barrierGained.Clear();
            brokenItems.Clear();
            lostDrones.Clear();
            lostDroneBodies.Clear();
            timeLeft = Mathf.Max(1f, ModConfig.EventDuration.Value);
            // e.g. started from the console, without an announcement
            if (!modeChosen)
            {
                ChooseMode();
            }
            foreach (var body in CharacterBody.readOnlyInstancesList.ToList())
            {
                Remove(body);
            }
            AssignTeams();
            UpdateOwnedTeams();
            OpponentMarks.Show(IsOpponent);
            // every player joins in, also those who died before
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (pc.isConnected && pc.master != null && pc.master.IsDeadAndOutOfLivesServer())
                {
                    pc.master.RespawnExtraLifeShrine();
                }
            }
        }

        public override void Stop()
        {
            OpponentMarks.Hide();
            RestoreBrokenItems();
            RestoreTeams();
            UpdateOwnedTeams();
            RestoreLostDrones();
            CleanseAll();
            HealAll();
            leftoverEnd = Time.fixedTime + LeftoverTime;
            modeChosen = false;
            scores.Clear();
            lastHits.Clear();
            healthLost.Clear();
            itemHealthLost.Clear();
            healthHealed.Clear();
            barrierGained.Clear();
        }
    }
}
