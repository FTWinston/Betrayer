using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Betrayer
{
    [BepInPlugin("com.ftwinston.valheim.betrayer", "Betrayer", "0.1.0.0")]
    public class BetrayerPlugin : BaseUnityPlugin
    {
        // TODO: put these in config settings
        const int minimumPlayersToAllocateBetrayer = 3;

        //const string targetBossSpawnerID = "Dragonqueen";
        //const string targetBossName = "Moder"; // TODO: is there a resource string for this?
        //const string goalItemID = "DragonEgg";
        //const string goalItemName = "wyvern egg"; // TODO: is there a resource string for this?

        const string targetBossSpawnerID = "Eikthyrnir";
        const string targetBossName = "Eikthyr"; // TODO: is there a resource string for this?
        const string goalItemID = "TrophyEikthyr";
        const string goalItemName = "Eikthyr trophy"; // TODO: is there a resource string for this?
        
        const float targetItemSpawnHeight = 5;
        const float targetItemSpawnCheckDelay = 10;
        const float targetItemSpawnCheckInterval = 30;

        private static BetrayerPlugin instance;

        // Game variables
        static bool hasSetupPositions = false;
        static Vector3 spawnPosition;
        static Vector3 targetPosition;
        static string betrayerPlayerName = null;
        static ZDOID betrayerPlayerID = ZDOID.None;

        public BetrayerPlugin()
        {
            instance = this;
        }

        public void Awake()
        {
            Harmony.CreateAndPatchAll(GetType());
            Harmony.CreateAndPatchAll(typeof(PlayerPositions));
        }

        [HarmonyPatch(typeof(Game), "Start")]
        [HarmonyPostfix]
        static void GameStart()
        {
            Debug.Log($"GameStart");
            betrayerPlayerName = null;
            betrayerPlayerID = ZDOID.None;
            hasSetupPositions = false;
            PlayerPositions.peersWhoCanSeeAll.Clear();
        }

        private static void SetupLocations()
        {
            Debug.Log("SetupLocations");

            spawnPosition = Utils.FindLocation("StartTemple") ?? Vector3.zero;

            targetPosition = (Utils.FindLocation(targetBossSpawnerID) ?? Vector3.zero) + Vector3.up * targetItemSpawnHeight;

            instance.InvokeRepeating(nameof(SpawnTargetItem), targetItemSpawnCheckDelay, targetItemSpawnCheckInterval);
            
            hasSetupPositions = true;
        }

        [HarmonyPatch(typeof(Game), "Update")]
        [HarmonyPrefix]
        static void GameTick()
        {
            if (!hasSetupPositions)
                return;


            foreach (var playerInfo in ZNet.instance.GetPlayerList())
            {
                if ((playerInfo.m_position - spawnPosition).sqrMagnitude < 100)
                {
                    Utils.Message(playerInfo.m_characterID.userID, MessageHud.MessageType.Center, "You are in the spawn area");
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.IsPVPEnabled))]
        [HarmonyPrefix]
        static bool IsPVPEnabled(ref bool __result)
        {
            __result = true;
            return true;
        }

        [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
        [HarmonyPostfix]
        static void PlayerSpawned(ZNet __instance, ZRpc rpc, ZDOID characterID)
        {
            if (characterID == ZDOID.None)
            {
                // Player died, just been unlinked from their character.
                return;
            }

            var peer = __instance.GetPeer(characterID.userID);

            Debug.Log($"Player {characterID} / {peer?.m_playerName} spawned, got {__instance.GetNrOfPlayers()} player(s)");

            if (!hasSetupPositions)
            {
                SetupLocations();
            }


            /*
            foreach (var playerInfo in __instance.GetPlayerList())
            {
                var peer = __instance.GetPeerByPlayerName(playerInfo.m_name);

                // playerInfo.m_characterID is still ZDOID.None at this point
                if (peer.m_characterID == characterID)
                {
                    Debug.Log($"The player that just spawned was {playerInfo.m_name}, and their peer is {peer.m_uid} / {peer.m_characterID}");
                }
                else
                {
                    Debug.Log($"An unrelated player is {playerInfo.m_name}, with ID {playerInfo.m_characterID}, and their peer is {peer.m_uid} / {peer.m_characterID}");
                }
            }
            */

            SendTargetLocation(characterID.userID);

            Utils.Message(characterID.userID, MessageHud.MessageType.Center, $"Work together to bring a {goalItemName}\nfrom {targetBossName}'s altar to the sacrificial stones.\nAt nightfall, one of you will be chosen\nto betray the others...");
        }

        private static void SendTargetLocation(long userID)
        {
            Utils.SendMapLocation(userID, $"Collect {goalItemName} here", targetPosition, Minimap.PinType.Boss);
        }

        private void SpawnTargetItem()
        {
            var targetItem = Utils.SpawnItem(goalItemID, targetPosition);

            var targetItemDrop = targetItem
                .GetComponent<ItemDrop>();

            if (targetItemDrop != null)
            {
                targetItemDrop.m_autoPickup = false;
            }
        }

        [HarmonyPatch(typeof(EnvMan), "OnEvening")]
        [HarmonyPostfix]
        static void EveningFalls()
        {
            //if (hasSetupLocations)
            //    instance.SpawnTargetItem();

            if (betrayerPlayerName == null && Player.m_localPlayer == null)
                AllocateBetrayer();
        }

        public static bool IsBetrayer(Player player)
        {
            return IsBetrayer(player.GetZDOID());
        }

        public static bool IsBetrayer(ZDOID playerID)
        {
            return playerID == betrayerPlayerID;
        }

        private static bool AllocateBetrayer()
        {
            var allPlayers = ZNet.instance.GetPlayerList();

            if (allPlayers.Count < minimumPlayersToAllocateBetrayer)
            {
                return false;
            }

            var betrayerPlayer = allPlayers[Random.Range(0, allPlayers.Count - 1)];

            betrayerPlayerID = betrayerPlayer.m_characterID;
            betrayerPlayerName = betrayerPlayer.m_name;
            PlayerPositions.peersWhoCanSeeAll.Add(betrayerPlayerID.userID);

            foreach (var player in allPlayers)
            {
                string message = IsBetrayer(player.m_characterID)
                    ? "You must <color=red>betray</color> your companions.\nStop them from retrieving the wyvern egg!"
                    : "One of your companions has been chosen\nto <color=red>betray</color> you.\nYou are not the betrayer!";

                Utils.Message(player.m_characterID.userID, MessageHud.MessageType.Center, message);
            }

            return true;
        }

        /*
        [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
        [HarmonyPrefix]
        static bool BossSpawnerInteract(OfferingBowl __instance, Humanoid user, bool hold)
        {
            // TODO: this doesn't fire ... guess it's client-side.

            // TODO: ensure this is checking the correct thing...
            if (__instance.m_name != targetBossSpawnerID)
            {
                Debug.Log($"Wrong boss spawner interact: {__instance.m_name} found, {targetBossSpawnerID} needed");
                return true;
            }

            Debug.Log("Correct boss spawner interact");
            return false;
        }

        [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.UseItem))]
        [HarmonyPrefix]
        static bool BossSpawnerUse(OfferingBowl __instance, Humanoid user, ItemDrop.ItemData item)
        {
            // TODO: this doesn't fire ... guess it's client-side.

            // TODO: ensure this is checking the correct thing...
            if (__instance.m_name != targetBossSpawnerID)
            {
                Debug.Log($"Wrong boss spawner use: {__instance.m_name} found, {targetBossSpawnerID} needed");
                return true;
            }

            Debug.Log("Correct boss spawner use");

            user.GetInventory().AddItem(goalItemID, 1, 1, 0, user.GetOwner(), user.name);

            // Play effect
            if ((bool)(Object)__instance.m_itemSpawnPoint)
                __instance.m_fuelAddedEffects.Create(__instance.m_itemSpawnPoint.position, __instance.transform.rotation);

            // If no betrayer already allocated, definitely do so now...
            if (betrayerPlayerName == null && !AllocateBetrayer())
                user.Message(MessageHud.MessageType.Center, "$msg_offerdone");

            return false; // Returning false disables the original method.
        }
        */

        // TODO: detect item dropping back at the start ... somehow

        /*
        To save world data, patch ZPackage.GetArray, only when called from within World.SaveWorldMetaData, to also save "the betrayer" if that's been set.
        To load world data, patch ZPackage.ReadInt, only when called for the 2nd time from within World.Loadworld, to also load "the betrayer" if specified there.
        */

        /*
        Have the raven introduce everything you need. Raven.Spawn and/or Raven.Talk are the methods to override there...

        Oh dear ... is he only handled locally? He does this check:
        
          private void CheckSpawn()
          {
            if ((UnityEngine.Object) Player.m_localPlayer == (UnityEngine.Object) null)
              return;

            ...
          }
        */
    }
}
