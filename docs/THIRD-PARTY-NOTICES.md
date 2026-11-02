# Riferimenti tecnici studiati (nessun riuso di codice incorporato)

Ogni riferimento è studiato solo come API/reference; nessun file sorgente è stato copiato nel prodotto. Se in futuro si incorpora anche una riga, registrare qui il file esatto, la licenza, il commit/hash e l'avviso nel binario.

1. Advanced Scene Switcher — WarmUpTill/SceneSwitcher
   - URL: https://github.com/WarmUpTill/SceneSwitcher
   - Tag/commit studiato: 1.37.0, commit e136086ca3588146796195399150a5aa2dd349b9
   - File studiato: plugins/now-playing/macro-condition-now-playing.cpp
   - Licenza dichiarata: GPL-2.0
   - Note: usa `GetCurrentSession()` (non enumera tutte le sessioni); riferimento per WinRT (`Windows.Media.Control`). Non risolve monitor secondario né immagini.

2. JWL-Assistant — mvpapen/JWL-Assistant
   - URL: https://github.com/mvpapen/JWL-Assistant
   - Snapshot studiato: d82fe4de4b3a35c425ec85d0206dae79c9ba93a3 (README e sorgente parziale)
   - Licenza: PERSONALIZZATA (non MIT). Richiede avvisi, dichiarazione modifiche, sorgenti ai destinatari, invio modifiche al repo ufficiale in caso di redistribuzione modificata. Non incorporare codice.
   - Note: app esterna, OBS WebSocket, Tesseract OCR, richiede opzione JW "Play video on second display".

3. osu-scene-switcher — FunOrange/osu-scene-switcher
   - URL: https://github.com/FunOrange/osu-scene-switcher
   - Snapshot studiato: 471fc654fe4a222abaf4fbcf062e8302dd52bb18
   - Licenza: MIT
   - File studiato: osu_scene_switcher.py
   - Note: modello helper → file stato → script Lua OBS; senza detector JW, senza ripristino dinamico scena precedente, senza override manuali.

4. Meeting Media Manager — sircharlo/meeting-media-manager
   - URL: https://github.com/sircharlo/meeting-media-manager
   - Licenza dichiarata: AGPL-3.0
   - Note: possiede il proprio player; non è un detector esterno di JW Library.

Fonti API:
- OBS scripting: https://docs.obsproject.com/scripting
- Windows media sessions: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager
