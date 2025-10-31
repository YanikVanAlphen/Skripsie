[Unity]: https://unity.com/
[Fish-Networking]: https://github.com/FirstGearGames/FishNet/
[uMuVR]: https://github.com/hpcvis/MuVR

# Multi-User Collaborative Data Analysis in VR

[uMuVR] is a [Unity] framework that provides a foundation for multiuser/player VR experiences. Its networking is provided by the [Fish-Networking] library. It is primarily designed around allowing users to quickly create VR applications. 
[Fish-Networking]'s documentation can be found here: https://fish-networking.gitbook.io/docs/
The journal paper for uMuVR can be found here: https://isca-hq.org/Documents/Journal/Archive/2023/2023volume3001/2023volume300104.pdf

Additional setup information about uMuVR can be found either in their Git repo or in the `README.md` of this repo's `MuVR` branch.

## Setup
Below is the setup instructions as given by [uMuVR]:

Clone the repository (or download it as a zip) make sure your clone is recursive (add `--recursive`) or that you run the following commands in the console after you have cloned the repository:
```bash
git submodule init
git submodule update
```
To use Git for your own version control, remove this repo's remote and replace it with your own:
```bash
git remote rm origin
git remote add origin <your new remote repository here> # Optional
```

**WINDOWS USERS:**  git for windows doesn't track symbolic links by default. Thus if you encounter weird issues claiming the files for FishNet or UltimateXR can't be found (or you get 500+ errors when opening the project in Unity) run from an administrative PowerShell or command prompt window: 
```bash
cd Submodules # From the root project directory
./FixSymlinks.bat
```

---

## Directory Structure for Scripts written for this project
The scene, scripts, prefabs and materials created for this project can be found in the folder named `Yanik`:
```
└── Assets/
    ├── EasyVolumeRendering
    ├── FishNet
    ├── ...
    └── Yanik
        ├── Materials
        │    └── ...
        ├── Prefabs
        │    └── ...
        └── Scripts
            ├── Data Analysis
            │    ├── CrossSectionManager.cs
            │    └── CrossSectionSync.cs
            ├── Networking
            │    ├── ConnectionSetup.cs
            │    └── VolumeRenderObjectFindUtility.cs
            ├── User
            │    ├── ActivateTeleportationRay.cs
            │    ├── AnimateHandOnInput.cs
            │    ├── HandData.cs
            │    └── TeleportProviderAssigner.cs
            ├── Voice chat
            │    └── VoiceAvatar.cs
            └── Volumetric data
                 ├── DataSerializer.cs
                 ├── DataSetLoader.cs
                 ├── VolumeDataControlUI.cs
                 └── VolumeDataNetworker.cs
```