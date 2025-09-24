using System.Collections;
using System.Collections.Generic;
using System.Linq; // for ToList
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SimpleFileBrowser;
using UnityVolumeRendering;
using FishNet.Object;
using FishNet.Component.Ownership;
using FishNet.Component.Transforming;
using FishNet.Object.Synchronizing; // For [SyncVar]
using System.IO;
using System.Threading.Tasks; // for asynchronous loading of data
using System; // for ArgumentException
using uMuVR;

public class VolumeDataImporter : NetworkBehaviour
{
  [Header("UI References")]
  public TMP_Dropdown formatDropdown;
  public Button browseButton;
  public Button importButton;
  public TMP_Text statusText;
  public TMP_Text resultText;

  [SyncVar]
  private string selectedPath;
  private VolumeRenderedObject currentObject;
  private DatasetType currentFormat;

  public override void OnStartClient()
  {
    base.OnStartClient();
    if (base.IsOwner)
    {
      browseButton.onClick.AddListener(OnBrowseClicked);
      importButton.onClick.AddListener(OnImportButtonClicked);

      FileBrowser.SetExcludedExtensions(".lnk", ".tmp");
      FileBrowser.AddQuickLink("Documents", System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), null);

      if (formatDropdown.options.Count == 0)
      {
        formatDropdown.AddOptions(new List<string> { "Raw", "DICOM", "NRRD", "NIFTI", "VASP/PARCHG", "Image Sequence" });
      }
    }
  }

  // INIT PUBLIC WRAPPER METHODS
  public void OnBrowseClickedPublic()
  {
    if (base.IsOwner)
    {
      OnBrowseClicked();
    }
  }

  public void OnImportBtnClickedPublic()
  {
    if (base.IsOwner)
    {
      OnImportButtonClicked();
    }
  }
  // END PUBLIC WRAPPER METHODS

  [ObserversRpc(IncludeOwner = true)]
  private void OnBrowseClicked()
  {
    if (!base.IsOwner) return;
    string selectedFormatStr = formatDropdown.options[formatDropdown.value].text;
    currentFormat = GetImageFileFormat(selectedFormatStr);
    var filters = GetFiltersForFormat(selectedFormatStr);
    bool showAllFiles = filters == null;
    FileBrowser.SetFilters(showAllFiles, filters ?? new string[0]);

    FileBrowser.PickMode pickMode = IsFolderFormat(selectedFormatStr) ? FileBrowser.PickMode.Folders : FileBrowser.PickMode.Files;
    bool multiSelect = false;

    // Start the coroutine internally
    StartCoroutine(ShowLoadDialogCoroutine(pickMode, multiSelect));
  }

  private IEnumerator ShowLoadDialogCoroutine(FileBrowser.PickMode pickMode, bool allowMultiSelection)
  {
    if (!base.IsOwner) yield break;
    yield return FileBrowser.WaitForLoadDialog(pickMode, allowMultiSelection, null, null, $"Select {formatDropdown.options[formatDropdown.value].text} Dataset", "Select");

    if (FileBrowser.Success)
    {
      selectedPath = FileBrowser.Result[0]; // SyncVar will propagate this
      UpdateStatusTextRpc($"Selected: {FileBrowserHelpers.GetFilename(selectedPath)}");
      Debug.Log($"Selected path: {selectedPath}");
    }
    else
    {
      selectedPath = null; // SyncVar will propagate this
      UpdateStatusTextRpc("Selection cancelled.");
      Debug.Log("File selection cancelled.");
    }
  }

  [ObserversRpc]
  private void UpdateStatusTextRpc(string text)
  {
    statusText.text = text;
  }

  [ObserversRpc(IncludeOwner = true)]
  private void OnImportButtonClicked()
  {
    if (!base.IsOwner) return;
    if (string.IsNullOrEmpty(selectedPath))
    {
      UpdateResultTextRpc("No file selected. Browse first.");
      return;
    }

    if (!FileBrowserHelpers.FileExists(selectedPath) && !FileBrowserHelpers.DirectoryExists(selectedPath))
    {
      UpdateResultTextRpc("Selected path does not exist.");
      return;
    }

    // Run async import task
    StartCoroutine(ImportDatasetAsync());
  }

  [ObserversRpc]
  private void UpdateResultTextRpc(string text)
  {
    resultText.text = text;
  }

  private IEnumerator ImportDatasetAsync()
  {
    // Trigger server RPC to start the import process
    ImportDatasetAsyncInternal(selectedPath, currentFormat);
    yield return null; // allow RPC to process

    while (currentObject == null && !IsServer) // wait until server spawns object
      yield return null;

    UpdateResultTextRpc($"Imported: {formatDropdown.options[formatDropdown.value].text}");
    Debug.Log($"Spawned volume from {selectedPath}");
  }

  [ServerRpc(RequireOwnership = true)]
  private void ImportDatasetAsyncInternal(string path, DatasetType formatType)
  {
    // start asynchronous import process
    StartCoroutine(PerformImportAsync(path, formatType));
  }

  private IEnumerator PerformImportAsync(string path, DatasetType formatType)
  {
    VolumeDataset dataset = null;

    switch (formatType)
    {
      case DatasetType.Raw:
        // Synchronous importer for raw - check docs for more info.
        // Assumed defaults if .ini file not found by importer - TODO: adjust dimensions, format, endianness, bytesToSkip as needed
        RawDatasetImporter rawImporter = new RawDatasetImporter(path, 256, 256, 256, DataContentFormat.Uint8, Endianness.LittleEndian, 0);
        dataset = rawImporter.Import();
        break;

      case DatasetType.NRRD:
      case DatasetType.NIFTI:
      case DatasetType.PARCHG:
        // Image file importer (sync)
        ImageFileFormat fileFormat = ConvertToImageFileFormat(formatType);
        IImageFileImporter fileImporter = ImporterFactory.CreateImageFileImporter(fileFormat);
        if (fileImporter == null)
        {
          UpdateResultTextRpc("Unsupported file format.");
          yield break;
        }
        dataset = fileImporter.Import(path);
        break;

      case DatasetType.DICOM:
      case DatasetType.ImageSequence:
        // Image sequence importer (async)
        ImageSequenceFormat sequenceFormat = ConvertToImageSequenceFormat(formatType);
        IImageSequenceImporter sequenceImporter = ImporterFactory.CreateImageSequenceImporter(sequenceFormat);
        if (sequenceImporter == null)
        {
          UpdateResultTextRpc("Unsupported sequence format.");
          yield break;
        }

        // Get all files in directory
        List<string> filePaths = Directory.GetFiles(path).ToList();

        // Use progress handler
        using (ProgressHandler progressHandler = new ProgressHandler(new RuntimeProgressView()))
        {
          progressHandler.StartStage(0.2f, "Loading series");

          IEnumerable<IImageSequenceSeries> seriesList = null;
          var loadTask = sequenceImporter.LoadSeriesAsync(filePaths, new ImageSequenceImportSettings { progressHandler = progressHandler });
          while (!loadTask.IsCompleted)
            yield return null;
          seriesList = loadTask.Result;

          progressHandler.EndStage();
          progressHandler.StartStage(0.8f, "Importing series");

          int seriesIndex = 0;
          int numSeries = seriesList.Count();
          foreach (IImageSequenceSeries series in seriesList)
          {
            progressHandler.StartStage(1.0f / numSeries, $"Importing series {seriesIndex + 1} of {numSeries}");
            var importTask = sequenceImporter.ImportSeriesAsync(series, new ImageSequenceImportSettings { progressHandler = progressHandler });
            while (!importTask.IsCompleted)
              yield return null;
            dataset = importTask.Result;
            progressHandler.EndStage();
            seriesIndex++;
          }

          progressHandler.EndStage();
        }
        break;

      default:
        UpdateResultTextRpc("Unknown format.");
        yield break;
    }

    if (dataset == null)
    {
      UpdateResultTextRpc("Import failed. Check console.");
      yield break;
    }

    // Create the volume object
    VolumeRenderedObject volumeObject = VolumeObjectFactory.CreateObject(dataset);
    volumeObject.transform.SetParent(transform);
    volumeObject.transform.localPosition = Vector3.zero;

    // Get the GameObject from the VolumeRenderedObject
    GameObject volumeGameObject = volumeObject.gameObject;

    // Add FishNet components
    volumeGameObject.AddComponent<NetworkObject>();
    volumeGameObject.AddComponent<NetworkTransform>();
    // Add uMuVR components
    volumeGameObject.AddComponent<OwnershipManager>();

    // Spawn the object on the network with the owner as the importing client
    base.Spawn(volumeGameObject, base.Owner);

    // Store reference for local use
    currentObject = volumeObject;
  }

  private DatasetType GetImageFileFormat(string uiFormat)
  {
    return uiFormat switch
    {
      "Raw" => DatasetType.Raw,
      "DICOM" => DatasetType.DICOM,
      "NRRD" => DatasetType.NRRD,
      "NIFTI" => DatasetType.NIFTI,
      "VASP/PARCHG" => DatasetType.PARCHG,
      "Image Sequence" => DatasetType.ImageSequence,
      _ => DatasetType.Raw
    };
  }

  private string[] GetFiltersForFormat(string uiFormat)
  {
    return uiFormat switch
    {
      "Raw" => new[] { ".raw", ".dat", ".vol" },
      "DICOM" => null,
      "NRRD" => new[] { ".nrrd" },
      "NIFTI" => new[] { ".nii" },
      "VASP/PARCHG" => new[] { ".vasp" },
      "Image Sequence" => new[] { ".jpg", ".jpeg", ".png" },
      _ => null
    };
  }

  private ImageSequenceFormat ConvertToImageSequenceFormat(DatasetType datasetType)
  {
    return datasetType switch
    {
      DatasetType.DICOM => ImageSequenceFormat.DICOM,
      DatasetType.ImageSequence => ImageSequenceFormat.ImageSequence,
      _ => throw new ArgumentException("Invalid dataset type for sequence import")
    };
  }

  private ImageFileFormat ConvertToImageFileFormat(DatasetType datasetType)
  {
    return datasetType switch
    {
      DatasetType.NRRD => ImageFileFormat.NRRD,
      DatasetType.NIFTI => ImageFileFormat.NIFTI,
      DatasetType.PARCHG => ImageFileFormat.VASP,
      _ => ImageFileFormat.Unknown
    };
  }

  private bool IsFolderFormat(string uiFormat) => uiFormat == "DICOM" || uiFormat == "Image Sequence";
}