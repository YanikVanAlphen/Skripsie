using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class TeleportProviderAssigner : MonoBehaviour
{
  private void Start()
  {
    TeleportationProvider provider = GetComponentInChildren<TeleportationProvider>();
    if (provider == null)
    {
      Debug.LogError("TeleportProviderAssigner: No TeleportationProvider found.");
      return;
    }

    TeleportationArea[] areas = FindObjectsOfType<TeleportationArea>();
    foreach (TeleportationArea area in areas)
    {
      area.teleportationProvider = provider;
    }

    Debug.Log($"TeleportProviderAssigner: Assigned TeleportationProvider to {areas.Length} TeleportationArea(s).");
  }
}