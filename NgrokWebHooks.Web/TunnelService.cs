using System.Text.Json.Nodes;
using CliWrap;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

/// Adapted from https://www.twilio.com/blog/integrate-ngrok-into-aspdotnet-core-startup-and-automatically-update-your-webhook-urls
namespace NgrokWebHooks.Web;

/// <summary>
/// Responsible for starting a tunnel using the ngrok CLI, and later on it will also configure  webhooks.
/// 
/// Update Program.cs, based on the highlighted lines below to run the tunnel service inthe background, but only when the application is run in a development environmen:
/// if (builder.Environment.IsDevelopment()) 
///     builder.Services.AddHostedService<NgrokAspNet.TunnelService>();
/// 
/// </summary>
public class TunnelService : BackgroundService
{
    private readonly IServer server;
    private readonly IHostApplicationLifetime hostApplicationLifetime;
    private readonly IConfiguration config;
    private readonly ILogger<TunnelService> logger;

    /// <summary>
    /// The constructor accepts multiple parameters that will be provided by the dependency injection container built into ASP.NET Core.
    /// All the parameters are stored in private fields, so they are accessible throughout the class.
    /// </summary>
    /// <param name="server">The server parameter contains information about the web server currently being started. 
    /// Once the web server is started, you can retrieve the local URLs from the server field.</param>
    /// <param name="hostApplicationLifetime">The hostApplicationLifetime parameter lets you hook into the different lifecycle events (started/stopping/stopped).
    /// </param>
    /// <param name="config">The config parameter will contain all the configuration passed into the .NET application through command-line arguments,
    /// environment variables, JSON files, user-secrets, etc. The config isn't used right now, but it will be used in an upcoming section.</param>
    /// <param name="logger">The logger parameter will be used to log any information relevant to running the tunnel.</param>
    public TunnelService(
        IServer server,
        IHostApplicationLifetime hostApplicationLifetime,
        IConfiguration config,
        ILogger<TunnelService> logger
    )
    {
        this.server = server;
        this.hostApplicationLifetime = hostApplicationLifetime;
        this.config = config;
        this.logger = logger;
    }

    /// <summary>
    /// Main method from the abstract class BackgroundService. will be invoked as the web application is starting.
    /// ExecuteAsync will wait for the web application to have started using WaitForApplicationStarted, and then grab the local URLs.
    /// A single URL will be taken from the local URLs. If you authenticated ngrok earlier,
    /// you can use the HTTPS URL instead of the HTTP URL by replacing "http://" with "https://".
    /// 
    /// Next, the ngrok tunnel will be started, then the public ngrok URL will be retrieved, and finally the Task for running the ngrok CLI is awaited.
    /// 
    /// When the web application stops, the ngrok process will also be stopped, which will complete the ngrokTask.
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
    /// 
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStarted();

        var urls = server.Features.Get<IServerAddressesFeature>()!.Addresses;
        // Use https:// if you authenticated ngrok, otherwise, you can only use http://
        var localUrl = urls.Single(u => u.StartsWith("http://"));
        var localHttpsUrl = urls.Single(u => u.StartsWith("https://"));

        logger.LogInformation("Starting ngrok tunnel for {LocalUrl}", localHttpsUrl);
        var ngrokTask = StartNgrokTunnel(localHttpsUrl, stoppingToken);

        var publicUrl = await GetNgrokPublicUrl();
        logger.LogInformation("Public ngrok URL: {NgrokPublicUrl}", publicUrl);

        await ngrokTask;

        logger.LogInformation("Ngrok tunnel stopped");
    }

    /// <summary>
    /// WaitForApplicationStarted will create an awaitable Task that will be completed when the ApplicationStarted event is triggered. 
    /// Oddly, the lifecycle events on IHostApplicationLifetime are not using delegates or C# events, but instead they are CancellationToken's.
    /// </summary>
    /// <returns></returns>
    private Task WaitForApplicationStarted()
    {
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hostApplicationLifetime.ApplicationStarted.Register(() => completionSource.TrySetResult());
        return completionSource.Task;
    }

    /// <summary>
    /// StartNgrokTunnel will use the CliWrap library to run the ngrok CLI. The resulting command will look like this:
    /// 
    /// ngrok http [YOUR_LOCAL_SERVER_URL]--host-header="[YOUR_LOCAL_SERVER_URL]" --log stdout
    /// </summary>
    /// <param name="localUrl">Local webserver url</param>
    /// <param name="stoppingToken">Cancellation token passed to execute async. . The stoppingToken will be canceled when the application is being stopped. You can use this token to gracefully handle when the application is about to be shutdown. 
    /// By passing the stoppingToken to ExecuteAsync, the ngrok process will also be stopped when the application is stopped.
    /// Thus, you won't have any ngrok child processes lingering around. </param>
    /// <returns></returns>
    private CommandTask<CommandResult> StartNgrokTunnel(string localUrl, CancellationToken stoppingToken)
    {
        var hostHeader = localUrl.Substring("https://".Length - 1).Trim('/');
        var ngrokTask = Cli.Wrap("ngrok")
            .WithArguments(args => args
                .Add("http")
                .Add(localUrl)
                .Add("--log")
                .Add("stdout")
                .Add($"--host-header=\"{hostHeader}\"", false))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger.LogDebug(s)))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger.LogError(s)))
            .ExecuteAsync(stoppingToken);
        return ngrokTask;
    }

    /// <summary>
    /// The GetNgrokPublicUrl will fetch the public HTTPS URL and return it. You can get the public tunnel URLs by requesting it from the local ngrok API at http://127.0.0.1:4040/api/tunnels.
    /// 
    /// Unfortunately, when the ngrok CLI is started, that doesn't mean the tunnel is ready yet. That's why this code is surrounded in a loop that will try to get the public URL up to 10 times, every 200 milliseconds.
    /// 
    /// Feel free to change the 200ms delay and the retryCount to whatever suits your needs.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<string> GetNgrokPublicUrl()
    {
        using var httpClient = new HttpClient();
        for (var ngrokRetryCount = 0; ngrokRetryCount < 10; ngrokRetryCount++)
        {
            logger.LogDebug("Get ngrok tunnels attempt: {RetryCount}", ngrokRetryCount + 1);

            try
            {
                var json = await httpClient.GetFromJsonAsync<JsonNode>("http://127.0.0.1:4040/api/tunnels");
                var publicUrl = json["tunnels"].AsArray()
                    .Select(e => e["public_url"].GetValue<string>())
                    .SingleOrDefault(u => u.StartsWith("https://"));
                if (!string.IsNullOrEmpty(publicUrl)) return publicUrl;
            }
            catch
            {
                // ignored
            }

            await Task.Delay(200);
        }

        throw new Exception("Ngrok dashboard did not start in 10 tries");
    }
}