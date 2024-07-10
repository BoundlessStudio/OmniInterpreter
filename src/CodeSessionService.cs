using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoundlessAi.OmniInterpreter;

public interface ICodeSessionService
{
  Task<Dictionary<string, string>> GetPackagesAsync(string session);
  Task<string?> ExecuteCodeAsync(string session, string code);
  Task<SessionsRemoteFileMetadata> UploadFileAsync(string session, string path, BinaryData data);
  Task<BinaryData> DownloadFileAsync(string session, string path);
  Task<IReadOnlyList<SessionsRemoteFileMetadata>> ListFilesAsync(string session);
}

public class CodeSessionService : ICodeSessionService
{
  private const string API_VERSION = "2024-02-02-preview";

  private readonly Uri endpoint;
  private readonly SessionsSettings settings;
  private readonly IAuthorizationTokenProvider tokenProvider;
  private readonly IHttpClientFactory httpClientFactory;
  private readonly ILogger logger;

  /// <summary>
  /// Initializes a new instance of the SessionsPythonTool class.
  /// </summary>
  /// <param name="settings">The settings for the Python tool plugin. </param>
  /// <param name="httpClientFactory">The HTTP client factory. </param>
  /// <param name="authTokenProvider"> Optional provider for auth token generation. </param>
  /// <param name="loggerFactory">The logger factory. </param>
  public CodeSessionService(
      IOptions<SessionsSettings> options,
      IHttpClientFactory httpClientFactory,
      IAuthorizationTokenProvider authTokenProvider,
      ILoggerFactory? loggerFactory = null)
  {
    this.settings = options.Value;

    if (settings == null)
      throw new ArgumentNullException(nameof(settings));

    if (settings.Endpoint == null)
      throw new ArgumentNullException(nameof(settings.Endpoint));

    if (httpClientFactory == null)
      throw new ArgumentNullException(nameof(httpClientFactory));

    // Ensure the endpoint won't change by reference 
    this.endpoint = new Uri(settings.Endpoint);

    this.tokenProvider = authTokenProvider;
    this.httpClientFactory = httpClientFactory;
    this.logger = loggerFactory?.CreateLogger(typeof(CodeSessionService)) ?? NullLogger.Instance;
  }

  /// <summary>
  /// Get the pre-installed packages in the sessions environment.
  /// </summary>
  /// <param name="session">Session id</param>
  /// <returns>A dictionary of the packages and their version number</returns>
  /// <exception cref="ArgumentException"></exception>
  /// <exception cref="InvalidOperationException"></exception>
  public async Task<Dictionary<string, string>> GetPackagesAsync(string session)
  {
    if (string.IsNullOrWhiteSpace(session))
      throw new ArgumentException("The argument cannot be null, empty, or whitespace.", nameof(session));

    var code = "import pkg_resources\n[(d.project_name, d.version) for d in pkg_resources.working_set]";
    var data = await ExecuteCodeAsync(session, code);
    if (string.IsNullOrWhiteSpace(data))
      throw new InvalidOperationException("Failed to get package list from the remote session.");

    var result = new Dictionary<string, string>();
    var matches = PythonTupleRegex().Matches(data);

    foreach (Match match in matches)
    {
      if (match.Groups.Count == 3)
      {
        string packageName = match.Groups[1].Value;
        string version = match.Groups[2].Value;
        result[packageName] = version;
      }
    }

    return result;
  }

  /// <summary>
  /// Executes the provided Python code.
  /// Start and end the code snippet with double quotes to define it as a string.
  /// Insert \n within the string wherever a new line should appear.
  /// Add spaces directly after \n sequences to replicate indentation.
  /// Use \"" to include double quotes within the code without ending the string.
  /// Keep everything in a single line; the \n sequences will represent line breaks
  /// when the string is processed or displayed.
  /// </summary>
  /// <param name="session">Session id</param>
  /// <param name="code"> The valid Python code to execute. </param>
  /// <returns> The result of the Python code execution. </returns>
  /// <exception cref="ArgumentNullException"></exception>
  /// <exception cref="HttpRequestException"></exception>
  public async Task<string?> ExecuteCodeAsync(string session, string code)
  {
    if (string.IsNullOrWhiteSpace(session))
      throw new ArgumentException("The argument cannot be null, empty, or whitespace.", nameof(session));

    if (string.IsNullOrWhiteSpace(code))
      throw new ArgumentException("The argument cannot be null, empty, or whitespace.", nameof(code));

    if (this.settings.SanitizeInput == true)
    {
      code = SanitizeCodeInput(code);
    }

    this.logger.LogTrace("Executing Python code: {Code}", code);

    using var httpClient = this.httpClientFactory.CreateClient();

    var requestBody = new
    {
      properties = new SessionsCodeExecutionProperties(this.settings, session, code)
    };

    await this.AddHeadersAsync(httpClient).ConfigureAwait(false);

    using var request = new HttpRequestMessage(HttpMethod.Post, this.endpoint + $"python/execute?api-version={API_VERSION}")
    {
      Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
    };

    var response = await httpClient.SendAsync(request).ConfigureAwait(false);

    if (!response.IsSuccessStatusCode)
    {
      var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      logger.LogTrace("Failed to execute python code: {error}", errorBody);
      throw new HttpRequestException($"Failed to execute python code. Status: {response.StatusCode}. Details: {errorBody}.");
    }

    var jsonElementResult = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var error = jsonElementResult.GetProperty("stderr").GetString();
    if (!string.IsNullOrEmpty(error))
    {
      logger.LogTrace("Failed to execute python code: {error}", error);
      throw new HttpRequestException($"Failed to execute python code. Status: 400. Details: {error}.");
    }

    var stdout = jsonElementResult.GetProperty("stdout").GetString();
    logger.LogTrace("Standard Output: {Log}", stdout);

    var result = jsonElementResult.GetProperty("result").GetString();

    if(!string.IsNullOrWhiteSpace(result))
      return result;

    if (!string.IsNullOrWhiteSpace(stdout))
      return stdout;

    return null;
  }

  private async Task AddHeadersAsync(HttpClient httpClient)
  {
    var token = await this.tokenProvider.GetToken().ConfigureAwait(false);
    httpClient.DefaultRequestHeaders.Add("User-Agent", $"Boundless.Ai/1.0.0-alpha (Language=dotnet)");
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
  }

  /// <summary>
  /// Upload a file to the session pool.
  /// </summary>
  /// <param name="session">Session id</param>
  /// <param name="remoteFilePath">The path to the file in the session.</param>
  /// <param name="localFilePath">The path to the file on the local machine.</param>
  /// <returns>The metadata of the uploaded file.</returns>
  /// <exception cref="ArgumentNullException"></exception>
  /// <exception cref="HttpRequestException"></exception>
  public async Task<SessionsRemoteFileMetadata> UploadFileAsync(string session, string path, BinaryData data)
  {
    if (string.IsNullOrWhiteSpace(session))
      throw new ArgumentException("The argument cannot be null, empty, or whitespace.", nameof(session));

    if (string.IsNullOrWhiteSpace(path))
      throw new ArgumentException("The argument cannot be null, empty, or whitespace.", nameof(path));

    if (data is null)
      throw new ArgumentNullException(nameof(path));

    this.logger.LogInformation("Uploading file to {path}", path);

    using var httpClient = this.httpClientFactory.CreateClient();

    await this.AddHeadersAsync(httpClient).ConfigureAwait(false);

    using var fileContent = new ByteArrayContent(data.ToArray());
    using var request = new HttpRequestMessage(HttpMethod.Post, $"{this.endpoint}python/uploadFile?identifier={session}&api-version={API_VERSION}")
    {
      Content = new MultipartFormDataContent
      {
        { fileContent, "file", path },
      }
    };

    var response = await httpClient.SendAsync(request).ConfigureAwait(false);

    if (!response.IsSuccessStatusCode)
    {
      var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      throw new HttpRequestException($"Failed to upload file. Status code: {response.StatusCode}. Details: {errorBody}.");
    }

    var JsonElementResult = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

    return JsonSerializer.Deserialize<SessionsRemoteFileMetadata>(JsonElementResult.GetProperty("$values")[0].GetRawText())!;
  }

  /// <summary>
  /// Downloads a file from the current Session ID.
  /// </summary>
  /// <param name="session">Session id</param>
  /// <param name="path"> The path to download the file from, relative to `/mnt/data`. </param>
  /// <returns> The data of the downloaded file as BinaryData.</returns>
  [Description("Downloads a file from the current Session ID.")]
  public async Task<BinaryData> DownloadFileAsync(string session, string path)
  {
    if (string.IsNullOrWhiteSpace(session))
      throw new ArgumentException("The argument cannot be null, empty, or whitespace.", nameof(session));

    if (string.IsNullOrWhiteSpace(path))
      throw new ArgumentException("The argument cannot be null, empty, or whitespace.", nameof(path));

    this.logger.LogTrace("Downloading file {RemoteFilePath}", path);

    using var httpClient = this.httpClientFactory.CreateClient();
    await this.AddHeadersAsync(httpClient).ConfigureAwait(false);

    var response = await httpClient.GetAsync(new Uri($"{this.endpoint}python/downloadFile?identifier={session}&filename={path}&api-version={API_VERSION}")).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
    {
      var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      throw new HttpRequestException($"Failed to download file. Status code: {response.StatusCode}. Details: {errorBody}.");
    }

    var fileContent = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    return new BinaryData(fileContent);
  }

  /// <summary>
  /// Lists all files in the provided session id pool.
  /// </summary>
  /// <returns> The list of files in the session. </returns>
  public async Task<IReadOnlyList<SessionsRemoteFileMetadata>> ListFilesAsync(string session)
  {
    if (string.IsNullOrWhiteSpace(session))
      throw new ArgumentException("The argument cannot be null, empty, or whitespace.", nameof(session));

    this.logger.LogTrace("Listing files for Session ID: {SessionId}", session);

    using var httpClient = this.httpClientFactory.CreateClient();
    await this.AddHeadersAsync(httpClient).ConfigureAwait(false);

    var response = await httpClient.GetAsync(new Uri($"{this.endpoint}python/files?identifier={session}&api-version={API_VERSION}")).ConfigureAwait(false);

    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException($"Failed to list files. Status code: {response.StatusCode}");
    }

    var jsonElementResult = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

    var files = jsonElementResult.GetProperty("$values");

    var result = new SessionsRemoteFileMetadata[files.GetArrayLength()];

    for (var i = 0; i < result.Length; i++)
    {
      result[i] = JsonSerializer.Deserialize<SessionsRemoteFileMetadata>(files[i].GetRawText())!;
    }

    return result;
  }

  private static string SanitizeCodeInput(string code)
  {
    // Remove leading whitespace and backticks and python (if llm mistakes python console as terminal)
    code = RemoveLeadingWhitespaceBackticksPython().Replace(code, "");

    // Remove trailing whitespace and backticks
    code = RemoveTrailingWhitespaceBackticks().Replace(code, "");

    return code;
  }

  private static Regex RemoveLeadingWhitespaceBackticksPython()
  {
    return new Regex(@"^(\s|`)*(?i:python)?\s*", RegexOptions.ExplicitCapture);
  }

  private static Regex RemoveTrailingWhitespaceBackticks()
  {
    return new Regex(@"(\s|`)*$", RegexOptions.ExplicitCapture);
  }

  private static Regex PythonTupleRegex()
  {
    return new Regex(@"\('([^']+)',\s*'([^']+)'\)");
  }

  //[GeneratedRegex(@"^(\s|`)*(?i:python)?\s*", RegexOptions.ExplicitCapture)]
  //private static partial Regex RemoveLeadingWhitespaceBackticksPython();

  //[GeneratedRegex(@"(\s|`)*$", RegexOptions.ExplicitCapture)]
  //private static partial Regex RemoveTrailingWhitespaceBackticks();

  //[GeneratedRegex(@"\('([^']+)',\s*'([^']+)'\)")]
  //private static partial Regex PythonTupleRegex();
}
