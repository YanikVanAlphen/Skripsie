using UnityVolumeRendering;
using System.IO;
using System.IO.Compression;
using System;
using UnityEngine;
using System.Linq;

public static class DatasetSerializer
{
  /* from UnityVolumeRendering source code:
   * ------------------------------------------------------------------------------------------------------------------------------------------------
   * Dataset returned from plugin's data loader is an instance of the plugin's custom VolumeDataset class with
   * description: "An imported dataset. Contains a 3D pixel array of density values."
   * Public attributes that can be accessed are:
   * float[] data,
   * int dimX, dimY, dimZ,
   * Vector3 scale,
   * Quaternion rotation,
   * float volumeScale,
   * string datasetName,
   * and then public get/set methods to access scale X,Y,Z but the plugin reccommends using scale itself - declared with the [System.Obsolete()] tag.
   * ------------------------------------------------------------------------------------------------------------------------------------------------
   */
  public static byte[] Serialize(VolumeDataset dataset)
  {
    if (dataset == null || dataset.data == null || dataset.data.Length == 0)
    {
      Debug.LogError("Dataset is null or empty.");
      return null;
    }

    try
    {
      Debug.Log($"Serializing dataset with dimensions dimX={dataset.dimX}, dimY={dataset.dimY}, dimZ={dataset.dimZ}");
      DateTime startTime = DateTime.Now;

      byte[] uncompressedData;
      using (MemoryStream stream = new MemoryStream())
      {
        // sequentially write dimensions (int values)
        WriteInt32(stream, dataset.dimX);
        WriteInt32(stream, dataset.dimY);
        WriteInt32(stream, dataset.dimZ);
        // sequentially write scale values (floats)
        WriteFloat(stream, dataset.scale.x);
        WriteFloat(stream, dataset.scale.y);
        WriteFloat(stream, dataset.scale.z);

        float[] data = dataset.data;
        WriteInt32(stream, data.Length);

        foreach (float value in data)
        {
          WriteFloat(stream, value);
        }
        // make sure all data is fully written to stream before doing anything further
        stream.Flush();

        uncompressedData = stream.ToArray();
      }
      DateTime endTime = DateTime.Now;
      TimeSpan duration = endTime - startTime;
      Debug.Log($"Serialization took {duration.TotalSeconds} seconds.");
      Debug.Log($"Data size before compression: {uncompressedData.Length / 1024f / 1024f:F2} MB"); // convert size from bytes to megabytes
      startTime = DateTime.Now;
      // compress the data
      using (MemoryStream compressedStream = new MemoryStream())
      {
        using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Compress, leaveOpen: true)) // explicitly leave stream open, gives errors with ToArray function otherwise
        {
          gzip.Write(uncompressedData, 0, uncompressedData.Length);
          gzip.Flush(); // make sure all compressed data is fully written
        }
        byte[] compressedData = compressedStream.ToArray();

        endTime = DateTime.Now;
        duration = endTime - startTime;
        Debug.Log($"Compression took {duration.TotalSeconds} seconds.");
        Debug.Log($"Data size after compression: {compressedData.Length / 1024f / 1024f:F2} MB");

        return compressedData;
      }
    }
    catch (Exception e)
    {
      Debug.LogError($"{e.Message}");
      return null;
    }
  }
  // Helper functions to convert int/float to bytes and write to the stream
  // Docs: (https://learn.microsoft.com/en-us/dotnet/api/system.bitconverter?view=net-9.0)
  private static void WriteInt32(Stream stream, int value)
  {
    byte[] bytes = BitConverter.GetBytes(value);
    stream.Write(bytes, 0, bytes.Length);
  }

  private static void WriteFloat(Stream stream, float value)
  {
    byte[] bytes = BitConverter.GetBytes(value);
    stream.Write(bytes, 0, bytes.Length);
  }
  //

  public static VolumeDataset Deserialize(byte[] compressedData)
  {
    if (compressedData == null || compressedData.Length == 0)
    {
      Debug.LogError("Dataset is null or empty.");
      return null;
    }

    try // similar process to serialize and compress just inverted
    {
      Debug.Log("Decompressing data.");
      DateTime startTime = DateTime.Now;

      byte[] decompressedData;
      using (MemoryStream compressedStream = new MemoryStream(compressedData))
      using (MemoryStream decompressedStream = new MemoryStream())
      {
        using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
          gzip.CopyTo(decompressedStream);
          gzip.Flush(); // ensure all data is written to new stream before continuing
        }
        decompressedData = decompressedStream.ToArray();
      }

      DateTime endTime = DateTime.Now;
      TimeSpan duration = endTime - startTime;
      Debug.Log($"Decompression took {duration.TotalSeconds} seconds.");
      Debug.Log($"Deserializing dataset.");
      startTime = DateTime.Now;

      using (MemoryStream stream = new MemoryStream(decompressedData)) // BinaryReader does not directly accept byte[] datatypes so best to first convert it to a MemoryStream before reading from it
      using (BinaryReader reader = new BinaryReader(stream)) // reads data in sequence
      {
        VolumeDataset dataset = ScriptableObject.CreateInstance<VolumeDataset>(); // init the volume data as a scriptableObject to avoid console warnings
        // read in parameters the same way that we stored them in transmitted data
        dataset.dimX = reader.ReadInt32();
        dataset.dimY = reader.ReadInt32();
        dataset.dimZ = reader.ReadInt32();
        dataset.scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        int dataLength = reader.ReadInt32();

        dataset.data = new float[dataLength];
        for (int i = 0; i < dataLength; i++) // rest of data
          dataset.data[i] = reader.ReadSingle();

        endTime = DateTime.Now;
        duration = endTime - startTime;
        Debug.Log($"Deserialized dataset successfully: dimX={dataset.dimX}, dimY={dataset.dimY}, dimZ={dataset.dimZ}.");
        Debug.Log($"Deserialization took {duration.TotalSeconds} seconds.");

        return dataset;
      }
    }
    catch (Exception e)
    {
      Debug.LogError($"Could not deserialize dataset: {e.Message}");
      return null;
    }
  }

  public static byte[][] ChunkData(byte[] data, int chunkSize = 1024 * 1024) // split data into 1MB chunks for transmission
  {
    if (data == null || data.Length == 0)
    {
      Debug.LogError("Data is null or empty.");
      return new byte[0][];
    }

    int numChunks = (int)Math.Ceiling((double)data.Length / chunkSize); // calc amount of chunks to create
    byte[][] chunks = new byte[numChunks][];
    for (int i = 0; i < numChunks; i++)
    {
      int length = Math.Min(chunkSize, data.Length - (i * chunkSize)); // for case when remaining data for last chunk is less than 1MB
      chunks[i] = new byte[length];
      Array.Copy(data, i * chunkSize, chunks[i], 0, length);
      Debug.Log($"Created chunk {(i + 1).ToString()}/{numChunks}, size={length} bytes");
    }
    return chunks;
  }

  public static byte[] CombineChunks(byte[][] chunks) // reassemble chunk 2D array into single data byte array
  {
    if (chunks == null || chunks.Length == 0)
    {
      Debug.LogError("Chunks array is null or empty.");
      return null;
    }

    int totalLength = 0;
    foreach (byte[] chunk in chunks) // get total length of all arrays within chunks = total length of data to store
    {
      if (chunk != null)
      {
        totalLength += chunk.Length;
      }
    }
    byte[] data = new byte[totalLength];
    
    int offset = 0;
    foreach (byte[] chunk in chunks)
    {
      if (chunk != null)
      {
        Array.Copy(chunk, 0, data, offset, chunk.Length);
        offset += chunk.Length;
      }
    }
    Debug.Log($"Combined {chunks.Length} chunks into {totalLength / 1024f / 1024f:F2} MB");
    return data;
  }
}