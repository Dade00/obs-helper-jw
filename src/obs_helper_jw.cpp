// obs_helper_jw.cpp — entry point plugin OBS (C API)
// Non contiene riferimenti al PC di sviluppo.
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
#include <fstream>

// Chiavi impostazioni (definizione)
const char *kSettingEnabled = "enabled";
const char *kSettingHelperPath = "helper_path";
const char *kSettingMediaScene = "media_scene";
const char *kSettingOutputId = "output_id";
const char *kSettingIncludeImages = "include_images";

// Legge config.json accanto al plugin DLL
static void load_config_file(ObsHelperJwPlugin &plugin)
{
    wchar_t module_path[MAX_PATH] = {0};
    GetModuleFileNameW(nullptr, module_path, MAX_PATH);
    std::wstring dir = module_path;
    size_t pos = dir.find_last_of(L"\\/");
    if (pos != std::wstring::npos)
        dir = dir.substr(0, pos + 1);
    std::string config_path(dir.begin(), dir.end());
    config_path += "config.json";

    std::ifstream file(config_path);
    if (!file.is_open())
        return;

    std::string content((std::istreambuf_iterator<char>(file)),
                        std::istreambuf_iterator<char>());

    // Parsing JSON minimo per i campi che ci servono
    auto find_bool = [&](const std::string &key) -> bool {
        std::string search = "\"" + key + "\":";
        size_t p = content.find(search);
        if (p == std::string::npos) return false;
        p += search.size();
        while (p < content.size() && (content[p] == ' ' || content[p] == '\t')) p++;
        return content.compare(p, 4, "true") == 0;
    };

    auto find_string = [&](const std::string &key) -> std::string {
        std::string search = "\"" + key + "\":";
        size_t p = content.find(search);
        if (p == std::string::npos) return "";
        p += search.size();
        while (p < content.size() && (content[p] == ' ' || content[p] == '\t')) p++;
        if (p >= content.size() || content[p] != '"') return "";
        p++;
        size_t end = content.find('"', p);
        if (end == std::string::npos) return "";
        return content.substr(p, end - p);
    };

    plugin.config_.enabled = find_bool(kSettingEnabled);
    plugin.config_.helper_path = find_string(kSettingHelperPath);
    plugin.config_.media_scene = find_string(kSettingMediaScene);
    plugin.config_.include_images = find_bool(kSettingIncludeImages);
}

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

    // Carica config.json se presente (permette configurazione senza ricompilare)
    load_config_file(*g_plugin);

    // Rilevamento automatico helper accanto al plugin (solo se non in config.json)
    if (g_plugin->config_.helper_path.empty()) {
        std::wstring helper_path_w = find_helper_path();
        g_plugin->config_.helper_path = std::string(helper_path_w.begin(), helper_path_w.end());
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
// Tick chiamato periodicamente (da script Lua wrapper o timer)
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
