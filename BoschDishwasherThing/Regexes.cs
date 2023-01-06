using System.Text.RegularExpressions;

namespace BoschDishwasherThing;

public static partial class Regexes
{
	[GeneratedRegex("(?<action>\\w+?) /(?<service>\\w+?)/(?<path>\\w+?)/v(?<version>\\d+)")]
	public static partial Regex UserProvidedRequest();
}
