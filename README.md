# DotnetBlazorNGrok
Dotnet6 MudBlazor sample application that autolaunches NGROK

**Using ngrok manually**

1. Retrieve your auth token from https://dashboard.ngrok.com/get-started/your-authtoken and then run the command:
``` bash
ngrok config add-authtoken ***your_authtoken
```
2. Launch local website.
3. Start ngrok.
ngrok http https://localhost:44393/ --host-header="localhost:44393"
4. Visit http://127.0.0.1:4040 to inspect, modify or replay requests
5. Vist the public url shown in the ngrok console output. Eg https://cbc6-108-24-170-180.ngrok.io

**Using ngrok programatically**

1. Copy `TunnelService.cs` to your local application.
2. Add the following code to Program.cs
``` csharp
if (builder.Environment.IsDevelopment()) 
    builder.Services.AddHostedService<NgrokAspNet.TunnelService>();
	```

Example:
``` csharp
var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment()) 
    builder.Services.AddHostedService<NgrokAspNet.TunnelService>();
var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();

```
3. Launch the app: dotnet run
4. Visit http://127.0.0.1:4040 to inspect, modify or replay requests
5. Vist the public url shown in the debugger console output. Eg https://cbc6-108-24-170-180.ngrok.io
6. Stop the applicaiton using ctrl-c. Note then when debugging using IIS with visual studio, the application terminates abrubtly and NGROK continues to run.
