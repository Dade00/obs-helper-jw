# Gate detector — evidenze raccolte (PC di sviluppo)

Data: 2026-11-01 | Stato: PARZIALMENTE CHIUSO (standby verificato; video/pausa/fine/immagine NON testabili senza presentazione reale)

## Metodo
Script PowerShell `gate_probe.ps1` (PS 5.1) e `gate_probe_ps7.ps1` (PS 7.6.9). Output JSON in `detector/gate_evidence_dev.json`.
Lo script NON fa screenshot, NON registra titoli contenuti, NON clicca in JW Library.

## Evidenze — stato STANDBY (JW Library aperto, nessun video in esecuzione)

### Processi
- JWLibrary.exe PID 12268, percorso `C:\Program Files\WindowsApps\WatchtowerBibleandTractSo.45909CDBADF3C_15.9.54.0_x64__5rz59y55nfz3e\JWLibrary.exe`
- 25+ processi figli msedgewebview2 (wrapper UWP/WebView2)
- Nessun processo OBS rilevato

### Finestre (EnumWindows + figli)
| Finestra | PID | Classe | Titolo | Coordinate |
|----------|-----|--------|--------|------------|
| CoreWindow | 12268 | Windows.UI.Core.CoreWindow | JW Library | (0, 33, 1200, 933) |
| MSCTFIME UI | 12268 | MSCTFIME UI | MSCTFIME UI | (0, 0, 0, 0) |
| IME | 12268 | IME | Default IME | (0, 0, 0, 0) |

**Nota**: NON esiste una seconda finestra JW Library sul monitor secondario. Conferma ARCHITETTURA.md: l'uscita secondaria potrebbe non essere attiva o esposta diversamente.

### Monitor
| Monitor | Primario | Bounds | Device |
|---------|----------|--------|--------|
| DISPLAY1 | Sì | (0, 0, 1707, 1067) | \\.\DISPLAY1 |
| DISPLAY5 | No | (-2560, 76, 0, 1516) | \\.\DISPLAY5 |

Coordinate negative sul secondario confermate.

### UI Automation
`Add-Type -AssemblyName UIAutomationClient` fallisce in PS 5.1 e PS 7 (`UIAutomation.AutomationElement` non trovato). UIA non accessibile da PowerShell puro su questa macchina.

### Windows Media Control (WinRT)
- Tipo `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` si risolve in PS 5.1 (`[type]::GetType` con nome completo + `ContentType=WindowsRuntime`)
- `RequestAsync()` restituisce `__ComObject`, ma `GetAwaiter()` / `GetResults()` non sono invocabili da PowerShell (proiezione WinRT incompleta)
- In PS 7.6.9 il tipo non si risolve neanche
- **Conclusione**: WinRT media session non interrogabile da PowerShell puro su questa macchina

### Conclusione provvisoria
"Nessuna sessione video attiva per JW Library (standby o nessun video in corso)"

## Cosa manca per chiudere il gate
1. **Presentazione reale**: avviare un video su JW Library (con opzione "second display" se disponibile) e rieseguire il probe per verificare:
   - Sessione media JW appare con `PlaybackStatus = Playing`
   - Associazione a output_id del monitor secondario
   - Pausa → `PlaybackStatus = Paused` (non deve essere fine)
   - Fine/stop → sessione scompare o `Stopped`
   - Immagine → tipo contenuto Image (se rappresentato)
2. **WinRT accessibile**: il probe richiede un eseguibile C# (compilato con Windows SDK) o Python con `winrt`/`pywin32` per chiamare `RequestAsync().AsTask().GetAwaiter().GetResult()` correttamente. PowerShell puro non è sufficiente.
3. **Calibrazione sul destinatario**: eseguire il probe sulla postazione finale con i suoi monitor e scene.

## File prodotti
- `detector/gate_probe.ps1` — script diagnostico PS 5.1
- `detector/gate_probe_ps7.ps1` — versione PS 7 (WinRT non risolto)
- `detector/gate_evidence_dev.json` — evidenza standby (PS 5.1)
- `detector/test_winrt*.ps1` — tentativi falliti di accesso WinRT (da rimuovere prima della consegna)

## Impatto sul prodotto
- Il detector C++ (plugin OBS interno) potrà chiamare WinRT direttamente tramite `winrt::Windows::Media::Control` (come in Advanced Scene Switcher) senza il limite di PowerShell.
- Il gate rimane aperto finché non si dimostra con una presentazione reale che JW Library espone sessioni media distinguibili.
