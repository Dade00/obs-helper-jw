-- obs-helper-jw.lua — Script Lua OBS con tab impostazioni nativa
-- Rileva JW Library via helper esterno e switcha scena OBS

local obs = obslua

local settings = {
    enabled = false,
    helper_path = "",
    media_scene = "",
    include_images = false
}

local state = "IDLE"
local saved_scene = ""
local cycle_armed = false
local last_reason = "caricato"
local expected_scene = ""

-- ================================================================
-- RICONOSCIMENTO AUTOMATICO WINRtMediaProbe.exe
-- Cerca l'helper accanto allo script .lua (stessa cartella /helper/)
-- ================================================================

local function find_helper_path()
    local script_path = debug.getinfo(1, "S").source:sub(2)
    local dir = script_path:match("^(.*)\\[^\\]*$") or script_path:match("^(.*)/[^/]*$") or ""
    if dir == "" then
        return ""
    end
    local candidate = dir .. "\\helper\\WinRtMediaProbe.exe"
    local f = io.open(candidate, "r")
    if f then
        f:close()
        return candidate
    end
    return ""
end

-- ================================================================
-- LETTURA STATO JW LIBRARY DALL'HELPER ESTERNO
-- ================================================================

local function read_jw_state()
    if settings.helper_path == nil or settings.helper_path == "" then
        return nil, "helper_path non configurato"
    end

    local output_file = os.getenv("TEMP") .. "\\obs_helper_jw_state.json"
    local cmd = string.format('"%s" --output "%s" 2>nul', settings.helper_path, output_file)
    os.execute(cmd)

    local f = io.open(output_file, "r")
    if not f then
        return nil, "helper non ha prodotto output"
    end
    local output = f:read("*a")
    f:close()

    if output == nil or output == "" then
        return nil, "helper ha restituito output vuoto"
    end

    local ok, result = pcall(function()
        return json_decode(output)
    end)

    if not ok then
        return nil, "errore parsing JSON helper: " .. tostring(result)
    end

    return result, nil
end

-- JSON decoder minimalista
local function json_decode(s)
    if s == nil then return nil end
    s = s:gsub("^%s+", ""):gsub("%s+$", "")
    if s:sub(1,1) == "{" then
        local obj = {}
        local inside = s:sub(2, -2)
        if inside ~= "" then
            for pair in inside:gmatch([=[[^{},]+]=]) do
                local k, v = pair:match("^[%s\"']*(.-)[%s\"']*:[%s\"']*(.*)[%s\"']*$")
                if k and v then
                    k = k:gsub("^\"|\"$", "")
                    if v:sub(1,1) == "{" then
                        obj[k] = json_decode(v)
                    elseif v == "true" then
                        obj[k] = true
                    elseif v == "false" then
                        obj[k] = false
                    elseif v == "null" then
                        obj[k] = nil
                    else
                        obj[k] = v:gsub("^\"|\"$", "")
                    end
                end
            end
        end
        return obj
    end
    return s
end

-- Nome scena corrente
local function get_current_scene()
    local scene = obs.obs_frontend_get_current_scene()
    if scene == nil then return "" end
    local name = obs.obs_source_get_name(scene)
    obs.obs_source_release(scene)
    return name or ""
end

-- Verifica se scena esiste
local function scene_exists(name)
    if name == nil or name == "" then return false end
    local s = obs.obs_get_scene_by_name(name)
    if s == nil then return false end
    obs.obs_source_release(s)
    return true
end

-- Imposta scena corrente
local function set_scene(name)
    if name == nil or name == "" then return false end
    local scene = obs.obs_get_scene_by_name(name)
    if scene == nil then return false end
    obs.obs_frontend_set_current_scene(scene)
    obs.obs_source_release(scene)
    return true
end

-- Rileva cambio manuale scena
local function check_manual_change()
    local current = get_current_scene()
    if expected_scene ~= "" and current ~= expected_scene and state == "MEDIA_ACTIVE" then
        return true
    end
    expected_scene = current
    return false
end

-- ================================================================
-- STATE MACHINE
-- ================================================================

local function evaluate()
    if not settings.enabled then
        state = "DISABLED"
        saved_scene = ""
        cycle_armed = false
        last_reason = "disabilitato"
        return
    end

    local result, err = read_jw_state()
    if err then
        state = "SIGNAL_LOST"
        last_reason = err
        return
    end

    if result == nil then
        state = "SIGNAL_LOST"
        last_reason = "nessuna risposta dall'helper"
        return
    end

    local jw_playing = result.jw_is_playing == true
    local jw_paused = result.jw_is_paused == true
    local jw_active = result.has_active_jw == true

    local is_media = jw_playing or jw_paused
    if settings.include_images == false then
        is_media = jw_playing
    end

    -- Override manuale durante contenuto
    if state == "MEDIA_ACTIVE" and check_manual_change() then
        state = "MANUAL_OVERRIDE"
        last_reason = "cambio manuale: utente prevale"
        return
    end

    -- Riarmo dopo override manuale quando fine contenuto
    if state == "MANUAL_OVERRIDE" then
        if not jw_active then
            state = "IDLE"
            cycle_armed = false
            saved_scene = ""
            last_reason = "fine contenuto: riarmo dopo override manuale"
        else
            last_reason = "override manuale attivo: nessun cambio"
        end
        return
    end

    -- Segnale perso
    if state == "SIGNAL_LOST" then
        if result.available == true and jw_active then
            state = "IDLE"
            cycle_armed = false
            last_reason = "segnale ripristinato"
        else
            last_reason = "segnale ancora perso: nessun cambio cieco"
        end
        return
    end

    if state == "DISABLED" then
        last_reason = "disabilitato"
        return
    end

    -- IDLE -> MEDIA_ACTIVE
    if state == "IDLE" then
        if is_media and jw_active then
            saved_scene = get_current_scene()
            cycle_armed = true
            state = "MEDIA_ACTIVE"
            last_reason = "ingresso media su uscita selezionata"

            if settings.media_scene ~= "" and scene_exists(settings.media_scene) then
                set_scene(settings.media_scene)
                expected_scene = settings.media_scene
                last_reason = last_reason .. " → scena: " .. settings.media_scene
            else
                last_reason = last_reason .. " (scena destinazione non trovata)"
            end
        else
            last_reason = "standby / nessun media attivo"
        end
        return
    end

    -- MEDIA_ACTIVE -> IDLE (ritorno a scena precedente)
    if state == "MEDIA_ACTIVE" then
        if not is_media then
            state = "IDLE"
            cycle_armed = false
            if saved_scene ~= "" and scene_exists(saved_scene) then
                set_scene(saved_scene)
                expected_scene = saved_scene
                last_reason = "uscita media: ritorno a " .. saved_scene
            else
                last_reason = "uscita media: scena salvata non trovata"
            end
            saved_scene = ""
        else
            last_reason = "media attivo: nessun cambio"
        end
        return
    end
end

-- ================================================================
-- TAB IMPOSTAZIONI OBS (appare in Strumenti → Script → Impostazioni)
-- ================================================================

function script_properties()
    local props = obs.obs_properties_create()

    obs.obs_properties_add_bool(props, "enabled", "Abilita automazione scena JW Library")

    obs.obs_properties_add_text(props, "helper_path", "Percorso eseguibile WinRtMediaProbe.exe", obs.OBS_TEXT_DEFAULT)

    local scene_list = obs.obs_properties_add_list(props, "media_scene", "Scena multimediale di destinazione",
        obs.OBS_COMBO_TYPE_EDITABLE, obs.OBS_COMBO_FORMAT_STRING)
    obs.obs_property_list_add_string(scene_list, "", "(nessuna)")
    local scenes = obs.obs_enum_sources()
    if scenes then
        for _, src in ipairs(scenes) do
            if obs.obs_source_get_unversioned_id(src) == "scene" then
                local name = obs.obs_source_get_name(src)
                obs.obs_property_list_add_string(scene_list, name, name)
            end
        end
        obs.source_list_release(scenes)
    end

    obs.obs_properties_add_bool(props, "include_images", "Includi immagini (default spento)")

    return props
end

function script_update(setting)
    settings.enabled = obs.obs_data_get_bool(setting, "enabled") or false
    settings.helper_path = obs.obs_data_get_string(setting, "helper_path") or ""
    settings.media_scene = obs.obs_data_get_string(setting, "media_scene") or ""
    settings.include_images = obs.obs_data_get_bool(setting, "include_images") or false
end

function script_description()
    return "OBS Helper JW — Switcha automaticamente sulla scena multimediale quando JW Library presenta video su uscita secondaria. Torna alla scena precedente al termine."
end

function script_load(settings_ud)
    -- Legge impostazioni da settings_ud (userdata OBS obs_data_t)
    settings.enabled = obs.obs_data_get_bool(settings_ud, "enabled") or false
    settings.helper_path = obs.obs_data_get_string(settings_ud, "helper_path") or ""
    settings.media_scene = obs.obs_data_get_string(settings_ud, "media_scene") or ""
    settings.include_images = obs.obs_data_get_bool(settings_ud, "include_images") or false

    -- Riconoscimento automatico helper accanto allo script (solo se non già impostato)
    if settings.helper_path == "" then
        local auto_path = find_helper_path()
        if auto_path ~= "" then
            settings.helper_path = auto_path
            obs.obs_data_set_string(settings_ud, "helper_path", auto_path)
        end
    end

    state = "IDLE"
    saved_scene = ""
    cycle_armed = false
    last_reason = "caricato"
end

function script_unload()
    state = "DISABLED"
end

-- Tick periodico (non bloccante)
local tick_interval = 0.5
local last_tick = 0

function script_tick(seconds)
    last_tick = last_tick + seconds
    if last_tick < tick_interval then return end
    last_tick = 0

    evaluate()
end
