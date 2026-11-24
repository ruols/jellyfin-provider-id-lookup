using Microsoft.Extensions.Primitives;

namespace Jellyfin.Plugin.ProviderIdLookup;

public readonly record struct ProviderId(string Provider, string Value);

public static class ProviderIdParser
{
    public const int MaxPairs = 16;
    public const int MaxLength = 2048;

    public static bool TryParse(StringValues values, out ProviderId[] pairs)
    {
        pairs = [];
        if (values.Count == 0 || values.Sum(v => v?.Length ?? 0) > MaxLength)
            return false;

        var result = new List<ProviderId>();
        var count = 0;
        foreach (var value in values)
        {
            if (value is null)
                return false;
            foreach (var part in value.Split(','))
            {
                if (++count > MaxPairs)
                    return false;
                var token = part.Trim();
                var dot = token.IndexOf('.');
                if (dot <= 0 || dot == token.Length - 1)
                    return false;
                var provider = token[..dot];
                var id = token[(dot + 1)..];
                if (provider.Length > 64 || id.Length > 256
                    || provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
                    || id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
                    return false;
                provider = provider.ToLowerInvariant() switch
                {
                    "tmdb" => "Tmdb",
                    "imdb" => "Imdb",
                    "tvdb" => "Tvdb",
                    _ => provider
                };
                if (!result.Any(p => p.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) && p.Value == id))
                    result.Add(new ProviderId(provider, id));
            }
        }
        pairs = result.ToArray();
        return pairs.Length > 0;
    }
}
