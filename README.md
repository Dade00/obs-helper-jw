# OBS Helper JW — plugin interno OBS

## Stato
Progetto in fase di implementazione. **Gate del detector chiuso**: WinRT `GlobalSystemMediaTransportControlsSessionManager` verificato funzionante — JW Library espone sessione media con `SourceAppUserModelId` contenente "Watchtower" e `PlaybackStatus = Playing` quando un video è in riproduzione.

Il plugin non è ancora compilabile qui (manca OBS SDK su questa macchina). Il binario finale va costruito sul PC di destinatario o su una macchina con OBS SDK installato.

## Architettura
- **Detector** (`src/detector/`): implementazione WinRT reale (`winrt::Windows::Media::Control`). Esegue in worker thread isolato, mai nel thread UI di OBS. Enumererà tutte le sessioni media (non solo `GetCurrentSession`), filtrerà per identità JW Library, restituirà `Observation` con content_type/playback/presentation/signal_valid.
- **State machine** (`src/state_machine/`): DISABLED, IDLE, MEDIA_ACTIVE, MANUAL_OVERRIDE, SIGNAL_LOST. Testabile senza Windows (`tests/state_machine_test.cpp`).
- **Adapter OBS** (`src/obs_adapter/`): `obs_frontend_set_current_scene`, osserva cambi manuali, rilascia riferimenti. Non legge file JSON esterni.
- **Tab settings interna OBS** (`src/settings/`): abilitazione, scena multimediale (elenco locale), monitor/uscita (device ID del destinatario, non indice fisso), includi immagini (default spento).

## Porta
- Nessun percorso `D:/`, nome utente, PID, HWND, coordinate o scena del PC di sviluppo nel prodotto.
- Nessuna scena predefinita hardcoded: le scene vengono lette da OBS sul PC destinatario.
- Packaging: ZIP plugin OBS (layout per OBS 33+ o OBS 32-, da verificare con la versione destinataria).
- Nessun runtime .NET o Python richiesto (plugin C++ nativo).
- Dipendenze: OBS SDK, Windows SDK (C++/WinRT per RoInitialize/WinRT).

## Gate — stato
**Chiuso**. JW Library espone sessione media WinRT distinguibile:
- 1 sessione trovata, `SourceAppUserModelId = WatchtowerBibleandTractSo...!App`, `PlaybackStatus = Playing`
- Titolo/Artista vuoti (nessun contenuto letto, rispetta privacy)
- PowerShell non può invocare WinRT async; C# e C++ sì

Da verificare sul destinatario: pausa (Paused), fine (Stopped/scomparsa), immagine (Image), associazione output al monitor secondario.

## Riferimenti esterni (solo studio, nessun codice copiato)
- Advanced Scene Switcher (GPL-2.0, 1.37.0): riferimento WinRT `Windows.Media.Control`, usa `GetCurrentSession()` (non enumera tutte le sessioni).
- JWL-Assistant (licenza personalizzata, NON MIT): non incorporare codice.
- osu-scene-switcher (MIT): modello helper→stato→OBS, senza detector JW.
- Meeting Media Manager (AGPL-3.0): non è detector esterno di JW Library.

Vedi `docs/THIRD-PARTY-NOTICES.md`.
