using UnityEngine;
using Adrenak.UniVoice;
using Adrenak.UniMic;
using FishNet.Object;
using System.Collections;
using System;

namespace FishyVoice
{
  public class VoiceAvatar : NetworkBehaviour
  {
    private Agent agent;
    private const string chatroomName = "<DEFAULT>";

    public override void OnStartClient()
    {
      base.OnStartClient();
      StartCoroutine(StartVoiceAgent());
    }

    private IEnumerator StartVoiceAgent()
    {
      // wait for VoiceNetwork instance + network to be ready
      while (VoiceNetwork.instance == null || !VoiceNetwork.instance.networkActive)
        yield return new WaitForSeconds(0.1f);
      // get created voicenetwork instance now that it is ready
      VoiceNetwork voiceNetwork = VoiceNetwork.instance;

      // wait for valid client Id from FishNet (client instance fully started), [-1] is invalid Id
      while (LocalConnection.ClientId < 0)
        yield return new WaitForSeconds(0.1f);

      agent = voiceNetwork.CreateAgent();

      // wait for the chatroom to be created/exist
      while (!voiceNetwork.openRooms.ContainsKey(chatroomName))
        yield return new WaitForSeconds(0.1f);

      // everyone joins the room
      voiceNetwork.JoinChatroom(chatroomName); // sync openRooms via ServerRpc
      agent.JoinChatroom(chatroomName); // route UniVoice audio
      Debug.Log($"Client {LocalConnection.ClientId} joined chatroom {chatroomName}");
    }

    public override void OnStopClient()
    {
      base.OnStopClient();

      if (agent != null)
      {
        try
        {
          // safely try to leave chatroom and free agent
          agent.LeaveChatroom();
          agent.Dispose();
          Debug.Log($"Voice agent disposed for client {LocalConnection.ClientId}.");
        }
        catch (Exception e)
        {
          Debug.LogError(e.Message);
        }
        agent = null;
      }
    }
  }
}