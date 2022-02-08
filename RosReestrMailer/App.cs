using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RosReestrMailer;

internal class App : IHostedService
{
	private readonly ConfigOptions _configOptions;
	private readonly ILogger<App> _logger;

	public App(
		IOptions<ConfigOptions> configOptions,
		ILogger<App> logger)
	{
		this._configOptions = configOptions.Value;
		this._logger = logger;
	}

	public static IServiceCollection Configure(HostBuilderContext hostBuilderContext, IServiceCollection services)
	{
		services
			.Configure<ConfigOptions>(hostBuilderContext.Configuration)
		;

		return services;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("START");
		throw new NotImplementedException();
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		throw new NotImplementedException();
	}
}
