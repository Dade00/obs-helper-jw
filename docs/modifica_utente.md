# Correzione utente — registrata

Data: 2026-11-01

## Correzione
- L'utente ha corretto la proposta: **plugin INTERNO OBS** (non helper C# esterno), con **tab interna OBS per le impostazioni** (scena multimediale, monitor/uscita, abilitazione, includi immagini).

## Implicazioni sulla proposta ARCHITETTURA.md
- La scelta raccomandata "script Lua OBS + piccolo helper Windows" è sostituita dall'alternativa "Plugin nativo C++ OBS" o "script Lua interno con tab settings".
- Il detector resta componente separato (osserva, non comanda); l'adapter OBS non legge più un file JSON esterno, ma interagisce con le API OBS all'interno del plugin.
- Il packaging diventa un singolo prodotto integrato (ZIP per OBS 33+ o OBS 32-), senza helper esterno, senza runtime .NET o Python richiesto (se C++ nativo o Lua).
- La tab interna OBS deve mostrare solo elenchi locali (scene, monitor/uscite) e non importare l'identità del monitor di sviluppo.
- Il gate del detector (standby/video/pausa/fine/immagine) resta aperto e indipendente dal packaging: nessun segnale affidabile = nessuna automazione promettuta, anche con plugin interno.

## Aggiornamenti effettuati
- `docs/design.md`: sezioni 1, 4, 5, 8 aggiornate.
- `docs/THIRD-PARTY-NOTICES.md`: invariato (riferimenti studiati, nessun codice copiato).
- Nessuna modifica a `ARCHITETTURA.md` (file dell'utente; la correzione è registrata qui e nel design spec).
- Nessun codice prodotto per il binario; nessun rilevatore fittizio; nessuna interfaccia costruita.
