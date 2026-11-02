# OBS Helper JW — Design Spec (validazione proposta ARCHITETTURA.md)

Data: 2026-11-01 | Stato: gate detector aperto, prodotto NON ancora costruito.

## 1. Comprensione (riflesso dal documento)
- **Obiettivo aggiornato (correzione utente)**: plugin **interno OBS** (C++ o Lua con tab settings integrata), non helper C# esterno né script Lua separato. Tab interna OBS per impostazioni: abilitazione, scena multimediale, monitor/uscita selezionata, includi immagini (default spento). Il detector resta componente separato (osserva, non comanda), ma il prodotto finale è un singolo plugin OBS con tab settings.
- **Portabilità**: consegna su altro PC; nessun riferimento al PC di sviluppo nel binario; diagnostica esportabile; funzionamento offline.
- **Gate centrale**: dimostrare che il detector distingue standby / video / pausa / fine / immagine sull'uscita secondaria PRIMA di implementare il prodotto. Nessun rilevamento affidabile = nessuna promessa di automazione affidabile.
- **Contratti**: detector (solo lettura), state machine (testabile senza Windows), adapter OBS, impostazioni + diagnostica.
- **Semantica**: DISABLED, IDLE, MEDIA_ACTIVE, MANUAL_OVERRIDE, SIGNAL_LOST. Pausa = restare su scena media; uscita solo se confermata; cambio manuale prevale; disabilitazione non implica cambio scena.

## 2. Evidenze locali raccolte (solo lettura, non incorporate nel prodotto)
- JWLibrary.exe presente (PID 12268, percorso `C:\Program Files\WindowsApps\WatchtowerBibleandTractSo...`), wrapper `msedgewebview2.exe` con flag `--embedded-browser-webview=1` e `--video-capture-use-gpu-memory-buffer`.
- Nello snapshot attuale NON risulta una seconda finestra Win32 di JW Library tra finestre principali o figli enumerati; NON è stato lanciato un video né modificata la configurazione dell'utente.
- Monitor: `wmic` non disponibile su questa sessione bash; le coordinate del documento (`(0,0,1707,1067)` e `(-2560,76,0,1516)`) restano riferimento del PC di sviluppo, NON dati del destinatario.
- Media session (`GlobalSystemMediaTransportControlsSessionManager`): NON verificato su questa macchina (richiede WinRT / pywin32 non installato nel venv attuale).
- Nessun processo OBS trovato nello snapshot.

## 3. Confronto con riferimenti GitHub (solo studio API, nessun riuso di codice)
- **Advanced Scene Switcher** (GPL-2.0, tag 1.37.0): condizione "Now Playing" usa `GetCurrentSession()` (NON enumera tutte le sessioni) e `SourceAppUserModelId`. Riferimento concreto per WinRT, ma non risolve associazione al monitor secondario né riconoscimento immagini. Non copiare `.get()` bloccanti nel thread UI del nostro adattatore.
- **JWL-Assistant** (licenza personalizzata, NON MIT): app esterna con OBS WebSocket, Tesseract OCR, richiede opzione JW "Play video on second display". Non dimostra eventi pubblici affidabili di riproduzione; le ricerche UIA basate su etichette inglesi sono un rischio per installazioni italiane. Non riprendere funzioni che cliccano o modificano JW Library.
- **osu-scene-switcher** (MIT): helper → file stato → script Lua OBS; modello semplice ma senza detector JW, senza ripristino dinamico della scena precedente, senza override manuali.
- **Meeting Media Manager** (AGPL-3.0): possiede il proprio player, non è un detector esterno di JW Library.

## 4. Decisioni aggiornate dal confronto
- Per la consegna aggiornata (plugin interno OBS con tab settings interna): preferire **plugin nativo C++ OBS** (accesso diretto alle API Windows e API OBS) o, se il costo di build lo giustifica, un **script Lua interno con tab settings** tramite le API di scripting OBS. In entrambi i casi il detector resta componente separato (osserva, non comanda) e il packaging è un singolo prodotto integrato (ZIP per OBS 33+ o layout OBS 32-, compatibilità verificata con la versione destinataria). Non costruire un helper esterno.
- Il detector JW deve essere verificato PRIMA di scegliere definitivamente il packaging; se il segnale non è affidabile, dichiarare il limite.
- Nessun riuso integrale di codice dai progetti esaminati; ogni riferimento va registrato in `docs/THIRD-PARTY-NOTICES.md` con licenza e commit/hash.

## 5. Componenti e contratti (come in ARCHITETTURA.md)
- Detector: restituisce stato + motivazione, senza conoscere OBS.
- State machine: testabile senza Windows; gestisce override, heartbeat, disabilitazione, cambio collezione.
- Adapter OBS: integrato nel plugin (C++ API OBS o Lua `obs_frontend_set_current_scene`); osserva cambi manuali tramite hook/observer; rilascia riferimenti; la state machine decide, non il thread UI.
- Impostazioni: tab interna OBS (non file esterni o interfaccia separata) — abilitazione, scena multimediale (da elenco locale scene), monitor/uscita selezionata (da identità dispositivo, non indice fisso), includi immagini (default spento), stato letto e motivo dell'ultimo cambio.

## 6. Gate da superare prima del prodotto
- [ ] Rilevare finestre, figli, processi e package prima/durante/dopo una presentazione reale.
- [ ] Ispezionare UI Automation per stato player e uscita presentazione; registrare elementi reali senza presumere AutomationId o testi.
- [ ] Verificare `GlobalSystemMediaTransportControlsSessionManager`: enumerare TUTTE le sessioni filtrando l'identità di JW Library (non solo `GetCurrentSession`).
- [ ] Se nessuna combinazione identifica in modo affidabile il contenuto secondario: dichiarare il limite esplicitamente.
- [ ] Modalità diagnostica eseguibile sul destinatario: versioni, finestre, proprietà strutturali, identità monitor, osservazioni/transizioni con timestamp; nessuno screenshot o titolo contenuto per default.
- [ ] Calibrazione su postazione destinataria: standby/video/pausa/fine/immagine; verifica requisiti cattura OBS.

## 7. Limiti dichiarati (da non nascondere)
- Il PC di sviluppo NON ha una seconda finestra JW attiva al momento dell'osservazione; NON è stato avviato un video; NON è stato verificato il segnale della sessione media.
- Le coordinate dei monitor e l'identità del monitor di sviluppo NON devono essere incorporate nel prodotto né importate automaticamente sul destinatario.
- L'estensione NON deve reagire alla semplice apertura dell'app, a un video sul solo schermo principale, o a un'altra applicazione.
- La scena multimediale deve catturare l'uscita corretta; lo switch NON crea automaticamente la cattura o il routing audio.

## 8. Prossimi passi (ordine)
1. Completare il gate del detector (UIA + sessioni media) su questa macchina, con una presentazione reale se possibile.
2. Se il segnale è affidabile: iniziare state machine (test unitari con osservazioni registrate) + adapter OBS (lettura snapshot locale).
3. Se il segnale NON è affidabile: dichiarare il limite nel report e valutare se procedere con un fallback esplicito (es. analisi visiva solo come opzione dichiarata) o fermare.
4. Costruire la modalità diagnostica esportabile.
5. Packaging (plugin interno OBS): verificare layout ZIP per OBS 33+ o OBS 32-; dichiarare dimensione effettiva, architettura (x64) e versione OBS richiesta; documentare avvisi SmartScreen per binari non firmati; nessun runtime Python o .NET richiesto se il plugin è C++ nativo o Lua.
6. Collaudo finale sul destinatario: nessun riferimento al PC di sviluppo nel binario; la tab settings deve mostrare solo scene, monitor/uscite e opzioni locali al destinatario.
