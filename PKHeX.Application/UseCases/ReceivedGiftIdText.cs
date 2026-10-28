using System.Globalization;

namespace PKHeX.Application.UseCases;

/// <summary>Validates a complete received-ID file before any staged state changes.</summary>
public static class ReceivedGiftIdText
{
    public static bool TryParse(string text, int count, out IReadOnlyList<int> ids)
    {
        var result = new SortedSet<int>();
        foreach (var token in text.Split(['\r', '\n', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 0 || id >= count)
            {
                ids = [];
                return false;
            }
            result.Add(id);
        }
        ids = result.ToArray();
        return true;
    }

    public static string Export(IEnumerable<int> ids) => string.Join(Environment.NewLine,
        ids.Distinct().Order().Select(id => id.ToString("D4", CultureInfo.InvariantCulture)));
}
