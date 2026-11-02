// state_machine_test.cpp — test unitari della macchina a stati con osservazioni registrate.
// Compilabile senza Windows/OBS (solo state_machine.h + detector_interface.h).
// I test sintetici NON provano che JW Library esponga i segnali richiesti.
#include "state_machine/state_machine.h"
#include "detector/detector_interface.h"

#include <cassert>
#include <iostream>
#include <string>

struct TestConfig {
    bool enabled = false;
    std::string media_scene = "Scena Media";
    std::string output_id = "output-2";
    bool include_images = false;
};

// Helper per costruire un'osservazione.
static Observation make_obs(MediaType ct, Playback pb, Presentation pr, bool valid, const std::string &reason)
{
    Observation o;
    o.content_type = ct;
    o.playback = pb;
    o.presentation = pr;
    o.signal_valid = valid;
    o.reason = reason;
    return o;
}

int main()
{
    int failures = 0;

    // 1. App aperta/standby: nessun cambio.
    {
        StateMachine sm;
        TestConfig cfg; cfg.enabled = true;
        auto r = sm.evaluate(make_obs(MediaType::None, Playback::Stopped, Presentation::Inactive, true, "standby"),
                             *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        assert(r.target_scene.empty() && "standby non deve cambiare scena");
        std::cout << "OK standby: " << r.reason << "\n";
    }

    // 2. Video solo su principale: nessun cambio (presentazione non attiva su secondario).
    {
        StateMachine sm;
        TestConfig cfg; cfg.enabled = true;
        auto r = sm.evaluate(make_obs(MediaType::Video, Playback::Playing, Presentation::Inactive, true, "video su principale"),
                             *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        assert(r.target_scene.empty() && "video su principale non deve switchare");
        std::cout << "OK video principale: " << r.reason << "\n";
    }

    // 3. Video su secondario: cambio alla scena selezionata.
    {
        StateMachine sm;
        TestConfig cfg; cfg.enabled = true; cfg.media_scene = "Scena Media";
        auto r = sm.evaluate(make_obs(MediaType::Video, Playback::Playing, Presentation::Active, true, "video su secondario"),
                             *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        assert(r.target_scene == "Scena Media" && "video su secondario deve switchare");
        std::cout << "OK video secondario: " << r.reason << " -> " << r.target_scene << "\n";
    }

    // 4. Pausa: resta su scena media (nessun ritorno).
    {
        StateMachine sm;
        TestConfig cfg; cfg.enabled = true; cfg.media_scene = "Scena Media";
        sm.evaluate(make_obs(MediaType::Video, Playback::Playing, Presentation::Active, true, "ingresso"),
                    *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        auto r = sm.evaluate(make_obs(MediaType::Video, Playback::Paused, Presentation::Active, true, "pausa"),
                             *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        assert(r.target_scene.empty() && "pausa non deve tornare indietro");
        std::cout << "OK pausa: " << r.reason << "\n";
    }

    // 5. Cambio manuale: nessuna sovrascrittura successiva nello stesso ciclo.
    {
        StateMachine sm;
        TestConfig cfg; cfg.enabled = true; cfg.media_scene = "Scena Media";
        sm.evaluate(make_obs(MediaType::Video, Playback::Playing, Presentation::Active, true, "ingresso"),
                    *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        auto r = sm.evaluate(make_obs(MediaType::Video, Playback::Playing, Presentation::Active, true, "ancora attivo"),
                             *reinterpret_cast<PluginConfig*>(&cfg), true, false);
        assert(r.target_scene.empty() && "override manuale blocca switch");
        std::cout << "OK override manuale: " << r.reason << "\n";
    }

    // 6. Disabilitazione: nessun cambio scena implicito.
    {
        StateMachine sm;
        TestConfig cfg; cfg.enabled = false;
        auto r = sm.evaluate(make_obs(MediaType::Video, Playback::Playing, Presentation::Active, true, "video"),
                             *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        assert(r.target_scene.empty() && "disabilitato non deve cambiare scena");
        std::cout << "OK disabilitato: " << r.reason << "\n";
    }

    // 7. Segnale perso: nessun cambio cieco.
    {
        StateMachine sm;
        TestConfig cfg; cfg.enabled = true;
        auto r = sm.evaluate(make_obs(MediaType::Unknown, Playback::Unknown, Presentation::Unknown, false, "segnale perso"),
                             *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        assert(r.target_scene.empty() && "segnale perso non deve cambiare scena");
        std::cout << "OK segnale perso: " << r.reason << "\n";
    }

    // 8. Cambio collezione: annulla ciclo, nessun riferimento obsoleto.
    {
        StateMachine sm;
        TestConfig cfg; cfg.enabled = true; cfg.media_scene = "Scena Media";
        sm.evaluate(make_obs(MediaType::Video, Playback::Playing, Presentation::Active, true, "ingresso"),
                    *reinterpret_cast<PluginConfig*>(&cfg), false, false);
        auto r = sm.evaluate(make_obs(MediaType::Video, Playback::Playing, Presentation::Active, true, "ancora attivo"),
                             *reinterpret_cast<PluginConfig*>(&cfg), false, true);
        assert(r.target_scene.empty() && "cambio collezione invalida ciclo");
        std::cout << "OK cambio collezione: " << r.reason << "\n";
    }

    if (failures == 0) {
        std::cout << "Tutti i test dello scheletro stati macchina passati.\n";
        return 0;
    }
    std::cout << failures << " test falliti.\n";
    return 1;
}
