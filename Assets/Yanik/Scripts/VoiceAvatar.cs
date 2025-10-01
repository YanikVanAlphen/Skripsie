using UnityEngine;
using FishyVoice;
using Adrenak.UniVoice;
using Adrenak.UniMic;
using FishNet.Object;
using NetworkBehaviour = FishNet.Object.NetworkBehaviour;

public class VoiceAvatar : NetworkBehaviour
{
  private Agent agent;

  //[Header("Positional Audio Settings")]
  //[SerializeField] private int maxDistance = 10;
  //[SerializeField] private int rolloffDistance = 5;
  //[SerializeField] private bool spatialize = true;
  //[SerializeField] private float minGain = 1f;
  //[SerializeField] private float maxGain = 1f;
  //[SerializeField] private float offset = 0f;
  //[SerializeField] private float rolloffFactor = 3f;

  private const string roomName = "<DEFAULT>";

  public override void OnStartClient()
  {
    base.OnStartClient();
    if (IsOwner)
    {
      VoiceNetwork voiceNetwork = VoiceNetwork.instance;
      if (voiceNetwork != null)
      {
        // default nonpositional agent for testing
        agent = voiceNetwork.CreateAgent();
        agent.JoinChatroom(roomName);
        Debug.Log($"Voice agent created and joined {roomName} room for local player (non-positional).");
        // microphone status
        if (Mic.Instance != null)
        {
          Debug.Log($"Microphone recording: {Mic.Instance.IsRecording}, Frequency: {Mic.Instance.Frequency}");
        }
        else
        {
          Debug.LogError("UniMic instance not found!");
        }
      }
      else
      {
        Debug.LogError("VoiceNetwork instance not found!");
      }
      // Log AudioSource status
      var audioSource = GetComponent<AudioSource>();
      if (audioSource != null)
      {
        Debug.Log($"AudioSource found: Enabled={audioSource.enabled}, Output={audioSource.outputAudioMixerGroup}, SpatialBlend={audioSource.spatialBlend}");
      }
      else
      {
        Debug.LogError("AudioSource component missing on UserAvatar!");
      }
      // Log audio output factory
      if (agent?.AudioOutputFactory != null)
      {
        Debug.Log($"AudioOutputFactory: {agent.AudioOutputFactory.GetType().Name}");
      }
      else
      {
        Debug.LogError("AudioOutputFactory not set on Agent!");
      }
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
        Debug.Log("Voice agent disposed for local player.");
      }
      catch (System.Exception e)
      {
        Debug.LogError($"Failed to dispose voice agent: {e.Message}");
      }
      agent = null;
    }
  }
}