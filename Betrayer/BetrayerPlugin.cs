using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace Betrayer
{
    [BepInPlugin("com.ftwinston.valheim.betrayer", "Betrayer", "0.1.0.0")]
    public class BetrayerPlugin : BaseUnityPlugin
    {
        public enum BetrayerGamePhase
        {
            AwaitingFirstPlayer,
            NotYetAllocated,
            TravelToTarget,
            TravelBackToGoal,
            Finished,
        }

        private static BetrayerPlugin instance;
        public BetrayerPlugin()
        {
            instance = this;
        }

        #region config settings
        private int minimumPlayersToAllocateBetrayer;
        private string targetLocationID;
        private string[] welcomeMessages;
        private string[] allocationMessages;
        private string[] allocationMessagesBetrayer;
        private string[] reachedTargetMessages;
        private string[] reachedTargetMessagesBetrayer;
        private string[] goalAchievedMessages;
        private string[] goalAchievedMessagesBetrayer;
        private string[] betrayerKilledMessages;
        private string[] betrayerKilledMessagesBetrayer;
        private string[] playersKilledMessages;
        private string[] playersKilledMessagesBetrayer;
        private string playerAtTargetMessage;
        private string playerAtGoalMessage;
        private float targetAreaRadiusSq;
        private float goalAreaRadiusSq;

        private void LoadConfiguration()
        {
            minimumPlayersToAllocateBetrayer = Config.Bind("General", "minimumPlayersToAllocate", 3, "This many players must be present before a betrayer will be allocated.").Value;
            targetLocationID = Config.Bind("General", "destinationLocationID", "Dragonqueen", "The ID of a game location that players must travel to. Suggested values are GDKing (easy), Bonemass, Dragonqueen or GoblinKing. See https://valheim.fandom.com/wiki/World_Limits for a full list.").Value;

            welcomeMessages = Config.BindArray("Messages", "welcome", new[] { "This server is running <color=red>Betrayer</color>.\nPlay with a <color=orange>new character</color>!", "When you die, you will be kicked.\n<color=orange>Your gear will probably be lost.</color>", "Work together to reach a distant target,\nthen return back to the spawn point.", "At sunset, one player will be chosen\nto <color=red>betray</color> the others.", "The <color=red>betrayer</color> should try to kill all the others.", "Please <b>enable PVP</b> manually for now." }, "Messages to show to all players when they first join the game.");
            allocationMessages = Config.BindArray("Messages", "allocation", new[] { "A <color=red>betrayer</color> has been chosen.\nYou <b>are not</b> the <color=red>betrayer</color>.", "The <color=red>betrayer</color> can always see other players on the map." }, "Messages to show to player (except the betrayer) when the betrayer is allocated.");
            allocationMessagesBetrayer = Config.BindArray("Messages", "allocationBetrayer", new[] { "A <color=red>betrayer</color> has been chosen.\nYou <b>are</b> the <color=red>betrayer</color>.", "Only you can see other players on the map." }, "Messages to show to the betrayer when they are allocated.");
            reachedTargetMessages = Config.BindArray("Messages", "reachedTarget", new[] { "The target has been reached, and night has fallen.", "Return to the spawn point\nbefore the <color=red>betrayer</color> can stop you!" }, "Messages to show to players (except the betrayer) when one of them first reaches their target.");
            reachedTargetMessagesBetrayer = Config.BindArray("Messages", "reachedTargetBetrayer", new[] { "The target has been reached, and night has fallen.", "Stop the other players from\nreturning to the spawn point!" }, "Messages to show to the betrayer when one of the players first reaches their target.");
            goalAchievedMessages = Config.BindArray("Messages", "goalAchieved", new[] { "The betrayer has been defeated.\n<b>You win!</b>" }, "Messages to show to players (except the betrayer) when they achieve their goal.");
            goalAchievedMessagesBetrayer = Config.BindArray("Messages", "goalAchievedBetrayer", new[] { "The betrayer has been defeated.\n<b>You lose!</b>" }, "Messages to show to the betrayer when the other players achieve their goal.");
            betrayerKilledMessages = Config.BindArray("Messages", "betrayerKilled", new[] { "The betrayer has been killed.\n<b>You win!</b>" }, "Messages to show to players (except the betrayer) when the betrayer has been killed.");
            betrayerKilledMessagesBetrayer = Config.BindArray("Messages", "betrayerKilledBetrayer", new[] { "The betrayer has been killed.\n<b>You lose!</b>" }, "Messages to show to the betrayer when they have been killed.");
            playersKilledMessages = Config.BindArray("Messages", "playersKilled", new[] { "The betrayer has killed all the other players.\n<b>You lose!</b>" }, "Messages to show to players (except the betrayer) when all other players have been killed.");
            playersKilledMessagesBetrayer = Config.BindArray("Messages", "playersKilledBetrayer", new[] { "The betrayer has killed all the other players.\n<b>You win!</b>" }, "Messages to show to the betrayer when all other players have been killed.");

            playerAtTargetMessage = Config.Bind("Message", "playerAtTarget", "You have reached the target", "Message to show to a player when they are at the target.").Value;
            playerAtGoalMessage = Config.Bind("Message", "playerAtGoal", "You have reached the spawn", "Message to show to a player when they are at the spawn, having been to the target.").Value;

            targetAreaRadiusSq = Config.Bind("General", "targetAreaRadius", 5, "The distance from the center of the target point a player must be to count as 'in' it.").Value ^ 2;
            goalAreaRadiusSq = Config.Bind("General", "spawnAreaRadius", 10, "The distance from the center of the spawn area a player must be to count as 'in' it.").Value ^ 2;
        }
        #endregion

        //const string targetBossName = "Moder"; // TODO: is there a resource string for this?
        const string targetBossName = "Eikthyr"; // TODO: is there a resource string for this?

        #region game variables
        private readonly Dictionary<long, Queue<string>> playerMessageQueues = new Dictionary<long, Queue<string>>();
        private readonly HashSet<long> livingCharacters = new HashSet<long>();
        private readonly HashSet<long> deadCharacters = new HashSet<long>();

        private BetrayerGamePhase currentPhase = BetrayerGamePhase.AwaitingFirstPlayer;
        private Vector3 finalGoalPosition;
        private Vector3 targetPosition;
        //private Vector3 afterDeathPosition;
        private string betrayerPlayerName = null;
        private ZDOID betrayerPlayerID = ZDOID.None;
        #endregion game variables

        #region patches
        [HarmonyPatch(typeof(Game), "Start")]
        [HarmonyPostfix]
        static void GameStart() => instance.AwaitFirstPlayer();

        [HarmonyPatch(typeof(Player), nameof(Player.IsPVPEnabled))]
        [HarmonyPrefix]
        static bool IsPVPEnabled(ref bool __result)
        {
            __result = true;
            return true;
        }

        [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
        [HarmonyPrefix]
        static void CharacterIDSet_Before(ZRpc rpc, ZDOID characterID)
        {
            if (characterID == ZDOID.None)
                instance.PlayerKilled(rpc, characterID);
        }

        [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
        [HarmonyPostfix]
        static void CharacterIDSet_After(ZRpc rpc, ZDOID characterID)
        {
            if (characterID != ZDOID.None)
                instance.PlayerSpawned(characterID);
        }

        [HarmonyPatch(typeof(EnvMan), "OnEvening")]
        [HarmonyPostfix]
        static void EveningFalls() => instance.AllocateBetrayer();

        /*
        [HarmonyPatch(typeof(ItemStand), "RPC_SetVisualItem")]
        [HarmonyPrefix]
        static void ItemPlacedOnStand2(ItemStand __instance, string itemName, int variant)
        {
            Debug.Log($"Detected {itemName} / {variant} being placed on a stand at {__instance.transform.position}, 2nd method");
        }

        [HarmonyPatch(typeof(ItemStand), "SetVisualItem")]
        [HarmonyPrefix]
        static void ItemPlacedOnStand(ItemStand __instance, string itemName, int variant)
        {
            Debug.Log($"Detected {itemName} / {variant} being placed on a stand at {__instance.transform.position}");
        }
        */
        #endregion

        #region plugin logic
        public void Awake()
        {
            Harmony.CreateAndPatchAll(GetType());
            Harmony.CreateAndPatchAll(typeof(PlayerPositions));
            LoadConfiguration();
        }

        public void FixedUpdate()
        {
            foreach (var playerInfo in ZNet.instance.GetPlayerList())
            {
                if ((playerInfo.m_position - finalGoalPosition).HorizMagnitudeSq() < goalAreaRadiusSq)
                {
                    Utils.Message(playerInfo.m_characterID.userID, MessageHud.MessageType.Center, "You are in the spawn area");
                }

                else if ((playerInfo.m_position - targetPosition).HorizMagnitudeSq() < targetAreaRadiusSq)
                {
                    Utils.Message(playerInfo.m_characterID.userID, MessageHud.MessageType.Center, "You are in the target area");
                }
            }
        }

        private void AwaitFirstPlayer()
        {
            Debug.Log($"Betrayer started, waiting for first player");

            currentPhase = BetrayerGamePhase.AwaitingFirstPlayer;
            playerMessageQueues.Clear();

            betrayerPlayerName = null;
            betrayerPlayerID = ZDOID.None;

            finalGoalPosition = Vector3.zero;
            targetPosition = Vector3.zero;

            PlayerPositions.canPlayerSeeOthers = _ => true;
            PlayerPositions.canPlayerBeSeenByOthers = _ => true;

            livingCharacters.Clear();
            deadCharacters.Clear();
        }

        private void SetupLocations()
        {
            if (currentPhase >= BetrayerGamePhase.NotYetAllocated)
                return;

            Debug.Log("Setting up game locations");

            currentPhase = BetrayerGamePhase.NotYetAllocated;

            finalGoalPosition = Utils.FindLocation("StartTemple") ?? Vector3.zero;

            targetPosition = (Utils.FindLocation(targetLocationID) ?? Vector3.zero);

            //afterDeathPosition = Utils.FindLocation("Meteorite") ?? new Vector3(0, 100, -7500);

            //InvokeRepeating(nameof(SpawnTargetItem), targetItemSpawnCheckDelay, targetItemSpawnCheckInterval);
        }

        private bool AllocateBetrayer()
        {
            if (betrayerPlayerName != null || currentPhase >= BetrayerGamePhase.TravelToTarget)
                return false;

            var allPlayers = ZNet.instance.GetPlayerList();

            if (allPlayers.Count < minimumPlayersToAllocateBetrayer)
            {
                Debug.Log($"Not enough players to allocate betrayer, got {allPlayers.Count} but need {minimumPlayersToAllocateBetrayer}");
                return false;
            }

            Debug.Log($"Allocating betrayer");
            currentPhase = BetrayerGamePhase.TravelToTarget;

            var betrayerPlayer = allPlayers[Random.Range(0, allPlayers.Count - 1)];

            betrayerPlayerID = betrayerPlayer.m_characterID;
            betrayerPlayerName = betrayerPlayer.m_name;

            PlayerPositions.canPlayerSeeOthers = userID => userID == betrayerPlayerID.userID;
            PlayerPositions.canPlayerBeSeenByOthers = _ => true;

            foreach (var player in allPlayers)
            {
                string message = IsBetrayer(player.m_characterID)
                    ? "You must <color=red>betray</color> your companions.\nStop them from retrieving the wyvern egg!"
                    : "One of your companions has been chosen\nto <color=red>betray</color> you.\nYou are not the betrayer!";

                Utils.Message(player.m_characterID.userID, MessageHud.MessageType.Center, message);
            }

            return true;
        }

        private void PlayerKilled(ZRpc rpc, ZDOID characterID)
        {
            foreach (var peer in ZNet.instance.GetPeers())
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

        private void PlayerSpawned(ZDOID characterID)
        {
            if (currentPhase == BetrayerGamePhase.AwaitingFirstPlayer)
            {
                SetupLocations();
            }

            var peer = ZNet.instance.GetPeer(characterID.userID);

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

                Debug.Log($"Player {characterID} / {peer?.m_playerName} spawned, got {ZNet.instance.GetNrOfPlayers()} player(s)");

                SendTargetLocation(characterID.userID);
            }

            //Utils.Message(characterID.userID, MessageHud.MessageType.Center, $"Work together to bring a {goalItemName}\nfrom {targetBossName}'s altar to the sacrificial stones.\nAt nightfall, one of you will be chosen\nto betray the others...");
        }

        private void SendTargetLocation(long userID)
        {
            Utils.SendMapLocation(userID, $"Travel here", targetPosition, Minimap.PinType.Boss);

            // Utils.SendMapLocation(userID, $"Purgatory", afterDeathPosition, Minimap.PinType.Death);
        }

        /*
        private void SpawnTargetItem()
        {
            var targetItem = Utils.SpawnItem(goalItemID, targetPosition);

            var targetItemDrop = targetItem
                .GetComponent<ItemDrop>();

            if (targetItemDrop != null)
            {
                Debug.Log("Setting auto-pickup to false");
                targetItemDrop.m_autoPickup = false;
                targetItemDrop.m_itemData.m_crafterName = "Betrayer";
            }
        }
        */

        public bool IsBetrayer(Player player)
        {
            return IsBetrayer(player.GetZDOID());
        }

        public bool IsBetrayer(ZDOID playerID)
        {
            return playerID == betrayerPlayerID;
        }

        /*
        To save world data, patch ZPackage.GetArray, only when called from within World.SaveWorldMetaData, to also save "the betrayer" if that's been set.
        To load world data, patch ZPackage.ReadInt, only when called for the 2nd time from within World.Loadworld, to also load "the betrayer" if specified there.
        */
        #endregion plugin logic
    }
}
