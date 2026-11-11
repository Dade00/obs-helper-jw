# OBS Helper JW — daemon Windows

Piccolo daemon per Windows che segue **JW Library**: quando un video viene
presentato, passa OBS alla scena multimediale configurata; al termine torna
alla scena precedente. Se cambi scena a mano durante il video, il ritorno
automatico non ti sovrascrive.

## Come funziona

1. Legge le sessioni multimediali di Windows (WinRT
   `GlobalSystemMediaTransportControlsSessionManager`) e cerca quelle di
   JW Library (`Watchtower` / `JWLibrary`), senza leggere titoli o contenuti.
2. Comanda OBS via **obs-websocket v5** su `ws://localhost:4455`
   (connessione locale, niente rete esterna).
3. Le immagini statiche non creano sessioni WinRT: opzione che confronta il
   secondo monitor con una "schermata di riposo" memorizzata da te
   (differenza media pixel, soglia regolabile).

## Requisiti

- Windows 10/11 a 64 bit, OBS Studio con obs-websocket attivo (porta 4455),
  JW Library.
- Per compilare: .NET 10 SDK. Il pubblicato è self-contained: sul PC che
  lo esegue non serve alcun runtime.

## Compilazione

```cmd
dotnet build -c Release /p:TargetPlatformVersion=10.0.26100.0
```

(Il flag `/p:...` serve con alcuni setup MSBuild; deve corrispondere al
Windows SDK installato, vedi `Include/10.0.*` in `Windows Kits/10`.)

## Installazione (PC destinatario)

**Via installer (consigliato):** scarica `OBS-Helper-JW-Setup-*.exe` dall'ultima
Release GitHub ed eseguilo. Installa in `%LOCALAPPDATA%\obs-studio\obs-helper-jw`
senza diritti admin, non tocca mai il tuo `config.json` esistente.

**Manuale:**

```cmd
dotnet publish -c Release -r win-x64 --self-contained true /p:TargetPlatformVersion=10.0.26100.0
```

Copia il contenuto di `bin/Release/net10.0-windows/win-x64/publish/` in una
cartella (es. `%APPDATA%\obs-studio\obs-helper-jw\`), avvia
`HelperObs.exe`, tasto destro sull'icona nel tray → Impostazioni:

- scena multimediale (elenco letto da OBS), abilita automazione,
  "In pausa torna alla scena precedente", "Includi immagini" + cattura
  della schermata di riposo, avvio con Windows.
- Log: `%TEMP%\obs_helper_jw.log`. Config: `config.json` accanto all'exe
  (contiene l'eventuale password di obs-websocket: non condividerlo).

## Stato del progetto

Uso reale: daemon C# (questa cartella). I vecchi tentativi (plugin C++,
script Lua) sono stati rimossi: non compilavano il prodotto e non erano
usati. La cronologia delle decisioni resta nelle sessioni di sviluppo,
non in questo repo.

## Licenza

MIT — vedi `LICENSE`. Codice originale, nessuna dipendenza oltre .NET,
WinRT e il protocollo obs-websocket (reimplementato a mano, nessuna
libreria copiata).
