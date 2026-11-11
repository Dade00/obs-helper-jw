# Contribuire — OBS Helper JW

> Regola unica: **ogni modifica passa da una pull request revisionata dal
> maintainer. I push diretti su `master` sono vietati** (branch protection).

## Flusso obbligatorio

1. Fork, branch da `master` aggiornato (`fix/...` o `feat/...`).
2. Una PR = un argomento. Modifica solo il necessario.
3. Compila prima di aprire la PR:
   `dotnet build -c Release /p:TargetPlatformVersion=10.0.26100.0`
   (0 errori; allega l'output se tocchi il protocollo websocket).
4. Apri la PR verso `master` compilando il template. Solo il maintainer
   fa merge (squash). Niente self-merge.

## Cosa la PR deve rispettare (il CI blocca il resto)

- **Mai** `bin/`, `obj/`, `.exe`, `.dll`, `.pdb`, `config.json`
  (contiene la password websocket), `baseline.png`, log.
- **Mai** password, token o percorsi macchina-specifici nel codice.
- Il pubblicato resta self-contained (`net10.0-windows`, `win-x64`):
  niente nuove dipendenze runtime senza dirlo in PR.

## Protezione `master` (su GitHub, Settings → Branches)

- Require a pull request before merging (1 approval, CODEOWNERS review)
- Require status checks (`guard-no-binaries`)
- Require conversation resolution
- Do not allow bypassing (nemmeno per il maintainer)
