using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RosReestrMailer;

public static class Extensions
{
	private const string ClassNamePrefix = "Options";

	public static TOptions GetOptions<TOptions>(this IConfiguration configuration)
		where TOptions : class =>
		configuration
			.GetSection(typeof(TOptions).Name.Replace(ClassNamePrefix, string.Empty))
			.Get<TOptions>();

	public static object GetOptions(this IConfiguration configuration, Type optionsType) =>
		configuration
			.GetSection(optionsType.Name.Replace(ClassNamePrefix, string.Empty))
			.Get(optionsType);

	public static IServiceCollection AddOptionsByClass<TOptions>(this IServiceCollection services, IConfiguration configuration)
		where TOptions : class =>
		services.Configure<TOptions>(configuration.GetSection(typeof(TOptions).Name.Replace(ClassNamePrefix, string.Empty)));
}
