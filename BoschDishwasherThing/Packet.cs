using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
public record Packet
{
	[JsonPropertyName("sID")]
	public uint SessionID { get; set; }

	[JsonPropertyName("msgID")]
	public long MessageID { get; set; }

	[JsonPropertyName("code")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? StatusCode { get; set; }

	[JsonPropertyName("resource")]
	public string Resource { get; set; } = null!;

	[JsonPropertyName("version")]
	public byte Version { get; set; }

	[JsonPropertyName("action")]
	public string Action { get; set; } = null!;

	[JsonPropertyName("data")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement[]? Data { get; set; }

	public override string ToString()
	{
		var sb = new StringBuilder($"sID={SessionID}, mID={MessageID}, req='{Action} {Resource}/v{Version}'");
		if (Data != null)
			sb.Append($", data='{string.Join(',', Data)}'");

		if (StatusCode != null)
			sb.Append($", code={(HttpStatusCode) StatusCode}");

		return sb.ToString();
	}
}
