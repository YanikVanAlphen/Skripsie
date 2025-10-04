using UnityEngine;
using Adrenak.UniVoice;
using Adrenak.UniMic;
using FishNet.Object;
using System.Collections;

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
      // wait for VoiceNetwork instance and network to be ready
      while (VoiceNetwork.instance == null || !VoiceNetwork.instance.networkActive)
      {
        yield return new WaitForSeconds(0.1f);
      }

      VoiceNetwork voiceNetwork = VoiceNetwork.instance;

      // wait for room to exist
      if (IsServer)
      {
        while (!voiceNetwork.openRooms.ContainsKey(chatroomName))
        {
          Debug.Log($"Client {LocalConnection.ClientId} waiting for {chatroomName} room creation on server.");
          yield return new WaitForSeconds(0.1f);
        }
      }

      try
      {
        // create agent for all clients
        agent = voiceNetwork.CreateAgent();
        if (agent == null)
        {
          yield break;
        }
        agent.JoinChatroom(chatroomName);
        Debug.Log($"Voice agent created and joined {chatroomName} room for Client {LocalConnection.ClientId}.");

        // verify microphone status for debugging
        if (Mic.Instance != null)
        {
          Debug.Log($"Microphone recording: {Mic.Instance.IsRecording}, Frequency: {Mic.Instance.Frequency}.");
        }
      }
      catch (System.Exception e)
      {
        Debug.LogError($"Failed to setup voice agent for client {LocalConnection.ClientId}: {e.Message}.");
      }
    }

    public override void OnStopClient()
    {
      base.OnStopClient(); // ensure original base functions are run
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