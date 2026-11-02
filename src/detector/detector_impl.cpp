// detector_impl.cpp — implementazione detector JW Library via helper esterno.
// Esegue WinRtMediaProbe.exe (self-contained .NET) che legge WinRT e scrive JSON su file.
// Nessuna chiamata WinRT diretta nel plugin C++ — evita problemi di compilazione C++/WinRT.
// Comunicazione: helper scrive JSON su file temporaneo, plugin lo legge. Nessuna console, nessuna finestra.
#include "detector_impl.h"

#include <windows.h>
#include <shlobj.h>
#include <mutex>
#include <atomic>
#include <chrono>
#include <thread>
#include <fstream>
#include <string>

namespace {

// Legge tutto il contenuto di un file di testo (wide string) e lo converte in UTF-8.
std::string read_file_text_utf8(const std::wstring &path)
{
    std::wifstream file(path);
    if (!file.is_open())
        return "";
    std::wstring content((std::istreambuf_iterator<wchar_t>(file)),
                         std::istreambuf_iterator<wchar_t>());
    if (content.empty())
        return "";
    int size_needed = WideCharToMultiByte(CP_UTF8, 0, content.c_str(),
                                          (int)content.size(), nullptr, 0, nullptr, nullptr);
    std::string result(size_needed, 0);
    WideCharToMultiByte(CP_UTF8, 0, content.c_str(), (int)content.size(),
                        &result[0], size_needed, nullptr, nullptr);
    return result;
}

// Esegue l'helper e legge il risultato JSON dal file.
Observation read_via_helper(const std::wstring &helper_path,
                            const std::wstring &output_file)
{
    Observation obs;
    obs.sequence = 0;
    obs.timestamp_ms = 0;
    obs.content_type = MediaType::Unknown;
    obs.playback = Playback::Unknown;
    obs.presentation = Presentation::Unknown;
    obs.signal_valid = false;
    obs.reason = "helper non eseguito";

    // Costruisci comando: "helper.exe" --output "file.json"
    std::wstring cmd = L"\"" + helper_path + L"\" --output \"" + output_file + L"\"";

    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = { 0 };

    if (!CreateProcessW(nullptr, &cmd[0], nullptr, nullptr, FALSE,
                        CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi))
    {
        obs.reason = "impossibile avviare helper";
        return obs;
    }

    // Attendi fine esecuzione (timeout 5 secondi)
    DWORD wait = WaitForSingleObject(pi.hProcess, 5000);
    if (wait != WAIT_OBJECT_0) {
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        obs.reason = "helper timeout";
        return obs;
    }

    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);

    // Leggi il file di output
    std::string json = read_file_text_utf8(output_file);
    if (json.empty()) {
        obs.reason = "helper output vuoto";
        return obs;
    }

    // Parsing JSON minimo: cerca chiavi booleane
    bool is_playing = json.find("\"is_playing\":true") != std::string::npos;
    bool is_paused = json.find("\"is_paused\":true") != std::string::npos;
    bool has_active = json.find("\"has_active_jw\":true") != std::string::npos;
    bool available = json.find("\"available\":true") != std::string::npos;

    obs.sequence = 1;
    obs.timestamp_ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();

    if (!available) {
        obs.content_type = MediaType::Unknown;
        obs.playback = Playback::Unknown;
        obs.presentation = Presentation::Unknown;
        obs.signal_valid = false;
        obs.reason = "helper non disponibile";
        return obs;
    }

    obs.signal_valid = true;
    obs.reason = "sessione media JW Library rilevata via helper";

    if (has_active) {
        obs.presentation = Presentation::Active;
        if (is_playing) {
            obs.content_type = MediaType::Video;
            obs.playback = Playback::Playing;
        } else if (is_paused) {
            obs.content_type = MediaType::Video;
            obs.playback = Playback::Paused;
        } else {
            obs.content_type = MediaType::None;
            obs.playback = Playback::Stopped;
        }
    } else {
        obs.content_type = MediaType::None;
        obs.playback = Playback::Stopped;
        obs.presentation = Presentation::Inactive;
        obs.reason = "nessuna sessione media JW Library attiva";
    }

    return obs;
}

} // namespace

struct JwDetector::Impl {
    std::atomic<bool> running{false};
    std::thread worker;
    mutable std::mutex mutex;
    Observation last_observation{};
    std::wstring helper_path;
    std::wstring output_file;

    ~Impl() { stop(); }

    void start()
    {
        if (running) return;
        running = true;
        worker = std::thread(&Impl::run_loop, this);
    }

    void stop()
    {
        if (!running) return;
        running = false;
        if (worker.joinable()) worker.join();
    }

    Observation read() const
    {
        std::lock_guard<std::mutex> lock(mutex);
        return last_observation;
    }

    bool is_available() const
    {
        return !helper_path.empty();
    }

private:
    void run_loop()
    {
        // Determina percorso output file nella cartella temp dell'utente
        wchar_t temp_path[MAX_PATH] = {0};
        GetTempPathW(MAX_PATH, temp_path);
        output_file = std::wstring(temp_path) + L"obs_helper_jw_state.json";

        int sequence = 0;
        while (running) {
            if (!helper_path.empty()) {
                Observation obs = read_via_helper(helper_path, output_file);
                obs.sequence = ++sequence;

                {
                    std::lock_guard<std::mutex> lock(mutex);
                    last_observation = obs;
                }
            }

            // Intervallo di lettura: 200 ms
            std::this_thread::sleep_for(std::chrono::milliseconds(200));
        }
    }
};

Observation JwDetector::read()
{
    if (!impl_) impl_ = std::make_unique<Impl>();
    impl_->start();
    return impl_->read();
}

bool JwDetector::is_available() const
{
    if (!impl_) impl_ = std::make_unique<Impl>();
    return impl_->is_available();
}

// Configura il percorso dell'helper (chiamato da obs_helper_jw.cpp dopo load impostazioni)
void JwDetector::set_helper_path(const std::wstring &path)
{
    if (!impl_) impl_ = std::make_unique<Impl>();
    impl_->helper_path = path;
}

JwDetector::JwDetector() = default;
JwDetector::~JwDetector() = default;
