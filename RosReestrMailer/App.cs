using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Org.BouncyCastle.Asn1.X509;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace RosReestrMailer;

internal class App : IHostedService
{
	private const string FileNameTemplate = "{0}{1}.zip";
	private const string IndexTemplate = " ({0})";
	private static readonly Regex InvalidNameRegex = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()))}]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

	private readonly ConfigOptions _configOptions;
	private readonly IHostApplicationLifetime _hostApplicationLifetime;
	private readonly ILogger<App> _logger;
	private readonly Regex _titleRegex;

	private static string StripInvalidPathChars(string path) =>
		InvalidNameRegex.Replace(path, "_");

	private static string GetUniqueFileName(string folder, string title)
	{
		string path;
		var index = 0;
		do
		{
			var indexText = index == 0
				? string.Empty
				: string.Format(IndexTemplate, index);

			var name = StripInvalidPathChars(string.Format(FileNameTemplate, title, indexText));
			path = Path.Combine(folder, name);
			++index;
		} while (File.Exists(path));

		return path;
	}

	private async IAsyncEnumerable<string> GetUnreadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
	{
		_logger.LogInformation("Инициализация подключения");
		using var client = new ImapClient();
		client.Timeout = (int)_configOptions.Timeout.TotalMilliseconds;

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

				if (_configOptions.AutoSetRead)
				{
					_logger.LogInformation("Отметка письма прочитанным");
					await folder.StoreAsync(item.UniqueId, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen) { Silent = false }, cancellationToken);
				}
			}
		}

		_logger.LogInformation("Отключение от сервера");
		await client.DisconnectAsync(true, cancellationToken);
	}

	private async Task<IEnumerable<LinkInfo>> ExtractUrisAsync(string source, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Извлечение ссылки из текста");
		var title = _titleRegex.Match(source).Groups["title"].Value;
		if (string.IsNullOrWhiteSpace(title))
		{
			_logger.LogWarning("Внимание! Номер запроса не найден в тексте. Используется текущая дата.");
			title = DateTime.Today.ToString("yyyy.MM.dd");
		}

		var config = AngleSharp.Configuration.Default;
		using var context = AngleSharp.BrowsingContext.New(config);

		var parser = context.GetService<AngleSharp.Html.Parser.IHtmlParser>();
		using var document = await parser.ParseDocumentAsync(source, cancellationToken);
		var links = document.QuerySelectorAll("a")
			.Select(s => new LinkInfo
			{
				Title = title,
				Uri = new Uri(s.Attributes["href"].Value),
			})
			.Where(l => string.Equals(l.Uri.Host, _configOptions.DownloadSource.Host, StringComparison.OrdinalIgnoreCase))
			.Where(l => string.Equals(l.Uri.AbsolutePath, _configOptions.DownloadSource.AbsolutePath, StringComparison.OrdinalIgnoreCase))
			.ToArray()
		;

		if (links.Length > 1)
		{
			_logger.LogWarning("Внимание! Найдено больше одной ссылки!");
		}

		return links;
	}
	
	private async Task<string> DownloadUrisAsync(IEnumerable<LinkInfo> links, CancellationToken cancellationToken)
	{
		var folder = _configOptions.DestinationFolder ?? string.Empty;
		if (_configOptions.GroupByDate)
		{
			folder = Path.Combine(folder, DateTime.Today.ToString("yyyy.MM.dd"));
		}

		var directory = Directory.CreateDirectory(folder);
		foreach (var link in links)
		{
			_logger.LogInformation("Скачивание файла: {title}", link.Title);

			using var client = new HttpClient();
			await using var stream = await client.GetStreamAsync(link.Uri, cancellationToken);

			var filePath = GetUniqueFileName(directory.FullName, link.Title);
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
			logger.LogError("Ошибка параметров: {failures}", ex.Failures);
		}

		this._hostApplicationLifetime = hostApplicationLifetime;
		this._logger = logger;

		_titleRegex = new Regex(_configOptions.TitleRegexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
		_logger.LogInformation("Текущая версия: {version}", Assembly.GetExecutingAssembly().GetName().Version);

		if (_configOptions == null)
		{
			_hostApplicationLifetime.StopApplication();
			return;
		}

		string lastFolder = null;
		for (var retry = 0; retry < _configOptions.Retries; retry++)
		{
			try
			{
				await foreach (var message in GetUnreadMessagesAsync(cancellationToken))
				{
					var links = await ExtractUrisAsync(message, cancellationToken);
					lastFolder = await DownloadUrisAsync(links, cancellationToken);
				}

				break;
			}
			catch (TaskCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ошибка обработки, перезапуск № {retry}", retry + 1);
			}
		}

		if (lastFolder != null && _configOptions.ExploreDestinationOnFinish)
		{
			using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
			{
				FileName = lastFolder,
				UseShellExecute = true,
				Verb = "open"
			});
		}

		_hostApplicationLifetime.StopApplication();
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
