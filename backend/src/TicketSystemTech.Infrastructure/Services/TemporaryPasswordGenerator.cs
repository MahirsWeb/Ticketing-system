using System.Security.Cryptography;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

public class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    // Avoids visually ambiguous characters (0/O, 1/l/I).
    private const string Lower = "abcdefghjkmnpqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string All = Lower + Upper + Digits;

    public string Generate()
    {
        Span<char> chars = stackalloc char[10];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        for (int i = 3; i < chars.Length; i++)
            chars[i] = Pick(All);

        // Shuffle so the guaranteed character classes aren't always in the same position.
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
