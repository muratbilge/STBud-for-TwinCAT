$d = "C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter"
$files = @(
    "STFormatter.TcXaeShell.dll",
    "STFormatter.TcXaeShell.pdb",
    "STFormatter.TcXaeShell.pkgdef",
    "extension.vsixmanifest"
)
foreach ($f in $files) {
    $p = Join-Path $d $f
    if (Test-Path -LiteralPath $p) {
        try {
            Remove-Item -LiteralPath $p -Force
            Write-Output "Removed $f"
        }
        catch {
            Write-Output "Failed to remove ${f}: $_"
        }
    }
    else {
        Write-Output "Not present: $f"
    }
}
