// settings.cpp — tab settings interna OBS (scheletro).
// Le scene e i monitor vengono popolati dall'elenco locale del destinatario.
#include "settings.h"
#include "obs_helper_jw.h"

#include <obs-module.h>
#include <obs-frontend-api.h>

void Settings::load(obs_data_t *settings)
{
    config_.enabled = obs_data_get_bool(settings, kSettingEnabled);
    config_.helper_path = obs_data_get_string(settings, kSettingHelperPath);
    config_.media_scene = obs_data_get_string(settings, kSettingMediaScene);
    config_.output_id = obs_data_get_string(settings, kSettingOutputId);
    config_.include_images = obs_data_get_bool(settings, kSettingIncludeImages);
}

void Settings::save(obs_data_t *settings) const
{
    obs_data_set_bool(settings, kSettingEnabled, config_.enabled);
    obs_data_set_string(settings, kSettingHelperPath, config_.helper_path.c_str());
    obs_data_set_string(settings, kSettingMediaScene, config_.media_scene.c_str());
    obs_data_set_string(settings, kSettingOutputId, config_.output_id.c_str());
    obs_data_set_bool(settings, kSettingIncludeImages, config_.include_images);
}

// Popola le proprietà della tab interna con le scene OBS locali del destinatario.
// Nessuna scena predefinita del PC di sviluppo.
void populate_scene_list(obs_properties_t *props, const char *setting_name)
{
    (void)props;
    (void)setting_name;
    // Da implementare: enumerare scene OBS con obs_enum_sources()
}

void populate_output_list(obs_properties_t *props, const char *setting_name)
{
    (void)props;
    (void)setting_name;
    // Da implementare: enumerare monitor con EnumDisplayDevices
}
