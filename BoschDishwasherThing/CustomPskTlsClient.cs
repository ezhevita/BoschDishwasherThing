using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace BoschDishwasherThing;

public class CustomPskTlsClient : PskTlsClient
{
	public override IDictionary<int, byte[]> GetClientExtensions() => clientExtensions.Value;

	private readonly Lazy<IDictionary<int, byte[]>> clientExtensions;

	public CustomPskTlsClient(TlsPskIdentity pskIdentity) : base(new BcTlsCrypto(), pskIdentity)
	{
		clientExtensions = new Lazy<IDictionary<int, byte[]>>(
			() =>
			{
				var baseClientExtensions = base.GetClientExtensions();

				// RFC7366, 3.
				// If a server receives an encrypt-then-MAC request extension from a client and then selects a stream or Authenticated Encryption
				// with Associated Data (AEAD) ciphersuite, it MUST NOT send an encrypt-then-MAC response extension back to the client.
				baseClientExtensions.Remove(ExtensionType.encrypt_then_mac);

				return baseClientExtensions;
			}
		);
	}
}
