using System.IO;
using System.Net.Http;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE;

/// <summary>
/// Monitors race progress and reports level/stage completion to a remote server.
/// This class is designed to be run in its own thread.
/// </summary>
internal class RaceProgress
{
    private string? _username = null;
    private string? _password = null;
    private bool _firstTimePositionDetected = true;
    private readonly MCCHaloCE _MCCHaloCE;

    private readonly HttpClient _httpClient;
    private const string ApiBaseUrl = "https://cursedhaloforcharity.com/Race/";
    private CancellationToken _cancellationToken;

    /// <summary>
    /// Initializes a new instance of the RaceProgress class.
    /// </summary>
    public RaceProgress(MCCHaloCE MCCHaloCE)
    {
        _MCCHaloCE = MCCHaloCE;
        _httpClient = new HttpClient();
    }    

    /// <summary>
    /// Starts the monitoring process in a new background thread.
    /// </summary>
    public void StartMonitoring(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        CcLog.Message($"Starting monitoring for racer");
        Task loopTask = Task.Run(MonitorLoop);
        CcLog.Message("Monitoring started. The application will continue running in the background.");
        loopTask.Wait();
    }

    /// <summary>
    /// The main loop that periodically checks for progress and reports it.
    /// </summary>
    private async Task MonitorLoop() {
        WaitUntilFileDataIsLoaded();
        byte[]? initialStatus = null;
        try
        {
            while (!_MCCHaloCE.GetRaceStatus(out initialStatus) || initialStatus == null)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    CcLog.Message("Cancellation requested. Exiting monitoring loop.");
                    return;
                }
                Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            CcLog.Error($"Something went wrong getting the race progress variable: {ex.Message}");
        }

        if (initialStatus == null)
        {
            CcLog.Error("Error reading the progress varaible, byte array is null");
            return;
        }

        int lastLevel = initialStatus[1];
        int lastStage = initialStatus[0];
        while (true)
        {
            try
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    CcLog.Message("Cancellation requested. Exiting monitoring loop.");
                    return;
                }
                if (!_MCCHaloCE.GetRaceStatus(out byte[]? status) || status == null)
                    continue;
                               
                int currentLevel = status[1];
                int currentStage = status[0];

                // The memory briefly zeroes during loading, so we need to ignore that case. Also, if the level and stage are the same as last time, we don't need to report it again.
                if ((currentLevel == 0 && currentStage == 0) || (currentLevel == lastLevel && currentStage == lastStage && !_firstTimePositionDetected))
                {
                    CcLog.Debug($"No progress detected");
                    Thread.Sleep(TimeSpan.FromSeconds(5));
                    continue;

                }                   
                CcLog.Message($"Detected progress: Level {currentLevel}, Stage {currentStage}. Reporting to server...");
                lastLevel = currentLevel;
                lastStage = currentStage;

                // Sanity check
                if (currentLevel >= 0 && currentLevel < 20 && currentStage >= 0 && currentStage < 10)
                {
                    // Report the progress to the web server
                    _firstTimePositionDetected = false;
                    await SetLevel(currentLevel, currentStage);
                }
                else
                {
                    CcLog.Message($"Invalid level/stage detected: Level {currentLevel}, Stage {currentStage}. Skipping report.");
                }
            }
            catch (Exception ex)
            {
                CcLog.Message($"An error occurred in the monitoring loop: {ex.Message}");
            }

            // Wait for a specified interval before checking again to avoid spamming the server.
            Thread.Sleep(TimeSpan.FromSeconds(30));
        }
    }

    private void WaitUntilFileDataIsLoaded()
    {
        while (!IsFileDataLoaded() && !_cancellationToken.IsCancellationRequested)
        {
            if (!TryGetFileData())
            {
                CcLog.Message("Failed to load racer configuration. Monitoring will not start.");
                Thread.Sleep(10000);
            }
            else
            {
                CcLog.Message($"Racer configuration loaded successfully. Welcome, {_username}!");
                return;
            }
        }
    }

    private bool IsFileDataLoaded()
    {
        return _username != null;
    }

    private bool TryGetFileData()
    {
        string fileName = "YourHaloRaceId.txt";
        string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), fileName);

        if (!File.Exists(configPath))
        {
            CcLog.Message($"Error: Racer configuration file not found at '{Path.GetFullPath(configPath)}'.");
            return false;

        }

        CcLog.Message($"Successfully loaded racer config from: {configPath}");

        // Verify the file
        var fileContents = File.ReadAllLines(configPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (fileContents.Count != 2)
        {
            CcLog.Message("Bad format for racer config file. Expected 2 lines (username and password).");
            return false;
        }

        _username = fileContents[0];
        _password = fileContents[1];

        return true;

    }

    /// <summary>
    /// Sends the current race progress to the server endpoint.
    /// </summary>
    /// <param name="level">The current level.</param>
    /// <param name="stage">The current stage.</param>
    private async Task SetLevel(int level, int stage)
    {
        // Construct the full URL with query parameters
        string requestUrl = $"{ApiBaseUrl}SetLevel?racerName={Uri.EscapeDataString(_username)}&level={level}&stage={stage}&password={Uri.EscapeDataString(_password)}";

        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);

            if (response.IsSuccessStatusCode)
            {
                CcLog.Message("Successfully reported progress to the server.");
            }
            else
            {
                // Log the error response from the server
                string errorContent = await response.Content.ReadAsStringAsync();
                CcLog.Message($"Failed to report progress. Server responded with {response.StatusCode}: {errorContent}");
            }
        }
        catch (HttpRequestException e)
        {
            CcLog.Message($"Error making the GET request: {e.Message}");
        }
    }
}
