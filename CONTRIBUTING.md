# Contribuire — OBS Helper JW

> Regola unica: **ogni modifica passa da una pull request. I push diretti
> su `master` sono vietati** (branch protection: niente push, niente force,
> niente merge con check rosso — vale anche per il maintainer).

## Flusso obbligatorio

1. Fork, branch da `master` aggiornato (`fix/...` o `feat/...`).
2. Una PR = un argomento. Modifica solo il necessario.
3. Compila prima di aprire la PR:
   `dotnet build -c Release /p:TargetPlatformVersion=10.0.26100.0`
   (0 errori; allega l'output se tocchi il protocollo websocket).
4. Apri la PR verso `master` compilando il template. Il merge lo fa il
   maintainer dopo averla revisionata (squash). Il check `guard-no-binaries`
   deve essere verde.

## Cosa la PR deve rispettare (il CI blocca il resto)

- **Mai** `bin/`, `obj/`, `.exe`, `.dll`, `.pdb`, `config.json`
  (contiene la password websocket), `baseline.png`, log.
- **Mai** password, token o percorsi macchina-specifici nel codice.
- Il pubblicato resta self-contained (`net10.0-windows`, `win-x64`):
  niente nuove dipendenze runtime senza dirlo in PR.

## Protezione `master` (su GitHub, Settings → Branches)

- Require a pull request before merging (0 approvazioni richieste:
  il maintainer unisce da solo, i contributi esterni solo dopo sua revisione)
- Require status checks (`guard-no-binaries`)
- Niente force push, niente cancellazioni, nessuna eccezione per
  l'amministratore
