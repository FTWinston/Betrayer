using BepInEx;
using Betrayer.Powers;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private float helpMessageInterval;

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

            playerAtTargetMessage = Config.Bind("Messages", "playerAtTarget", "You have reached the target", "Message to show to a player when they are at the target.").Value;
            playerAtGoalMessage = Config.Bind("Messages", "playerAtGoal", "You have reached the spawn", "Message to show to a player when they are at the spawn, having been to the target.").Value;

            targetAreaRadiusSq = Config.Bind("General", "targetAreaRadius", 5, "The distance from the center of the target point a player must be to count as 'in' it.").Value ^ 2;
            goalAreaRadiusSq = Config.Bind("General", "spawnAreaRadius", 10, "The distance from the center of the spawn area a player must be to count as 'in' it.").Value ^ 2;

            helpMessageInterval = Config.Bind("General", "helpMessageInterval", 8f, "The time between help messages, shown to the player.").Value;
        }
        #endregion

        #region game variables
        private readonly Dictionary<long, Queue<string>> playerMessageQueues = new Dictionary<long, Queue<string>>();
        private readonly HashSet<long> livingCharacters = new HashSet<long>();
        private readonly HashSet<long> deadCharacters = new HashSet<long>();
        private readonly HashSet<long> charactersWhoReachedTarget = new HashSet<long>();

        private BetrayerGamePhase currentPhase = BetrayerGamePhase.AwaitingFirstPlayer;
        private Vector3 finalGoalPosition;
        private Vector3 targetPosition;
        //private Vector3 afterDeathPosition;
        private string betrayerPlayerName = null;
        private long betrayerUserID = 0;
        private double fixedMidnightTime;
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
        static void BeforeCharacterIDSet(ZRpc rpc, ZDOID characterID)
        {
            if (characterID == ZDOID.None)
                instance.PlayerKilled(rpc);
        }

        [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
        [HarmonyPostfix]
        static void CharacterIDSet(ZDOID characterID)
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

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        [HarmonyPostfix]
        static void Disconnected(ZNetPeer peer)
        {
            if (ZNet.instance.GetNrOfPlayers() == 0)
                instance.LastPlayerQuit();
        }
        #endregion

        #region plugin logic
        public void Awake()
        {
            Harmony.CreateAndPatchAll(GetType());
            Harmony.CreateAndPatchAll(typeof(PlayerPositions));

            LoadConfiguration();

            InvokeRepeating(nameof(SendHelpMessages), helpMessageInterval, helpMessageInterval);
        }

        public void FixedUpdate()
        {
            if (currentPhase == BetrayerGamePhase.AwaitingFirstPlayer)
                return;

            if (currentPhase == BetrayerGamePhase.TravelBackToGoal)
            {
                bool justReachedGoal = false;
                foreach (var playerInfo in ZNet.instance.GetPlayerList())
                {
                    if ((playerInfo.m_position - finalGoalPosition).HorizMagnitudeSq() < goalAreaRadiusSq)
                    {
                        Utils.Message(playerInfo.m_characterID.userID, MessageHud.MessageType.Center, playerAtGoalMessage);

                        if (!justReachedGoal)
                        {
                            justReachedGoal = true;
                            GoalReached(playerInfo);
                        }
                    }
                }

                ZNet.instance.SetNetTime(fixedMidnightTime);
            }

            bool justReachedTarget = false;
            foreach (var playerInfo in ZNet.instance.GetPlayerList())
            {
                // Surely setting this true every tick is overkill, but let's try it out...
                ZDOMan.instance.GetZDO(playerInfo.m_characterID).Set("pvp", true);

                if ((playerInfo.m_position - targetPosition).HorizMagnitudeSq() < targetAreaRadiusSq)
                {
                    if (charactersWhoReachedTarget.Add(playerInfo.m_characterID.userID))
                    {
                        Utils.Message(playerInfo.m_characterID.userID, MessageHud.MessageType.Center, playerAtTargetMessage);

                        if (!justReachedTarget)
                        {
                            justReachedTarget = true;
                            TargetReached(playerInfo);
                        }
                    }
                }
            }
        }

        private void SendHelpMessages()
        {
            foreach (var entry in playerMessageQueues)
            {
                if (entry.Value.Count == 0)
                    continue;

                var message = entry.Value.Dequeue();

                // A "null" message is a kick, to make it clear the game has ended.
                if (message == null)
                {
                    Utils.Kick(entry.Key);
                }
                else
                {
                    Utils.ChatMessage(entry.Key, "Info", message);
                }
            }
        }

        private void QueueHelpMessages(long userID, params string[] messages)
        {
            if (!playerMessageQueues.TryGetValue(userID, out var queue))
            {
                queue = new Queue<string>();
                playerMessageQueues.Add(userID, queue);
            }

            foreach (var message in messages)
            {
                queue.Enqueue(message);
            }
        }

        private void QueueMessagesForAll(string[] betrayerMessages, string[] nonBetrayerMessages)
        {
            foreach (var player in ZNet.instance.GetPlayerList())
            {
                var messages = IsBetrayer(player)
                    ? betrayerMessages
                    : nonBetrayerMessages;

                QueueHelpMessages(player.m_characterID.userID, messages);
            }
        }

        private void QueueKickAllAfterMessages()
        {
            var nullArray = new string[] { null };

            foreach (var player in ZNet.instance.GetPlayerList())
            {
                QueueHelpMessages(player.m_characterID.userID, nullArray);
            }
        }

        private void AwaitFirstPlayer()
        {
            Debug.Log($"Betrayer started, waiting for first player");

            currentPhase = BetrayerGamePhase.AwaitingFirstPlayer;
            playerMessageQueues.Clear();

            betrayerPlayerName = null;
            betrayerUserID = 0;

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

        private void TargetReached(ZNet.PlayerInfo player)
        {
            Debug.Log($"Player {player.m_name} / {player.m_characterID} reached the target for the first time");

            currentPhase = BetrayerGamePhase.TravelBackToGoal;
            QueueMessagesForAll(reachedTargetMessages, reachedTargetMessagesBetrayer);

            EternalNightPlugin.GetDateInfo(out _, out var day, out var dayLengthSec, out _);
            fixedMidnightTime = day * dayLengthSec;

            //EternalNightPlugin.instance.Activate();
            //Utils.StartEvent("foresttrolls", targetPosition);
        }

        private void GoalReached(ZNet.PlayerInfo player)
        {
            Debug.Log($"Player {player.m_name} / {player.m_characterID} returned to the spawn point and defeated the betrayer");
            currentPhase = BetrayerGamePhase.Finished;

            QueueMessagesForAll(goalAchievedMessages, goalAchievedMessagesBetrayer);
            QueueKickAllAfterMessages();
        }

        private void BetrayerKilled()
        {
            Debug.Log("The betrayer was killed");
            currentPhase = BetrayerGamePhase.Finished;
            
            QueueMessagesForAll(betrayerKilledMessagesBetrayer, betrayerKilledMessages);
            QueueKickAllAfterMessages();
        }

        private void AllExceptBetrayerKilled()
        {
            Debug.Log("The betrayer killed all other players");
            currentPhase = BetrayerGamePhase.Finished;

            QueueMessagesForAll(playersKilledMessagesBetrayer, playersKilledMessages);
            QueueKickAllAfterMessages();
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

                Debug.Log($"Player {characterID} / {peer?.m_playerName} spawned, they're new");

                SendTargetLocation(characterID.userID);
                QueueHelpMessages(characterID.userID, welcomeMessages);
            }
        }

        private void PlayerKilled(ZRpc rpc)
        {
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_rpc == rpc)
                {
                    long peerID = peer.m_characterID.userID;

                    if (livingCharacters.Remove(peerID))
                    {
                        deadCharacters.Add(peerID);

                        if (IsBetrayer(peerID))
                        {
                            BetrayerKilled();
                        }
                        else if (!livingCharacters.Any(id => !IsBetrayer(id)))
                        {
                            AllExceptBetrayerKilled();
                        }
                    }
                    break;
                }
            }
        }

        private bool AllocateBetrayer()
        {
            if (currentPhase >= BetrayerGamePhase.TravelToTarget || betrayerPlayerName != null)
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

            betrayerUserID = betrayerPlayer.m_characterID.userID;
            betrayerPlayerName = betrayerPlayer.m_name;

            PlayerPositions.canPlayerSeeOthers = userID => userID == betrayerUserID;
            PlayerPositions.canPlayerBeSeenByOthers = _ => true;

            foreach (var playerInfo in allPlayers)
            {
                var messages = IsBetrayer(playerInfo)
                    ? allocationMessagesBetrayer
                    : allocationMessages;

                QueueHelpMessages(playerInfo.m_characterID.userID, messages);
            }

            return true;
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

        public bool IsBetrayer(long userID)
        {
            return userID == betrayerUserID;
        }

        public bool IsBetrayer(ZNet.PlayerInfo player)
        {
            return IsBetrayer(player.m_characterID.userID);
        }

        /*
        To save world data, patch ZPackage.GetArray, only when called from within World.SaveWorldMetaData, to also save "the betrayer" if that's been set.
        To load world data, patch ZPackage.ReadInt, only when called for the 2nd time from within World.Loadworld, to also load "the betrayer" if specified there.
        */

        private void LastPlayerQuit()
        {
            Debug.Log("Last player quit, attempting to delete the world");

            var worldName = ZNet.instance.GetWorldName();
            World.RemoveWorld(worldName);
            Application.Quit();

            /*
            var world = World.GetCreateWorld(worldName);

            var worldField = typeof(ZNet).GetField("m_world", BindingFlags.NonPublic | BindingFlags.Static);
            worldField.SetValue(null, world);

            WorldGenerator.Initialize(world);
            ZNet.ResetServerHost();
            */
        }

        #endregion plugin logic
    }
}
