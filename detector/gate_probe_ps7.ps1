#requires -Version 7
[CmdletBinding()]
param(
    [string]$OutFile = ""
)

# ── 1. Versioni di sistema ───────────────────────────────────────────
$os = Get-CimInstance Win32_OperatingSystem | Select-Object -First 1
$osVersion = "$($os.Caption) $($os.Version) Build $($os.BuildNumber)"

$obsProcess = Get-Process -Name "obs64","obs32","obs" -ErrorAction SilentlyContinue | Select-Object -First 1
$obsVersion = if ($obsProcess) { $obsProcess.FileVersionInfo.FileVersion } else { $null }
$obsRunning = $null -ne $obsProcess

# ── 2. Processi JW Library ───────────────────────────────────────────
$jwProcesses = @()
Get-Process -Name "JWLibrary" -ErrorAction SilentlyContinue | ForEach-Object {
    $p = $_
    $jwProc = @{
        pid = $p.Id
        name = $p.ProcessName
        path = $null
        version = $null
        mainwindowtitle = $null
        mainwindowhandle = $null
        child_processes = @()
    }
    try { $jwProc.path = $p.Path } catch {}
    try { $jwProc.version = $p.FileVersionInfo.FileVersion } catch {}
    try { $jwProc.mainwindowtitle = $p.MainWindowTitle } catch {}
    try { $jwProc.mainwindowhandle = $p.MainWindowHandle.ToString() } catch {}

    $children = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.Id -ne $p.Id -and $_.ProcessName -in @("msedgewebview2","JWLibrary")
    } | ForEach-Object {
        @{ pid = $_.Id; name = $_.ProcessName; mainwindowtitle = $_.MainWindowTitle }
    }
    $jwProc.child_processes = $children
    $jwProcesses += $jwProc
}

$jwWebView = @()
Get-Process -Name "msedgewebview2" -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -like "*Watchtower*" -or $_.Path -like "*JWLibrary*"
} | ForEach-Object {
    $jwWebView += @{
        pid = $_.Id
        name = $_.ProcessName
        path = $_.Path
        version = $_.FileVersionInfo.FileVersion
    }
}

# ── 3. Finestre JW Library (EnumWindows via C#) ──────────────────────
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Win32WindowEnum
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    public const uint GW_CHILD = 5;
    public const uint GW_HWNDNEXT = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static List<WindowInfo> EnumerateJWWindows(uint targetPid)
    {
        var result = new List<WindowInfo>();
        EnumWindows((hWnd, lParam) =>
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid != targetPid) return true;

            var rect = new RECT();
            GetWindowRect(hWnd, out rect);

            var sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, sbClass.Capacity);

            int len = GetWindowTextLength(hWnd);
            var sbTitle = new StringBuilder(len + 1);
            GetWindowText(hWnd, sbTitle, sbTitle.Capacity);

            var info = new WindowInfo
            {
                Hwnd = hWnd.ToInt64(),
                Pid = (int)pid,
                Title = sbTitle.ToString(),
                ClassName = sbClass.ToString(),
                Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom,
                Children = new List<WindowInfo>()
            };

            IntPtr child = GetWindow(hWnd, GW_CHILD);
            while (child != IntPtr.Zero)
            {
                uint childPid;
                GetWindowThreadProcessId(child, out childPid);
                var childRect = new RECT();
                GetWindowRect(child, out childRect);
                var childSbClass = new StringBuilder(256);
                GetClassName(child, childSbClass, childSbClass.Capacity);
                int clen = GetWindowTextLength(child);
                var childSbTitle = new StringBuilder(clen + 1);
                GetWindowText(child, childSbTitle, clen + 1);

                info.Children.Add(new WindowInfo
                {
                    Hwnd = child.ToInt64(),
                    Pid = (int)childPid,
                    Title = childSbTitle.ToString(),
                    ClassName = childSbClass.ToString(),
                    Left = childRect.Left, Top = childRect.Top, Right = childRect.Right, Bottom = childRect.Bottom,
                    Children = new List<WindowInfo>()
                });
                child = GetWindow(child, GW_HWNDNEXT);
            }

            result.Add(info);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static List<WindowInfo> EnumerateAllTopLevel()
    {
        var result = new List<WindowInfo>();
        EnumWindows((hWnd, lParam) =>
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            var rect = new RECT();
            GetWindowRect(hWnd, out rect);
            var sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, sbClass.Capacity);
            int len = GetWindowTextLength(hWnd);
            var sbTitle = new StringBuilder(len + 1);
            GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
            result.Add(new WindowInfo
            {
                Hwnd = hWnd.ToInt64(),
                Pid = (int)pid,
                Title = sbTitle.ToString(),
                ClassName = sbClass.ToString(),
                Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom,
                Children = new List<WindowInfo>()
            });
            return true;
        }, IntPtr.Zero);
        return result;
    }
}

public class WindowInfo
{
    public long Hwnd { get; set; }
    public int Pid { get; set; }
    public string Title { get; set; }
    public string ClassName { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public List<WindowInfo> Children { get; set; }
}
"@

$allWindows = [Win32WindowEnum]::EnumerateAllTopLevel()
$jwWindows = @()
foreach ($proc in $jwProcesses) {
    $wins = [Win32WindowEnum]::EnumerateJWWindows([uint32]$proc.pid)
    foreach ($w in $wins) {
        $jwWindows += @{
            pid = $w.Pid
            hwnd = $w.Hwnd
            title = $w.Title
            classname = $w.ClassName
            left = $w.Left
            top = $w.Top
            right = $w.Right
            bottom = $w.Bottom
            children = @($w.Children | ForEach-Object {
                @{
                    pid = $_.Pid
                    hwnd = $_.Hwnd
                    title = $_.Title
                    classname = $_.ClassName
                    left = $_.Left
                    top = $_.Top
                    right = $_.Right
                    bottom = $_.Bottom
                }
            })
        }
    }
}

# ── 4. Monitor (System.Windows.Forms.Screen) ─────────────────────────
Add-Type -AssemblyName System.Windows.Forms
$monitors = @()
[System.Windows.Forms.Screen]::AllScreens | ForEach-Object {
    $monitors += @{
        device_name = $_.DeviceName
        primary = $_.Primary
        bounds_left = $_.Bounds.Left
        bounds_top = $_.Bounds.Top
        bounds_right = $_.Bounds.Right
        bounds_bottom = $_.Bounds.Bottom
        width = $_.Bounds.Width
        height = $_.Bounds.Height
        working_left = $_.WorkingArea.Left
        working_top = $_.WorkingArea.Top
    }
}

# ── 5. UI Automation: proprietà strutturali (no testi contenuti) ─────
$uiA = @{
    available = $false
    root_element_found = $false
    jw_elements = @()
    error = $null
}
try {
    Add-Type -AssemblyName UIAutomationClient -ErrorAction Stop
    Add-Type -AssemblyName UIAutomationTypes -ErrorAction SilentlyContinue
    $uiA.available = $true
    $root = [UIAutomation.AutomationElement]::RootElement
    $uiA.root_element_found = $true

    foreach ($proc in $jwProcesses) {
        $cond = [UIAutomation.PropertyCondition]::new(
            [UIAutomation.AutomationElement]::ProcessIdProperty, $proc.pid)
        $elems = $root.FindAll([UIAutomation.TreeScope]::Descendants, $cond)
        foreach ($e in $elems) {
            $className = $null
            try { $className = $e.Current.ClassName } catch {}
            $ctrlType = $null
            try { $ctrlType = $e.Current.ControlType.ProgrammaticName } catch {}
            $rect = $null
            try { $r = $e.Current.BoundingRectangle; $rect = "[$($r.Left),$($r.Top),$($r.Right),$($r.Bottom)]" } catch {}
            $aid = $null
            try { $aid = $e.Current.AutomationId } catch {}
            $name = $null
            try { $name = $e.Current.Name } catch {}

            $uiA.jw_elements += @{
                pid = $proc.pid
                classname = $className
                controltype = $ctrlType
                automationid = $aid
                boundingrectangle = $rect
                isenabled = $e.Current.IsEnabled
                iskeyboardfocusable = $e.Current.IsKeyboardFocusable
                name_length = if ($null -eq $name) { 0 } else { $name.Length }
            }
        }
    }
} catch {
    $uiA.error = $_.Exception.Message
}

# ── 6. Windows Media Control: TUTTE le sessioni (non solo GetCurrent) ─
$media = @{
    available = $false
    manager_obtained = $false
    sessions_enumerated = $false
    jw_sessions = @()
    other_sessions_count = 0
    error = $null
}
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction Stop
    $mgrTypeName = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime"
    $mgrType = [type]::GetType($mgrTypeName)
    if ($null -eq $mgrType) {
        $media.error = "Tipo WinRT GlobalSystemMediaTransportControlsSessionManager non risolto"
        throw $media.error
    }

    # PowerShell 7 supporta GetAwaiter su IAsyncOperation WinRT
    $op = $mgrType::RequestAsync()
    $mgr = $op.GetAwaiter().GetResult()
    $media.manager_obtained = $true
    $media.available = $true

    $sessions = $mgr.GetSessions()
    $media.sessions_enumerated = $true

    foreach ($s in $sessions) {
        $appId = $null
        try { $appId = $s.SourceAppUserModelId() } catch {}
        $isJw = $false
        if ($appId -like "*Watchtower*" -or $appId -like "*JWLibrary*" -or $appId -like "*jwlibrary*") {
            $isJw = $true
        }

        $pbStatus = "unknown"
        try { $pbInfo = $s.GetPlaybackInfo(); $pbStatus = $pbInfo.PlaybackStatus.ToString() } catch {}

        $title = $null
        $appName = $null
        try {
            $propsAsync = $s.TryGetMediaPropertiesAsync()
            $props = $propsAsync.GetAwaiter().GetResult()
            $title = $props.Title
            $appName = $props.AppUserModelId
        } catch {}

        $sessionEntry = @{
            source_app_user_model_id = $appId
            is_jw_library = $isJw
            playback_status = $pbStatus
            title = $title
            app_user_model_id = $appName
        }

        if ($isJw) {
            $media.jw_sessions += $sessionEntry
        } else {
            $media.other_sessions_count++
        }
    }
} catch {
    $media.error = $_.Exception.Message
}

# ── 7. Assembla risultato ───────────────────────────────────────────
$ts = Get-Date -Format "yyyy-MM-ddTHH:mm:sszzz"
$result = @{
    timestamp = $ts
    os_version = $osVersion
    obs_running = $obsRunning
    obs_version = $obsVersion
    jw_processes = $jwProcesses
    jw_webview_wrappers = $jwWebView
    jw_windows = $jwWindows
    monitors = $monitors
    ui_automation = $uiA
    media_sessions = $media
    conclusion = $null
}

$hasActiveVideo = $false
foreach ($s in $media.jw_sessions) {
    if ($s.is_jw_library -and $s.playback_status -eq "Playing") {
        $hasActiveVideo = $true
    }
}
if (-not $media.manager_obtained) {
    $result.conclusion = "WinRT media session manager non disponibile: impossibile valutare segnale media"
} elseif ($hasActiveVideo) {
    $result.conclusion = "Sessione JW Library attiva (Playing) rilevata via GlobalSystemMediaTransportControls"
} else {
    $result.conclusion = "Nessuna sessione video attiva per JW Library (standby o nessun video in corso)"
}

$json = $result | ConvertTo-Json -Depth 10

if ([string]::IsNullOrEmpty($OutFile)) {
    $scriptDir = Split-Path -Parent $PSCommandPath
    $OutFile = Join-Path $scriptDir ("gate_evidence_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".json")
}
$json | Out-File -FilePath $OutFile -Encoding utf8

Write-Host "=== GATE PROBE COMPLETATO ==="
Write-Host "Output: $OutFile"
Write-Host "Conclusion: $($result.conclusion)"
Write-Host "JW processes: $($jwProcesses.Count)"
Write-Host "JW windows: $($jwWindows.Count)"
Write-Host "Monitors: $($monitors.Count)"
Write-Host "Media sessions available: $($media.available)"
Write-Host "JW media sessions: $($media.jw_sessions.Count)"
Write-Host "Other media sessions: $($media.other_sessions_count)"
