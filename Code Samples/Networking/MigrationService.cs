using Gamble.BattleCards.Logging;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Gamble.BattleCards.Infrastructure.Network
{
    /// <summary>
    /// Migration Service responsible for transfering ownership in P2P networks 
    /// </summary>
    public class MigrationService : NetworkService
    {
        public override string Name => "[NETWORK] Migration Service";

        public event Action<bool> OnMigrationCompleted;
        public bool IsMigrating { get; private set; }

        private List<ulong> migrationConfirmations = new List<ulong>();

        private ulong migratingDestination;
        private bool migratedAshost = false;
        private ulong hostClient;

        private Coroutine migrationCorutine = null;

        public override void RegisterService()
        {
            Infrastructure.NetworkManager.Instance.MigrationHandler = this;
            OnMigrationCompleted += Infrastructure.NetworkManager.Instance.OnMigrationCompleted;
        }

        public override void UnregisterService()
        {
            OnMigrationCompleted -= Infrastructure.NetworkManager.Instance.OnMigrationCompleted;
            Infrastructure.NetworkManager.Instance.MigrationHandler = null;
        }


        /// <summary>
        /// Nominate a client for ownership, notify all connected clients to migrate and leaves the current lobby.
        /// </summary>
        /// <param name="lobby"> The current lobby. </param>
        public void MigrateHost(Lobby lobby)
        {
            if (!NetworkManager.IsHost)
                return;

            LOG.Log(message: $"Migrating host...", LOG.Type.NETWORK, LOG.Type.MIGRATION);

            this.IsMigrating = true;
            this.migrationConfirmations = new List<ulong>();

            hostClient = NetworkManager.ConnectedClientsIds.First(x => x != NetworkManager.LocalClientId);

            if (Infrastructure.NetworkManager.Instance.SessionHandler.TryGetPlatformIDFromClient(hostClient, out ulong platformID))
            {
                lobby.Owner = new Friend(platformID);
            }

            migrationConfirmations.Add(OwnerClientId);

            NotifyMigration_ClientRpc(lobby.Id, hostClient);
        }

        [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Reliable)]
        private void OnClientRecievedMigrationInstructions_ServerRpc(ulong clientID)
        {
            LOG.Log(message: $"Client recieved migration instructions. ", LOG.Type.NETWORK, LOG.Type.MIGRATION);

            if (!migrationConfirmations.Contains(clientID))
            {
                migrationConfirmations.Add(clientID);
            }

            migrationCorutine ??= StartCoroutine(WaitUntilAllCLientsRecievedInstructions());

            IEnumerator WaitUntilAllCLientsRecievedInstructions()
            {
                yield return new WaitUntil(() => migrationConfirmations.Count >= NetworkManager.ConnectedClientsIds.Count);

                OnMigrationCompleted?.Invoke(true);

                ClientRpcParams hostClientParams = new ClientRpcParams()
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[] { hostClient }
                    }
                };

                MigrateClients_ClientRpc(hostClientParams);

                yield return new WaitForSeconds(5f);

                ClientRpcParams joinClientParams = new ClientRpcParams()
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = migrationConfirmations.Where(x => x != hostClient && x != NetworkManager.LocalClientId).ToArray(),
                    }
                };

                MigrateClients_ClientRpc(joinClientParams);

                yield return new WaitForSeconds(5f);

                IsMigrating = false;

                Infrastructure.NetworkManager.Instance.InitialiseNetwork(true, null, (_) =>
                {
                    LOG.Log(message: $"Migration Completed", LOG.Type.NETWORK, LOG.Type.MIGRATION);
                });
            }
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Reliable)]
        private void NotifyMigration_ClientRpc(ulong migratingDestination, ulong hostClient)
        {
            LOG.Log(message: $"Migration notification received.", LOG.Type.NETWORK, LOG.Type.MIGRATION);

            this.IsMigrating = true;

            this.migratingDestination = migratingDestination;
            this.migratedAshost = NetworkManager.LocalClientId == hostClient;

            OnClientRecievedMigrationInstructions_ServerRpc(NetworkManager.LocalClientId);
        }

        [ClientRpc(Delivery = RpcDelivery.Reliable)]
        private void MigrateClients_ClientRpc(ClientRpcParams clientParams)
        {
            LOG.Log(message: $"Migrating client...", LOG.Type.NETWORK, LOG.Type.MIGRATION);

            Infrastructure.NetworkManager.Instance.InitialiseNetwork(migratedAshost, migratingDestination, (success) =>
            {
                IsMigrating = false;

                if (success)
                {
                    LOG.Log(message: $"Client sucessfully migrated.", LOG.Type.NETWORK, LOG.Type.MIGRATION);
                }

                OnMigrationCompleted?.Invoke(success);
            });
        }
    }
}
