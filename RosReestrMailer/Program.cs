using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RosReestrMailer;

internal static class Program
{
	private static Task<int> Main(string[] args)
	{
		var configuration = new ConfigurationBuilder()
			.AddEnvironmentVariables("RRM_")
			.AddJsonFile("config.json")
			.AddCommandLine(args)
			.Build();

		using var services = new ServiceCollection()
			.AddOptionsByClass<ConfigOptions>(configuration)
			.AddTransient<App>()
			.BuildServiceProvider(true);

		using var cancellationTokenSource = new CancellationTokenSource();
		var app = services.GetRequiredService<App>();
		return app.Start(cancellationTokenSource.Token);
	}
}