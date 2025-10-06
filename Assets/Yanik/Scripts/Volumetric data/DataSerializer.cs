using UnityVolumeRendering;
using System.IO;
using System.IO.Compression;
using System;
using UnityEngine;
using System.Linq;

public static class DatasetSerializer
{
  public static byte[] Serialize(VolumeDataset dataset)
  {
    if (dataset == null || dataset.data == null || dataset.data.Length == 0)
    {
      Debug.LogError("Error in serializing dataset: dataset is null or empty.");
      return null;
    }

    try
    {
      byte[] uncompressedData;
      using (MemoryStream stream = new MemoryStream())
      {
        WriteInt32(stream, dataset.dimX); // 32 bit int
        WriteInt32(stream, dataset.dimY);
        WriteInt32(stream, dataset.dimZ);
        WriteSingle(stream, dataset.scale.x); // float values
        WriteSingle(stream, dataset.scale.y);
        WriteSingle(stream, dataset.scale.z);

        float[] data = dataset.data;
        WriteInt32(stream, data.Length);
        Debug.Log($"Serializing dataset: dimX={dataset.dimX}, dimY={dataset.dimY}, dimZ={dataset.dimZ}, dataLength={data.Length}");
        
        foreach (float value in data)
        {
          WriteSingle(stream, value);
        }
        stream.Flush(); // make sure all data is fully written to stream before doing anything further
        
        uncompressedData = stream.ToArray();
        Debug.Log($"Data size before compression: {uncompressedData.Length / 1024f / 1024f:F2} MB"); // convert size from bytes to megabytes
      }

      // compress the data
      using (MemoryStream compressedStream = new MemoryStream())
      {
        using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Compress, leaveOpen: true)) // explicitly leave stream open - gives errors with ToArray function otherwise
        {
          gzip.Write(uncompressedData, 0, uncompressedData.Length);
          gzip.Flush(); // make sure all compressed data is fully written
        }
        byte[] compressedData = compressedStream.ToArray();
        Debug.Log($"Data size after compression: {compressedData.Length / 1024f / 1024f:F2} MB");
        return compressedData;
      }
    }
    catch (Exception e)
    {
      Debug.LogError($"Could not serialize dataset: {e.Message}");
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

  private static void WriteSingle(Stream stream, float value)
  {
    byte[] bytes = BitConverter.GetBytes(value);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static VolumeDataset Deserialize(byte[] compressedData)
  {
    if (compressedData == null || compressedData.Length == 0)
    {
      Debug.LogError("Error in deserializing dataset: dataset is null or empty.");
      return null;
    }

    try // similar process to serialize and compress just inverted
    {
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

      using (MemoryStream stream = new MemoryStream(decompressedData))
      using (BinaryReader reader = new BinaryReader(stream)) // reads data in sequence
      {
        //VolumeDataset dataset = new VolumeDataset
        //{
        //  dimX = reader.ReadInt32(),
        //  dimY = reader.ReadInt32(),
        //  dimZ = reader.ReadInt32(),
        //  scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
        //};
        VolumeDataset dataset = ScriptableObject.CreateInstance<VolumeDataset>(); // properly inits the volume data as a scriptableObject to avoid console warning
        dataset.dimX = reader.ReadInt32();
        dataset.dimY = reader.ReadInt32();
        dataset.dimZ = reader.ReadInt32();
        dataset.scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        int dataLength = reader.ReadInt32();
        dataset.data = new float[dataLength];
        Debug.Log($"Deserializing dataset: dimX={dataset.dimX}, dimY={dataset.dimY}, dimZ={dataset.dimZ}, dataLength={dataLength}");
        for (int i = 0; i < dataLength; i++) // rest of data
          dataset.data[i] = reader.ReadSingle();
        Debug.Log($"Deserialized dataset successfully: dimX={dataset.dimX}, dimY={dataset.dimY}, dimZ={dataset.dimZ}");
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
      Debug.Log($"Created chunk {(i+1).ToString()}/{numChunks}, size={length} bytes");
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