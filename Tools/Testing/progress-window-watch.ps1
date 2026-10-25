# Finds the test progress window and logs its position, size, top-most flag and title once a few seconds.
param([int]$Seconds = 60, [string]$Out)
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static string Find() {
    string found = null;
    EnumWindows((h, l) => {
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      var t = sb.ToString();
      if (t.EndsWith("SMSModForge tests") && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        bool top = (GetWindowLong(h, -20) & 0x8) != 0;
        found = string.Format("'{0}' at {1},{2} size {3}x{4} topmost={5}", t, r.L, r.T, r.R - r.L, r.B - r.T, top);
      }
      return true; }, IntPtr.Zero);
    return found;
  }
}
"@
$end = (Get-Date).AddSeconds($Seconds); $last = ""
while ((Get-Date) -lt $end) {
  $f = [W]::Find(); if ($f -eq $null) { $f = "(none)" }
  $key = $f -replace "'\d+% - ", "'N% - "
  if ($key -ne $last) { "$(Get-Date -Format HH:mm:ss) $f" | Out-File $Out -Append -Encoding utf8; $last = $key }
  Start-Sleep -Milliseconds 500
}
