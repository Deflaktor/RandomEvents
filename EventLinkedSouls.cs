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
    public class EventLinkedSouls : AbstractEvent
    {
        // the shares of a hit are damage too, which must not be split again
        // while a player's damage is passed on to the others, as hits that keep its attacker
        public static bool Sharing { get; private set; }
        // Shared Suffering's tethers link the players, along the shortest tree that connects them all, rebuilt as they
        // move. A new tree replaces the current one only if it is this much shorter, so that links don't flicker
        // between two about as long.
        private const float LinkUpdateInterval = 0.25f;
        private const float LinkRebuildGain = 0.9f;
        private AsyncOperationHandle<GameObject> TetherOrigin;
        private readonly List<(CharacterBody parent, CharacterBody child)> links = new List<(CharacterBody parent, CharacterBody child)>();
        private readonly Dictionary<CharacterBody, Tethers> tethers = new Dictionary<CharacterBody, Tethers>();
        private float linkTimer;

        // the tether origin on a player, with the players it links to
        private class Tethers
        {
            public GameObject origin;
            public SharedSufferingTetherManager manager;
            public List<CharacterBody> targets = new List<CharacterBody>();
            // the manager drops what it's given before its Start, so a new one is given its targets again
            public bool fresh = true;
        }

        public override EventTags Tags => EventTags.PlayerDamage;
        public override bool LoadCondition()
        {
            return true;
        }
        public override bool Condition(List<AbstractEvent> activeOtherEvents)
        {
            return Helper.GetLivingPlayerBodies().Count >= 2;
        }
        public override string GetEventConfigName()
        {
            return "LinkedSouls";
        }
        public override string GetAnnouncement()
        {
            return Language.GetStringFormatted("ANNOUNCE_EVENT_LINKED_SOULS");
        }
        public override string GetDescription()
        {
            return "Damage a player takes is split among all living players. Tethers link the players.";
        }
        public override string GetPopupDescription()
        {
            return "Your souls are linked: damage you take is split among all living players.";
        }
        public override string GetConditionDescription()
        {
            return "At least two players are alive.";
        }
        protected override void AddConfig(ConfigFile config)
        {
        }
        public override void Preload()
        {
            TetherOrigin = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Items/SharedSuffering/SharedSufferingTetherOrigin.prefab");
        }
        public override void Hook()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            RoR2Application.onFixedUpdate += RoR2Application_onFixedUpdate;
        }

        private void RoR2Application_onFixedUpdate()
        {
            if (!NetworkServer.active || !IsActive())
            {
                return;
            }
            linkTimer -= Time.fixedDeltaTime;
            if (linkTimer <= 0f)
            {
                linkTimer = LinkUpdateInterval;
                UpdateLinks();
            }
        }

        private void UpdateLinks()
        {
            var players = Helper.GetLivingPlayerBodies();
            var tree = SpanningTree(players);
            // the current tree, if it still spans exactly these players and isn't much longer than the new one
            if (!(links.Count == players.Count - 1 && links.All(link => players.Contains(link.parent) && players.Contains(link.child))
                && Length(tree) >= Length(links) * LinkRebuildGain))
            {
                links.Clear();
                links.AddRange(tree);
            }
            ShowLinks();
        }

        // Prim's minimum spanning tree over the players' positions, each link from the player it was reached from
        private static List<(CharacterBody parent, CharacterBody child)> SpanningTree(List<CharacterBody> bodies)
        {
            var tree = new List<(CharacterBody parent, CharacterBody child)>();
            int count = bodies.Count;
            if (count < 2)
            {
                return tree;
            }
            var inTree = new bool[count];
            var nearest = new float[count];
            var reachedFrom = new int[count];
            for (int i = 0; i < count; i++)
            {
                nearest[i] = float.PositiveInfinity;
                reachedFrom[i] = -1;
            }
            nearest[0] = 0f;
            for (int step = 0; step < count; step++)
            {
                int next = -1;
                for (int i = 0; i < count; i++)
                {
                    if (!inTree[i] && (next < 0 || nearest[i] < nearest[next]))
                    {
                        next = i;
                    }
                }
                inTree[next] = true;
                if (reachedFrom[next] >= 0)
                {
                    tree.Add((bodies[reachedFrom[next]], bodies[next]));
                }
                for (int i = 0; i < count; i++)
                {
                    float distance = (bodies[i].corePosition - bodies[next].corePosition).sqrMagnitude;
                    if (!inTree[i] && distance < nearest[i])
                    {
                        nearest[i] = distance;
                        reachedFrom[i] = next;
                    }
                }
            }
            return tree;
        }

        private static float Length(List<(CharacterBody parent, CharacterBody child)> tree)
        {
            return tree.Sum(link => (link.parent.corePosition - link.child.corePosition).magnitude);
        }

        // each player with links gets a tether origin, which every player's game draws to the players it lists
        private void ShowLinks()
        {
            var targetsByParent = links.GroupBy(link => link.parent).ToDictionary(group => group.Key, group => group.Select(link => link.child).ToList());
            foreach (var body in tethers.Keys.ToList())
            {
                if (body == null || tethers[body].origin == null || !targetsByParent.ContainsKey(body))
                {
                    RemoveTethers(body);
                }
            }
            foreach (var (parent, targets) in targetsByParent)
            {
                if (!tethers.TryGetValue(parent, out var tether))
                {
                    tether = SpawnTethers(parent);
                    if (tether == null)
                    {
                        continue;
                    }
                }
                if (tether.fresh || !tether.targets.SequenceEqual(targets))
                {
                    tether.fresh = tether.targets.Count == 0;
                    tether.targets = targets;
                    tether.manager.SetAfflicted(targets);
                }
            }
        }

        // like Shared Suffering does on its carrier
        private Tethers SpawnTethers(CharacterBody body)
        {
            var prefab = TetherOrigin.WaitForCompletion();
            if (prefab == null)
            {
                return null;
            }
            var origin = Object.Instantiate(prefab, body.transform);
            if (!origin.TryGetComponent(out NetworkedBodyAttachment attachment) || !origin.TryGetComponent(out SharedSufferingTetherManager manager))
            {
                Object.Destroy(origin);
                return null;
            }
            attachment.AttachToGameObjectAndSpawn(body.gameObject);
            var tether = new Tethers { origin = origin, manager = manager };
            tethers[body] = tether;
            return tether;
        }

        private void RemoveTethers(CharacterBody body)
        {
            if (tethers.TryGetValue(body, out var tether) && tether.origin != null)
            {
                Object.Destroy(tether.origin);
            }
            tethers.Remove(body);
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            // the environment's damage, e.g. the void fog, hits each player in full, and HotPotato's bomb its carrier
            if (Sharing || EventHotPotato.KillingCarrier || !NetworkServer.active || damageInfo == null || damageInfo.damage <= 0f || !ModConfig.Enabled.Value || !IsActive() || Helper.IsHazard(damageInfo))
            {
                orig(self, damageInfo);
                return;
            }
            var victim = self.body;
            // nothing to share while invincible, and players don't pass on what they do to themselves
            if (victim == null || !victim.isPlayerControlled || !self.alive || damageInfo.attacker == victim.gameObject || IsInvincible(self))
            {
                orig(self, damageInfo);
                return;
            }
            var others = Helper.GetLivingPlayerBodies().Where(body => body != victim).ToList();
            if (others.Count == 0)
            {
                orig(self, damageInfo);
                return;
            }
            float share = damageInfo.damage / (others.Count + 1);
            damageInfo.damage = share;
            orig(self, damageInfo);
            Sharing = true;
            try
            {
                foreach (var other in others)
                {
                    if (other == null || !other.healthComponent.alive)
                    {
                        continue;
                    }
                    other.healthComponent.TakeDamage(new DamageInfo
                    {
                        damage = share,
                        attacker = damageInfo.attacker,
                        inflictor = damageInfo.inflictor,
                        position = other.corePosition,
                        crit = damageInfo.crit,
                        damageColorIndex = damageInfo.damageColorIndex,
                        procCoefficient = 0f,
                        force = Vector3.zero
                    });
                }
            }
            finally
            {
                Sharing = false;
            }
        }

        private static bool IsInvincible(HealthComponent healthComponent)
        {
            return healthComponent.godMode || healthComponent.body.HasBuff(RoR2Content.Buffs.Immune) || healthComponent.body.HasBuff(RoR2Content.Buffs.HiddenInvincibility);
        }

        public override void Prepare()
        {
        }

        public override void Start(List<AbstractEvent> activeOtherEvents)
        {
            links.Clear();
            linkTimer = 0f;
        }

        public override void Stop()
        {
            foreach (var body in tethers.Keys.ToList())
            {
                RemoveTethers(body);
            }
            tethers.Clear();
            links.Clear();
        }
    }
}
