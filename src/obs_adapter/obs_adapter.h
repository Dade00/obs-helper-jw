// obs_adapter.h — adattatore OBS (integrato nel plugin, non legge file JSON esterni).
// Usa API frontend OBS, osserva cambi manuali, rilascia riferimenti.
// Non effettuare chiamate UIA/WinRT nel thread UI di OBS.
#pragma once

#include <string>
#include <functional>
#include <obs-module.h>
#include <obs-frontend-api.h>

class ObsAdapter {
public:
    ObsAdapter() = default;
    ~ObsAdapter() = default;

    // Imposta la scena corrente (Program, non solo anteprima).
    bool set_scene(const std::string &scene_name);

    // Ritorna il nome della scena corrente su Program.
    std::string current_scene() const;

    // Verifica se una scena esiste (utile dopo cambio collezione/rinomina).
    bool scene_exists(const std::string &scene_name) const;

    // Registra callback per cambi manuali scena (observer OBS).
    void observe_scene_changes(std::function<void(const std::string &new_scene)> callback);

    // Rilascia tutti i riferimenti OBS (chiamato da module_unload).
    void release_references();

private:
    std::function<void(const std::string &)> on_scene_changed_;
    obs_source_t *current_scene_source_ = nullptr;
};
