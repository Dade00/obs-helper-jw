// settings.h — configurazione letta dalla tab interna OBS.
// Popolata con scene e monitor del PC destinatario, non del PC di sviluppo.
#pragma once

#include <string>
#include <obs-module.h>
#include "obs_helper_jw.h"

class Settings {
public:
    Settings() = default;
    ~Settings() = default;

    // Carica impostazioni da OBS (obs_get_config / obs_data).
    void load(obs_data_t *settings);

    // Salva impostazioni su OBS.
    void save(obs_data_t *settings) const;

    const PluginConfig &config() const { return config_; }

private:
    PluginConfig config_;
};
