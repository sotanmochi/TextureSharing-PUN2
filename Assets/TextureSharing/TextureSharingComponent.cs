using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using UniRx;
using UniRxExtension;

namespace TextureSharing
{
    public enum StreamingBytesEventCode
    {
        BeginStream = 10,
        Streaming = 11,
    }

    public class TextureSharingComponent : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        [SerializeField]
        int messagePerSecond = 100; // 100 Messages / Second

        int bytePerMessage = 1000; // 1KBytes / Message
     
        Texture2D texture; // ★ Readable texture ★

        bool isReceiving;
        byte[] receiveBuffer;
        int totalDataSize;
        int currentReceivedDataSize;
        int receivedMessageCount;

        void Start()
        {
            texture = (Texture2D)GetComponent<Renderer>().material.mainTexture;
            try
            {
                texture.GetPixels32();
            }
            catch(UnityException e)
            {
                Debug.LogError("!! This texture is not readable !!");
            }
        }

        public void GetRawTextureDataFromMasterClient()
        {
            photonView.RPC("GetRawTextureDataRPC", RpcTarget.MasterClient);
        }

        //**************************************************************************
        // Client -> MasterClient (These methods are executed by the master client)
        //**************************************************************************
        [PunRPC]
        public void GetRawTextureDataRPC(PhotonMessageInfo info)
        {
            byte[] rawTextureData = texture.GetRawTextureData();

            int width = texture.width;
            int height = texture.height;
            int dataSize = rawTextureData.Length;
            int viewId = this.photonView.ViewID;

            Debug.Log("*************************");
            Debug.Log(" GetRawTextureDataRPC");
            Debug.Log(" RPC sender: " + info.Sender);
            Debug.Log(" Texture size: " + width + "x" + height + " = " + width*height + "px");
            Debug.Log(" RawTextureData: " + rawTextureData.Length + "bytes");
            Debug.Log("*************************");

            StreamTextureDataToRequestSender(rawTextureData, width, height, dataSize, viewId, info.Sender);
        }

        void StreamTextureDataToRequestSender(byte[] rawTextureData, int width, int height, int dataSize, int viewId, Player requestSender)
        {
            Debug.Log("***********************************");
            Debug.Log(" StreamTextureDataToRequestSender  ");
            Debug.Log("***********************************");
            
            RaiseEventOptions raiseEventOptions = new RaiseEventOptions
            {
                CachingOption = EventCaching.DoNotCache,
                Receivers = ReceiverGroup.All,
                TargetActors = new int[]{ requestSender.ActorNumber },
            };

            SendOptions sendOptions = new ExitGames.Client.Photon.SendOptions
            {
                Reliability = true,
            };

            // Send info
            int[] textureInfo = new int[4];
            textureInfo[0] = viewId;
            textureInfo[1] = width;
            textureInfo[2] = height;
            textureInfo[3] = dataSize;
            PhotonNetwork.RaiseEvent((byte)StreamingBytesEventCode.BeginStream, textureInfo, raiseEventOptions, sendOptions);

            // Send raw data
            rawTextureData.ToObservable()
                .Buffer(bytePerMessage)
                .SlowDown(1.0f/messagePerSecond)
                .Subscribe(byteSubList =>
                {
                    byte[] sendData = new byte[byteSubList.Count];
                    byteSubList.CopyTo(sendData, 0);
                    PhotonNetwork.RaiseEvent((byte)StreamingBytesEventCode.Streaming, sendData, raiseEventOptions, sendOptions);
                });
        }

        //***************************************************************************
        // MasterClient -> Client (These methods are executed by the master client)
        //***************************************************************************
        public void OnEvent(ExitGames.Client.Photon.EventData photonEvent)
        {
            if(photonEvent.Code == (byte)StreamingBytesEventCode.BeginStream)
            {
                int[] data = (int[])photonEvent.Parameters[ParameterCode.Data];
                OnReceivedTextureInfo(data);
            }
            if(photonEvent.Code == (byte)StreamingBytesEventCode.Streaming)
            {
                byte[] data = (byte[])photonEvent.Parameters[ParameterCode.Data];
                OnReceivedRawTextureDataStream(data);
            }
        }

        void OnReceivedTextureInfo(int[] data)
        {
            int viewId = data[0];
            if (viewId != this.photonView.ViewID)
            {
                this.isReceiving = false;
                this.totalDataSize = 0;
                this.currentReceivedDataSize = 0;
                this.receivedMessageCount = 0;
                return;
            }

            this.isReceiving = true;
            this.currentReceivedDataSize = 0;
            this.receivedMessageCount = 0;

            int width = data[1];
            int height = data[2];
            int dataSize = data[3];
            this.totalDataSize = dataSize;
            this.receiveBuffer = new byte[dataSize];

            Debug.Log("*************************");
            Debug.Log(" OnReceivedTextureInfo");
            Debug.Log(" Texture size: " + width + "x" + height + "px");
            Debug.Log(" RawTextureDataSize: " + data[2]);
            Debug.Log("*************************");
        }

        void OnReceivedRawTextureDataStream(byte[] data)
        {
            if (this.isReceiving)
            {
                data.CopyTo(this.receiveBuffer, this.currentReceivedDataSize);
                this.currentReceivedDataSize += data.Length;
                this.receivedMessageCount++;

                if (this.currentReceivedDataSize >= (this.totalDataSize))
                {
                    this.isReceiving = false;
                    this.currentReceivedDataSize = 0;
                    this.receivedMessageCount = 0;

                    OnReceivedRawTextureData();
                }
            }
        }

        void OnReceivedRawTextureData()
        {
            Debug.Log("********************************");
            Debug.Log(" OnReceivedRawTextureData ");
            Debug.Log("********************************");

            texture.LoadRawTextureData(this.receiveBuffer);
            texture.Apply();
            GetComponent<Renderer>().material.mainTexture = texture;
        }
    }
}