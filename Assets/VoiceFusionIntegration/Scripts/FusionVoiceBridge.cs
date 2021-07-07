namespace Photon.Voice.Fusion
{
    using global::Fusion;
    using global::Fusion.Sockets;
    using PhotonAppSettings = global::Fusion.Photon.Realtime.PhotonAppSettings;
    using System.Collections.Generic;
    using Realtime;
    using ExitGames.Client.Photon;
    using UnityEngine;
    using Unity;

    [RequireComponent(typeof(NetworkRunner))]
    [RequireComponent(typeof(VoiceConnection))]
    public class FusionVoiceBridge : VoiceComponent, INetworkRunnerCallbacks
    {
        #region Private Fields

        private NetworkRunner networkRunner;
        private VoiceConnection voiceConnection;

        [SerializeField]
        private bool useFusionAppSettings = true;

        private EnterRoomParams voiceRoomParams = new EnterRoomParams
        {
            RoomOptions = new RoomOptions { IsVisible = false }
        };

        #endregion

        #region Properties

        public bool UseFusionAppSettings
        {
            get => this.useFusionAppSettings;
            set => this.useFusionAppSettings = value;
        }

        #endregion

        #region Private Methods

        protected override void Awake()
        {
            base.Awake();
            RegisterCustomTypes();
            this.networkRunner = this.GetComponent<NetworkRunner>();
            this.voiceConnection = this.GetComponent<VoiceConnection>();
            if (this.useFusionAppSettings)  // todo: review
            {
                this.voiceConnection.Settings.AppIdVoice = PhotonAppSettings.Instance.AppSettings.AppIdVoice;
                this.voiceConnection.Settings.AppVersion = PhotonAppSettings.Instance.AppSettings.AppVersion;
                this.voiceConnection.Settings.FixedRegion = PhotonAppSettings.Instance.AppSettings.FixedRegion;
                this.voiceConnection.Settings.UseNameServer = PhotonAppSettings.Instance.AppSettings.UseNameServer;
                this.voiceConnection.Settings.Server = PhotonAppSettings.Instance.AppSettings.Server;
                this.voiceConnection.Settings.Port = PhotonAppSettings.Instance.AppSettings.Port;
                this.voiceConnection.Settings.ProxyServer = PhotonAppSettings.Instance.AppSettings.ProxyServer;
                this.voiceConnection.Settings.BestRegionSummaryFromStorage = PhotonAppSettings.Instance.AppSettings.BestRegionSummaryFromStorage;
                this.voiceConnection.Settings.EnableLobbyStatistics = false;
                //this.voiceConnection.Settings.Protocol = ConnectionProtocol.Udp; // todo: decide if use same protocol or prefer UDP always?
                //this.voiceConnection.Settings.EnableProtocolFallback = PhotonAppSettings.Instance.AppSettings.EnableProtocolFallback;
                //this.voiceConnection.Settings.Protocol = PhotonAppSettings.Instance.AppSettings.Protocol;
                //this.voiceConnection.Settings.AuthMode = (AuthModeOption)(int)PhotonAppSettings.Instance.AppSettings.AuthMode;

            }
            this.voiceConnection.SpeakerFactory = this.FusionSpeakerFactory;
        }

        private void OnEnable()
        {
            this.voiceConnection.Client.StateChanged += this.OnVoiceClientStateChanged;
        }

        private void OnDisable()
        {
            this.voiceConnection.Client.StateChanged -= this.OnVoiceClientStateChanged;
        }

        private void OnVoiceClientStateChanged(ClientState previous, ClientState current)
        {
            this.ConnectOrJoinRoom(current);
        }

        private Speaker FusionSpeakerFactory(int playerId, byte voiceId, object userData)
        {
            if (!(userData is NetworkId))
            {
                if (this.Logger.IsWarningEnabled)
                {
                    this.Logger.LogWarning("UserData ({0}) is not of type NetworkId. Remote voice {1}/{2} not linked. Do you have a Recorder not used with a VoiceNetworkObject? is this expected?",
                        userData == null ? "null" : userData.ToString(), playerId, voiceId);
                }
                return null;
            }
            NetworkId networkId = (NetworkId)userData;
            if (!networkId.IsValid)
            {
                if (this.Logger.IsWarningEnabled)
                {
                    this.Logger.LogWarning("NetworkId is not valid ({0}). Remote voice {1}/{2} not linked.", networkId, playerId, voiceId);
                }
                return null;
            }
            VoiceNetworkObject voiceNetworkObject = this.networkRunner.TryGetNetworkedBehaviourFromNetworkedObjectRef<VoiceNetworkObject>(networkId);
            if (voiceNetworkObject == null)
            {
                if (this.Logger.IsWarningEnabled)
                {
                    this.Logger.LogWarning("No voiceNetworkObject found with ID {0}. Remote voice {1}/{2} not linked.", networkId, playerId, voiceId);
                }
                return null;
            }
            if (!voiceNetworkObject.IgnoreGlobalLogLevel)
            {
                voiceNetworkObject.LogLevel = this.LogLevel;
            }
            if (!voiceNetworkObject.IsSpeaker)
            {
                voiceNetworkObject.SetupSpeakerInUse();
            }
            //voiceNetworkObject.name = $"{playerId} {voiceId} {userData}";
            return voiceNetworkObject.SpeakerInUse;
        }

        // todo: finish implementing this or maybe make sure we don't need it
        //internal void CheckLateLinking(Speaker speaker, NetworkId networkId)
        //{
        //    if (!speaker || speaker == null)
        //    {
        //        if (this.Logger.IsWarningEnabled)
        //        {
        //            this.Logger.LogWarning("Cannot check late linking for null Speaker");
        //        }
        //        return;
        //    }
        //    if (!networkId.IsValid)
        //    {
        //        if (this.Logger.IsWarningEnabled)
        //        {
        //            this.Logger.LogWarning("Cannot check late linking invalid NetworkId {0}", networkId);
        //        }
        //        return;
        //    }
        //}

        private string GetVoiceRoomName()
        {
            return "Test"; // todo: change this
        }

        private void ConnectOrJoinRoom()
        {
            this.ConnectOrJoinRoom(this.voiceConnection.ClientState);
        }

        private void ConnectOrJoinRoom(ClientState state)
        {
            if (ConnectionHandler.AppQuits)
            {
                return;
            }
            switch (state)
            {
                case ClientState.PeerCreated:
                case ClientState.Disconnected:
                    if (!this.Connect())
                    {
                        if (this.Logger.IsErrorEnabled)
                        {
                            this.Logger.LogError("Connecting to server failed.");
                        }
                    }
                    break;
                case ClientState.ConnectedToMasterServer:
                    if (!this.JoinRoom())
                    {
                        if (this.Logger.IsErrorEnabled)
                        {
                            this.Logger.LogError("Joining a voice room failed.");
                        }
                    }
                    break;
                case ClientState.Joined:
                    string expectedRoomName = this.GetVoiceRoomName();
                    string currentRoomName = this.voiceConnection.Client.CurrentRoom.Name;
                    if (!currentRoomName.Equals(expectedRoomName))
                    {
                        if (this.Logger.IsWarningEnabled)
                        {
                            this.Logger.LogWarning("Voice room mismatch: Expected:\"{0}\" Current:\"{1}\", leaving the second to join the first.", expectedRoomName, currentRoomName);
                        }
                        if (!this.voiceConnection.Client.OpLeaveRoom(false))
                        {
                            if (this.Logger.IsErrorEnabled)
                            {
                                this.Logger.LogError("Leaving the current voice room failed.");
                            }
                        }
                    }
                    break;
            }
        }

        private bool Connect()
        {
            return this.voiceConnection.ConnectUsingSettings();
        }

        private void Disconnect()
        {
            this.voiceConnection.Client.Disconnect();
        }

        private bool JoinRoom(string voiceRoomName)
        {
            if (string.IsNullOrEmpty(voiceRoomName))
            {
                if (this.Logger.IsErrorEnabled)
                {
                    this.Logger.LogError("Voice room name is null or empty.");
                }
                return false;
            }
            this.voiceRoomParams.RoomName = voiceRoomName;
            return this.voiceConnection.Client.OpJoinOrCreateRoom(this.voiceRoomParams);
        }

        private bool JoinRoom()
        {
            return this.JoinRoom(this.GetVoiceRoomName());
        }

        private static void RegisterCustomTypes()
        {
            PhotonPeer.RegisterType(typeof(NetworkId), FusionNetworkIdTypeCode, SerializeFusionNetworkId, DeserializeFusionNetworkId);
        }

        private const byte FusionNetworkIdTypeCode = 0; // we need to make sure this does not clash with other custom types?

        private static object DeserializeFusionNetworkId(StreamBuffer instream, short length)
        {
            NetworkId networkId = new NetworkId();
            lock (memCompressedUInt64)
            {
                ulong ul = ReadCompressedUInt64(instream);
                networkId.Raw = (uint)ul;
            }
            return networkId;
        }

        private static ulong ReadCompressedUInt64(StreamBuffer stream)
        {
            ulong value = 0;
            int shift = 0;

            byte[] data = stream.GetBuffer();
            int offset = stream.Position;

            while (shift != 70)
            {
                if (offset >= data.Length)
                {
                    throw new System.IO.EndOfStreamException("Failed to read full ulong.");
                }

                byte b = data[offset];
                offset++;

                value |= (ulong)(b & 0x7F) << shift;
                shift += 7;
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            stream.Position = offset;
            return value;
        }

        private static byte[] memCompressedUInt64 = new byte[10];

        private static int WriteCompressedUInt64(StreamBuffer stream, ulong value)
        {
            int count = 0;
            lock (memCompressedUInt64)
            {
                // put values in an array of bytes with variable length encoding
                memCompressedUInt64[count] = (byte)(value & 0x7F);
                value = value >> 7;
                while (value > 0)
                {
                    memCompressedUInt64[count] |= 0x80;
                    memCompressedUInt64[++count] = (byte)(value & 0x7F);
                    value = value >> 7;
                }
                count++;

                stream.Write(memCompressedUInt64, 0, count);
            }
            return count;
        }

        private static short SerializeFusionNetworkId(StreamBuffer outstream, object customobject)
        {
            NetworkId networkId = (NetworkId) customobject;
            return (short)WriteCompressedUInt64(outstream, networkId.Raw);
        }

        #endregion

        #region INetworkRunnerCallbacks

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner.LocalPlayer == player)
            {
                this.ConnectOrJoinRoom();
            }
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
        }

        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
        }

        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
            this.ConnectOrJoinRoom();
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner)
        {
            this.Disconnect();
        }

        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request)
        {
        }

        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
        }

        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        void INetworkRunnerCallbacks.OnObjectWordsChanged(NetworkRunner runner, NetworkObject networkedObject, HashSet<int> changedWords,
            NetworkObjectMemoryPtr oldMemory)
        {
        }

        #endregion
    }
}

