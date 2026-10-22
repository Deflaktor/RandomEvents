using BepInEx.Configuration;
using HG;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomEvents
{
    public class EventGiantBalloons : AbstractEvent
    {
        private const float UpdateInterval = 0.25f;
        private ConfigEntry<float> SpawnRate;
        private ConfigEntry<float> HealthMultiplier;
        private readonly List<Balloon> balloons = new List<Balloon>();
        private float updateTimer;

        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            // the item that makes them big comes with that expansion
            return DLC4Content.DLC4 != null && Run.instance != null && Run.instance.IsExpansionEnabled(DLC4Content.DLC4);
        }
        public override string GetEventConfigName()
        {
            return "GiantBalloons";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_GIANT_BALLOONS");
        }
        public override string GetDescription()
        {
            return "Fewer monsters spawn, but they are inflated to giant size with ten times their health. They shrink as they lose health.";
        }
        public override string GetConditionDescription()
        {
            return "Hallowed Concepts is enabled.";
        }
        protected override void AddConfig(ConfigFile config)
        {
            SpawnRate = config.Bind(GetEventConfigName(), "Spawn Rate", 0.25f, "How many monsters spawn during the event, relative to normal.");
            HealthMultiplier = config.Bind(GetEventConfigName(), "Health Multiplier", 10f, "The inflated monsters' health, relative to normal.");
        }
        public override void Preload()
        {
        }
        public override void Hook()
        {
            CharacterBody.onBodyStartGlobal += CharacterBody_onBodyStartGlobal;
            On.RoR2.CombatDirector.DirectorMoneyWave.Update += DirectorMoneyWave_Update;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        // fewer monsters, since each is so big and tough: the directors earn less to spend on them
        private float DirectorMoneyWave_Update(On.RoR2.CombatDirector.DirectorMoneyWave.orig_Update orig, CombatDirector.DirectorMoneyWave self, float deltaTime, float difficultyCoefficient)
        {
            var credits = orig(self, deltaTime, difficultyCoefficient);
            return NetworkServer.active && IsActive() ? credits * Mathf.Max(0f, SpawnRate.Value) : credits;
        }

        // monsters spawning during the event are inflated too
        private void CharacterBody_onBodyStartGlobal(CharacterBody body)
        {
            if (NetworkServer.active && IsActive())
            {
                Inflate(body);
            }
        }

        // +1% size per stack: 10 times the size for small monsters, 5 times for medium and 3 times for large ones
        private static int GetMaxScale(CharacterBody body)
        {
            switch (body.hullClassification)
            {
                case HullClassification.Human: return 900;
                case HullClassification.Golem: return 400;
                default: return 200;
            }
        }

        private void Inflate(CharacterBody body)
        {
            var master = body != null ? body.master : null;
            if (master == null || master.inventory == null || master.isBoss || body.healthComponent == null
                || body.teamComponent.teamIndex == TeamIndex.Player || body.teamComponent.teamIndex == TeamIndex.Neutral
                || balloons.Exists(balloon => balloon.master == master))
            {
                return;
            }
            var balloon = new Balloon
            {
                master = master,
                body = body,
                maxScale = GetMaxScale(body),
                // each Boost Hp adds 10% of the base health
                maxBoostHp = Mathf.Max(0, Mathf.RoundToInt((HealthMultiplier.Value - 1f) * 10f)),
            };
            balloons.Add(balloon);
            balloon.SetInflation(1f);
            body.RecalculateStats();
            balloon.startHealth = body.healthComponent.fullCombinedHealth;
            body.healthComponent.Networkhealth = body.healthComponent.fullHealth;
        }

        // Each balloon keeps the share of its size and extra health that it has left of its health at the start. Its
        // maximum health shrinks with the extra health, so it stays at its current health.
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
            balloons.RemoveAll(balloon => balloon.master == null || balloon.body == null || !balloon.body.healthComponent.alive);
            foreach (var balloon in balloons)
            {
                if (balloon.startHealth > 0f)
                {
                    balloon.SetInflation(Mathf.Clamp01(balloon.body.healthComponent.combinedHealth / balloon.startHealth));
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            balloons.Clear();
            updateTimer = 0f;
            // Simulacrum's waves get their credits from the wave instead
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.waveController != null)
            {
                var spawnRate = Mathf.Max(0f, SpawnRate.Value);
                infiniteTowerRun.waveController.totalWaveCredits *= spawnRate;
                infiniteTowerRun.waveController.creditsPerSecond *= spawnRate;
            }
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                Inflate(body);
            }
        }

        public override void Stop()
        {
            // the survivors shrink back to normal
            foreach (var balloon in balloons)
            {
                if (balloon.master != null)
                {
                    balloon.SetInflation(0f);
                }
            }
            balloons.Clear();
        }

        private class Balloon
        {
            public CharacterMaster master;
            public CharacterBody body;
            public int maxScale;
            public int maxBoostHp;
            public float startHealth;
            private int scale;
            private int boostHp;

            public void SetInflation(float inflation)
            {
                var inventory = master.inventory;
                if (inventory == null)
                {
                    return;
                }
                var newScale = Mathf.RoundToInt(maxScale * inflation);
                var newBoostHp = Mathf.RoundToInt(maxBoostHp * inflation);
                if (newScale != scale)
                {
                    inventory.GiveItemPermanent(DLC4Content.Items.BoostCharacterScale, newScale - scale);
                    scale = newScale;
                }
                if (newBoostHp != boostHp)
                {
                    inventory.GiveItemPermanent(RoR2Content.Items.BoostHp, newBoostHp - boostHp);
                    boostHp = newBoostHp;
                }
            }
        }
    }
}
