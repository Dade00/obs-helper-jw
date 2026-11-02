# OBS Helper JW — Pacchetto di installazione

## Contenuto

| Cartella/File | Descrizione |
|--------------|-------------|
| `script-lua/obs-helper-jw.lua` | Script Lua per OBS (installa nella cartella script di OBS) |
| `helper/` | Eseguibile Windows self-contained per rilevazione WinRT (non richiede .NET sul destinatario) |
| `README.md` | Questo file |

## Installazione (trascina e installa)

### 1. Installa lo script Lua in OBS

Copia la cartella `obs-helper-jw/` (o solo il file `script-lua/obs-helper-jw.lua`) nella directory script di OBS:

```
%APPDATA%\obs-studio\script-lua\obs-helper-jw\
```

Esempio percorso completo:
```
C:\Users\<tuo-utente>\AppData\Roaming\obs-studio\script-lua\obs-helper-jw\obs-helper-jw.lua
```

Copia anche la cartella `helper/` accanto allo script (o in qualsiasi percorso, poi configuralo nelle impostazioni).

### 2. Attiva lo script in OBS

1. Apri OBS Studio (32.2.2 o superiore).
2. Vai su **Strumenti → Script**.
3. Clicca **+** (aggiungi script).
4. Seleziona `obs-helper-jw.lua`.
5. Lo script apparirà nell'elenco con la spunta "Enabled".

### 3. Configura le impostazioni

Nelle proprietà dello script (pannello a destra nella finestra Script di OBS):

| Impostazione | Descrizione |
|-------------|-------------|
| **Abilita automazione scena JW Library** | Attiva/disattiva il plugin |
| **Percorso eseguibile WinRtMediaProbe.exe** | Percorso completo alla cartella `helper/` contenente l'eseguibile. Esempio: `C:\Program Files\OBS Helper JW\helper\WinRtMediaProbe.exe` |
| **Scena multimediale di destinazione** | Scena OBS su cui switchare quando JW Library presenta video (seleziona dall'elenco delle scene OBS) |
| **Includi immagini** | Default spento. Le immagini non sono ancora rappresentate da WinRT. |

### 4. Verifica il funzionamento

Apri JW Library e avvia un video. Lo script dovrebbe:
1. Rilevare la sessione media `Playing` via WinRT
2. Salvare la scena corrente su Program
3. Switchare sulla scena multimediale configurata

Al termine del video, torna alla scena precedente.

## Come funziona

- **Detector**: l'eseguibile `WinRtMediaProbe.exe` (self-contained .NET) enumera tutte le sessioni `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`, filtra per identità JW Library (`Watchtower...`) e restituisce JSON con lo stato di riproduzione.
- **State machine** (nello script Lua): DISABLED → IDLE → MEDIA_ACTIVE → (pausa: resta) → fine → ritorno scena precedente. Cambio manuale prevale. Disabilitazione non implica cambio scena.
- **Adapter OBS**: usa `obs_frontend_set_current_scene` per switchare su Program.

## Portabilità

- Nessun riferimento al PC di sviluppo (nessun percorso, PID, HWND, scena hardcoded).
- Nessun runtime .NET richiesto sul destinatario (helper self-contained).
- Nessuna installazione amministratore richiesta.
- Funziona offline dopo l'installazione.

## Limiti noti (da verificare sul destinatario)

- [ ] **Pausa**: verificare che `PlaybackStatus` diventi `Paused` e che lo script mantenga la scena media.
- [ ] **Fine video**: verificare che la sessione sdiventi `Stopped` o scompaia, e che lo script torni alla scena precedente.
- [ ] **Immagini**: WinRT potrebbe non rappresentare le immagini come sessioni media. L'opzione "Includi immagini" è presente ma potrebbe non funzionare finché JW Library non espone immagini via WinRT.
- [ ] **Secondo monitor**: lo script rileva che c'è una sessione JW attiva, ma non verifica che sia sul monitor secondario. L'associazione output-monitor va configurata manualmente sul destinatario se necessario.

## File temporanei (non inclusi)

La cartella `detector_test/` nel repository sorgente era usata solo per verificare WinRT durante lo sviluppo. Non è inclusa nel pacchetto.

## Build da sorgente (opzionale)

Se devi ricompilare l'helper C#:
1. Apri `detector_test/WinRtMediaProbe/WinRtMediaProbe.csproj`
2. `dotnet publish -c Release -r win-x64 --self-contained`
3. Copia il contenuto di `bin/Release/net10.0-windows/win-x64/publish/` in `helper/`
