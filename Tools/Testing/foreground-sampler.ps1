# Logs every change of the foreground window's process for $Seconds, to $Out.
# Used to prove a test run never takes the author's focus: start it hidden, run the tests, read the log -
# a line naming testhost means a test window took the foreground.
#   Start-Process powershell -WindowStyle Hidden -ArgumentList "-File","Tools/Testing/foreground-sampler.ps1","-Seconds","600","-Out","fg.txt"
param([int]$Seconds = 120, [string]$Out)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Fg {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@
$last = ""
$end = (Get-Date).AddSeconds($Seconds)
"start $(Get-Date -Format HH:mm:ss.fff)" | Out-File $Out -Encoding utf8
while ((Get-Date) -lt $end) {
  $h = [Fg]::GetForegroundWindow(); $procId = 0; [void][Fg]::GetWindowThreadProcessId($h, [ref]$procId)
  $name = try { (Get-Process -Id $procId -ErrorAction Stop).ProcessName } catch { "?" }
  $now = "$name($procId)"
  if ($now -ne $last) { "$(Get-Date -Format HH:mm:ss.fff) $now hwnd=$h" | Out-File $Out -Append -Encoding utf8; $last = $now }
  Start-Sleep -Milliseconds 50
}
"end $(Get-Date -Format HH:mm:ss.fff)" | Out-File $Out -Append -Encoding utf8
