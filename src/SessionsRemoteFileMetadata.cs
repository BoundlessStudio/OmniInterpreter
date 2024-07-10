using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BoundlessAi.OmniInterpreter;

public class SessionsRemoteFileMetadata
{
  /// <summary>
  /// Initializes a new instance of the SessionRemoteFileMetadata class.
  /// </summary>
  [JsonConstructor]
  public SessionsRemoteFileMetadata(string filename, int size)
  {
    this.Filename = filename;
    this.Size = size;
  }

  /// <summary>
  /// The filename relative to `/mnt/data`.
  /// </summary>
  [Description("The filename relative to `/mnt/data`.")]
  [JsonPropertyName("filename")]
  public string Filename { get; set; }

  /// <summary>
  /// The size of the file in bytes.
  /// </summary>
  [Description("The size of the file in bytes.")]
  [JsonPropertyName("size")]
  public int Size { get; set; }

  /// <summary>
  /// The last modified time.
  /// </summary>
  [Description("Last modified time.")]
  [JsonPropertyName("last_modified_time")]
  public DateTime? LastModifiedTime { get; set; }


  public override string ToString()
  {
    return this.Filename;
  }
}
