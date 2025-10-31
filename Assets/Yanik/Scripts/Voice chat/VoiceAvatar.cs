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
    private Agent agent; // ref to UniVoice Agent, handles audio input/output and voice data transmission over network
    private const string chatroomName = "<DEFAULT>"; //  same chatroom name as connection setup's created chat room

    public override void OnStartClient()
    {
      base.OnStartClient();
      StartCoroutine(StartVoiceAgent());
    }

    private IEnumerator StartVoiceAgent()
    {
      // wait for VoiceNetwork instance to be created and that the network is actually running
      while (VoiceNetwork.instance == null || !VoiceNetwork.instance.networkActive)
        yield return new WaitForSeconds(0.1f);
      // get created voicenetwork instance now that it is ready
      VoiceNetwork voiceNetwork = VoiceNetwork.instance;

      // wait for valid client Id from FishNet (client instance fully started), [-1] is invalid Id
      while (LocalConnection.ClientId < 0)
        yield return new WaitForSeconds(0.1f);

      // create a new agent object
      agent = voiceNetwork.CreateAgent();

      // wait for the chatroom to be created/exist, openrooms is a dictionary that contains all active chatrooms and clients in the rooms
      while (!voiceNetwork.openRooms.ContainsKey(chatroomName))
        yield return new WaitForSeconds(0.1f);
      // adds my clientID to rooms client list and lets other clients know that I joined. Tell server I want to join chat
      voiceNetwork.JoinChatroom(chatroomName);
      // UniVoice joining. Start my microphone and get ready to send/receive audio
      agent.JoinChatroom(chatroomName);
      Debug.Log($"Client {LocalConnection.ClientId} joined chatroom {chatroomName}");
    }

    public override void OnStopClient() // runs when client instance stops, provided by Fishnetworking
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