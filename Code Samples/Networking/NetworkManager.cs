using Gamble.BattleCards.Infrastructure.Network;
using Gamble.BattleCards.Logging;
using Gamble.BattleCards.Online;
using Gamble.BattleCards.UI;
using Gamble.Utils;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace Gamble.BattleCards.Infrastructure
{
    /// <summary>
    /// Handles server / client traffic and platform network logic
    /// </summary>
    public class NetworkManager : Singleton<NetworkManager>
    {
        private enum ConnectionAttemptStatus
        {
            SUCCESS,
            PLATFORM_FAIL,
            NETWORK_FAIL
        }

        public event Action OnNetworkInitialised;
        public event Action OnNetworkShutdown;

        public event Action<ulong, bool> OnPlayerJoined;

        public event Action<ulong> OnPlayerLeft;

        public event Action<List<ulong>, bool> OnLobbyEntered;

        public event Action OnLobbyLeft;

        public event Action<bool> OnLocalClientConnectionChanged;

        // TODO: Add a subscription to this event in this class
        public event Action<bool> OnNetworkMigrationCompleted;

        public Unity.Netcode.NetworkManager Network { get; private set; } = null;
        public MigrationService MigrationHandler { get; set; } = null;
        public SessionService SessionHandler { get; set; } = null;
        public ChatService ChatService { get; set; } = null;

        public Lobby? currentLobby { get; private set; } = null;
        public bool IsConnected => Network.IsConnectedClient;
        public SteamId LocalSteamID => SteamClient.SteamId;
        public ulong LocalClientID => Network.LocalClientId;

        [SerializeField] private Unity.Netcode.NetworkManager networkPrefab;
        [SerializeField] private Unity.Netcode.NetworkTransport transport;

        [SerializeField] private MigrationService migrationServicePrefab;
        [SerializeField] private SessionService sessionServicePrefab;
        [SerializeField] private ChatService chatServicePrefab;

        private CancellationTokenSource cts;
        private bool processRunning = false;

        private void OnDestroy()
        {
            Shutdown();
        }

        public float CurrentPing
        {
            get
            {
                if (Network != null)
                {
                    if (Network.IsClient)
                    {
                        // For a client, get ping from the network client
                        return transport.GetCurrentRtt(Network.LocalClient.ClientId);
                    }
                    else if (Network.IsServer)
                    {
                        // For the server, get the ping of all connected clients
                        float maxPing = -1f;
                        foreach (NetworkClient client in Network.ConnectedClientsList)
                        {
                            float clientPing = transport.GetCurrentRtt(client.ClientId);
                            maxPing = Mathf.Max(maxPing, clientPing);
                        }
                        return maxPing;
                    }
                }

                return -1;
            }
        }

        private void UnregisterNetworkCallbacks()
        {
            if (Network != null)
            {
                Network.OnServerStarted -= Network_OnServerStarted;
                Network.OnServerStopped -= Network_OnServerStopped;
                Network.OnClientStarted -= Network_OnClientStarted;
                Network.OnClientStopped -= Network_OnClientStopped;
            }
        }

        private void RegisterTransportCallbacks()
        {
            SteamMatchmaking.OnLobbyCreated += Steam_OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += Steam_OnLobbyEntered;

            SteamMatchmaking.OnLobbyMemberJoined += Steam_OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += Steam_OnLobbyMemberLeave;
            SteamMatchmaking.OnLobbyMemberDisconnected += Steam_OnLobbyMemberDisconnected;

            SteamMatchmaking.OnLobbyInvite += Steam_OnLobbyInvite;
            SteamMatchmaking.OnLobbyGameCreated += Steam_OnLobbyGameCreated;
            SteamFriends.OnGameLobbyJoinRequested += SteamFriends_OnGameLobbyJoinRequested;
        }

        private void UnregisterTransportCallbacks()
        {
            SteamMatchmaking.OnLobbyCreated -= Steam_OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered -= Steam_OnLobbyEntered;

            SteamMatchmaking.OnLobbyMemberJoined -= Steam_OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= Steam_OnLobbyMemberLeave;
            SteamMatchmaking.OnLobbyMemberDisconnected -= Steam_OnLobbyMemberDisconnected;

            SteamMatchmaking.OnLobbyInvite -= Steam_OnLobbyInvite;
            SteamMatchmaking.OnLobbyGameCreated -= Steam_OnLobbyGameCreated;
            SteamFriends.OnGameLobbyJoinRequested -= SteamFriends_OnGameLobbyJoinRequested;
        }

        public override IEnumerator Initalise()
        {
            cts = new CancellationTokenSource();

            InitialiseNetwork(true, null, (sucess) =>
            {
                initialised = true;
            });

            return base.Initalise();
        }

        public void InitialiseNetwork(bool createParty, ulong? code = null, Action<bool> onComplete = null)
        {
            StartCoroutine(InitialiseNetwork_Internal(onComplete));

            IEnumerator InitialiseNetwork_Internal(Action<bool> onComplete)
            {
                Disconnect();

                if (Network != null)
                {
                    LOG.Log(message: $"Disconnecting from network...", LOG.Type.NETWORK);

                    OnNetworkShutdown?.Invoke();

                    Network.Shutdown();

                    yield return new WaitUntil(() => !Network.ShutdownInProgress);

                    LOG.Log(message: $"Disconnected.", LOG.Type.NETWORK);

                    UnregisterNetworkCallbacks();

                    transport.Shutdown();
                    UnregisterTransportCallbacks();

                    Destroy(Network.gameObject);
                }

                LOG.Log(message: $"Network Initialising...", LOG.Type.NETWORK);

                Network = Instantiate(networkPrefab);
                GameObject.DontDestroyOnLoad(Network);

                Network.SetSingleton();
                Network.NetworkConfig.NetworkTransport = transport;

                yield return null;

                RegisterTransportCallbacks();

                Network.gameObject.SetActive(true);

                if (createParty)
                {
                    CreateParty(code, (success) =>
                    {
                        onComplete?.Invoke(success);
                        OnNetworkInitialised?.Invoke();
                    });
                }
                else
                {
                    JoinLobby(code, (success) =>
                    {
                        onComplete?.Invoke(success);
                        OnNetworkInitialised?.Invoke();
                    });
                }
            }
        }

        private void SpawnServices()
        {
            NetworkObject.InstantiateAndSpawn(migrationServicePrefab.gameObject, Network);
            NetworkObject.InstantiateAndSpawn(sessionServicePrefab.gameObject, Network);
            NetworkObject.InstantiateAndSpawn(chatServicePrefab.gameObject, Network);
        }

        private void CreateParty(ulong? code = null, Action<bool> onComplete = null)
        {
            LobbyDetails details = new LobbyDetails("Test Lobby", 6, code);
            StartHost(details, (success) =>
            {
                if (success)
                {
                    LOG.Log(message: $"Created Party Lobby. ID: {currentLobby?.Id}", LOG.Type.NETWORK, LOG.Type.LOBBY);
                }

                onComplete?.Invoke(success);
            });
        }

        public void JoinLobby(string lobbyCode)
        {
            if (!ulong.TryParse(lobbyCode, out ulong code))
                return;

            InitialiseNetwork(false, code);
        }

        private void JoinLobby(ulong? code, Action<bool> onComplete = null)
        {
            if (processRunning)
            {
                LOG.Log(message: $"Process already running: Cannot Join Lobby.", LOG.Type.NETWORK, LOG.Type.LOBBY);
                onComplete?.Invoke(false);
                return;
            }

            LOG.Log(message: $"Attempting to join Lobby: {code}...", LOG.Type.NETWORK, LOG.Type.LOBBY);
            processRunning = true;

            UILoading.ShowIndicator(() => !processRunning);

            if (currentLobby?.Id == code)
            {
                LOG.LogError(message: $"Cannot Join Lobby: {code} - Client already in lobby.", LOG.Type.NETWORK, LOG.Type.LOBBY);
                onComplete?.Invoke(false);
                processRunning = false;
                return;
            }

            JoinLobby_Internal(code, (lobby) =>
            {
                if (lobby == null)
                {
                    LOG.Log(message: $"Could not join lobby.", LOG.Type.NETWORK, LOG.Type.LOBBY);
                    onComplete?.Invoke(false);
                }
                else
                {
                    StartClient(lobby.Value.Owner.Id);

                    LOG.Log(message: $"Joined Lobby: {lobby?.Id}.", LOG.Type.NETWORK, LOG.Type.LOBBY);
                    onComplete?.Invoke(true);
                }
            });

            void JoinLobby_Internal(ulong? code, Action<Lobby?> onComplete)
            {
                Task.Run(async () =>
                {
                    Lobby? lobby = await SteamMatchmaking.JoinLobbyAsync(code.Value);

                    LOG.Log(message: $"Lobby Owner: {lobby?.Owner.Id}", LOG.Type.NETWORK, LOG.Type.LOBBY);

                    ConnectionAttemptStatus status = currentLobby != null ? ConnectionAttemptStatus.SUCCESS : ConnectionAttemptStatus.PLATFORM_FAIL;

                    await Awaitable.MainThreadAsync();

                    onComplete.Invoke(lobby);
                },
                cts.Token);
            }
        }

        public void LeaveLobby()
        {
            if (processRunning)
            {
                LOG.Log(message: $"Process already running: Cannot Leave Lobby.", LOG.Type.NETWORK, LOG.Type.LOBBY);
                return;
            }

            LOG.Log(message: $"Leaving Lobby: {currentLobby?.Id}...", LOG.Type.NETWORK, LOG.Type.LOBBY);
            processRunning = true;

            UILoading.ShowIndicator(() => !processRunning);

            if (currentLobby != null)
            {
                // if we're the host and theres additional clients in the lobby, migrate the host
                if (Network.IsHost && Network.ConnectedClients.Count > 1)
                {
                    MigrationHandler.MigrateHost(currentLobby.Value);
                }
                else
                {
                    InitialiseNetwork(true);
                }
            }
        }

        private void StartHost(LobbyDetails details, Action<bool> onComplete = null)
        {
            LOG.Log(message: $"Starting host...", LOG.Type.NETWORK);

            UILoading.ShowIndicator(() => !processRunning);

            Network.OnServerStarted += Network_OnServerStarted;
            Network.OnClientStarted += Network_OnClientStarted;

            StartHost_Internal(details, (status) =>
            {
                switch (status)
                {
                    case ConnectionAttemptStatus.SUCCESS:
                        {
                            LOG.Log(message: $"Host started sucessfully.", LOG.Type.NETWORK);

                            OnLocalClientConnectionChanged?.Invoke(true);
                            onComplete?.Invoke(true);
                            break;
                        }
                    case ConnectionAttemptStatus.PLATFORM_FAIL:
                        {
                            LOG.Log(message: $"Could not start host. (STEAM)", LOG.Type.NETWORK);

                            Network.OnServerStarted -= Network_OnServerStarted;
                            Network.OnClientStarted -= Network_OnClientStarted;

                            onComplete?.Invoke(false);
                            break;
                        }
                    case ConnectionAttemptStatus.NETWORK_FAIL:
                        {
                            LOG.Log(message: $"Could not start host. (NETWORK)", LOG.Type.NETWORK);

                            Network.OnServerStarted -= Network_OnServerStarted;
                            Network.OnClientStarted -= Network_OnClientStarted;

                            onComplete?.Invoke(false);
                            break;
                        }
                }
            });

            void StartHost_Internal(LobbyDetails details, Action<ConnectionAttemptStatus> onComplete)
            {
                if (Network.StartHost())
                {
                    Task.Run(async () =>
                    {
                        if (details.lobbyCode == null)
                            currentLobby = await SteamMatchmaking.CreateLobbyAsync(details.maxPlayers);
                        else
                            currentLobby = await SteamMatchmaking.JoinLobbyAsync(details.lobbyCode.Value);

                        ConnectionAttemptStatus status = currentLobby != null ? ConnectionAttemptStatus.SUCCESS : ConnectionAttemptStatus.PLATFORM_FAIL;

                        if (currentLobby.HasValue)
                        {
                            currentLobby.Value.SetData("name", details.lobbyName);
                        }

                        await Awaitable.MainThreadAsync();

                        onComplete.Invoke(status);
                    },
                    cts.Token);
                }
                else
                {
                    onComplete.Invoke(ConnectionAttemptStatus.NETWORK_FAIL);
                }
            }

        }

        private void StartClient(SteamId steamId)
        {
            LOG.Log(message: $"Starting Client...", LOG.Type.NETWORK);

            Network.OnServerStarted += Network_OnServerStarted;
            Network.OnClientStarted += Network_OnClientStarted;

            UILoading.ShowIndicator(() => !processRunning);

            if (transport is FacepunchTransport facepunchTransport)
                facepunchTransport.targetSteamId = steamId;

            try
            {
                if (!Network.StartClient())
                {
                    LOG.Log(message: $"Could not start client.", LOG.Type.NETWORK);

                    Network.OnServerStarted -= Network_OnServerStarted;
                    Network.OnClientStarted -= Network_OnClientStarted;
                }
            }
            catch (Exception ex)
            {
                LOG.LogError(message: $"Could not start client. Reason: {ex}", LOG.Type.NETWORK);
            }
        }

        private void Disconnect()
        {
            currentLobby?.Leave();
            OnLobbyLeft?.Invoke();

            LOG.Log(message: $"Lobby Left.", LOG.Type.NETWORK, LOG.Type.LOBBY);

            if (Network == null)
                return;

            currentLobby = null;
        }

        private void Shutdown()
        {
            LOG.Log(message: $"Network shutting down...", LOG.Type.NETWORK);

            cts.Cancel();

            UnregisterTransportCallbacks();
            UnregisterNetworkCallbacks();
        }

        #region Service Callbacks

        public void RegisterMigrationService(MigrationService migrationHandler)
        {
            this.MigrationHandler = migrationHandler;
            MigrationHandler.OnMigrationCompleted += OnMigrationCompleted;
        }

        public void UnregisterMigrationService()
        {
            if (MigrationHandler != null)
            {
                MigrationHandler.OnMigrationCompleted -= OnMigrationCompleted;
                this.MigrationHandler = null;
            }
        }

        public void OnSessionMemberAdded(ulong platformID, bool isSelf)
        {
            OnPlayerJoined?.Invoke(platformID, isSelf);
        }

        public void OnSessionMemberRemoved(ulong platformID)
        {
            OnPlayerLeft?.Invoke(platformID);
        }

        public void OnSessionMembersUpdated(List<ulong> platformIDs, bool isHost)
        {
            OnLobbyEntered?.Invoke(platformIDs, isHost);
        }

        public void OnMigrationCompleted(bool success)
        {
            OnNetworkMigrationCompleted?.Invoke(success);
        }

        #endregion

        #region Transport & Network Callbacks

        private void Network_OnServerStarted()
        {
            LOG.Log($"Server started. (Network)", LOG.Type.NETWORK);
            processRunning = false;

            Network.OnServerStarted -= Network_OnServerStarted;
            Network.OnServerStopped += Network_OnServerStopped;
        }

        private void Network_OnServerStopped(bool isHost)
        {
            LOG.Log($"Server stopped. (Network)", LOG.Type.NETWORK); //
            processRunning = false;

            Network.OnServerStopped -= Network_OnServerStopped;
        }

        private void Network_OnClientStarted()
        {
            LOG.Log(message: $"Client started.", LOG.Type.NETWORK);

            processRunning = false;

            Network.OnClientStarted -= Network_OnClientStarted;
            Network.OnClientStopped += Network_OnClientStopped;

            OnLocalClientConnectionChanged?.Invoke(true);
        }

        private void Network_OnClientStopped(bool isHost)
        {
            LOG.Log($"Client stopped.", LOG.Type.NETWORK);
            processRunning = false;

            Network.OnClientStopped -= Network_OnClientStopped;
            OnLocalClientConnectionChanged?.Invoke(false);
        }

        private void Steam_OnLobbyEntered(Lobby lobby)
        {
            LOG.Log($"Lobby Entered: {lobby.Id}.", LOG.Type.NETWORK, LOG.Type.LOBBY);

            currentLobby = lobby;

            if (Network.IsServer)
            {
                SpawnServices();
            }
        }

        private void Steam_OnLobbyMemberJoined(Lobby lobby, Friend friend)
        {
            LOG.Log($"Member Joined: {friend.Id}.", LOG.Type.NETWORK, LOG.Type.LOBBY);
        }

        private void Steam_OnLobbyMemberLeave(Lobby lobby, Friend friend)
        {
            LOG.Log($"Member Left: {friend.Id}.", LOG.Type.NETWORK, LOG.Type.LOBBY);

            if (Network.IsHost)
            {
                SessionHandler.RemoveSessionMember(friend.Id);
            }
        }

        private void Steam_OnLobbyMemberDisconnected(Lobby lobby, Friend friend)
        {
            LOG.Log($"Member Disconnected: {friend.Id}.", LOG.Type.NETWORK, LOG.Type.LOBBY);

            if (Network.IsHost)
            {
                SessionHandler.RemoveSessionMember(friend.Id);
            }
        }

        private void Steam_OnLobbyCreated(Result result, Lobby lobby)
        {
            if (result != Result.OK)
            {
                LOG.LogError($"Lobby not created. Reason: {result}", LOG.Type.LOBBY);
            }
        }

        private void SteamFriends_OnGameLobbyJoinRequested(Lobby lobby, SteamId id)
        {
            throw new NotImplementedException();
        }

        private void Steam_OnLobbyGameCreated(Lobby lobby, uint arg2, ushort arg3, SteamId id)
        {
            throw new NotImplementedException();
        }

        private void Steam_OnLobbyInvite(Friend friend, Lobby lobby)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
