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
    private const string roomName = "<DEFAULT>";

    public override void OnStartClient()
    {
      base.OnStartClient();
      StartCoroutine(InitializeVoiceAgent());
    }

    private IEnumerator InitializeVoiceAgent()
    {
      // Wait for VoiceNetwork instance and network to be ready
      while (VoiceNetwork.instance == null || !VoiceNetwork.instance.networkActive)
      {
        Debug.Log($"VoiceAvatar: Waiting for VoiceNetwork.instance on client {LocalConnection.ClientId}...");
        yield return new WaitForSeconds(0.1f);
      }

      VoiceNetwork voiceNetwork = VoiceNetwork.instance;

      // Wait for room to exist (host creates it, clients need to sync)
      if (IsServer)
      {
        while (!voiceNetwork.openRooms.ContainsKey(roomName))
        {
          Debug.Log($"VoiceAvatar: Waiting for <DEFAULT> room creation on server (client {LocalConnection.ClientId})...");
          yield return new WaitForSeconds(0.1f);
        }
      }

      // Now initialize agent and join room
      try
      {
        // Create agent for all clients, not just owners
        agent = voiceNetwork.CreateAgent();
        if (agent == null)
        {
          Debug.LogError($"VoiceAvatar: Failed to create voice agent for client {LocalConnection.ClientId}");
          yield break;
        }

        agent.JoinChatroom(roomName);
        Debug.Log($"VoiceAvatar: Voice agent created and joined {roomName} room for client {LocalConnection.ClientId}");

        // Microphone status
        if (Mic.Instance != null)
        {
          Debug.Log($"VoiceAvatar: Microphone recording: {Mic.Instance.IsRecording}, Frequency: {Mic.Instance.Frequency}");
        }
        else
        {
          Debug.LogError("VoiceAvatar: UniMic instance not found!");
        }

        // Log AudioSource status
        var audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
          Debug.Log($"VoiceAvatar: AudioSource found: Enabled={audioSource.enabled}, Output={audioSource.outputAudioMixerGroup}, SpatialBlend={audioSource.spatialBlend}");
        }
        else
        {
          Debug.LogError("VoiceAvatar: AudioSource component missing on UserAvatar!");
        }

        // Log audio output factory
        if (agent?.AudioOutputFactory != null)
        {
          Debug.Log($"VoiceAvatar: AudioOutputFactory: {agent.AudioOutputFactory.GetType().Name}");
        }
        else
        {
          Debug.LogError("VoiceAvatar: AudioOutputFactory not set on Agent!");
        }
      }
      catch (System.Exception e)
      {
        Debug.LogError($"VoiceAvatar: Failed to initialize voice agent for client {LocalConnection.ClientId}: {e.Message}");
      }
    }

    public override void OnStopClient()
    {
      base.OnStopClient();
      if (agent != null)
      {
        try
        {
          agent.LeaveChatroom();
          agent.Dispose();
          Debug.Log($"VoiceAvatar: Voice agent disposed for client {LocalConnection.ClientId}");
        }
        catch (System.Exception e)
        {
          Debug.LogError($"VoiceAvatar: Failed to dispose voice agent: {e.Message}");
        }
        agent = null;
      }
    }
  }
}