// obs_adapter.cpp — implementazione adattatore OBS (scheletro).
#include "obs_adapter.h"

bool ObsAdapter::set_scene(const std::string &scene_name)
{
    if (scene_name.empty()) return false;
    obs_scene_t *scene_obs = obs_get_scene_by_name(scene_name.c_str());
    if (!scene_obs) return false;
    obs_source_t *scene = reinterpret_cast<obs_source_t*>(scene_obs);
    obs_frontend_set_current_scene(scene);
    obs_source_release(scene);
    return true;
}

std::string ObsAdapter::current_scene() const
{
    obs_source_t *scene = obs_frontend_get_current_scene();
    if (!scene) return "";
    const char *name = obs_source_get_name(scene);
    obs_source_release(scene);
    return name ? name : "";
}

bool ObsAdapter::scene_exists(const std::string &scene_name) const
{
    obs_scene_t *scene_obs = obs_get_scene_by_name(scene_name.c_str());
    if (!scene_obs) return false;
    obs_source_t *scene = reinterpret_cast<obs_source_t*>(scene_obs);
    obs_source_release(scene);
    return true;
}

void ObsAdapter::observe_scene_changes(std::function<void(const std::string &new_scene)> callback)
{
    on_scene_changed_ = std::move(callback);
    // Collegare observer OBS (obs_frontend_add_obs_source_removed / scene change).
    // Implementazione da completare con le API OBS corrette.
}

void ObsAdapter::release_references()
{
    if (current_scene_source_) {
        obs_source_release(current_scene_source_);
        current_scene_source_ = nullptr;
    }
    on_scene_changed_ = nullptr;
}
