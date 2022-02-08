using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RosReestrMailer;

internal static class Program
{
	private static IHostBuilder PrepareHost(string[] args)
	{
		var builder = Host.CreateDefaultBuilder()
			.ConfigureAppConfiguration(cfg =>
			{
				cfg
					.AddEnvironmentVariables("RRM_")
					.AddJsonFile("config.json", true, false)
					.AddCommandLine(args)
				;
			})
			.ConfigureLogging((ctx, cfg) =>
			{
				cfg
					.ClearProviders()
					.AddConsole()
					.AddFile("RosReestrMailer.log", false)
				;
			})
			.ConfigureServices((ctx, srv) =>
			{
				srv
					.AddHostedService<App>()
				;

				App.Configure(ctx, srv);
			})
		;

		builder.UseConsoleLifetime(cfg =>
		{
			cfg.SuppressStatusMessages = true;
		});

		return builder;
	}

	private static async Task<int> Main(string[] args)
	{
		Environment.ExitCode = 0;

		using var host = PrepareHost(args).Build();
		var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
		var logger = loggerFactory.CreateLogger("root");

		try
		{
			using var cancellationTokenSource = new CancellationTokenSource();
			await host.RunAsync(cancellationTokenSource.Token);
			return 0;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Ошибка выполнения");
			Environment.ExitCode = 1;
			return 1;
		}
	}
}