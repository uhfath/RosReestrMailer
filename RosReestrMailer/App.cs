using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Org.BouncyCastle.Asn1.X509;
using System.Runtime.CompilerServices;

namespace RosReestrMailer;

internal class App : IHostedService
{
	private const string FileNameTemplate = "{0:yyyy.MM.dd}-{1:D4}.zip";

	private readonly ConfigOptions _configOptions;
	private readonly IHostApplicationLifetime _hostApplicationLifetime;
	private readonly ILogger<App> _logger;

	private static string GetUniqueFileName(string folder)
	{
		string path;

		var index = 0;
		do
		{
			++index;
			path = Path.Combine(folder, string.Format(FileNameTemplate, DateTime.Today, index));
		} while (File.Exists(path));

		return path;
	}

	private async IAsyncEnumerable<string> GetUnreadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var client = new ImapClient();

		_logger.LogInformation("Подключение к серверу: {server}:{port}", _configOptions.ImapHost, _configOptions.ImapPort);
		await client.ConnectAsync(_configOptions.ImapHost, _configOptions.ImapPort, _configOptions.UseSSL, cancellationToken);

		_logger.LogInformation("Аутентификация: {user}", _configOptions.UserName);
		await client.AuthenticateAsync(_configOptions.UserName, _configOptions.Password, cancellationToken);

		var folderPath = string.IsNullOrWhiteSpace(_configOptions.Folder)
			? client.Inbox.FullName
			: _configOptions.Folder;

		var splittedFolderPath = folderPath
			.Split("/", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		var fullFolderPath = string.Join("/", splittedFolderPath);
		var defaultFolderPath = string.Join("/", client.Inbox.FullName, fullFolderPath);

		_logger.LogInformation("Поиск папки: {folder}", _configOptions.Folder);
		IMailFolder folder = null;

		try
		{
			folder = await client.GetFolderAsync(fullFolderPath, cancellationToken);
		}
		catch (FolderNotFoundException)
		{
			try
			{
				folder = await client.GetFolderAsync(defaultFolderPath, cancellationToken);
			}
			catch (FolderNotFoundException)
			{
				_logger.LogError("Папка не найдена: {folder}", _configOptions.Folder);
				yield break;
			}
		}

		await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
		_logger.LogInformation("Получение списка писем");

		var itemIds = await folder.SearchAsync(MailKit.Search.SearchQuery.NotSeen, cancellationToken);
		_logger.LogInformation("Всего непрочитано: {items}", itemIds.Count);

		_logger.LogInformation("Получение данных о письмах");
		var items = await folder.FetchAsync(itemIds, MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure | MessageSummaryItems.Envelope, cancellationToken);

		foreach (var item in items)
		{
			if (item.HtmlBody != null)
			{
				_logger.LogInformation("Загрузка письма от {date}", item.Date.ToString("dd.MM.yyyy, HH:mm:ss"));

				using var html = await folder.GetBodyPartAsync(item.UniqueId, item.HtmlBody, cancellationToken);
				var source = (html as TextPart).Text;
				yield return source;

				_logger.LogInformation("Отметка письма прочитанным");
				await folder.StoreAsync(item.UniqueId, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen) { Silent = false }, cancellationToken);
			}
		}

		_logger.LogInformation("Отключение от сервера");
		await client.DisconnectAsync(true, cancellationToken);
	}

	private async Task<IEnumerable<Uri>> ExtractUrisAsync(string source, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Извлечение ссылки из текста");
		var config = AngleSharp.Configuration.Default;
		using var context = AngleSharp.BrowsingContext.New(config);

		var parser = context.GetService<AngleSharp.Html.Parser.IHtmlParser>();
		using var document = await parser.ParseDocumentAsync(source, cancellationToken);
		var links = document.QuerySelectorAll("a")
			.Select(a => new Uri(a.Attributes["href"].Value))
			.Where(l => string.Equals(l.Host, _configOptions.DownloadSource.Host, StringComparison.OrdinalIgnoreCase))
			.Where(l => string.Equals(l.AbsolutePath, _configOptions.DownloadSource.AbsolutePath, StringComparison.OrdinalIgnoreCase))
			.ToArray()
		;

		if (links.Length > 1)
		{
			_logger.LogWarning("Внимание! Найдено больше одной ссылки!");
		}

		return links;
	}
	
	private async Task<string> DownloadUrisAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken)
	{
		var folder = _configOptions.DestinationFolder ?? string.Empty;
		if (_configOptions.GroupByDate)
		{
			folder = Path.Combine(folder, DateTime.Today.ToString("yyyy.MM.dd"));
		}

		var directory = Directory.CreateDirectory(folder);
		foreach (var uri in uris)
		{
			_logger.LogInformation("Скачивание файла по ссылке: {uri}", uri);

			using var client = new HttpClient();
			await using var stream = await client.GetStreamAsync(uri, cancellationToken);

			var filePath = GetUniqueFileName(directory.FullName);
			_logger.LogInformation("Сохранение на диске: {path}", filePath);

			await using var destination = File.Create(filePath);
			await stream.CopyToAsync(destination, cancellationToken);
		}

		return directory.FullName;
	}

	public App(
		IOptions<ConfigOptions> configOptions,
		IHostApplicationLifetime hostApplicationLifetime,
		ILogger<App> logger)
	{
		try
		{
			this._configOptions = configOptions.Value;
		}
		catch (OptionsValidationException ex)
		{
			foreach (var failure in ex.Failures)
			{
				logger.LogError(failure);
			}

			hostApplicationLifetime.StopApplication();
		}

		this._hostApplicationLifetime = hostApplicationLifetime;
		this._logger = logger;
	}

	public static IServiceCollection Configure(HostBuilderContext hostBuilderContext, IServiceCollection services)
	{
		services
			.AddOptions<ConfigOptions>()
			.Bind(hostBuilderContext.Configuration)
			.ValidateDataAnnotations()
		;

		return services;
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (_configOptions == null)
		{
			return;
		}

		string lastFolder = null;

		_logger.LogInformation("Инициализация клиента");
		await foreach (var message in GetUnreadMessagesAsync(cancellationToken))
		{
			var uris = await ExtractUrisAsync(message, cancellationToken);
			lastFolder = await DownloadUrisAsync(uris, cancellationToken);
		}

		if (lastFolder != null && _configOptions.ExploreDestinationOnFinish)
		{
			using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
			{
				FileName = lastFolder,
				UseShellExecute = true,
				Verb = "open"
			}));
		}

		_hostApplicationLifetime.StopApplication();
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
