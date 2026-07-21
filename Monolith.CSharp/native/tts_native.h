#ifndef TTS_NATIVE_H
#define TTS_NATIVE_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_MSC_VER)
#define TTS_API __declspec(dllexport)
#else
#define TTS_API __attribute__((visibility("default")))
#endif

typedef void* TtsHandle;

typedef struct {
    const char* model_path;
    int sample_rate;
    int channels;
    int opus_bitrate;
    int max_concurrency;
} TtsConfig;

typedef void (*OnOpusPacket)(const uint8_t* data, int len, void* user);
typedef void (*OnLog)(const char* msg, void* user);

TTS_API TtsHandle tts_create(const TtsConfig* cfg, OnOpusPacket packet_cb, OnLog log_cb, void* user);

TTS_API int tts_speak_async(TtsHandle h, const char* text, const char* style, int utterance_id);

TTS_API int tts_stop(TtsHandle h, int utterance_id);

TTS_API char* tts_get_metrics(TtsHandle h);

TTS_API void tts_free_string(char* s);

TTS_API void tts_destroy(TtsHandle h);

#ifdef __cplusplus
}
#endif

#endif /* TTS_NATIVE_H */
