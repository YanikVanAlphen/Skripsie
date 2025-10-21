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
      {
        yield return new WaitForSeconds(0.1f);
      }

      VoiceNetwork voiceNetwork = VoiceNetwork.instance;

      // wait for valid Id -> [-1] is invalid
      while (LocalConnection.ClientId < 0)
        yield return new WaitForSeconds(0.1f);

      try
      {
        agent = voiceNetwork.CreateAgent();
        if (agent == null)
        {
          Debug.LogError("Failed to create voice agent");
          yield break;
        }
      }
      catch (Exception e)
      {
        Debug.LogError($"Error creating agent: {e.Message}");
        yield break;
      }

      // wait for the chatroom to exist
      while (!voiceNetwork.openRooms.ContainsKey(chatroomName))
      {
        yield return new WaitForSeconds(0.1f);
      }

      // everyone joins the room
      try
      {
        voiceNetwork.JoinChatroom(chatroomName); // sync openRooms via ServerRpc
        agent.JoinChatroom(chatroomName); // routes UniVoice audio
        Debug.Log($"Client {LocalConnection.ClientId} joined chatroom {chatroomName}");
      }
      catch (Exception e)
      {
        Debug.LogError($"Error joining chatroom: {e.Message}");
        yield break;
      }
    }

    public override void OnStopClient()
    {
      base.OnStopClient();

      if (agent != null)
      {
        try
        {
          // safely leave chatroom and free agent
          agent.LeaveChatroom();
          agent.Dispose();
          Debug.Log($"Voice agent disposed for client {LocalConnection.ClientId}.");
        }
        catch (Exception e)
        {
          Debug.LogError($"Failed to dispose voice agent: {e.Message}");
        }
        agent = null;
      }
    }
  }
}