// state_machine.cpp — implementazione logica stati (testabile senza Windows).
#include "state_machine.h"

TransitionResult StateMachine::evaluate(const Observation &obs,
                                         const PluginConfig &cfg,
                                         bool user_manual_changed,
                                         bool scene_collection_changed)
{
    TransitionResult result;
    result.content_type = obs.content_type;

    // Cambio collezione: annulla ciclo, non ripristinare scene della vecchia collezione.
    if (scene_collection_changed) {
        state_ = State::IDLE;
        saved_scene_.clear();
        cycle_armed_ = false;
        result.reason = "collezione scene cambiata: ciclo annullato";
        return result;
    }

    // Disabilitato: nessun cambio scena implicito.
    if (!cfg.enabled) {
        state_ = State::DISABLED;
        saved_scene_.clear();
        cycle_armed_ = false;
        result.reason = "disabilitato";
        return result;
    }

    // Segnale non affidabile / perso: sospendi nuovi cambi, mantieni scena, mostra errore.
    if (!obs.signal_valid) {
        state_ = State::SIGNAL_LOST;
        result.reason = obs.reason.empty() ? "segnale non valido" : obs.reason;
        return result;
    }

    const bool is_media = (obs.content_type == MediaType::Video) ||
                          (cfg.include_images && obs.content_type == MediaType::Image);

    switch (state_) {
    case State::DISABLED:
        // non dovrebbe accadere se enabled == true, ma gestito per sicurezza.
        break;

    case State::IDLE:
        if (is_media && obs.presentation == Presentation::Active) {
            // Entrata: salva scena corrente una sola volta per ciclo.
            // La scena corrente va recuperata dall'adapter OBS (non qui).
            cycle_armed_ = true;
            state_ = State::MEDIA_ACTIVE;
            result.target_scene = cfg.media_scene;
            result.reason = "ingresso media su uscita selezionata";
        } else {
            state_ = State::IDLE;
            result.reason = "standby / nessun media attivo";
        }
        break;

    case State::MEDIA_ACTIVE:
        if (user_manual_changed) {
            state_ = State::MANUAL_OVERRIDE;
            result.reason = "cambio manuale: utente prevale";
        } else if (!is_media || obs.presentation != Presentation::Active) {
            // Uscita: confermata fine/stop/chiusura. Ritorno a saved_scene_ se esiste.
            // Pausa (Playback::Paused) NON è uscita: resta su scena media.
            state_ = State::IDLE;
            cycle_armed_ = false;
            if (!saved_scene_.empty()) {
                result.target_scene = saved_scene_;
            }
            result.reason = "uscita media: ritorno scena precedente";
        } else {
            state_ = State::MEDIA_ACTIVE;
            result.reason = "media attivo: nessun cambio";
        }
        break;

    case State::MANUAL_OVERRIDE:
        if (!is_media || obs.presentation != Presentation::Active) {
            // Riarmo dopo fine confermata del contenuto.
            state_ = State::IDLE;
            cycle_armed_ = false;
            saved_scene_.clear();
            result.reason = "fine contenuto: riarmo dopo override manuale";
        } else {
            state_ = State::MANUAL_OVERRIDE;
            result.reason = "override manuale attivo: nessun cambio";
        }
        break;

    case State::SIGNAL_LOST:
        if (obs.signal_valid && is_media && obs.presentation == Presentation::Active) {
            state_ = State::IDLE;
            cycle_armed_ = false;
            result.reason = "segnale ripristinato";
        } else {
            state_ = State::SIGNAL_LOST;
            result.reason = "segnale ancora perso: nessun cambio cieco";
        }
        break;
    }

    return result;
}
