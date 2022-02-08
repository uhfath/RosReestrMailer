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
			.ConfigureLogging(cfg =>
			{
				cfg
					.ClearProviders()
					.AddSimpleConsole()
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
		using var cancellationTokenSource = new CancellationTokenSource();

		try
		{
			await PrepareHost(args)
				.Build()
				.RunAsync(cancellationTokenSource.Token);

			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.Write(ex.ToString());
			return 1;
		}
	}
}