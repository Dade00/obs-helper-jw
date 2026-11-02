using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.WebSockets;
using System.Text;
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
}

class JWSessionInfo
{
    public string source_app_user_model_id { get; set; }
    public string playback_status { get; set; }
    public bool is_playing { get; set; }
    public bool is_paused { get; set; }
    public bool is_stopped { get; set; }
}

class Program
{
    static ObsWsConfig config = new ObsWsConfig();
    static string currentProgramScene = "";
    static string savedScene = "";
    static bool isInMediaCycle = false;
    static bool manualOverride = false;
    static ClientWebSocket ws;
    static int rpcMessageId = 0;
    static bool identified = false;
    static AutoResetEvent responseEvent = new AutoResetEvent(false);

    static NotifyIcon trayIcon;
    static ContextMenuStrip trayMenu;
    static Form settingsForm;
    static ComboBox sceneCombo;
    static CheckBox enabledCheck;
    static CheckBox imagesCheck;
    static Label statusLabel;

    static async Task Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Carica config
        string exeDir = AppContext.BaseDirectory;
        string configPath = Path.Combine(exeDir, "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var c = JsonSerializer.Deserialize<ObsWsConfig>(json);
                if (c != null) config = c;
                Console.WriteLine($"Config: {config.url}, scena={config.media_scene}, enabled={config.enabled}, immagini={config.include_images}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore config: {ex.Message}");
            }
        }

        // Crea tray icon
        CreateTrayIcon();

        // Connetti obs-websocket in background
        _ = Task.Run(async () =>
        {
            try { await ConnectObsWs(); } catch (Exception ex) { Console.WriteLine($"Errore WS: {ex.Message}"); }
        });

        // Ottieni manager WinRT
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        // Loop monitoraggio
        while (true)
        {
            try
            {
                if (!config.enabled) { await Task.Delay(500); continue; }

                var sessions = manager.GetSessions();
                var jwSessions = new List<JWSessionInfo>();

                foreach (var session in sessions)
                {
                    var appId = session.SourceAppUserModelId ?? "";
                    var isJw = appId.Contains("Watchtower", StringComparison.OrdinalIgnoreCase)
                        || appId.Contains("JWLibrary", StringComparison.OrdinalIgnoreCase)
                        || appId.Contains("jwlibrary", StringComparison.OrdinalIgnoreCase);

                    if (!isJw) continue;

                    var playbackInfo = session.GetPlaybackInfo();
                    var status = playbackInfo.PlaybackStatus.ToString();

                    jwSessions.Add(new JWSessionInfo
                    {
                        source_app_user_model_id = appId,
                        playback_status = status,
                        is_playing = status == "Playing",
                        is_paused = status == "Paused",
                        is_stopped = status == "Stopped"
                    });
                }

                bool hasActiveJw = jwSessions.Any(s => s.is_playing || s.is_paused);
                bool jwPlaying = jwSessions.Any(s => s.is_playing);
                bool isMedia = config.include_images ? (jwPlaying || jwPaused(jwSessions)) : jwPlaying;

                UpdateStatusText(isMedia, hasActiveJw);

                if (!isInMediaCycle && isMedia)
                {
                    await EnterMediaCycle();
                }
                else if (isInMediaCycle && !isMedia)
                {
                    await ExitMediaCycle();
                }
                else if (isInMediaCycle && manualOverride)
                {
                    if (!isMedia)
                    {
                        manualOverride = false;
                        await ExitMediaCycle();
                    }
                }

                // Rileva cambio manuale
                if (isInMediaCycle)
                {
                    string newScene = await GetCurrentSceneSafe();
                    if (newScene != currentProgramScene && newScene != config.media_scene)
                    {
                        manualOverride = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore: {ex.Message}");
            }

            await Task.Delay(500);
        }
    }

    static bool jwPaused(List<JWSessionInfo> sessions) => sessions.Any(s => s.is_paused);

    static void CreateTrayIcon()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Impostazioni", null, (s, e) => ShowSettings());
        trayMenu.Items.Add("-");
        trayMenu.Items.Add("Esci", null, (s, e) => Application.Exit());

        trayIcon = new NotifyIcon();
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "OBS Helper JW";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (s, e) => ShowSettings();
    }

    static void ShowSettings()
    {
        if (settingsForm != null && !settingsForm.IsDisposed)
        {
            settingsForm.BringToFront();
            return;
        }

        settingsForm = new Form();
        settingsForm.Text = "OBS Helper JW - Impostazioni";
        settingsForm.Size = new Size(400, 220);
        settingsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
        settingsForm.MaximizeBox = false;
        settingsForm.StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel();
        layout.ColumnCount = 2;
        layout.RowCount = 5;
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(15);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));

        enabledCheck = new CheckBox() { Text = "Abilita automazione", Checked = config.enabled, Dock = DockStyle.Fill };
        imagesCheck = new CheckBox() { Text = "Includi immagini", Checked = config.include_images, Dock = DockStyle.Fill };
        
        var sceneLabel = new Label() { Text = "Scena multimediale:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        sceneCombo = new ComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        sceneCombo.Items.Add("(nessuna)");
        sceneCombo.SelectedItem = string.IsNullOrEmpty(config.media_scene) ? "(nessuna)" : config.media_scene;

        statusLabel = new Label() { Text = "Stato: in attesa...", Dock = DockStyle.Fill, ForeColor = Color.Gray };
        statusLabel.ColumnSpan = 2;

        var btnPanel = new Panel() { Dock = DockStyle.Fill };
        var btnSalva = new Button() { Text = "Salva", Dock = DockStyle.Right, Width = 80 };
        var btnChiudi = new Button() { Text = "Chiudi", Dock = DockStyle.Right, Width = 80 };
        btnPanel.Controls.Add(btnChiudi);
        btnPanel.Controls.Add(btnSalva);

        layout.Controls.Add(enabledCheck, 0, 0);
        layout.Controls.Add(new Label(), 1, 0);
        layout.Controls.Add(imagesCheck, 0, 1);
        layout.Controls.Add(new Label(), 1, 1);
        layout.Controls.Add(sceneLabel, 0, 2);
        layout.Controls.Add(sceneCombo, 1, 2);
        layout.Controls.Add(statusLabel, 0, 3);
        layout.SetColumnSpan(statusLabel, 2);
        layout.Controls.Add(btnPanel, 0, 4);
        layout.SetColumnSpan(btnPanel, 2);

        btnSalva.Click += (s, e) =>
        {
            config.enabled = enabledCheck.Checked;
            config.include_images = imagesCheck.Checked;
            config.media_scene = sceneCombo.SelectedItem?.ToString() ?? "";
            SaveConfig();
            UpdateTrayText();
            settingsForm.Hide();
            MessageBox.Show("Impostazioni salvate!", "OBS Helper JW", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        btnChiudi.Click += (s, e) => settingsForm.Hide();
        settingsForm.FormClosing += (s, e) => { e.Cancel = true; settingsForm.Hide(); };

        settingsForm.Controls.Add(layout);
        settingsForm.Show();
    }

    static void SaveConfig()
    {
        string exeDir = AppContext.BaseDirectory;
        string configPath = Path.Combine(exeDir, "config.json");
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);
        Console.WriteLine("Config salvata");
    }

    static void UpdateTrayText()
    {
        var stato = config.enabled ? "attivo" : "disattivo";
        var scena = string.IsNullOrEmpty(config.media_scene) ? "nessuna" : config.media_scene;
        trayIcon.Text = $"OBS Helper JW - {stato} (scena: {scena})";
    }

    static void UpdateStatusText(bool isMedia, bool hasActive)
    {
        if (settingsForm != null && !settingsForm.IsDisposed && statusLabel != null)
        {
            var text = isMedia ? "MEDIA ATTIVO" : hasActive ? "JW attivo (pausa)" : "standby";
            var color = isMedia ? Color.Green : hasActive ? Color.Orange : Color.Gray;
            settingsForm.Invoke((MethodInvoker)delegate {
                statusLabel.Text = $"Stato: {text}";
                statusLabel.ForeColor = color;
            });
        }
    }

    static async Task ConnectObsWs()
    {
        ws = new ClientWebSocket();
        if (!string.IsNullOrEmpty(config.password))
            ws.Options.SetRequestHeader("Password", config.password);

        await ws.ConnectAsync(new Uri(config.url), CancellationToken.None);
        Console.WriteLine($"Connesso a obs-websocket: {config.url}");

        // Ricevi Hello
        await ReceiveAndProcess();

        // Invia Identify
        await SendIdentify();

        // Ricevi Identified
        await ReceiveAndProcess();

        identified = true;
        Console.WriteLine("Identificato con obs-websocket");
        UpdateTrayText();
    }

    static async Task SendIdentify()
    {
        var request = new
        {
            op = 6,
            d = new
            {
                requestType = "Identify",
                requestId = Guid.NewGuid().ToString(),
                requestData = new { obsWebSocketVersion = "5", rpcVersion = 1 }
            }
        };
        await SendRaw(request);
    }

    static async Task ReceiveAndProcess()
    {
        var buffer = new byte[8192];
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);

        try
        {
            var doc = JsonDocument.Parse(msg);
            var op = doc.RootElement.GetProperty("op").GetInt32();
            if (op == 7)
            {
                lastResponse = msg;
                responseEvent.Set();
            }
        }
        catch { }
    }

    static string lastResponse = "";

    static async Task<string> SendRpc(string method, object requestData)
    {
        if (!identified) throw new InvalidOperationException("Non identificato");

        var requestId = Guid.NewGuid().ToString();
        var request = new
        {
            op = 6,
            d = new
            {
                requestType = method,
                requestId = requestId,
                requestData = requestData
            }
        };
        await SendRaw(request);

        responseEvent.Reset();
        if (!responseEvent.WaitOne(TimeSpan.FromSeconds(5)))
            throw new TimeoutException($"Timeout {method}");

        // Ricevi finché non troviamo la risposta per questo requestId
        while (true)
        {
            var buffer = new byte[8192];
            var recvResult = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var responseMsg = Encoding.UTF8.GetString(buffer, 0, recvResult.Count);

            try
            {
                var doc = JsonDocument.Parse(responseMsg);
                var op = doc.RootElement.GetProperty("op").GetInt32();
                if (op == 7)
                {
                    var rid = doc.RootElement.GetProperty("d").GetProperty("requestId").GetString();
                    if (rid == requestId) return responseMsg;
                }
            }
            catch { }
        }
    }

    static async Task SendRaw(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    static async Task<string> GetCurrentSceneSafe()
    {
        try
        {
            var resp = await SendRpc("GetCurrentProgramScene", new {});
            var doc = JsonDocument.Parse(resp);
            var name = doc.RootElement.GetProperty("d").GetProperty("responseData").GetProperty("currentProgramSceneName").GetString();
            currentProgramScene = name ?? "";
            return currentProgramScene;
        }
        catch { return currentProgramScene; }
    }

    static async Task SetCurrentScene(string sceneName)
    {
        await SendRpc("SetCurrentProgramScene", new { sceneName = sceneName });
        currentProgramScene = sceneName;
        Console.WriteLine($"Scena: {sceneName}");
    }

    static async Task EnterMediaCycle()
    {
        Console.WriteLine($"Video rilevato, switcho a: {config.media_scene}");
        savedScene = currentProgramScene;
        isInMediaCycle = true;
        manualOverride = false;
        await SetCurrentScene(config.media_scene);
    }

    static async Task ExitMediaCycle()
    {
        Console.WriteLine($"Fine video, ritorno a: {savedScene}");
        isInMediaCycle = false;
        manualOverride = false;
        if (!string.IsNullOrEmpty(savedScene))
            await SetCurrentScene(savedScene);
        savedScene = "";
    }
}
