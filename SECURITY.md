# Sicurezza — OBS Helper JW

Non aprire issue pubbliche per vulnerabilità. Contatta @Dade00 con:
versione, Windows/OBS, passi per riprodurre, log anonimizzato (niente
titoli di contenuti o percorsi personali).

Note sul modello di minaccia: il daemon parla solo con OBS su
`ws://localhost`; la password di obs-websocket sta in chiaro in
`config.json` accanto all'exe — è il formato richiesto da OBS, non una
scelta. Non esporre la porta 4455 fuori dal PC e non condividere mai il
tuo `config.json`.
