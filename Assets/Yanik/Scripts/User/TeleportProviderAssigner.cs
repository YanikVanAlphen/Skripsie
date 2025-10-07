using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
/// <summary>
/// Dynamically assigns Teleportation Provider component of XR Rig to the Teleportation Area of the scene floor plane upon
/// spawn.
/// </summary>
public class TeleportProviderAssigner : MonoBehaviour
{
  private void Start()
  {
    TeleportationProvider provider = GetComponentInChildren<TeleportationProvider>();
    if (provider == null)
    {
      return;
    }

    // find all teleportation areas in scene (the floor) and add user teleportation provider to enable teleport movement function
    TeleportationArea[] areas = FindObjectsOfType<TeleportationArea>();
    foreach (TeleportationArea area in areas)
    {
      area.teleportationProvider = provider;
    }
  }
}