using UnityEngine;
using FishyVoice;
using Adrenak.UniVoice;
using FishNet.Object;
using NetworkBehaviour = FishNet.Object.NetworkBehaviour;

public class VoiceAvatar : NetworkBehaviour
{
  private Agent agent;

  [Header("Positional Audio Settings")]
  [SerializeField] private int maxDistance = 10;
  [SerializeField] private int rolloffDistance = 5;
  [SerializeField] private bool spatialize = true;
  [SerializeField] private float minGain = 1f;
  [SerializeField] private float maxGain = 1f;
  [SerializeField] private float offset = 0f;
  [SerializeField] private float rolloffFactor = 3f;

  private const string roomName = "<DEFAULT>";

  public override void OnStartClient()
  {
    base.OnStartClient();
    if (IsOwner)
    {
      VoiceNetwork voiceNetwork = VoiceNetwork.instance;
      if (voiceNetwork != null)
      {
        // Create positional audio output factory based on example from FishNet script
        agent = voiceNetwork.CreateAgent(new PositionalAudioOutputFactory(maxDistance, rolloffDistance, new PositionalAudioParameters(spatialize, minGain, maxGain, offset, rolloffFactor)));
        agent.JoinChatroom(roomName);
        Debug.Log($"Positional voice agent created and joined {roomName} room for local player.");
      }
      else
      {
        Debug.LogError("VoiceNetwork instance not found!");
      }
    }
  }

  private void OnDestroy()
  {
    agent?.Dispose();
  }
}