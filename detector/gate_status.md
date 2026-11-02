# Gate detector — stato

Data: 2026-11-01 | Stato: CHIUSO (segnale affidabile verificato)

## Cosa è stato fatto (solo lettura)
- Rilevati processi JWLibrary.exe (PID 12268) e wrapper msedgewebview2.exe con flag video-capture e embedded-browser-webview=1.
- Monitor rilevati: DISPLAY1 primario (0,0,1707,1067), DISPLAY5 secondario (-2560,76,0,1516).
- WinRT `GlobalSystemMediaTransportControlsSessionManager` verificato funzionante da C# (non da PowerShell):
  - Manager ottenuto: True
  - Sessioni totali: 1
  - Sessione JW: `WatchtowerBibleandTractSo.45909CDBADF3C_5rz59y55nfz3e!App`, Status: Playing
  - Titolo/Artista: vuoti (nessun contenuto letto)
- OBS in esecuzione: PID 30780, versione 32.2.2
- Eseguibile C# di test in `detector_test/WinRtMediaProbe/` (temporaneo)
- Studiati riferimenti GitHub solo come API/reference; nessun codice incorporato.

## Cosa NON è ancora verificato (da fare sul destinatario)
- [ ] Pausa → `PlaybackStatus` deve diventare `Paused` (non deve tornare alla scena precedente)
- [ ] Fine video → sessione scompare o `Stopped` (ritorno scena precedente)
- [ ] Immagine → tipo contenuto `Image` (se rappresentato da WinRT)
- [ ] Monitor/uscita del destinatario: identità dispositivo, non indice fisso
- [ ] Calibrazione completa standby/video/pausa/fine/immagine sulla postazione finale
- [ ] Verifica requisiti cattura OBS (scena multimediale deve catturare l'uscita corretta)

## Impatto sul prodotto
- Detector C++ può chiamare WinRT direttamente (riferimento: Advanced Scene Switcher, usa `Windows.Media.Control`)
- `detector_impl.cpp` stub va sostituito con implementazione WinRT reale
- Lo stato `signal_valid = false` può diventare `true` quando WinRT è disponibile
- Nessun riferimento al PC di sviluppo nel binario

## Prossimo passo
Implementare `detector_impl.cpp` con WinRT (`winrt::Windows::Media::Control`), enumerare tutte le sessioni, filtrare per identità JW Library, restituire `Observation` con `content_type`, `playback`, `presentation`.
