$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /platform:x64 `
  /out:BodianLyrics.exe `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  *.cs
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED ($LASTEXITCODE)"
    exit $LASTEXITCODE
}
Write-Host "BUILD OK"
