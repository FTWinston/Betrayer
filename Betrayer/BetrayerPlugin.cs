using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

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
        //const string goalItemName = "$item_dragonegg";

        const string targetBossSpawnerID = "Eikthyrnir";
        const string targetBossName = "Eikthyr"; // TODO: is there a resource string for this?
        const string goalItemID = "TrophyEikthyr";
        const string goalItemName = "$item_trophy_eikthyr";
        
        const float targetItemSpawnHeight = 5;
        const float targetItemSpawnCheckDelay = 10;
        const float targetItemSpawnCheckInterval = 30;

        private static BetrayerPlugin instance;

        // Game variables
        static bool hasSetupPositions = false;
        static Vector3 spawnPosition;
        static Vector3 targetPosition;
        //static Vector3 afterDeathPosition;
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
            livingCharacters.Clear();
            deadCharacters.Clear();
        }

        private static void SetupLocations()
        {
            Debug.Log("SetupLocations");

            spawnPosition = Utils.FindLocation("StartTemple") ?? Vector3.zero;

            targetPosition = (Utils.FindLocation(targetBossSpawnerID) ?? Vector3.zero) + Vector3.up * targetItemSpawnHeight;

            //afterDeathPosition = Utils.FindLocation("Meteorite") ?? new Vector3(0, 100, -7500);

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

        private static readonly HashSet<long> livingCharacters = new HashSet<long>();
        private static readonly HashSet<long> deadCharacters = new HashSet<long>();

        [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
        [HarmonyPrefix]
        static void DetectDeath(ZNet __instance, ZRpc rpc, ZDOID characterID)
        {
            if (characterID != ZDOID.None)
                return;

            foreach (var peer in __instance.GetPeers())
            {
                if (peer.m_rpc == rpc)
                {
                    long peerID = peer.m_characterID.userID;

                    if (peer.m_characterID != characterID && livingCharacters.Remove(peerID))
                    {
                        // This player must have just died, they're being unlinked from their character.
                        deadCharacters.Add(peerID);
                    }
                    break;
                }
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
        [HarmonyPostfix]
        static void PlayerSpawned(ZNet __instance, ZRpc rpc, ZDOID characterID)
        {
            if (characterID == ZDOID.None)
            {
                // Player died, has just been unlinked from their character.
                return;
            }

            if (!hasSetupPositions)
            {
                SetupLocations();
            }

            var peer = __instance.GetPeer(characterID.userID);

            if (deadCharacters.Contains(characterID.userID))
            {
                Debug.Log($"Player {characterID} / {peer?.m_playerName} spawned, is already dead");
                Utils.Kick(characterID.userID);
                return;
            }
            else if (livingCharacters.Contains(characterID.userID))
            {
                Debug.Log($"Player {characterID} / {peer?.m_playerName} spawned, was already alive before ... reconnected?");
            }
            else
            {
                livingCharacters.Add(characterID.userID);

                Debug.Log($"Player {characterID} / {peer?.m_playerName} spawned, got {__instance.GetNrOfPlayers()} player(s)");
            
                SendTargetLocation(characterID.userID);
            }

            Utils.Message(characterID.userID, MessageHud.MessageType.Center, $"Work together to bring a {goalItemName}\nfrom {targetBossName}'s altar to the sacrificial stones.\nAt nightfall, one of you will be chosen\nto betray the others...");
        }

        private static void SendTargetLocation(long userID)
        {
            Utils.SendMapLocation(userID, $"Collect {goalItemName} here", targetPosition, Minimap.PinType.Boss);

            // Utils.SendMapLocation(userID, $"Purgatory", afterDeathPosition, Minimap.PinType.Death);
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
        To save world data, patch ZPackage.GetArray, only when called from within World.SaveWorldMetaData, to also save "the betrayer" if that's been set.
        To load world data, patch ZPackage.ReadInt, only when called for the 2nd time from within World.Loadworld, to also load "the betrayer" if specified there.
        */
    }
}
