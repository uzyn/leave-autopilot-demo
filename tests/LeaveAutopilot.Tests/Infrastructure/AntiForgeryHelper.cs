using System.Text.RegularExpressions;

namespace LeaveAutopilot.Tests.Infrastructure;

/// <summary>Pulls the hidden `__RequestVerificationToken` field out of a rendered form so integration tests can submit valid POSTs.</summary>
public static partial class AntiForgeryHelper
{
    public static async Task<string> ExtractTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var match = TokenRegex().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Antiforgery token not found in response from {response.RequestMessage?.RequestUri}.");
        }

        return match.Groups["token"].Value;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex TokenRegex();
}
