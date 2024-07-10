using System.Text.Json.Serialization;
using static BoundlessAi.OmniInterpreter.SessionsSettings;


namespace BoundlessAi.OmniInterpreter;


internal sealed class SessionsCodeExecutionProperties
{
  /// <summary>
  /// The session identifier.
  /// </summary>
  [JsonPropertyName("identifier")]
  public string Identifier { get; }

  /// <summary>
  /// Code input type.
  /// </summary>
  [JsonPropertyName("codeInputType")]
  public CodeInputTypeSetting CodeInputType { get; } = CodeInputTypeSetting.Inline;

  /// <summary>
  /// Code execution type.
  /// </summary>
  [JsonPropertyName("executionType")]
  public CodeExecutionTypeSetting CodeExecutionType { get; } = CodeExecutionTypeSetting.Synchronous;

  /// <summary>
  /// Timeout in seconds for the code execution.
  /// </summary>
  [JsonPropertyName("timeoutInSeconds")]
  public int TimeoutInSeconds { get; } = 100;

  /// <summary>
  /// The Python code to execute.
  /// </summary>
  [JsonPropertyName("pythonCode")]
  public string? PythonCode { get; }

  public SessionsCodeExecutionProperties(SessionsSettings settings, string sessionId, string pythonCode)
  {
    this.Identifier = sessionId;
    this.PythonCode = pythonCode;
    this.TimeoutInSeconds = settings.TimeoutInSeconds;
    this.CodeInputType = settings.CodeInputType;
    this.CodeExecutionType = settings.CodeExecutionType;
  }
}
