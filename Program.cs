using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Media.Control;

class ObsWsConfig
{
    public string url { get; set; } = "ws://localhost:4455";
    public string password { get; set; } = "";
    public string media_scene { get; set; } = "";
    public bool enabled { get; set; } = true;
    public bool include_images { get; set; } = false;
    public bool return_on_pause { get; set; } = true;
    public double image_threshold { get; set; } = 10.0;
}

class Program
{
    static ObsWsConfig config = new ObsWsConfig();
    static string currentProgramScene = "";
    static string savedScene = "";
    static bool isInMediaCycle = false;
    static bool lastImageShowing = false;

    static ClientWebSocket? ws;
    static volatile bool identified = false;
    static volatile string wsState = "disconnesso";
    static volatile string lastError = "";
    static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> pending = new();
    static CancellationTokenSource? wsLoopCts;
    static readonly SemaphoreSlim sendLock = new(1, 1);
    static volatile bool isExiting = false;

    static NotifyIcon? trayIcon;
    static ContextMenuStrip? trayMenu;
    static Form? settingsForm;
    static ComboBox? sceneCombo;
    static CheckBox? enabledCheck;
    static CheckBox? imagesCheck;
    static CheckBox? pauseReturnCheck;
    static CheckBox? startupCheck;
    static Label? statusLabel;
    static Label? connLabel;

    static Mutex? singleInstance;

    static void Main(string[] args)
    {
        singleInstance = new Mutex(true, @"Global\OBSHelperJW_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("OBS Helper JW è già in esecuzione (cerca l'icona nel tray).",
                "OBS Helper JW", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) => LogToFile("UI crash: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LogToFile("Crash: " + e.ExceptionObject);

        RotateLogIfNeeded();

        string exeDir = AppContext.BaseDirectory;
        string configPath = Path.Combine(exeDir, "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var c = JsonSerializer.Deserialize<ObsWsConfig>(File.ReadAllText(configPath));
                if (c != null) config = c;
            }
            catch (Exception ex) { LogToFile("Errore config: " + ex.Message); }
        }
        LogToFile($"Avvio. url={config.url} scena={config.media_scene} enabled={config.enabled}");
        LoadBaseline();

        CreateTrayIcon();
        UpdateTrayText();

        var monitorThread = new Thread(() =>
        {
            try { RunMonitor().Wait(); }
            catch (Exception ex) { LogToFile("Errore monitor: " + ex.Message); }
        });
        monitorThread.IsBackground = true;
        monitorThread.Start();

        Application.Run();
    }

    // ---------------- Monitor ----------------

    static async Task RunMonitor()
    {
        GlobalSystemMediaTransportControlsSessionManager manager;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            LogToFile("WinRT non disponibile: " + ex.Message);
            return;
        }

        while (true)
        {
            try
            {
                if (!identified)
                {
                    wsState = "connessione...";
                    RefreshConnLabel();
                    try { await ConnectObsWs(); }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        wsState = "disconnesso";
                        LogToFile("WS connect fallita: " + ex.Message);
                        RefreshConnLabel();
                        await Task.Delay(3000);
                        continue;
                    }
                }

                if (!config.enabled) { await Task.Delay(500); continue; }

                bool jwPlaying = false, jwPaused = false;
                try
                {
                    foreach (var session in manager.GetSessions())
                    {
                        string appId = session.SourceAppUserModelId ?? "";
                        bool isJw = appId.Contains("Watchtower", StringComparison.OrdinalIgnoreCase)
                            || appId.Contains("JWLibrary", StringComparison.OrdinalIgnoreCase);
                        if (!isJw) continue;
                        string status;
                        try { status = session.GetPlaybackInfo().PlaybackStatus.ToString(); }
                        catch { continue; }
                        if (status == "Playing") jwPlaying = true;
                        else if (status == "Paused") jwPaused = true;
                    }
                }
                catch (Exception ex)
                {
                    LogToFile("WinRT read: " + ex.Message);
                }

                bool pausedIsMedia = config.include_images || !config.return_on_pause;
                bool imageShowing = false;
                double imgDiff = -1;
                if (config.include_images)
                    imageShowing = DetectImageByDiff(out imgDiff);
                if (imageShowing != lastImageShowing)
                {
                    lastImageShowing = imageShowing;
                    LogToFile("Immagine secondo schermo: " + (imageShowing ? "RILEVATA" : "terminata")
                        + $" (diff {imgDiff:F1}, soglia {config.image_threshold})");
                }
                bool isMedia = jwPlaying || (jwPaused && pausedIsMedia) || imageShowing;
                UpdateStatusText(isMedia, jwPlaying || jwPaused || imageShowing, imgDiff);

                if (!isInMediaCycle && isMedia)
                    await EnterMediaCycle();
                else if (isInMediaCycle && !isMedia)
                    await ExitMediaCycle();
            }
            catch (Exception ex)
            {
                LogToFile("Loop: " + ex.Message);
                lastError = ex.Message;
            }
            await Task.Delay(500);
        }
    }

    static async Task EnterMediaCycle()
    {
        if (string.IsNullOrEmpty(config.media_scene) || config.media_scene == "(nessuna)")
        {
            LogToFile("Video rilevato ma nessuna scena multimediale configurata");
            return;
        }
        try { currentProgramScene = await GetCurrentSceneSafe(); } catch { }
        savedScene = currentProgramScene;
        isInMediaCycle = true;
        LogToFile($"Video rilevato, switcho a: {config.media_scene} (era: {savedScene})");
        try { await SetCurrentScene(config.media_scene); }
        catch (Exception ex)
        {
            LogToFile("Switch fallito: " + ex.Message);
            lastError = ex.Message;
            isInMediaCycle = false;
        }
    }

    static async Task ExitMediaCycle()
    {
        isInMediaCycle = false;
        // Se l'utente ha cambiato scena a mano durante il video, non sovrascriverla.
        string cur = "";
        try { cur = await GetCurrentSceneSafe(); } catch { }
        if (!string.IsNullOrEmpty(savedScene) && (string.IsNullOrEmpty(cur) || cur == config.media_scene))
        {
            LogToFile($"Fine video, ritorno a: {savedScene}");
            try { await SetCurrentScene(savedScene); }
            catch (Exception ex) { LogToFile("Ritorno scena fallito: " + ex.Message); }
        }
        else
        {
            LogToFile($"Fine video, scena cambiata a mano (ora: {cur}): nessun ritorno automatico");
        }
        savedScene = "";
    }

    // ---------------- obs-websocket v5 (protocollo corretto) ----------------
    // Hello (op 0) -> Identify (op 1) -> Identified (op 2) -> Request (op 6) / Response (op 7)

    static async Task ConnectObsWs()
    {
        DisconnectWs();
        wsLoopCts = new CancellationTokenSource();
        var cts = wsLoopCts;

        ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(config.url), cts.Token);
        LogToFile("TCP connesso a " + config.url);

        string helloJson = await ReceiveJsonAsync(ws, cts.Token);
        using var helloDoc = JsonDocument.Parse(helloJson);
        if (helloDoc.RootElement.GetProperty("op").GetInt32() != 0)
            throw new Exception("Handshake: atteso Hello (op 0), ricevuto: " + helloJson.Substring(0, Math.Min(120, helloJson.Length)));
        LogToFile("Hello ricevuto");

        string? auth = ComputeAuth(helloDoc.RootElement, config.password);
        // Nessun evento consumato dal daemon: sottoscrizione a zero.
        var identify = new { op = 1, d = new { rpcVersion = 1, authentication = auth, eventSubscriptions = 0 } };
        await SendJsonAsync(ws, identify, cts.Token);

        string reply = await ReceiveJsonAsync(ws, cts.Token);
        using var replyDoc = JsonDocument.Parse(reply);
        int op = replyDoc.RootElement.GetProperty("op").GetInt32();
        if (op == 2)
        {
            identified = true;
            wsState = "connesso";
            lastError = "";
            LogToFile("Identificato con obs-websocket");
        }
        else if (op == 7)
        {
            string err = replyDoc.RootElement.GetProperty("d").TryGetProperty("requestStatus", out var rs)
                && rs.TryGetProperty("comment", out var c) ? c.GetString() ?? reply : reply;
            throw new Exception("Identify rifiutato: " + err);
        }
        else
        {
            throw new Exception("Handshake: risposta inattesa op=" + op);
        }

        RefreshConnLabel();
        _ = Task.Run(() => ReceiveLoop(cts.Token));
    }

    static void DisconnectWs()
    {
        try { wsLoopCts?.Cancel(); } catch { }
        pending.Clear();
        identified = false;
        if (ws != null)
        {
            try { ws.Dispose(); } catch { }
            ws = null;
        }
    }

    static async Task ReceiveLoop(CancellationToken ct)
    {
        var socket = ws;
        if (socket == null) return;
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                string msg;
                try { msg = await ReceiveJsonAsync(socket, ct); }
                catch { break; }
                try
                {
                    using var doc = JsonDocument.Parse(msg);
                    int op = doc.RootElement.GetProperty("op").GetInt32();
                    if (op == 7)
                    {
                        var d = doc.RootElement.GetProperty("d");
                        string? rid = d.TryGetProperty("requestId", out var r) ? r.GetString() : null;
                        if (rid != null && pending.TryRemove(rid, out var tcs))
                            tcs.TrySetResult(msg);
                    }
                }
                catch { }
            }
        }
        finally
        {
            if (ReferenceEquals(ws, socket))
            {
                identified = false;
                wsState = "disconnesso";
                RefreshConnLabel();
                LogToFile("WS receive loop terminato, riconnetto al prossimo giro");
            }
            // Sblocca subito le richieste in volo invece di farle scadere al timeout.
            foreach (var kv in pending.ToArray())
                if (pending.TryRemove(kv.Key, out var tcs))
                    tcs.TrySetException(new Exception("Connessione obs-websocket persa"));
        }
    }

    static async Task<string> SendRequestAsync(string requestType, object requestData, int timeoutMs = 8000)
    {
        var socket = ws;
        if (socket == null || !identified || socket.State != WebSocketState.Open)
            throw new Exception("WebSocket non connesso (apri OBS e abilita obs-websocket su " + config.url + ")");
        string requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[requestId] = tcs;
        try
        {
            await sendLock.WaitAsync();
            try { await SendJsonAsync(socket, new { op = 6, d = new { requestType, requestId, requestData } }, CancellationToken.None); }
            finally { sendLock.Release(); }
            using var cts = new CancellationTokenSource(timeoutMs);
            using (cts.Token.Register(() => tcs.TrySetException(new TimeoutException("Timeout " + requestType))))
                return await tcs.Task;
        }
        catch
        {
            pending.TryRemove(requestId, out _);
            throw;
        }
    }

    static async Task<string> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16384];
        using var ms = new MemoryStream();
        while (true)
        {
            var res = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (res.MessageType == WebSocketMessageType.Close)
                throw new Exception("Connessione chiusa dal server");
            ms.Write(buffer, 0, res.Count);
            if (res.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static Task SendJsonAsync(ClientWebSocket socket, object obj, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    static string? ComputeAuth(JsonElement helloRoot, string password)
    {
        if (!helloRoot.TryGetProperty("d", out var d)) return null;
        if (!d.TryGetProperty("authentication", out var a) || a.ValueKind == JsonValueKind.Null) return null;
        string? salt = a.TryGetProperty("salt", out var s) ? s.GetString() : null;
        string? challenge = a.TryGetProperty("challenge", out var c) ? c.GetString() : null;
        if (salt == null || challenge == null) return null;
        if (string.IsNullOrEmpty(password))
            throw new Exception("OBS richiede una password (obs-websocket -> Impostazioni -> Abilita autenticazione). Impostala in config.json oppure disabilitala in OBS.");
        using var sha = SHA256.Create();
        string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    static async Task<string> GetCurrentSceneSafe()
    {
        try
        {
            var resp = await SendRequestAsync("GetCurrentProgramScene", new { });
            using var doc = JsonDocument.Parse(resp);
            string? name = doc.RootElement.GetProperty("d").GetProperty("responseData").GetProperty("currentProgramSceneName").GetString();
            if (!string.IsNullOrEmpty(name)) currentProgramScene = name;
            return currentProgramScene;
        }
        catch { return currentProgramScene; }
    }

    static Task SetCurrentScene(string sceneName) =>
        SendRequestAsync("SetCurrentProgramScene", new { sceneName });

    // ---------- Rilevamento immagini su secondo monitor ----------
    // Le immagini statiche non creano sessioni WinRT: si confronta lo schermo
    // secondario con una "schermata di riposo" memorizzata dall'utente.
    static Bitmap? baselineSmall = null;
    static double lastDiff = -1;

    static Screen? SecondaryScreen() => Screen.AllScreens.FirstOrDefault(s => !s.Primary);

    static Bitmap CaptureSmall(Rectangle bounds)
    {
        var small = new Bitmap(96, 54);
        using (var full = new Bitmap(bounds.Width, bounds.Height))
        {
            using (var g = Graphics.FromImage(full))
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            using (var g2 = Graphics.FromImage(small))
                g2.DrawImage(full, 0, 0, 96, 54);
        }
        return small;
    }

    static double MeanAbsDiff(Bitmap a, Bitmap b)
    {
        double sum = 0;
        int n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
                int ga = (ca.R + ca.G + ca.B) / 3, gb = (cb.R + cb.G + cb.B) / 3;
                sum += Math.Abs(ga - gb);
                n++;
            }
        return n == 0 ? 0 : sum / n;
    }

    static string BaselinePath() => Path.Combine(AppContext.BaseDirectory, "baseline.png");

    static void LoadBaseline()
    {
        baselineSmall = null;
        try
        {
            string p = BaselinePath();
            if (File.Exists(p))
            {
                using var img = new Bitmap(p);
                baselineSmall = new Bitmap(img, 96, 54);
            }
        }
        catch (Exception ex) { LogToFile("Baseline: " + ex.Message); }
    }

    static void CaptureBaseline()
    {
        var sec = SecondaryScreen() ?? throw new Exception("Nessun secondo monitor rilevato");
        using var small = CaptureSmall(sec.Bounds);
        small.Save(BaselinePath(), System.Drawing.Imaging.ImageFormat.Png);
        baselineSmall = new Bitmap(small);
        LogToFile($"Schermata di riposo memorizzata ({sec.Bounds.Width}x{sec.Bounds.Height} -> 96x54)");
    }

    static bool DetectImageByDiff(out double diff)
    {
        diff = -1;
        try
        {
            var sec = SecondaryScreen();
            if (sec == null) return false;
            if (baselineSmall == null) LoadBaseline();
            if (baselineSmall == null) return false;
            using var cur = CaptureSmall(sec.Bounds);
            diff = lastDiff = MeanAbsDiff(baselineSmall, cur);
            return diff >= config.image_threshold;
        }
        catch (Exception ex) { LogToFile("Diff: " + ex.Message); return false; }
    }

    // ---------------- UI ----------------

    static void CreateTrayIcon()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Impostazioni", null, (s, e) => ShowSettings());
        trayMenu.Items.Add("-");
        trayMenu.Items.Add("Esci", null, (s, e) =>
        {
            isExiting = true;
            try { DisconnectWs(); } catch { }
            try
            {
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
            }
            catch { }
            try { Application.Exit(); } catch { }
            Environment.Exit(0);
        });

        trayIcon = new NotifyIcon();
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "OBS Helper JW";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (s, e) => ShowSettings();
    }

    static void ShowSettings()
    {
        try
        {
            ShowSettingsInner();
        }
        catch (Exception ex)
        {
            LogToFile("ShowSettings crash: " + ex);
            MessageBox.Show("Errore apertura impostazioni:\n" + ex.Message + "\n\nDettagli nel log:\n" + Path.Combine(Path.GetTempPath(), "obs_helper_jw.log"),
                "OBS Helper JW", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void ShowSettingsInner()
    {
        if (settingsForm != null && !settingsForm.IsDisposed)
        {
            if (!settingsForm.Visible)
                settingsForm.Show();
            settingsForm.BringToFront();
            settingsForm.Activate();
            return;
        }

        settingsForm = new Form();
        settingsForm.Text = "OBS Helper JW - Impostazioni";
        settingsForm.Size = new Size(500, 500);
        settingsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
        settingsForm.MaximizeBox = false;
        settingsForm.StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel();
        layout.ColumnCount = 1;
        layout.RowCount = 6;
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(12);
        for (int i = 0; i < 6; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // --- Gruppo Automazione ---
        var grpAuto = new GroupBox() { Text = "Automazione", Dock = DockStyle.Top, AutoSize = true };
        var autoLayout = new TableLayoutPanel();
        autoLayout.ColumnCount = 2;
        autoLayout.Dock = DockStyle.Fill;
        autoLayout.AutoSize = true;
        autoLayout.Padding = new Padding(8);
        autoLayout.RowCount = 7;
        for (int i = 0; i < 7; i++)
            autoLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        autoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
        autoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));

        enabledCheck = new CheckBox() { Text = "Abilita automazione", Checked = config.enabled, Dock = DockStyle.Fill, AutoSize = true };
        autoLayout.SetColumnSpan(enabledCheck, 2);
        var sceneLabel = new Label() { Text = "Scena multimediale:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true };
        sceneCombo = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        sceneCombo.Items.Add("(nessuna)");
        var btnRefresh = new Button() { Text = "Aggiorna scene", Anchor = AnchorStyles.Left, Width = 110, AutoSize = true };
        pauseReturnCheck = new CheckBox() { Text = "In pausa torna alla scena precedente", Checked = config.return_on_pause, Dock = DockStyle.Fill, AutoSize = true };
        autoLayout.SetColumnSpan(pauseReturnCheck, 2);
        imagesCheck = new CheckBox() { Text = "Includi immagini", Checked = config.include_images, Dock = DockStyle.Fill, AutoSize = true };
        autoLayout.SetColumnSpan(imagesCheck, 2);

        autoLayout.Controls.Add(enabledCheck, 0, 0);
        autoLayout.Controls.Add(sceneLabel, 0, 1);
        autoLayout.Controls.Add(sceneCombo, 1, 1);
        autoLayout.Controls.Add(btnRefresh, 1, 2);
        autoLayout.Controls.Add(pauseReturnCheck, 0, 3);
        autoLayout.Controls.Add(imagesCheck, 0, 4);

        var btnCapture = new Button() { Text = "Cattura schermata riposo", Width = 150 };
        var btnClear = new Button() { Text = "Cancella", Width = 80 };
        var basePanel = new Panel() { AutoSize = true };
        basePanel.Controls.Add(btnCapture);
        basePanel.Controls.Add(btnClear);
        autoLayout.Controls.Add(basePanel, 0, 5);
        autoLayout.SetColumnSpan(basePanel, 2);

        var thrLabel = new Label() { Text = "Soglia cambio immagine:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        var thrInput = new NumericUpDown() { Minimum = 1, Maximum = 100, DecimalPlaces = 1, Increment = 0.5M, Value = (decimal)config.image_threshold, Width = 80 };
        autoLayout.Controls.Add(thrLabel, 0, 6);
        autoLayout.Controls.Add(thrInput, 1, 6);

        grpAuto.Controls.Add(autoLayout);

        btnCapture.Click += (s, e) =>
        {
            try { CaptureBaseline(); MessageBox.Show("Schermata di riposo memorizzata!\nOra mostra un'immagine sul secondo monitor per testare.", "OBS Helper JW", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception ex) { MessageBox.Show("Errore: " + ex.Message, "OBS Helper JW", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        btnClear.Click += (s, e) =>
        {
            try { File.Delete(BaselinePath()); baselineSmall = null; LogToFile("Baseline cancellata"); MessageBox.Show("Schermata di riposo cancellata.", "OBS Helper JW", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch { MessageBox.Show("Nessuna schermata da cancellare.", "OBS Helper JW", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        thrInput.ValueChanged += (s, e) => config.image_threshold = (double)thrInput.Value;

        // --- Gruppo Connessione ---
        var grpConn = new GroupBox() { Text = "Connessione OBS", Dock = DockStyle.Top, AutoSize = true };
        connLabel = new Label() { Text = "Connessione: " + wsState, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        grpConn.Controls.Add(connLabel);

        // --- Gruppo Sistema ---
        var grpSys = new GroupBox() { Text = "Sistema", Dock = DockStyle.Top, AutoSize = true };
        startupCheck = new CheckBox() { Text = "Avvia con Windows", Checked = IsStartupEnabled(), Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        grpSys.Controls.Add(startupCheck);

        // --- Stato ---
        statusLabel = new Label() { Text = "Stato: in attesa...", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true };
        var logLabel = new Label()
        {
            Text = "Log: " + Path.Combine(Path.GetTempPath(), "obs_helper_jw.log"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray,
            AutoSize = true
        };

        var btnPanel = new Panel() { Dock = DockStyle.Fill, Height = 36 };
        var btnChiudi = new Button() { Text = "Chiudi", Dock = DockStyle.Right, Width = 80 };
        var btnSalva = new Button() { Text = "Salva", Dock = DockStyle.Right, Width = 80 };
        btnPanel.Controls.Add(btnChiudi);
        btnPanel.Controls.Add(btnSalva);

        layout.Controls.Add(grpAuto, 0, 0);
        layout.Controls.Add(grpConn, 0, 1);
        layout.Controls.Add(grpSys, 0, 2);
        layout.Controls.Add(statusLabel, 0, 3);
        layout.Controls.Add(logLabel, 0, 4);
        layout.Controls.Add(btnPanel, 0, 5);

        btnRefresh.Click += async (s, e) => { await LoadScenesIntoCombo(); };

        btnSalva.Click += (s, e) =>
        {
            config.enabled = enabledCheck!.Checked;
            config.include_images = imagesCheck!.Checked;
            config.return_on_pause = pauseReturnCheck!.Checked;
            string sel = sceneCombo!.SelectedItem?.ToString() ?? "";
            config.media_scene = sel == "(nessuna)" ? "" : sel;
            SaveConfig();
            try { SetStartupEnabled(startupCheck!.Checked); }
            catch (Exception ex) { LogToFile("Startup: " + ex.Message); }
            UpdateTrayText();
            settingsForm!.Hide();
            MessageBox.Show("Impostazioni salvate!", "OBS Helper JW", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        btnChiudi.Click += (s, e) => settingsForm!.Hide();
        settingsForm.FormClosing += (s, e) =>
        {
            if (!isExiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                settingsForm!.Hide();
            }
        };

        settingsForm.Controls.Add(layout);
        settingsForm.Show();
        _ = LoadScenesIntoCombo();
    }

    static async Task LoadScenesIntoCombo()
    {
        var combo = sceneCombo;
        if (combo == null || combo.IsDisposed) return;
        for (int i = 0; i < 100 && !identified; i++)
            await Task.Delay(100);
        if (!identified)
        {
            lastError = "WebSocket non connesso: apri OBS, abilita obs-websocket (porta 4455), poi premi Aggiorna scene";
            LogToFile("LoadScenes: " + lastError);
            RefreshConnLabel();
            return;
        }
        try
        {
            var resp = await SendRequestAsync("GetSceneList", new { });
            using var doc = JsonDocument.Parse(resp);
            var d = doc.RootElement.GetProperty("d");
            if (d.TryGetProperty("requestStatus", out var rs) && rs.TryGetProperty("result", out var ok) && !ok.GetBoolean())
            {
                string comment = rs.TryGetProperty("comment", out var c) ? c.GetString() ?? "errore" : "errore";
                throw new Exception(comment);
            }
            var scenes = d.GetProperty("responseData").GetProperty("scenes").EnumerateArray();
            var names = new List<string>();
            foreach (var s in scenes)
            {
                string? n = s.GetProperty("sceneName").GetString();
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }
            combo.BeginInvoke((MethodInvoker)delegate
            {
                string prev = config.media_scene;
                combo.Items.Clear();
                combo.Items.Add("(nessuna)");
                foreach (var n in names) combo.Items.Add(n);
                combo.SelectedItem = !string.IsNullOrEmpty(prev) && combo.Items.Contains(prev) ? prev : "(nessuna)";
            });
            lastError = "";
            LogToFile($"Scene caricate: {names.Count} ({string.Join(", ", names)})");
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            LogToFile("LoadScenes errore: " + ex.Message);
        }
        RefreshConnLabel();
    }

    static void SaveConfig()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    static readonly string StartupValueName = "OBS Helper JW";
    static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(StartupValueName) != null;
        }
        catch { return false; }
    }

    static void SetStartupEnabled(bool on)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null) throw new Exception("Chiave Run non accessibile");
        if (on)
            key.SetValue(StartupValueName, "\"" + Application.ExecutablePath + "\"");
        else
            key.DeleteValue(StartupValueName, false);
    }

    static void UpdateTrayText()
    {
        if (trayIcon == null) return;
        string stato = config.enabled ? "attivo" : "disattivo";
        string scena = string.IsNullOrEmpty(config.media_scene) ? "nessuna" : config.media_scene;
        string t = $"OBS Helper JW - {stato} (scena: {scena})";
        trayIcon.Text = t.Length > 63 ? t.Substring(0, 63) : t;
    }

    static void RefreshConnLabel()
    {
        var lbl = connLabel;
        if (lbl == null || lbl.IsDisposed) return;
        string text = identified ? "Connessione: connesso a " + config.url
            : string.IsNullOrEmpty(lastError) ? "Connessione: " + wsState
            : "Connessione: " + wsState + " - " + lastError;
        try
        {
            if (lbl.InvokeRequired)
                lbl.BeginInvoke((MethodInvoker)delegate { if (!lbl.IsDisposed) lbl.Text = text; });
            else lbl.Text = text;
        }
        catch { }
    }

    static void UpdateStatusText(bool isMedia, bool hasActive, double diff = -1)
    {
        var lbl = statusLabel;
        if (lbl == null || lbl.IsDisposed) return;
        string text = isMedia ? "Stato: MEDIA ATTIVO" : hasActive ? "Stato: JW attivo (pausa)" : "Stato: standby";
        if (diff >= 0) text += $" (diff {diff:F1})";
        Color color = isMedia ? Color.Green : hasActive ? Color.Orange : Color.Gray;
        try
        {
            if (lbl.InvokeRequired)
                lbl.BeginInvoke((MethodInvoker)delegate
                {
                    if (lbl.IsDisposed) return;
                    lbl.Text = text;
                    lbl.ForeColor = color;
                });
            else { lbl.Text = text; lbl.ForeColor = color; }
        }
        catch { }
    }

    static void RotateLogIfNeeded()
    {
        try
        {
            string p = Path.Combine(Path.GetTempPath(), "obs_helper_jw.log");
            var fi = new FileInfo(p);
            if (fi.Exists && fi.Length > 1024 * 1024)
                File.WriteAllText(p, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log ruotato (superava 1 MB){Environment.NewLine}");
        }
        catch { }
    }

    static void LogToFile(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "obs_helper_jw.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
