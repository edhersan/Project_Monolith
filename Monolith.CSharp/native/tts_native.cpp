#include "tts_native.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include <sstream>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

// ---------- internal engine ----------

struct Utterance {
    int id;
    std::atomic<bool> cancel{false};
    std::thread worker;
};

struct Engine {
    TtsConfig config;
    OnOpusPacket packet_cb;
    OnLog log_cb;
    void* user;

    std::mutex mtx;
    std::vector<std::unique_ptr<Utterance>> utterances;
    std::atomic<int> total_utterances{0};
    std::atomic<long long> total_samples_generated{0};

    Engine(const TtsConfig& cfg, OnOpusPacket pcb, OnLog lcb, void* u)
        : config(cfg), packet_cb(pcb), log_cb(lcb), user(u) {}

    void log(const char* msg) {
        if (log_cb) log_cb(msg, user);
    }
};

static Engine* engine_from_handle(TtsHandle h) {
    return static_cast<Engine*>(h);
}

// ---------- sine wave synthesis ----------

static int generate_sine_pcm(int16_t* buffer, int num_samples, double freq, double sample_rate, double& phase) {
    for (int i = 0; i < num_samples; ++i) {
        double val = sin(2.0 * M_PI * freq * phase / sample_rate);
        buffer[i] = static_cast<int16_t>(val * 0.3 * 32767.0);
        phase += 1.0;
        if (phase >= sample_rate) phase -= sample_rate;
    }
    return num_samples;
}

static void synthesis_thread(Engine* eng, std::string text, std::string style, int utterance_id) {
    int sample_rate = eng->config.sample_rate ? eng->config.sample_rate : 48000;
    int channels = eng->config.channels ? eng->config.channels : 1;
    int frame_ms = 20;
    int frame_samples = sample_rate * frame_ms / 1000;
    size_t text_len = text.length();

    // Duration: ~80ms per character, at least 500ms, at most 8000ms
    int duration_ms = std::max(500, std::min(8000, static_cast<int>(text_len) * 80));
    int total_frames = duration_ms / frame_ms;

    std::vector<int16_t> pcm(frame_samples * channels);
    double phase = 0.0;
    double freq = 220.0 + (static_cast<double>(utterance_id % 10) * 20.0);

    eng->log(("synthesis start: utterance=" + std::to_string(utterance_id) +
              " text_len=" + std::to_string(text_len) +
              " frames=" + std::to_string(total_frames)).c_str());

    for (int f = 0; f < total_frames; ++f) {
        // check cancellation
        {
            std::lock_guard<std::mutex> lock(eng->mtx);
            for (auto& u : eng->utterances) {
                if (u && u->id == utterance_id && u->cancel.load()) {
                    eng->log(("synthesis cancelled: utterance=" + std::to_string(utterance_id)).c_str());
                    return;
                }
            }
        }

        generate_sine_pcm(pcm.data(), frame_samples, freq, static_cast<double>(sample_rate), phase);

        // For mono, just use the generated pcm; for stereo, duplicate
        if (channels > 1) {
            std::vector<int16_t> stereo(frame_samples * channels);
            for (int s = 0; s < frame_samples; ++s) {
                for (int c = 0; c < channels; ++c) {
                    stereo[s * channels + c] = pcm[s];
                }
            }
            if (eng->packet_cb) {
                eng->packet_cb(reinterpret_cast<const uint8_t*>(stereo.data()),
                               static_cast<int>(stereo.size() * sizeof(int16_t)),
                               eng->user);
            }
        } else {
            if (eng->packet_cb) {
                eng->packet_cb(reinterpret_cast<const uint8_t*>(pcm.data()),
                               static_cast<int>(frame_samples * sizeof(int16_t)),
                               eng->user);
            }
        }

        eng->total_samples_generated.fetch_add(frame_samples);

        // simulate real-time: sleep for frame duration
        std::this_thread::sleep_for(std::chrono::milliseconds(frame_ms));
    }

    // EOS marker: callback with len=0
    if (eng->packet_cb) {
        eng->packet_cb(nullptr, 0, eng->user);
    }

    eng->log(("synthesis done: utterance=" + std::to_string(utterance_id)).c_str());
}

// ---------- API implementation ----------

TTS_API TtsHandle tts_create(const TtsConfig* cfg, OnOpusPacket packet_cb, OnLog log_cb, void* user) {
    auto* eng = new Engine(*cfg, packet_cb, log_cb, user);

    if (eng->log_cb) {
        std::string msg = "tts_create: sr=" + std::to_string(cfg->sample_rate) +
                          " ch=" + std::to_string(cfg->channels) +
                          " bitrate=" + std::to_string(cfg->opus_bitrate);
        eng->log_cb(msg.c_str(), user);
    }

    return static_cast<TtsHandle>(eng);
}

TTS_API int tts_speak_async(TtsHandle h, const char* text, const char* style, int utterance_id) {
    if (!h || !text) return -1;
    auto* eng = engine_from_handle(h);

    auto utt = std::make_unique<Utterance>();
    utt->id = utterance_id;
    utt->cancel.store(false);
    utt->worker = std::thread(synthesis_thread, eng, std::string(text),
                              style ? std::string(style) : std::string(), utterance_id);

    std::lock_guard<std::mutex> lock(eng->mtx);
    eng->utterances.push_back(std::move(utt));
    eng->total_utterances.fetch_add(1);
    return 0;
}

TTS_API int tts_stop(TtsHandle h, int utterance_id) {
    if (!h) return -1;
    auto* eng = engine_from_handle(h);

    std::lock_guard<std::mutex> lock(eng->mtx);
    for (auto& u : eng->utterances) {
        if (u && u->id == utterance_id) {
            u->cancel.store(true);
            return 0;
        }
    }
    return -1;
}

TTS_API char* tts_get_metrics(TtsHandle h) {
    if (!h) return nullptr;
    auto* eng = engine_from_handle(h);

    std::ostringstream json;
    json << "{"
         << "\"total_utterances\":" << eng->total_utterances.load() << ","
         << "\"total_samples_generated\":" << eng->total_samples_generated.load() << ","
         << "\"sample_rate\":" << eng->config.sample_rate << ","
         << "\"channels\":" << eng->config.channels
         << "}";

    std::string s = json.str();
    char* result = static_cast<char*>(std::malloc(s.size() + 1));
    if (result) {
        std::memcpy(result, s.c_str(), s.size() + 1);
    }
    return result;
}

TTS_API void tts_free_string(char* s) {
    std::free(s);
}

TTS_API void tts_destroy(TtsHandle h) {
    if (!h) return;
    auto* eng = engine_from_handle(h);

    // Cancel all and join threads
    {
        std::lock_guard<std::mutex> lock(eng->mtx);
        for (auto& u : eng->utterances) {
            if (u) u->cancel.store(true);
        }
    }

    {
        std::lock_guard<std::mutex> lock(eng->mtx);
        for (auto& u : eng->utterances) {
            if (u && u->worker.joinable()) {
                u->worker.join();
            }
        }
        eng->utterances.clear();
    }

    delete eng;
}
