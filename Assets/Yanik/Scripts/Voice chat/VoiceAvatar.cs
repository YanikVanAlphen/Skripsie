using UnityEngine;
using Adrenak.UniVoice;
using Adrenak.UniMic;
using FishNet.Object;
using System.Collections;
using System;

namespace FishyVoice
{
  public class VoiceAvatar : FishNet.Object.NetworkBehaviour
  {
    private Agent agent;
    private const string chatroomName = "<DEFAULT>";

    public override void OnStartClient()
    {
      base.OnStartClient(); // ensure default init is done
      StartCoroutine(StartVoiceAgent());
    }

    private IEnumerator StartVoiceAgent()
    {
      // wait for VoiceNetwork instance and the network to be ready
      while (VoiceNetwork.instance == null || !VoiceNetwork.instance.networkActive)
      {
        yield return new WaitForSeconds(0.1f);
      }

      VoiceNetwork voiceNetwork = VoiceNetwork.instance;

      try
      {
        agent = voiceNetwork.CreateAgent();
        if (agent == null)
          yield break;
      }
      catch (Exception e)
      {
        Debug.LogError($"{e.Message}");
        yield break;
      }

      // if room does not exist, host creates it
      if (IsServer)
      {
        if (voiceNetwork.openRooms.ContainsKey(chatroomName))
        {
          Debug.Log($"Host creating chatroom {chatroomName}");
          voiceNetwork.HostChatroom(chatroomName);
        }
      }

      // wait for room to exist
      if (IsServer)
      {
        while (!voiceNetwork.openRooms.ContainsKey(chatroomName))
        {
          Debug.Log($"Waiting for {chatroomName} room creation on server.");
          yield return new WaitForSeconds(0.1f);
        }
      }

      try
      {
        agent.JoinChatroom(chatroomName);
        Debug.Log($"Client {LocalConnection.ClientId} joined chatroom {chatroomName}");
      }
      catch (Exception e)
      {
        Debug.LogError($"{e.Message}");
        yield break;
      }
    }

    public override void OnStopClient()
    {
      base.OnStopClient(); // ensure original OnStopClient functions are run
      if (IsServer)
      {
        return; // dont want host to leave the chatroom
      }

      if (agent != null)
      {
        try
        {
          // exit voice chat room and release resources safely
          agent.LeaveChatroom();
          agent.Dispose();
          Debug.Log($"Voice agent disposed for client {LocalConnection.ClientId}.");
        }
        catch (System.Exception e)
        {
          Debug.LogError($"Failed to dispose voice agent: {e.Message}.");
        }
        agent = null;
      }
    }
  }
}