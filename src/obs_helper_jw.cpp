// obs_helper_jw.cpp — entry point plugin OBS (C API)
// Non contiene riferimenti al PC di sviluppo.
// Impostazioni tramite tab interna OBS (obs_module_properties).
#include "obs_helper_jw.h"
#include "detector/detector_impl.h"
#include "state_machine/state_machine.h"
#include "obs_adapter/obs_adapter.h"
#include "settings/settings.h"

#include <obs-module.h>
#include <obs-frontend-api.h>
#include <memory>
#include <string>
#include <windows.h>
#include <shlobj.h>

// Chiavi impostazioni (definizione)
const char *kSettingEnabled = "enabled";
const char *kSettingHelperPath = "helper_path";
const char *kSettingMediaScene = "media_scene";
const char *kSettingOutputId = "output_id";
const char *kSettingIncludeImages = "include_images";

OBS_MODULE_AUTHOR("OBS Helper JW")

const char *obs_module_name(void) { return "OBS Helper JW"; }
const char *obs_module_description(void) { return "Switcha scena OBS quando JW Library presenta video/immagine su uscita secondaria"; }
const char *obs_module_version(void) { return "0.1.0"; }

// ————————————————————————————————————————————————————————————————
// Stato globale del plugin (istanza unica)
// ————————————————————————————————————————————————————————————————
static std::unique_ptr<ObsHelperJwPlugin> g_plugin;

// ————————————————————————————————————————————————————————————————
// Trova il percorso dell'helper accanto al plugin DLL
// ————————————————————————————————————————————————————————————————
static std::wstring find_helper_path()
{
    wchar_t module_path[MAX_PATH] = {0};
    GetModuleFileNameW(nullptr, module_path, MAX_PATH);
    std::wstring dir = module_path;
    size_t pos = dir.find_last_of(L"\\/");
    if (pos != std::wstring::npos)
        dir = dir.substr(0, pos + 1);
    return dir + L"helper\\WinRtMediaProbe.exe";
}

// ————————————————————————————————————————————————————————————————
// Entry point OBS: caricamento modulo
// ————————————————————————————————————————————————————————————————
bool obs_module_load(obs_module_t *module)
{
    (void)module;

    g_plugin = std::make_unique<ObsHelperJwPlugin>();
    g_plugin->config_.enabled = false;
    g_plugin->config_.media_scene = "";
    g_plugin->config_.include_images = false;
    g_plugin->config_.last_reason = "caricato";

    // Rilevamento automatico helper accanto al plugin DLL
    std::wstring helper_path_w = find_helper_path();
    if (!helper_path_w.empty()) {
        std::string helper_path_str;
        helper_path_str.reserve(helper_path_w.size());
        for (wchar_t c : helper_path_w) {
            helper_path_str += static_cast<char>(c);
        }
        g_plugin->config_.helper_path = helper_path_str;
    }

    // Inizializza componenti
    g_plugin->detector_ = std::make_unique<JwDetector>();
    g_plugin->state_machine_ = std::make_unique<StateMachine>();
    g_plugin->obs_adapter_ = std::make_unique<ObsAdapter>();

    // Configura percorso helper per il detector
    if (!g_plugin->config_.helper_path.empty()) {
        std::wstring helper_path_w2(g_plugin->config_.helper_path.begin(), g_plugin->config_.helper_path.end());
        g_plugin->detector_->set_helper_path(helper_path_w2);
    }

    return true;
}

// ————————————————————————————————————————————————————————————————
// Entry point OBS: scaricamento modulo
// ————————————————————————————————————————————————————————————————
void obs_module_unload(void)
{
    g_plugin.reset();
}

// ————————————————————————————————————————————————————————————————
// Impostazioni predefinite (chiamato da OBS all'avvio)
//
// ObsData: http://docs.obsproject.com/scripting#obs_data_t
// ————————————————————————————————————————————————————————————————
void obs_module_set_defaults(obs_data_t *settings)
{
    obs_data_set_default_bool(settings, kSettingEnabled, false);
    obs_data_set_default_string(settings, kSettingHelperPath, "");
    obs_data_set_default_string(settings, kSettingMediaScene, "");
    obs_data_set_default_bool(settings, kSettingIncludeImages, false);
}

// ————————————————————————————————————————————————————————————————
// Pannello impostazioni OBS (tab interna)
// Chiamato da OBS quando l'utente apre le impostazioni del plugin.
// ————————————————————————————————————————————————————————————————
obs_properties_t *obs_module_properties(obs_module_t *module, obs_properties_t *props)
{
    (void)module;

    obs_properties_add_bool(props, kSettingEnabled, "Abilita automazione scena JW Library");

    obs_properties_add_text(props, kSettingHelperPath, "Percorso eseguibile WinRtMediaProbe.exe", OBS_TEXT_DEFAULT);

    obs_properties_add_text(props, kSettingMediaScene, "Scena multimediale di destinazione", OBS_TEXT_DEFAULT);

    obs_properties_add_bool(props, kSettingIncludeImages, "Includi immagini (default spento)");

    return props;
}

// ————————————————————————————————————————————————————————————————
// Tick chiamato periodicamente (da script Lua wrapper o timer esterno)
// ————————————————————————————————————————————————————————————————
void obs_helper_jw_tick(void)
{
    if (!g_plugin) return;
    if (!g_plugin->config_.enabled) return;

    // 1. Legge osservazione dal detector
    Observation obs = g_plugin->detector_->read();

    // 2. Valuta transizione stato
    TransitionResult result = g_plugin->state_machine_->evaluate(
        obs, g_plugin->config_, g_plugin->user_manual_changed_,
        g_plugin->scene_collection_changed_);

    // 3. Esegue cambio scena se richiesto
    if (!result.target_scene.empty()) {
        if (g_plugin->obs_adapter_->scene_exists(result.target_scene)) {
            g_plugin->obs_adapter_->set_scene(result.target_scene);
            g_plugin->config_.last_reason = result.reason;
        } else {
            g_plugin->config_.last_reason =
                "scena destinazione non trovata: " + result.target_scene;
        }
    } else if (!result.reason.empty()) {
        g_plugin->config_.last_reason = result.reason;
    }

    // 4. Aggiorna scena salvata per il ritorno
    if (!result.saved_scene.empty()) {
        g_plugin->saved_scene_ = result.saved_scene;
    }

    // Reset flag ciclo singolo
    g_plugin->user_manual_changed() = false;
    g_plugin->scene_collection_changed() = false;
}
