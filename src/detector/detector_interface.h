// detector_interface.h — contratto del detector (solo osservazione, non conosce OBS).
// Gate: nessuna implementazione concreta finché non è dimostrato che il segnale esiste.
#pragma once

#include <string>
#include <cstdint>

enum class MediaType { None, Video, Image, Unknown };
enum class Playback { Playing, Paused, Stopped, Unknown };
enum class Presentation { Active, Inactive, Unknown };

struct Observation {
    int sequence = 0;
    int64_t timestamp_ms = 0; // timestamp monotonic
    std::string session_id;   // identità sessione media (JW Library, se disponibile)
    std::string output_id;    // identità uscita/presentazione (device ID, non indice fisso)
    MediaType content_type = MediaType::None;
    Playback playback = Playback::Unknown;
    Presentation presentation = Presentation::Unknown;
    bool signal_valid = false;
    std::string reason;       // motivo della valutazione (es. "standby", "video su secondario")
};

// UNKNOWN non è STOPPED: non dedurre fine video dall'assenza di sessione.
class IDetector {
public:
    virtual ~IDetector() = default;
    // Lettura non bloccante. Ritorna l'osservazione corrente.
    virtual Observation read() = 0;
    // Vero se il detector è inizializzato e il segnale è affidabile.
    virtual bool is_available() const = 0;
};
