using Gamble.BattleCards.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

namespace Gamble.BattleCards.Infrastructure.Network
{
    /// <summary>
    /// Session Service responsible for authenticating clients and notifying members of session changes
    /// </summary>
    public class SessionService : NetworkService
    {
        private enum AuthenticationStatus
        {
            UNDEFINED,
            INVALID_CLIENT,
            MIGRATION_IN_PROGRESS,
            SUCCESS
        }

        private NetworkDictionary<ulong, ulong> sessionMembers = null;

        public override string Name => "[NETWORK] Session Service";

        public event Action<ulong, bool> OnMemberAdded;
        public event Action<ulong> OnMemberRemoved;
        public event Action<List<ulong>, bool> OnClientAuthenticated;

        private void Awake()
        {
            sessionMembers = new NetworkDictionary<ulong, ulong>(NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        }

        public void OnEnable()
        {
            sessionMembers.OnDictionaryChanged += OnSessionClientsDictionaryChanged;
        }

        public override void RegisterService()
        {
            AuthenticateAndRegisterClient_ServerRpc(
               Infrastructure.NetworkManager.Instance.LocalClientID,
               Infrastructure.NetworkManager.Instance.LocalSteamID);

            Infrastructure.NetworkManager.Instance.SessionHandler = this;

            OnMemberAdded += Infrastructure.NetworkManager.Instance.OnSessionMemberAdded;
            OnMemberRemoved += Infrastructure.NetworkManager.Instance.OnSessionMemberRemoved;
            OnClientAuthenticated += Infrastructure.NetworkManager.Instance.OnSessionMembersUpdated;
        }

        public override void UnregisterService()
        {
            OnMemberAdded -= Infrastructure.NetworkManager.Instance.OnSessionMemberAdded;
            OnMemberRemoved -= Infrastructure.NetworkManager.Instance.OnSessionMemberRemoved;
            OnClientAuthenticated -= Infrastructure.NetworkManager.Instance.OnSessionMembersUpdated;

            Infrastructure.NetworkManager.Instance.SessionHandler = null;

            if (sessionMembers != null)
            {
                sessionMembers.OnDictionaryChanged -= OnSessionClientsDictionaryChanged;

                sessionMembers.Dispose();
                sessionMembers = null;
            }
        }

        /// <summary>
        /// Server side client authetication / registration
        /// </summary>
        /// <param name="clientID"> The Network ID of the client. </param>
        /// <param name="platformID"> The platform ID of the client. </param>
        [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        private void AuthenticateAndRegisterClient_ServerRpc(ulong clientID, ulong platformID)
        {
            AuthenticationStatus status = AuthenticationStatus.SUCCESS;

            if (Infrastructure.NetworkManager.Instance.MigrationHandler.IsMigrating)
                status = AuthenticationStatus.MIGRATION_IN_PROGRESS;

            if (sessionMembers.ContainsKey(platformID) && sessionMembers[platformID] != clientID)
                status = AuthenticationStatus.INVALID_CLIENT;

            if (!sessionMembers.ContainsKey(platformID) && sessionMembers.Values.Contains(clientID))
                status = AuthenticationStatus.INVALID_CLIENT;

            if (sessionMembers.ContainsKey(platformID))
                status = AuthenticationStatus.INVALID_CLIENT;

            // tell the client if they've been autheticated
            AuthenticationResponse_ClientRpc(status);

            LOG.Log($"Client Autheticated. PID: {platformID} UID: {clientID}", LOG.Type.NETWORK, LOG.Type.SESSION);

            if (status == AuthenticationStatus.SUCCESS)
            {
                sessionMembers.Add(platformID, clientID);
            }
        }

        /// <summary>
        /// Client RPC containing server authetication response 
        /// </summary>
        /// <param name="status"></param>
        [ClientRpc(Delivery = RpcDelivery.Reliable)]
        private void AuthenticationResponse_ClientRpc(AuthenticationStatus status)
        {
            if (NetworkManager.IsHost)
                return;

            LOG.Log($"Client Autheticated. PID: {Infrastructure.NetworkManager.Instance.LocalSteamID} " +
                $"UID: {Infrastructure.NetworkManager.Instance.LocalClientID}", 
                LOG.Type.NETWORK, LOG.Type.SESSION);

            if (status != AuthenticationStatus.SUCCESS)
            {
                Infrastructure.NetworkManager.Instance.InitialiseNetwork(true);
            }
            else
            {
                OnClientAuthenticated?.Invoke(sessionMembers.Keys.ToList(), IsOwner);
            }
        }

        public void RemoveSessionMember(ulong platformID)
        {
            if (sessionMembers.ContainsKey(platformID))
            {
                sessionMembers.Remove(platformID);
            }
        }

        private void OnSessionClientsDictionaryChanged(NetworkDictionaryEvent<ulong, ulong> changeEvent)
        {
            switch (changeEvent.Type)
            {
                case NetworkDictionaryEvent<ulong, ulong>.EventType.Add:
                    {
                        OnMemberAdded?.Invoke(changeEvent.Key, changeEvent.Value == NetworkManager.LocalClientId);
                        break;
                    }
                case NetworkDictionaryEvent<ulong, ulong>.EventType.Remove:
                    {
                        OnMemberRemoved?.Invoke(changeEvent.Key);
                        break;
                    }
            }
        }

        public bool TryGetPlatformIDFromClient(ulong clientID, out ulong platformID)
        {
            foreach ((ulong Key, ulong Value) id in sessionMembers)
            {
                if (id.Value == clientID)
                {
                    platformID = id.Key;
                    return true;
                }
            }

            platformID = default;
            return false;
        }
    }
}
