// state_machine.h — macchina a stati testabile senza Windows.
// Stati: DISABLED, IDLE, MEDIA_ACTIVE, MANUAL_OVERRIDE, SIGNAL_LOST.
// Pausa = resta su scena media (non è fine).
// Cambio manuale prevale per il resto del ciclo.
// Disabilitazione non implica cambio scena implicito.
#pragma once

#include "detector/detector_interface.h"
#include "obs_helper_jw.h"
#include <string>

struct TransitionResult {
    MediaType content_type = MediaType::None;
    bool scene_changed = false;
    std::string target_scene;     // scena a cui switchare (vuoto = nessun cambio)
    std::string saved_scene;      // scena salvata per il ritorno
    std::string reason;
};

class StateMachine {
public:
    StateMachine() = default;

    // Valuta l'osservazione corrente e decide la transizione.
    // user_manual_changed: vero se l'utente ha cambiato scena manualmente in questo ciclo.
    // scene_collection_changed: vero se la collezione scene è cambiata (invalida riferimenti).
    TransitionResult evaluate(const Observation &obs,
                              const PluginConfig &cfg,
                              bool user_manual_changed,
                              bool scene_collection_changed);

    enum class State { DISABLED, IDLE, MEDIA_ACTIVE, MANUAL_OVERRIDE, SIGNAL_LOST };
    State get_state() const { return state_; }

private:
    State state_ = State::DISABLED;
    std::string saved_scene_;
    bool cycle_armed_ = false; // vero se il ciclo corrente è attivo
};
