using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MLAPI.Configuration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MLAPI.RuntimeTests
{
    /// <summary>
    /// Provides helpers for running multi instance tests.
    /// </summary>
    public static class MultiInstanceHelpers
    {
        private static List<NetworkManager> s_NetworkManagerInstances = new List<NetworkManager>();
        private static bool s_IsStarted;
        private static int s_ClientCount;
        private static int s_OriginalTargetFrameRate = -1;

        public static List<NetworkManager> NetworkManagerInstances => s_NetworkManagerInstances;

        /// <summary>
        /// Creates NetworkingManagers and configures them for use in a multi instance setting.
        /// </summary>
        /// <param name="clientCount">The amount of clients</param>
        /// <param name="server">The server NetworkManager</param>
        /// <param name="clients">The clients NetworkManagers</param>
        /// <param name="targetFrameRate">The targetFrameRate of the Unity engine to use while the multi instance helper is running. Will be reset on shutdown.</param>
        public static bool Create(int clientCount, out NetworkManager server, out NetworkManager[] clients, int targetFrameRate = 60)
        {
            s_NetworkManagerInstances = new List<NetworkManager>();

            CreateNewClients(clientCount, out clients);

            {
                // Create gameObject
                var go = new GameObject("NetworkManager - Server");

                // Create networkManager component
                server = go.AddComponent<NetworkManager>();
                NetworkManagerInstances.Insert(0, server);

                // Set the NetworkConfig
                server.NetworkConfig = new NetworkConfig()
                {
                    // Set the current scene to prevent unexpected log messages which would trigger a failure
                    RegisteredScenes = new List<string>() { SceneManager.GetActiveScene().name },
                    // Set transport
                    NetworkTransport = go.AddComponent<SIPTransport>()
                };
            }

            s_OriginalTargetFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = targetFrameRate;

            return true;
        }

        /// <summary>
        /// Used to add a client to the already existing list of clients
        /// </summary>
        /// <param name="clientCount">The amount of clients</param>
        /// <param name="clients"></param>
        /// <returns></returns>
        public static bool CreateNewClients(int clientCount, out NetworkManager[] clients)
        {
            clients = new NetworkManager[clientCount];

            for (int i = 0; i < clientCount; i++)
            {
                // Create gameObject
                var go = new GameObject("NetworkManager - Client - " + i);
                // Create networkManager component
                clients[i] = go.AddComponent<NetworkManager>();

                // Set the NetworkConfig
                clients[i].NetworkConfig = new NetworkConfig()
                {
                    // Set the current scene to prevent unexpected log messages which would trigger a failure
                    RegisteredScenes = new List<string>() { SceneManager.GetActiveScene().name },
                    // Set transport
                    NetworkTransport = go.AddComponent<SIPTransport>()
                };
            }

            NetworkManagerInstances.AddRange(clients);
            return true;
        }

        /// <summary>
        /// Stops one single client and makes sure to cleanup any static variables in this helper
        /// </summary>
        /// <param name="clientToStop"></param>
        public static void StopOneClient(NetworkManager clientToStop)
        {
            clientToStop.StopClient();
            Object.Destroy(clientToStop.gameObject);
            NetworkManagerInstances.Remove(clientToStop);
        }

        /// <summary>
        /// Should always be invoked when finished with a single unit test
        /// (i.e. during TearDown)
        /// </summary>
        public static void Destroy()
        {
            if (s_IsStarted == false)
            {
                return;
            }

            s_IsStarted = false;

            // Shutdown the server which forces clients to disconnect
            foreach (var networkManager in NetworkManagerInstances)
            {
                if (networkManager.IsHost)
                {
                    networkManager.StopHost();
                }
                else if (networkManager.IsServer)
                {
                    networkManager.StopServer();
                }
                else if (networkManager.IsClient)
                {
                    networkManager.StopClient();
                }
            }

            // Destroy the network manager instances
            foreach (var networkManager in NetworkManagerInstances)
            {
                Object.Destroy(networkManager.gameObject);
            }

            NetworkManagerInstances.Clear();

            CoroutineHelper.Cleanup();

            Application.targetFrameRate = s_OriginalTargetFrameRate;
        }

        /// <summary>
        /// Starts NetworkManager instances created by the Create method.
        /// </summary>
        /// <param name="host">Whether or not to create a Host instead of Server</param>
        /// <param name="server">The Server NetworkManager</param>
        /// <param name="clients">The Clients NetworkManager</param>
        public static bool Start(bool host, NetworkManager server, NetworkManager[] clients)
        {
            if (s_IsStarted)
            {
                throw new InvalidOperationException("MultiInstanceHelper already started. Did you forget to Destroy?");
            }

            s_IsStarted = true;
            s_ClientCount = clients.Length;

            if (host)
            {
                server.StartHost();
            }
            else
            {
                server.StartServer();
            }

            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].StartClient();
            }

            return true;
        }

        /// <summary>
        /// Normally we would only allow player prefabs to be set to a prefab. Not runtime created objects.
        /// In order to prevent having a Resource folder full of a TON of prefabs that we have to maintain,
        /// MultiInstanceHelper has a helper function that lets you mark a runtime created object to be
        /// treated as a prefab by the MLAPI. That's how we can get away with creating the player prefab
        /// at runtime without it being treated as a SceneObject or causing other conflicts with the MLAPI.
        /// </summary>
        /// <param name="networkObject">The networkObject to be treated as Prefab</param>
        /// <param name="globalObjectIdHash">The GlobalObjectId to force</param>
        public static void MakeNetworkedObjectTestPrefab(NetworkObject networkObject, uint globalObjectIdHash = default)
        {
            // Set a globalObjectId for prefab
            if (globalObjectIdHash != default)
            {
                networkObject.TempGlobalObjectIdHashOverride = globalObjectIdHash;
            }

            // Force generation
            networkObject.GenerateGlobalObjectIdHash();

            // Prevent object from being snapped up as a scene object
            networkObject.IsSceneObject = false;
        }

        // We use GameObject instead of SceneObject to be able to keep hierarchy
        public static void MarkAsSceneObjectRoot(GameObject networkObjectRoot, NetworkManager server, NetworkManager[] clients)
        {
            networkObjectRoot.name += " - Server";

            NetworkObject[] serverNetworkObjects = networkObjectRoot.GetComponentsInChildren<NetworkObject>();

            for (int i = 0; i < serverNetworkObjects.Length; i++)
            {
                serverNetworkObjects[i].NetworkManagerOwner = server;
            }

            for (int i = 0; i < clients.Length; i++)
            {
                GameObject root = Object.Instantiate(networkObjectRoot);
                root.name += " - Client - " + i;

                NetworkObject[] clientNetworkObjects = root.GetComponentsInChildren<NetworkObject>();

                for (int j = 0; j < clientNetworkObjects.Length; j++)
                {
                    clientNetworkObjects[j].NetworkManagerOwner = clients[i];
                }
            }
        }

        /// <summary>
        /// Waits on the client side to be connected.
        /// </summary>
        /// <param name="client">The client</param>
        /// <param name="result">The result. If null, it will automatically assert</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator WaitForClientConnected(NetworkManager client, CoroutineResultWrapper<bool> result = null, int maxFrames = 64)
        {
            yield return WaitForClientsConnected(new NetworkManager[] { client }, result, maxFrames);
        }

        /// <summary>
        /// Similar to WaitForClientConnected, this waits for multiple clients to be connected.
        /// </summary>
        /// <param name="clients">The clients to be connected</param>
        /// <param name="result">The result. If null, it will automatically assert<</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        /// <returns></returns>
        public static IEnumerator WaitForClientsConnected(NetworkManager[] clients, CoroutineResultWrapper<bool> result = null, int maxFrames = 64)
        {
            Debug.Log("007");
            // Make sure none are the host client
            foreach (var client in clients)
            {
                if (client.IsServer)
                {
                    Debug.Log("008");
                    throw new InvalidOperationException("Cannot wait for connected as server");
                }
            }

            Debug.Log("009");
            var startFrameNumber = Time.frameCount;
            var allConnected = true;
            while (Time.frameCount - startFrameNumber <= maxFrames)
            {
                allConnected = true;
                foreach (var client in clients)
                {
                    if (!client.IsConnectedClient)
                    {
                        allConnected = false;
                        break;
                    }
                }
                if (allConnected)
                {
                    break;
                }
                Debug.Log("00A - (" + (Time.frameCount - startFrameNumber) + ")");
                yield return CoroutineHelper.WaitOneFrame();
            }

            if (result != null)
            {
                Debug.Log("00B");
                result.Result = allConnected;
            }
            else
            {
                Debug.Log("00C");
                for (var i = 0; i < clients.Length; ++i)
                {
                    var client = clients[i];
                    // Logging i+1 because that's the local client ID they'll get (0 is server)
                    // Can't use client.LocalClientId because that doesn't get assigned until IsConnectedClient == true,
                    Assert.True(client.IsConnectedClient, $"Client {i+1} never connected");
                }
            }
        }

        /// <summary>
        /// Waits on the server side for 1 client to be connected
        /// </summary>
        /// <param name="server">The server</param>
        /// <param name="result">The result. If null, it will automatically assert</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator WaitForClientConnectedToServer(NetworkManager server, CoroutineResultWrapper<bool> result = null, int maxFrames = 64)
        {
            yield return WaitForClientsConnectedToServer(server, server.IsHost ? s_ClientCount + 1 : s_ClientCount, result, maxFrames);
        }

        /// <summary>
        /// Waits on the server side for 1 client to be connected
        /// </summary>
        /// <param name="server">The server</param>
        /// <param name="result">The result. If null, it will automatically assert</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator WaitForClientsConnectedToServer(NetworkManager server, int clientCount = 1, CoroutineResultWrapper<bool> result = null, int maxFrames = 64)
        {
            Debug.Log("00D");
            if (!server.IsServer)
            {
                throw new InvalidOperationException("Cannot wait for connected as client");
            }

            var startFrameNumber = Time.frameCount;

            Debug.Log("00E");
            while (Time.frameCount - startFrameNumber <= maxFrames && server.ConnectedClients.Count != clientCount)
            {
                Debug.Log("00F - (" + (Time.frameCount - startFrameNumber) + ")");
                yield return CoroutineHelper.WaitOneFrame();
            }

            var res = server.ConnectedClients.Count == clientCount;

            if (result != null)
            {
                Debug.Log("010");
                result.Result = res;
            }
            else
            {
                Debug.Log("011");
                Assert.True(res, "A client never connected to server");
            }
        }

        /// <summary>
        /// Gets a NetworkObject instance as it's represented by a certain peer.
        /// </summary>
        /// <param name="networkObjectId">The networkObjectId to get</param>
        /// <param name="representation">The representation to get the object from</param>
        /// <param name="result">The result</param>
        /// <param name="failIfNull">Whether or not to fail if no object is found and result is null</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator GetNetworkObjectByRepresentation(ulong networkObjectId, NetworkManager representation, CoroutineResultWrapper<NetworkObject> result, bool failIfNull = true, int maxFrames = 64)
        {
            if (result == null)
            {
                throw new ArgumentNullException("Result cannot be null");
            }

            yield return CoroutineHelper.WaitUntilConditionWithTimeout(() =>
            {
                return !representation.SpawnManager.SpawnedObjects.All(x => x.Value.NetworkObjectId != networkObjectId);
            }, maxFramesBeforeTimeout: maxFrames);

            result.Result = representation.SpawnManager.SpawnedObjects.First(x => x.Value.NetworkObjectId == networkObjectId).Value;

            if (failIfNull && result.Result == null)
            {
                Assert.Fail("NetworkObject could not be found");
            }
        }

        /// <summary>
        /// Gets a NetworkObject instance as it's represented by a certain peer.
        /// </summary>
        /// <param name="predicate">The predicate used to filter for your target NetworkObject</param>
        /// <param name="representation">The representation to get the object from</param>
        /// <param name="result">The result</param>
        /// <param name="failIfNull">Whether or not to fail if no object is found and result is null</param>
        /// <param name="maxFrames">The max frames to wait for</param>
        public static IEnumerator GetNetworkObjectByRepresentation(Func<NetworkObject, bool> predicate, NetworkManager representation, CoroutineResultWrapper<NetworkObject> result, bool failIfNull = true, int maxFrames = 64)
        {
            if (result == null)
            {
                throw new ArgumentNullException("Result cannot be null");
            }

            if (predicate == null)
            {
                throw new ArgumentNullException("Predicate cannot be null");
            }

            yield return CoroutineHelper.WaitUntilConditionWithTimeout(() =>
            {
                return representation.SpawnManager.SpawnedObjects.Any(x => predicate(x.Value));
            }, maxFramesBeforeTimeout: maxFrames);

            result.Result = representation.SpawnManager.SpawnedObjects.FirstOrDefault(x => predicate(x.Value)).Value;

            if (failIfNull && result.Result == null)
            {
                Assert.Fail("NetworkObject could not be found");
            }
        }
    }
}
