using System.Diagnostics;
using System.Text;

namespace Monolith.Voice.Services;

public class TtsPhonemizerService : IDisposable
{
    private readonly string _espeakExePath;
    private readonly string _dataPath;
    private bool _disposed;

    public TtsPhonemizerService(string espeakExePath, string dataPath)
    {
        _espeakExePath = espeakExePath;
        _dataPath = dataPath;
    }

    public string? ToPhonemes(string text, string espeakVoice = "es")
    {
        if (_disposed) return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _espeakExePath,
                Arguments = $"-q -v {espeakVoice} --ipa \"{SanitizeArg(text)}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };

            psi.EnvironmentVariables["ESPEAK_DATA_PATH"] = _dataPath;

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return null;
            }

            if (process.ExitCode != 0 && string.IsNullOrEmpty(output))
                return null;

            return output?.Trim();
        }
        catch
        {
            return null;
        }
    }

    public long[]? ToPhonemeIds(string text, string espeakVoice, Dictionary<string, long[]> phonemeIdMap)
    {
        var ipa = ToPhonemes(text, espeakVoice);
        if (string.IsNullOrEmpty(ipa)) return null;

        var result = new List<long>();
        var chars = ipa.EnumerateRunes().ToArray();

        foreach (var rune in chars)
        {
            var ch = rune.ToString();

            if (ch == " ")
            {
                if (phonemeIdMap.TryGetValue(" ", out var spaceIds))
                    result.AddRange(spaceIds);
                continue;
            }

            if (phonemeIdMap.TryGetValue(ch, out var ids))
            {
                result.AddRange(ids);
            }
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    private static string SanitizeArg(string text)
    {
        text = text.Replace("\"", "\\\"");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[\*\#\_\~\`\[\]\(\)\>\<]", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
