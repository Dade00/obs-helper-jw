// detector_impl.h — implementazione WinRT reale del detector JW Library.
// Esegue WinRT in worker isolato, mai nel thread UI di OBS.
// Gate: chiuso (segnale WinRT verificato funzionante da C#).
#pragma once

#include "detector_interface.h"
#include <memory>
#include <string>

class JwDetector : public IDetector {
public:
    JwDetector();
    ~JwDetector();

    Observation read() override;
    bool is_available() const override;

    // Configura il percorso dell'helper (chiamato dopo load impostazioni)
    void set_helper_path(const std::wstring &path);

private:
    struct Impl;
    mutable std::unique_ptr<Impl> impl_;
};
