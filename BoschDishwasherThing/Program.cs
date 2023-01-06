using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BoschDishwasherThing;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl;

const int BufferSize = 8192;

Config config;
await using (var file = File.Open("config.json", FileMode.Open, FileAccess.Read))
{
	config = (await JsonSerializer.DeserializeAsync<Config[]>(file, new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase}))![0];
}

var paddedKey = config.Key + (config.Key.Length % 4) switch
{
	2 => "==",
	3 => "=",
	_ => ""
};

var client = new CustomPskTlsClient(new BasicTlsPskIdentity("Client_identity", Convert.FromBase64String(paddedKey)));

using var tcpSocket = new TcpClient();
await tcpSocket.ConnectAsync(config.Host, 443);

var tlsTransport = new TlsClientProtocol(tcpSocket.GetStream());
tlsTransport.Connect(client);

#if DEBUG
await ExportSecretsToFile(tlsTransport, "secrets.log");
#endif

var tlsStream = tlsTransport.Stream;

var request = ComposeWebSocketRequest(config.Host);
await tlsStream.WriteAsync(request);

byte[] tcpBuffer = null!;
try
{
	tcpBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
	var length = await tlsStream.ReadAsync(tcpBuffer);
	Console.WriteLine($"Received {length} bytes");
	Console.WriteLine(Encoding.UTF8.GetString(tcpBuffer.AsSpan()[..length]));
} finally
{
	ArrayPool<byte>.Shared.Return(tcpBuffer);
}

var tcs = new TaskCompletionSource<Packet>();

var webSocket = WebSocket.CreateFromStream(
	tlsStream, new WebSocketCreationOptions
	{
		IsServer = false
	}
);

_ = Task.Run(async () => await Start(webSocket, tcs));

await Task.Delay(3000);

var initRequest = await tcs.Task;

var sessionID = initRequest.SessionID;
var messageID = initRequest.MessageID;

Console.WriteLine("Enter your request (in the format ACTION /SERVICE/PATH/vVERSION):");
while (true)
{
	await Task.Delay(5000);
	var userRequestText = "GET /ci/wifiSetting/v3";
	if (string.IsNullOrEmpty(userRequestText))
		continue;

	var match = Regexes.UserProvidedRequest().Match(userRequestText);
	if (!match.Success)
		continue;

	await SendRequest(
		webSocket, new Packet
		{
			Action = match.Groups["action"].Value,
			MessageID = messageID++,
			Resource = "/" + match.Groups["service"] + "/" + match.Groups["path"],
			SessionID = sessionID,
			Version = byte.Parse(match.Groups["version"].ValueSpan)
		}
	);

	return;
}

static byte[] ComposeWebSocketRequest(string host)
{
	Span<byte> randomBuffer = stackalloc byte[16];
	Random.Shared.NextBytes(randomBuffer);

	var req = $@"GET /homeconnect HTTP/1.1
Connection: Upgrade
Host: {host}
Upgrade: websocket
Sec-WebSocket-Key: {Convert.ToBase64String(randomBuffer)}
Sec-WebSocket-Version: 13

".ReplaceLineEndings("\r\n");

	return Encoding.UTF8.GetBytes(req);
}

static async Task Start(WebSocket webSocket, TaskCompletionSource<Packet> initTcs)
{
	var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

	try
	{
		long transactionID = 0;
		Dictionary<string, byte> services;
		while (true)
		{
			var response = await webSocket.ReceiveAsync(buffer, CancellationToken.None);

			if (response.MessageType == WebSocketMessageType.Close || response.CloseStatus != null)
			{
				Console.WriteLine($"Connection closing with reason {response.CloseStatus}/{response.CloseStatusDescription}.");

				return;
			}

			Debug.Assert(response.MessageType == WebSocketMessageType.Text, "Message type must be text");

			var packet = JsonSerializer.Deserialize<Packet>(new MemoryStream(buffer, 0, response.Count));
			Console.WriteLine($"RX: {string.Join(',', packet)}");
			switch (packet!.Resource)
			{
				case "/ei/initialValues":
					var initData = JsonSerializer.SerializeToElement(new {deviceType = "Application", deviceName = nameof(BoschDishwasherThing), deviceID = "deadbeef"});
					transactionID = packet.Data![0].GetProperty("edMsgID").GetInt64();
					await SendRequest(webSocket, packet with {Action = "RESPONSE", Data = new[] {initData}});
					await SendRequest(webSocket, new Packet {Action = "GET", SessionID = packet.SessionID, MessageID = transactionID++, Version = 1, Resource = "/ci/services"});

					break;
				case "/ci/services":
					services = packet.Data!.ToDictionary(
						service => service.GetProperty("service").GetString()!,
						service => service.GetProperty("version").GetByte()
					);

					await SendRequest(webSocket, new Packet {Action = "NOTIFY", SessionID = packet.SessionID, MessageID = transactionID++, Version = services["ei"], Resource = "/ei/deviceReady"});
					await SendRequest(webSocket, new Packet {Action = "GET", SessionID = packet.SessionID, MessageID = transactionID++, Version = services["iz"], Resource = "/iz/info"});
					await SendRequest(webSocket, new Packet {Action = "GET", SessionID = packet.SessionID, MessageID = transactionID++, Version = services["ci"], Resource = "/ci/registeredDevices"});

					initTcs.SetResult(new Packet {SessionID = packet.SessionID, MessageID = transactionID});

					break;
			}
		}
	} finally
	{
		ArrayPool<byte>.Shared.Return(buffer);
	}
}

static async Task SendRequest(WebSocket webSocket, Packet request)
{
	Console.WriteLine("TX: " + request);
	await webSocket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(request), WebSocketMessageType.Text, true, CancellationToken.None);
}

#if DEBUG
static async Task ExportSecretsToFile(TlsClientProtocol protocol, string path)
{
	var context = (TlsContext) typeof(TlsClientProtocol)
		.GetField("m_tlsClientContext", BindingFlags.NonPublic | BindingFlags.Instance)!
		.GetValue(protocol)!;

	var masterKey = (byte[]) typeof(AbstractTlsSecret)
		.GetField("m_data", BindingFlags.NonPublic | BindingFlags.Instance)!
		.GetValue(context.Session.ExportSessionParameters().MasterSecret)!;

	var exportedSecrets = $"CLIENT_RANDOM {BinToHex(context.SecurityParameters.ClientRandom)} {BinToHex(masterKey)}";

	await File.AppendAllLinesAsync(path, new[] {exportedSecrets});
}

static string BinToHex(byte[] data) => BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();
#endif
