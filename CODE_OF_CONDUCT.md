# Codice di condotta — OBS Helper JW

Progetto single-maintainer (@Dade00). Chi partecipa accetta queste regole.

1. **Solo pull request, mai push diretto su `master`.**
2. **Ambito stretto.** Il daemon osserva JW Library e commuta una scena OBS
   via websocket locale. Niente telemetria, niente controllo del player,
   niente modifiche a JW Library, niente rete oltre `ws://localhost`.
3. **Niente dati personali.** Non inviare log con titoli di contenuti,
   screenshot, percorsi locali, password di obs-websocket. I diagnostici
   vanno anonimizzati.
4. **Niente binari nel codice.** Non committare `bin/`, `obj/`, `.exe`,
   `.dll`, `config.json`, `baseline.png`. I binari viaggiano solo come
   asset delle GitHub Release.
5. **Niente attacchi personali, spam o PR automatiche.** Una violazione
   grave chiude la PR e può portare al ban.

Segnalazioni: apri un issue `[CoC]`. Il maintainer risponde entro 7 giorni;
le sue decisioni sono finali. Ispirato al Contributor Covenant v2.1,
ridotto all'essenziale.
