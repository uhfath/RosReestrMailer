namespace RosReestrMailer;

internal class ConfigOptions
{
	public string ImapHost { get; set; }
	public int ImapPort { get; set; }
	public bool UseSSL { get; set; }
	public string UserName { get; set; }
	public string Password { get; set; }
	public string Folder { get; set; }
	public Uri DownloadSource { get; set; }
}
