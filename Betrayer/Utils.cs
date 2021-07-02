using UnityEngine;

namespace Betrayer
{
    static class Utils
    {
        public static void Message(long userID, MessageHud.MessageType type, string message)
        {
            Debug.Log($"Sending message to {userID}: {message}");

            ZRoutedRpc.instance.InvokeRoutedRPC(userID, nameof(MessageHud.ShowMessage), (object)(int)type, (object)message);
        }

        public static void MessageAll(MessageHud.MessageType type, string message)
        {
            Debug.Log($"Sending message to all: {message}");

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nameof(MessageHud.ShowMessage), (object)(int)type, (object)message);
        }

        public static GameObject SpawnItem(string type, Vector3 position)
        {
            var prefab = ZNetScene.instance.GetPrefab(type);
            if (prefab == null)
            {
                Debug.Log($"Cannot spawn {type}, prefab not found");
                return null;
            }

            Debug.Log($"Spawning {type} at {position}");

            return Object.Instantiate<GameObject>(prefab, position, Quaternion.identity);
        }

        // See https://valheim.fandom.com/wiki/World_Limits
        public static Vector3? FindLocation(string locationName, bool logError = true)
        {
            if (ZoneSystem.instance.FindClosestLocation(locationName, Vector3.zero, out ZoneSystem.LocationInstance location))
            {
                return location.m_position;
            }

            if (logError)
            {
                Debug.LogError($"Failed to find {locationName} location");
            }

            return null;
        }

        public static void SendMapLocation(long userID, string label, Vector3 position, Minimap.PinType type)
        {
            Debug.Log($"Sending {label} location to user {userID}");

            ZRoutedRpc.instance.InvokeRoutedRPC(userID, "DiscoverLocationRespons", (object)label, (object)(int)type, (object)position);
        }

    }
}
