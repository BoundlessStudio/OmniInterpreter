using Microsoft.Extensions.DependencyInjection;
using static System.Net.Mime.MediaTypeNames;
using System.IO;

namespace BoundlessAi.OmniInterpreter.Tests;

[TestClass]
public class CodeSessionServiceTests
{
  // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
  #pragma warning disable CS8618 
  private CodeSessionService codeService;
  private HttpClient httpClient;
#pragma warning restore CS8618

  [TestInitialize]
  public void TestInitialize()
  {
    var endpoint = Environment.GetEnvironmentVariable("AZURE_DYNAMIC_SESSION_ENDPOINT") 
      ?? throw new ArgumentException("Environment variable for endpoint is required.", "AZURE_DYNAMIC_SESSION_ENDPOINT");

    var services = new ServiceCollection();
    services.AddHttpClient();
    services.AddOptions<SessionsSettings>().Configure(o =>
    {
      o.Endpoint = endpoint;
      o.SanitizeInput = true;
    });
    services.AddSingleton<IAuthorizationTokenProvider, DefaultAzureCredentialTokenProvider>();
    services.AddSingleton<CodeSessionService>();
    var sp = services.BuildServiceProvider();

    this.httpClient = sp.GetRequiredService<HttpClient>();
    this.codeService = sp.GetRequiredService<CodeSessionService>();
  }

  private async Task<BinaryData> DownloadFileAsync(string url)
  {
    var data = await this.httpClient.GetByteArrayAsync(url);
    return BinaryData.FromBytes(data);
  }

  [TestMethod]
  public async Task Test1_ExecuteCodeAsync_ValidCode_ReturnsResult()
  {
    // Arrange
    var sessionId = "FAC4BAE7-15F4-494A-8F78-78D0A3E9EBA1";
    var code = "print('Hello, World!')";

    // Act
    var result = await codeService.ExecuteCodeAsync(sessionId, code);

    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual("Hello, World!", result.Trim());
  }

  [TestMethod]
  public async Task Test1_ExecuteCodeAsync_JSCode2_ReturnsResult()
  {
    // Arrange
    var sessionId = "FAC4BAE7-15F4-494A-8F78-78D0A3E9EBA2";

    // Create JavaScript file content
    var jsFileContent = @"console.log(""Hello, World!"");";

    // Create Python code
    var pythonCode = @"
import os
import subprocess
import sys

def install_node(filename):
    data_dir = '/mnt/data'
    file_path = os.path.join(data_dir, filename)
    subprocess.run([""tar"", ""-xf"", file_path, ""-C"", data_dir], check=True)
    extracted_dir = filename.replace("".tar.xz"", """")
    os.environ[""PATH""] = os.path.join(data_dir, extracted_dir, ""bin"") + os.pathsep + os.environ[""PATH""]

def run_hello_world():
    js_file_path = os.path.join('/mnt/data', 'hello_world.js')
    result = subprocess.run([""node"", js_file_path], capture_output=True, text=True, check=True)
    print(f""JavaScript output: {result.stdout}"")

try:
    install_node(""node-v14.17.0-linux-x64.tar.xz"")
    run_hello_world()
except subprocess.CalledProcessError as e:
    print(f""An error occurred during installation or execution: {e}"")
    sys.exit(1)
except OSError as e:
    print(f""An error occurred: {e}"")
    sys.exit(1)
";

    // Download node package (for uploading to the environment)
    var nodePackageContent = await DownloadFileAsync("https://nodejs.org/dist/v14.17.0/node-v14.17.0-linux-x64.tar.xz");
    var jsContent = BinaryData.FromString(jsFileContent);

    // Act
    await this.codeService.UploadFileAsync(sessionId, "node-v14.17.0-linux-x64.tar.xz", nodePackageContent);
    await this.codeService.UploadFileAsync(sessionId, "hello_world.js", jsContent);
    var result = await this.codeService.ExecuteCodeAsync(sessionId, pythonCode);

    // Assert
    Assert.IsNotNull(result);
    Assert.IsTrue(result.Contains("Hello, World!"));
  }


  [TestMethod]
  public async Task Test1_ExecuteCodeAsync_JSCode_ReturnsResult()
  {
    // Arrange
    var sessionId = "FAC4BAE7-15F4-494A-8F78-78D0A3E9EBA1";
    var code = @"
import os
import subprocess
import sys
import urllib.request

def download_node():
    url = ""https://nodejs.org/dist/v14.17.0/node-v14.17.0-linux-x64.tar.xz""
    filename = ""node-v14.17.0-linux-x64.tar.xz""
    print(f""Downloading Node.js from {url}"")
    urllib.request.urlretrieve(url, filename)
    return filename

def install_node(filename):
    subprocess.run([""tar"", ""-xf"", filename], check=True)
    extracted_dir = filename.replace("".tar.xz"", """")
    os.environ[""PATH""] += os.pathsep + os.path.join(os.getcwd(), extracted_dir, ""bin"")

def create_hello_world():
    with open(""hello_world.js"", ""w"") as f:
        f.write('console.log(""Hello, World!"");')
    print(""Created hello_world.js"")

def run_hello_world():
    subprocess.run([""node"", ""hello_world.js""], check=True)

try:
    filename = download_node()
    install_node(filename)
    create_hello_world()
    run_hello_world()
except urllib.error.URLError as e:
    print(f""An error occurred while downloading: {e}"")
    sys.exit(1)
except subprocess.CalledProcessError as e:
    print(f""An error occurred during installation or execution: {e}"")
    sys.exit(1)
except OSError as e:
    print(f""An error occurred: {e}"")
    sys.exit(1)
";

    // Act
    var result = await codeService.ExecuteCodeAsync(sessionId, code);

    // Assert
    Assert.IsNotNull(result);
  }

  [TestMethod]
  public async Task Test1_ExecuteCodeAsync_InValidCode_ReturnsResult()
  {
    // Arrange
    var sessionId = "FAC4BAE7-15F4-494A-8F78-78D0A3E9EBA1";
    var code = "print('Hello, \nWorld!')";

    // Act
    var ex = await Assert.ThrowsExceptionAsync<HttpRequestException>(async () =>
    {
      await codeService.ExecuteCodeAsync(sessionId, code);
    });


    // Assert
    Assert.IsNotNull(ex);
    Assert.IsTrue(ex.Message.StartsWith("Failed to execute python code. Status: 400. Details: unterminated string literal"));
  }

  [TestMethod]
  public async Task Test2_GetPackagesAsync_ReturnsPackages()
  {
    // Arrange
    var sessionId = "A3FD055E-A291-47D6-8D75-4C1D8DCD5FCA";

    // Act
    var result = await codeService.GetPackagesAsync(sessionId);

    // Assert
    Assert.IsNotNull(result);
    Assert.IsTrue(result.Count > 0);
  }

  [TestMethod]
  public async Task Test3_UploadFileAsync_UploadsFile()
  {
    // Arrange
    var sessionId = "0C5C7E19-5CF3-46F1-A75D-70DE28E7EDD7";
    var filePath = "test.txt";
    var fileContent = "This is a test file.";
    var fileData = new BinaryData(System.Text.Encoding.UTF8.GetBytes(fileContent));

    // Act
    var result = await codeService.UploadFileAsync(sessionId, filePath, fileData);

    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(filePath, result.Filename);
  }

  [TestMethod]
  public async Task Test4_DownloadFileAsync_DownloadsFile()
  {
    // Arrange
    var sessionId = "0C5C7E19-5CF3-46F1-A75D-70DE28E7EDD7";
    var filePath = "test.txt";

    // Act
    var result = await codeService.DownloadFileAsync(sessionId, filePath);

    // Assert
    Assert.IsNotNull(result);
    var content = System.Text.Encoding.UTF8.GetString(result.ToArray());
    Assert.IsTrue(content.Contains("This is a test file."));
  }

  [TestMethod]
  public async Task Test5_ListFilesAsync_ListsFiles()
  {
    // Arrange
    var sessionId = "0C5C7E19-5CF3-46F1-A75D-70DE28E7EDD7";

    // Act
    var result = await codeService.ListFilesAsync(sessionId);

    // Assert
    Assert.IsNotNull(result);
    Assert.IsTrue(result.Count > 0);
    Assert.IsTrue(result.Any(f => f.Filename == "test.txt"));
  }
}
