using System.ComponentModel.DataAnnotations;

namespace RosReestrMailer;

internal class ConfigOptions
{
	[Required(AllowEmptyStrings = false, ErrorMessage = "Не указан адрес IMAP сервера")]
	public string ImapHost { get; set; }

	[Required(ErrorMessage = "Не указан порт IMAP сервера")]
	public int ImapPort { get; set; }

	[Required(ErrorMessage = "Не указан флаг SSL IMAP сервера")]
	public bool UseSSL { get; set; }

	[Required(AllowEmptyStrings = false, ErrorMessage = "Не указан логин")]
	public string UserName { get; set; }

	[Required(AllowEmptyStrings = false, ErrorMessage = "Не указан пароль")]
	public string Password { get; set; }

	public string Folder { get; set; }

	[Required(AllowEmptyStrings = false, ErrorMessage = "Не указан источник скачивания")]
	public Uri DownloadSource { get; set; }

	public string DestinationFolder { get; set; }
	public bool GroupByDate { get; set; }
	public bool ExploreDestinationOnFinish { get; set; }
}
