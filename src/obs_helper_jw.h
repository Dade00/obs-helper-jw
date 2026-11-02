// obs_helper_jw.h — interfaccia pubblica del plugin OBS
// Non contiene riferimenti al PC di sviluppo (nessun percorso, PID, HWND, scena predefinita).
#pragma once

#include <string>
#include <memory>
#include <obs-module.h>

// Chiavi impostazioni (dichiarate in obs_helper_jw.cpp)
extern const char *kSettingEnabled;
extern const char *kSettingHelperPath;
extern const char *kSettingMediaScene;
extern const char *kSettingOutputId;
extern const char *kSettingIncludeImages;

// Configurazione letta dalla tab interna OBS (non da file esterni).
struct PluginConfig {
    bool enabled = false;
    std::string helper_path;        // percorso eseguibile WinRtMediaProbe.exe
    std::string media_scene;        // scena selezionata dall'elenco locale OBS
    std::string output_id;          // identità uscita/presentazione (device ID, non indice fisso)
    bool include_images = false;    // default spento
    std::string last_reason;        // motivo dell'ultimo cambio
};

// Contratto esposto dal plugin al core OBS.
class ObsHelperJwPlugin {
public:
    ObsHelperJwPlugin() = default;
    ~ObsHelperJwPlugin() = default;

    // Impostazioni lette dalla tab interna OBS.
    const PluginConfig &config() const { return config_; }

    PluginConfig config_;
    std::unique_ptr<class JwDetector> detector_;
    std::unique_ptr<class StateMachine> state_machine_;
    std::unique_ptr<class ObsAdapter> obs_adapter_;
    std::string saved_scene_;
    bool user_manual_changed_ = false;
    bool scene_collection_changed_ = false;

    // Accessor per obs_helper_jw_tick (funzione C, non membro della classe)
    bool &user_manual_changed() { return user_manual_changed_; }
    bool &scene_collection_changed() { return scene_collection_changed_; }
};
