using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BoundlessAi.OmniInterpreter;


/// <summary>
/// Settings for a Python Sessions Plugin.
/// </summary>
public class SessionsSettings
{
  /// <summary>
  /// Determines if the input should be sanitized.
  /// </summary>
  public bool? SanitizeInput { get; set; }

  /// <summary>
  /// The target endpoint.
  /// </summary>
  public string? Endpoint { get; set; }

  /// <summary>
  /// Timeout in seconds for the code execution.
  /// </summary>
  public int TimeoutInSeconds { get; set; } = 100;

  /// <summary>
  /// Code input type.
  /// </summary>
  [JsonIgnore]
  public CodeInputTypeSetting CodeInputType { get; set; } = CodeInputTypeSetting.Inline;

  /// <summary>
  /// Code execution type.
  /// </summary>
  [JsonIgnore]
  public CodeExecutionTypeSetting CodeExecutionType { get; set; } = CodeExecutionTypeSetting.Synchronous;

  public SessionsSettings()
  {
  }

  /// <summary>
  /// Code input type.
  /// </summary>
  [Description("Code input type.")]
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public enum CodeInputTypeSetting
  {
    /// <summary>
    /// Code is provided as a inline string.
    /// </summary>
    [Description("Code is provided as a inline string.")]
    [JsonPropertyName("inline")]
    Inline
  }

  /// <summary>
  /// Code input type.
  /// </summary>
  [Description("Code input type.")]
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public enum CodeExecutionTypeSetting
  {
    /// <summary>
    /// Code is provided as a inline string.
    /// </summary>
    [Description("Code is provided as a inline string.")]
    [JsonPropertyName("synchronous")]
    Synchronous
  }
}