namespace AltPane.Core.Improvements;

public static class SelfUpdate
{
    /// <summary>
    /// A PowerShell script that waits for AltPane to exit, keeps the old executable as .old, copies the new one in,
    /// and starts it. If the copy fails (for example the folder needs administrator rights) the old file is restored
    /// and the reason is written to the error file.
    /// </summary>
    public static string BuildScript(int processId, string source, string target, string errorFile)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"$src = {Quote(source)}",
            $"$dst = {Quote(target)}",
            $"$err = {Quote(errorFile)}",
            "$old = $dst + '.old'",
            $"try {{ Wait-Process -Id {processId} -Timeout 60 -ErrorAction SilentlyContinue }} catch {{ }}",
            "try {",
            "  if (Test-Path -LiteralPath $dst) { Copy-Item -LiteralPath $dst -Destination $old -Force }",
            "  Copy-Item -LiteralPath $src -Destination $dst -Force",
            "} catch {",
            "  if (Test-Path -LiteralPath $old) { try { Copy-Item -LiteralPath $old -Destination $dst -Force } catch { } }",
            "  Set-Content -LiteralPath $err -Value $_.Exception.Message",
            "}",
            "Start-Process -FilePath $dst",
            ""
        });
    }

    /// <summary>A PowerShell single-quoted string literal. Quotes are doubled, so no value can end the string early.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
