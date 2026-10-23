using BepInEx;
using HG;
using RoR2;
using RoR2.Navigation;
using RoR2.Skills;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RandomEvents
{
    public class Helper
    {

        public static Vector3? RaycastToCeiling(Vector3 position, float maxDistance, bool lenient)
        {
            return RaycastTo(position, Vector3.up, maxDistance, lenient);
        }

        public static Vector3? RaycastToFloor(Vector3 position, float maxDistance, bool lenient)
        {
            return RaycastTo(position, Vector3.down, maxDistance, lenient);
        }


        private static Vector3? RaycastTo(Vector3 position, Vector3 direction, float maxDistance, bool lenient)
        {
            // only works up or down
            Vector3 lowestY = new Vector3(position.x, float.MaxValue, position.z);
            Vector3 shiftedPosition = new Vector3(position.x, position.y, position.z);
            bool foundCeiling = false;
            // 0,0
            {
                if (Physics.Raycast(new Ray(shiftedPosition, direction), out var hitInfo, maxDistance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    foundCeiling = true;
                    if (hitInfo.point.y < lowestY.y)
                        lowestY.y = hitInfo.point.y;
                }
            }
            if (lenient)
            {
                // 5,0
                shiftedPosition.x = position.x + 5f;
                {
                    if (Physics.Raycast(new Ray(shiftedPosition, direction), out var hitInfo, maxDistance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        foundCeiling = true;
                        if (hitInfo.point.y < lowestY.y)
                            lowestY.y = hitInfo.point.y;
                    }
                }
                // -5,0
                shiftedPosition.x = position.x - 5f;
                {
                    if (Physics.Raycast(new Ray(shiftedPosition, direction), out var hitInfo, maxDistance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        foundCeiling = true;
                        if (hitInfo.point.y < lowestY.y)
                            lowestY.y = hitInfo.point.y;
                    }
                }
                // 0,5
                shiftedPosition.x = position.x;
                shiftedPosition.z = position.z + 5f;
                {
                    if (Physics.Raycast(new Ray(shiftedPosition, direction), out var hitInfo, maxDistance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        foundCeiling = true;
                        if (hitInfo.point.y < lowestY.y)
                            lowestY.y = hitInfo.point.y;
                    }
                }
                // 0,-5
                shiftedPosition.z = position.z - 5f;
                {
                    if (Physics.Raycast(new Ray(shiftedPosition, direction), out var hitInfo, maxDistance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        foundCeiling = true;
                        if (hitInfo.point.y < lowestY.y)
                            lowestY.y = hitInfo.point.y;
                    }
                }
            }
            if (foundCeiling)
                return lowestY;
            return null;
        }


        private static Quaternion GetRotationFacingTargetPositionFromPoint(Vector3 targetPosition, Vector3 point)
        {
            point.y = targetPosition.y;
            return Util.QuaternionSafeLookRotation(targetPosition - point);
        }

        // setup runs on each object before it is spawned for the clients
        public static List<GameObject> ApproximatePlacement(GameObject prefab, Vector3 targetPosition, float minDistance, float maxDistance, int amount = 1, MapNodeGroup.GraphType nodeGraphType = MapNodeGroup.GraphType.Ground, HullClassification hullSize = HullClassification.Human, bool occupyPosition = true, NodeFlags requiredNodeFlags = NodeFlags.None, NodeFlags forbiddenNodeFlags = NodeFlags.NoCharacterSpawn, bool preventOverhead = true, Action<GameObject> setup = null)
        {
            var placedObjects = new List<GameObject>();
            DirectorCore instance = DirectorCore.instance;
            Xoroshiro128Plus rng = Run.instance.runRNG;
            var nodeGraph = SceneInfo.instance.GetNodeGraph(nodeGraphType);
            List<NodeGraph.NodeIndex> list = nodeGraph.FindNodesInRangeWithFlagConditions(targetPosition, minDistance, maxDistance, (HullMask)(1 << (int)hullSize), requiredNodeFlags, forbiddenNodeFlags, preventOverhead);
            while (list.Count > 0)
            {
                int index = rng.RangeInt(0, list.Count);
                NodeGraph.NodeIndex nodeIndex = list[index];
                if (nodeGraph.GetNodePosition(nodeIndex, out var position) && CheckPositionFree(nodeGraph, nodeIndex, hullSize, nodeGraphType))
                {
                    Quaternion rotation = GetRotationFacingTargetPositionFromPoint(targetPosition, position);
                    GameObject gameObject = GameObject.Instantiate(prefab, position, rotation);
                    setup?.Invoke(gameObject);
                    NetworkServer.Spawn(gameObject);
                    placedObjects.Add(gameObject);
                    if (occupyPosition)
                    {
                        instance.AddOccupiedNode(nodeGraph, nodeIndex);
                        var occupiedNode = gameObject.AddComponent<OccupiedNode>();
                        occupiedNode.nodeGraph = nodeGraph;
                        occupiedNode.nodeIndex = nodeIndex;
                    }
                    if (placedObjects.Count >= amount)
                    {
                        break;
                    }
                }
                list.RemoveAt(index);
            }
            return placedObjects;
        }

        private static bool CheckPositionFree(NodeGraph nodeGraph, NodeGraph.NodeIndex nodeIndex, HullClassification hullSize, MapNodeGroup.GraphType nodeGraphType)
        {
            DirectorCore instance = DirectorCore.instance;
            if (Array.IndexOf(value: new DirectorCore.NodeReference(nodeGraph, nodeIndex), array: instance.occupiedNodes) != -1)
            {
                return false;
            }
            float num = HullDef.Find(hullSize).radius * 0.7f;
            nodeGraph.GetNodePosition(nodeIndex, out var position);
            if (nodeGraphType == MapNodeGroup.GraphType.Ground)
            {
                position += Vector3.up * (num + 0.25f);
            }
            return !HGPhysics.DoesOverlapSphere(position, num, (int)LayerIndex.world.mask | (int)LayerIndex.CommonMasks.characterBodiesOrDefault | (int)LayerIndex.CommonMasks.fakeActorLayers);
        }

        // frees the node of a placed object once the object is destroyed
        private class OccupiedNode : MonoBehaviour
        {
            public NodeGraph nodeGraph;
            public NodeGraph.NodeIndex nodeIndex;

            private void OnDestroy()
            {
                DirectorCore director = DirectorCore.instance;
                if (!director)
                {
                    return;
                }
                int index = Array.IndexOf(director.occupiedNodes, new DirectorCore.NodeReference(nodeGraph, nodeIndex));
                if (index != -1)
                {
                    ArrayUtils.ArrayRemoveAtAndResize(ref director.occupiedNodes, index);
                }
            }
        }

        // a random ground node in the distance range, or the floor below the center if there is none
        public static Vector3? FindGroundPositionNear(Vector3 center, float minDistance, float maxDistance)
        {
            var nodeGraph = SceneInfo.instance ? SceneInfo.instance.GetNodeGraph(MapNodeGroup.GraphType.Ground) : null;
            if (nodeGraph)
            {
                var nodes = nodeGraph.FindNodesInRangeWithFlagConditions(center, minDistance, maxDistance, HullMask.Human, NodeFlags.None, NodeFlags.NoCharacterSpawn, false);
                if (nodes.Count > 0 && nodeGraph.GetNodePosition(nodes[RoR2Application.rng.RangeInt(0, nodes.Count)], out var position))
                {
                    return position;
                }
            }
            return RaycastToFloor(center, 50f, true);
        }

        // The director that spawns the stage's monsters. In Simulacrum the wave's, so that its monsters count for the wave.
        public static CombatDirector FindCombatDirector()
        {
            if (Run.instance is InfiniteTowerRun infiniteTowerRun && infiniteTowerRun.waveController != null && infiniteTowerRun.waveController.combatDirector != null)
            {
                return infiniteTowerRun.waveController.combatDirector;
            }
            foreach (var director in CombatDirector.instancesList)
            {
                // not the teleporter's boss director, whose monsters join the boss fight
                if (director != null && director.isActiveAndEnabled && director.teamIndex == TeamIndex.Monster && director.combatSquad == null)
                {
                    return director;
                }
            }
            return null;
        }

        // Spawns a monster at the position as the director does, with an elite's boosts and gold and experience for the kill
        public static CharacterMaster SpawnMonster(SpawnCard spawnCard, Vector3 position, EliteDef eliteDef = null)
        {
            var director = FindCombatDirector();
            var spawnedByDirector = director != null ? SpawnWithDirector(director, spawnCard, position, eliteDef) : null;
            if (spawnedByDirector != null)
            {
                return spawnedByDirector;
            }
            // without a director, or where it failed, e.g. at its limit of monsters
            var spawnRequest = new DirectorSpawnRequest(spawnCard, new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Direct,
                position = position
            }, RoR2Application.rng)
            {
                teamIndexOverride = TeamIndex.Monster,
                ignoreTeamMemberLimit = true
            };
            var spawnedObject = DirectorCore.instance != null ? DirectorCore.instance.TrySpawnObject(spawnRequest) : null;
            var master = spawnedObject != null ? spawnedObject.GetComponent<CharacterMaster>() : null;
            if (master != null && eliteDef != null && eliteDef.eliteEquipmentDef != null)
            {
                master.inventory.SetEquipmentIndex(eliteDef.eliteEquipmentDef.equipmentIndex, master.inventory.activeEquipmentLocation);
            }
            // like the director's own monsters, e.g. a Simulacrum wave needs it killed too
            if (master != null && director != null && director.combatSquad != null)
            {
                director.combatSquad.AddMember(master);
            }
            return master;
        }

        private static CharacterMaster SpawnWithDirector(CombatDirector director, SpawnCard spawnCard, Vector3 position, EliteDef eliteDef)
        {
            var target = new GameObject("RandomEventsSpawnTarget");
            target.transform.position = position;
            GameObject spawned = null;
            UnityEngine.Events.UnityAction<GameObject> listener = spawnedInstance => spawned = spawnedInstance;
            director.onSpawnedServer.AddListener(listener);
            try
            {
                director.Spawn(spawnCard, eliteDef, target.transform, DirectorCore.MonsterSpawnDistance.CloseNoMinimum, false, 1f, DirectorPlacementRule.PlacementMode.Direct);
            }
            finally
            {
                director.onSpawnedServer.RemoveListener(listener);
                GameObject.Destroy(target);
            }
            return spawned != null ? spawned.GetComponent<CharacterMaster>() : null;
        }

        // Whether the next Simulacrum wave is a boss wave, which may also be one that only spawns Mithrix or a
        // Scavenger, so that events that change which monsters spawn would do nothing
        public static bool IsSimulacrumBossWaveNext()
        {
            return Run.instance is InfiniteTowerRun infiniteTowerRun && (infiniteTowerRun.waveIndex + 1) % 5 == 0;
        }

        // the bodies of connected players who are alive
        // The damage of the environment, like the void fog's, which the events' damage reductions leave alone
        public static DamageTypeCombo HazardDamageType => new DamageTypeCombo(DamageType.BypassArmor | DamageType.BypassBlock, DamageTypeExtended.DamageField, DamageSource.Hazard);

        public static bool IsHazard(DamageInfo damageInfo)
        {
            return (damageInfo.damageType.damageTypeExtended & DamageTypeExtended.DamageField) != 0 && damageInfo.damageType.damageSource == DamageSource.Hazard;
        }

        public static List<CharacterBody> GetLivingPlayerBodies()
        {
            var bodies = new List<CharacterBody>();
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (pc.isConnected && pc.master != null)
                {
                    var body = pc.master.GetBody();
                    if (body != null && body.healthComponent != null && body.healthComponent.alive)
                    {
                        bodies.Add(body);
                    }
                }
            }
            return bodies;
        }

        public struct PlayersInRadiusResult {
            public List<TeamComponent> playersInRadius;
            public List<TeamComponent> playersNotInRadius;
        }

        public static PlayersInRadiusResult GetPlayersInRadius(HoldoutZoneController.HoldoutZoneShape holdoutZoneShape, Vector3 origin, float radius, TeamIndex teamIndex)
        {
            ReadOnlyCollection<TeamComponent> teamMembers = TeamComponent.GetTeamMembers(teamIndex);
            List<TeamComponent> teamMembersInRadius = new List<TeamComponent>();
            List<TeamComponent> teamMembersNotInRadius = new List<TeamComponent>();
            for (int i = 0; i < teamMembers.Count; i++)
            {
                TeamComponent teamComponent = teamMembers[i];
                if (teamComponent.body.isPlayerControlled)
                {
                    if (IsPointInRadius(holdoutZoneShape, origin, radius, teamComponent.body.corePosition))
                    {
                        teamMembersInRadius.Add(teamComponent);
                    }
                    else
                    {
                        teamMembersNotInRadius.Add(teamComponent);
                    }
                }
            }
            var result = new PlayersInRadiusResult();
            result.playersInRadius = teamMembersInRadius;
            result.playersNotInRadius = teamMembersNotInRadius;
            return result;
        }

        public static bool IsPointInRadius(HoldoutZoneController.HoldoutZoneShape holdoutZoneShape, Vector3 origin, float radius, Vector3 point)
        {
            float radiusSqr = radius * radius;
            switch (holdoutZoneShape)
            {
                case HoldoutZoneController.HoldoutZoneShape.Sphere:
                    if ((point - origin).sqrMagnitude <= radiusSqr)
                    {
                        return true;
                    }
                    break;
                case HoldoutZoneController.HoldoutZoneShape.VerticalTube:
                    point.y = 0f;
                    origin.y = 0f;
                    if ((point - origin).sqrMagnitude <= radiusSqr)
                    {
                        return true;
                    }
                    break;
            }
            return false;
        }

        private static int FindSkillSlotIndex(BodyIndex bodyIndex, SkillFamily skillFamily)
        {
            GenericSkill[] bodyPrefabSkillSlots = BodyCatalog.GetBodyPrefabSkillSlots(bodyIndex);
            for (int i = 0; i < bodyPrefabSkillSlots.Length; i++)
            {
                if (bodyPrefabSkillSlots[i].skillFamily == skillFamily)
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindVariantIndex(SkillFamily skillFamily, SkillDef skillDef)
        {
            SkillFamily.Variant[] variants = skillFamily.variants;
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i].skillDef == skillDef)
                {
                    return i;
                }
            }
            return -1;
        }
        public static SkillFamily FindSkillByFamilyName(string skillFamilyName)
        {
            for (int i = 0; i < SkillCatalog._allSkillFamilies.Length; i++)
            {
                if (SkillCatalog.GetSkillFamilyName(SkillCatalog._allSkillFamilies[i].catalogIndex) == skillFamilyName)
                {
                    return SkillCatalog._allSkillFamilies[i];
                }
            }
            return null;
        }

        public static bool HasSkillVariantEnabled(Loadout loadout, BodyIndex bodyIndex, SkillFamily skillFamily, SkillDef skillDef)
        {
            int num = FindSkillSlotIndex(bodyIndex, skillFamily);
            int num2 = FindVariantIndex(skillFamily, skillDef);
            if (num == -1 || num2 == -1)
            {
                return false;
            }
            return loadout.bodyLoadoutManager.GetSkillVariant(bodyIndex, num) == num2;
        }

        public static void FillEquipment(Inventory inventory, EquipmentIndex equipmentIndex)
        {
            if (inventory._equipmentSlots.Length == 0)
                return;
            for (uint slot = 0; slot < inventory._equipmentSlots.Length; slot++)
            {
                for (uint set = 0; set < inventory._equipmentSlots[slot]._setCount; set++)
                {
                    ref EquipmentState equipmentState = ref inventory._equipmentSlots[slot].GetEquipment(set);
                    equipmentState.equipmentDef = EquipmentCatalog.GetEquipmentDef(EquipmentIndex.None);
                    equipmentState.equipmentIndex = EquipmentIndex.None;
                }
            }
        }

        public static void FillEquipment(Inventory inventory, Inventory.EquipmentStateSlot[] equipmentStateSlots)
        {
            if (inventory._equipmentSlots.Length == 0)
                return;
            for (uint slot = 0; slot < inventory._equipmentSlots.Length; slot++)
            {
                for (uint set = 0; set < inventory._equipmentSlots[slot]._setCount; set++)
                {


                    ref EquipmentState equipmentState = ref inventory._equipmentSlots[slot].GetEquipment(set);
                    equipmentState.equipmentDef = EquipmentCatalog.GetEquipmentDef(EquipmentIndex.None);
                    equipmentState.equipmentIndex = EquipmentIndex.None;
                }
            }
        }

        public static bool IsToolbotWithSwapSkill(CharacterMaster master)
        {
            var body = master.bodyPrefab.GetComponent<CharacterBody>();
            var skillFamily = Helper.FindSkillByFamilyName("ToolbotBodySpecialFamily");
            var skillDef = SkillCatalog.GetSkillDef(SkillCatalog.FindSkillIndexByName("Swap"));
            return Helper.HasSkillVariantEnabled(master.loadout, body.bodyIndex, skillFamily, skillDef);
        }

        public static Inventory.EquipmentStateSlot[] CopyEquipmentState(Inventory.EquipmentStateSlot[] source)
        {
            if (source == null)
                return null;

            var destination = new Inventory.EquipmentStateSlot[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                {
                    destination[i] = null;
                    continue;
                }

                destination[i] = new Inventory.EquipmentStateSlot(source[i]._setCount);
                destination[i].activeSetIndex = source[i].activeSetIndex;

                for (uint j = 0; j < source[i]._setCount; j++)
                {
                    var equipmentState = source[i].GetEquipment(j);
                    destination[i].GetEquipment(j) = new EquipmentState(equipmentState.equipmentIndex, equipmentState.chargeFinishTime, equipmentState.charges);
                }
            }

            return destination;
        }

        public static void TransferEquipmentIndex(Inventory.EquipmentStateSlot[] source, Inventory.EquipmentStateSlot[] target)
        {
            if (source == null || source.Length == 0 || target == null || target.Length == 0)
                return;

            for (uint slot = 0; slot < target.Length; slot++)
            {
                for (uint set = 0; set < target[slot]._setCount; set++)
                {
                    ref EquipmentState targetState = ref target[slot].GetEquipment(set);
                    if (slot < source.Length && set < source[slot]._setCount)
                    {
                        var sourceEquipmentIndex = source[slot].GetEquipment(set).equipmentIndex;
                        targetState.equipmentDef = EquipmentCatalog.GetEquipmentDef(sourceEquipmentIndex);
                        targetState.equipmentIndex = sourceEquipmentIndex;
                    }
                    else
                    {
                        targetState.equipmentDef = EquipmentCatalog.GetEquipmentDef(EquipmentIndex.None);
                        targetState.equipmentIndex = EquipmentIndex.None;
                    }
                }
            }
        }

        // The inventory tracks its equipment set count separately from the slots, so swapped in slots have to match it
        public static void SwapEquipmentSlots(Inventory inventory, Inventory.EquipmentStateSlot[] equipmentStateSlots)
        {
            foreach (var equipmentStateSlot in equipmentStateSlots)
            {
                equipmentStateSlot.UpdateSetCount(inventory.equipmentSetCount);
                equipmentStateSlot.EnsureValidActiveSetIndex(inventory.accessibleEquipmentSetCount);
            }
            inventory._equipmentSlots = equipmentStateSlots;
        }
    }
}
