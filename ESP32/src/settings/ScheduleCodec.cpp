#include "settings/ScheduleCodec.h"

#include <cmath>

#include "cJSON.h"
#include "config/Config.h"
#include "esp_log.h"
#include "settings/ScheduleEvaluator.h"
#include "settings/SettingsCodec.h"
#include "util/CivilTime.h"

static const char* TAG = "ScheduleCodec";

const char* const ScheduleCodec::kVersionKey     = "sched_v";
const char* const ScheduleCodec::kEnabledKey     = "enabled";
const char* const ScheduleCodec::kFallbackKey    = "fallback";
const char* const ScheduleCodec::kProfilesKey    = "profiles";
const char* const ScheduleCodec::kRulesKey       = "rules";
const char* const ScheduleCodec::kOverrideKey    = "override";
const char* const ScheduleCodec::kSlotKey        = "slot";
const char* const ScheduleCodec::kNameKey        = "name";
const char* const ScheduleCodec::kDaysKey        = "days";
const char* const ScheduleCodec::kStartMinuteKey = "start_m";
const char* const ScheduleCodec::kDurationKey    = "dur_m";
const char* const ScheduleCodec::kPriorityKey    = "prio";
const char* const ScheduleCodec::kOrdinalKey     = "ord";
const char* const ScheduleCodec::kUntilKey       = "until";

namespace {

  // Slots are dense from zero and bounded by the profile cap, so the highest
  // legal one is one less than it. Kept here rather than in ScheduleBundle
  // because it is a property of the wire format, not of the container.
  constexpr int kMinSlot = 0;
  const int     kMaxSlot = static_cast<int>(config::kMaxScheduleProfiles) - 1;

  // Longest profile name we will store, matching ScheduleRules.MaxProfileNameLength.
  // A longer one is truncated rather than rejected: the name is only ever printed
  // to the serial log, so losing its tail is not worth failing a whole schedule.
  constexpr std::size_t kMaxNameLength = 40;

}  // namespace

std::string ScheduleCodec::encode(const ScheduleBundle& bundle) {
  cJSON* root = cJSON_CreateObject();
  if (root == nullptr) {
    ESP_LOGE(TAG, "encode: out of memory");
    return std::string();
  }

  cJSON_AddNumberToObject(root, kVersionKey,
                          static_cast<double>(bundle.version()));
  cJSON_AddBoolToObject(root, kEnabledKey, bundle.enabled());
  cJSON_AddNumberToObject(root, kFallbackKey,
                          static_cast<double>(bundle.fallbackSlot()));

  cJSON* profiles = cJSON_AddArrayToObject(root, kProfilesKey);
  if (profiles != nullptr) {
    for (const ScheduleBundle::Profile& profile : bundle.profiles()) {
      cJSON* item = cJSON_CreateObject();
      if (item == nullptr) {
        continue;
      }
      cJSON_AddNumberToObject(item, kSlotKey, static_cast<double>(profile.slot));
      cJSON_AddStringToObject(item, kNameKey, profile.name.c_str());
      // The one encoder for the seven values, shared with the config document.
      SettingsCodec::encodeInto(item, profile.values, /*includeVersion=*/false);
      cJSON_AddItemToArray(profiles, item);
    }
  }

  cJSON* rules = cJSON_AddArrayToObject(root, kRulesKey);
  if (rules != nullptr) {
    for (const ScheduleBundle::Rule& rule : bundle.rules()) {
      cJSON* item = cJSON_CreateObject();
      if (item == nullptr) {
        continue;
      }
      cJSON_AddNumberToObject(item, kSlotKey,
                              static_cast<double>(rule.profileSlot));
      cJSON_AddNumberToObject(item, kDaysKey,
                              static_cast<double>(rule.daysMask));
      cJSON_AddNumberToObject(item, kStartMinuteKey,
                              static_cast<double>(rule.startMinute));
      cJSON_AddNumberToObject(item, kDurationKey,
                              static_cast<double>(rule.durationMinutes));
      cJSON_AddNumberToObject(item, kPriorityKey,
                              static_cast<double>(rule.priority));
      cJSON_AddNumberToObject(item, kOrdinalKey,
                              static_cast<double>(rule.ordinal));
      cJSON_AddItemToArray(rules, item);
    }
  }

  // Absent, not null, when there is none - the decoder tests for the key.
  if (bundle.hasOverride()) {
    cJSON* item = cJSON_CreateObject();
    if (item != nullptr) {
      cJSON_AddStringToObject(
          item, kUntilKey,
          CivilTime::formatIso(bundle.overrideUntilEpoch()).c_str());
      SettingsCodec::encodeInto(item, bundle.overrideValues(),
                                /*includeVersion=*/false);
      cJSON_AddItemToObject(root, kOverrideKey, item);
    }
  }

  std::string out;
  char*       printed = cJSON_PrintUnformatted(root);
  if (printed != nullptr) {
    out = printed;
    cJSON_free(printed);
  } else {
    ESP_LOGE(TAG, "encode: serialisation failed");
  }
  cJSON_Delete(root);
  return out;
}

bool ScheduleCodec::decode(const char* json, std::size_t length,
                           ScheduleBundle& bundle) {
  if (json == nullptr || length == 0) {
    return false;
  }

  // ParseWithLength, not Parse: an MQTT payload is a length-delimited byte range
  // and is not guaranteed to be NUL-terminated.
  cJSON* root = cJSON_ParseWithLength(json, length);
  if (root == nullptr) {
    ESP_LOGW(TAG, "decode: payload is not valid JSON");
    return false;
  }
  if (!cJSON_IsObject(root)) {
    ESP_LOGW(TAG, "decode: payload is not a JSON object");
    cJSON_Delete(root);
    return false;
  }

  // Built to one side and only swapped in at the very end. Unlike a config
  // document, a bundle must never be applied in part: a rule set missing its
  // last entry is not an incomplete schedule, it is a different one, and the
  // device would follow it with total confidence.
  ScheduleBundle decoded;
  bool           ok = true;

  int version = 0;
  if (!readBoundedInt(root, kVersionKey, 1, INT32_MAX, version)) {
    ESP_LOGW(TAG, "decode: missing or unusable '%s'", kVersionKey);
    ok = false;
  } else {
    decoded.setVersion(static_cast<uint32_t>(version));
  }

  if (ok) {
    const cJSON* enabled = cJSON_GetObjectItemCaseSensitive(root, kEnabledKey);
    if (!cJSON_IsBool(enabled)) {
      ESP_LOGW(TAG, "decode: missing or unusable '%s'", kEnabledKey);
      ok = false;
    } else {
      decoded.setEnabled(cJSON_IsTrue(enabled));
    }
  }

  if (ok) {
    // ScheduleBundle::kNoSlot (-1) is the legal "no fallback" value, hence the
    // lower bound: the API sends the sentinel rather than a JSON null so this
    // stays one integer read.
    int fallback = ScheduleBundle::kNoSlot;
    if (!readBoundedInt(root, kFallbackKey, ScheduleBundle::kNoSlot, kMaxSlot,
                        fallback)) {
      ESP_LOGW(TAG, "decode: missing or unusable '%s'", kFallbackKey);
      ok = false;
    } else {
      decoded.setFallbackSlot(fallback);
    }
  }

  if (ok) {
    const cJSON* profiles = cJSON_GetObjectItemCaseSensitive(root, kProfilesKey);
    if (!cJSON_IsArray(profiles)) {
      ESP_LOGW(TAG, "decode: '%s' is missing or not an array", kProfilesKey);
      ok = false;
    } else if (static_cast<uint32_t>(cJSON_GetArraySize(profiles)) >
               config::kMaxScheduleProfiles) {
      ESP_LOGW(TAG, "decode: %d profiles exceeds the cap of %u",
               cJSON_GetArraySize(profiles),
               (unsigned)config::kMaxScheduleProfiles);
      ok = false;
    } else {
      const cJSON* item = nullptr;
      cJSON_ArrayForEach(item, profiles) {
        ScheduleBundle::Profile profile;
        if (!decodeProfile(item, profile)) {
          ok = false;
          break;
        }
        decoded.addProfile(profile);
      }
    }
  }

  if (ok) {
    const cJSON* rules = cJSON_GetObjectItemCaseSensitive(root, kRulesKey);
    if (!cJSON_IsArray(rules)) {
      ESP_LOGW(TAG, "decode: '%s' is missing or not an array", kRulesKey);
      ok = false;
    } else if (static_cast<uint32_t>(cJSON_GetArraySize(rules)) >
               config::kMaxScheduleRules) {
      ESP_LOGW(TAG, "decode: %d rules exceeds the cap of %u",
               cJSON_GetArraySize(rules), (unsigned)config::kMaxScheduleRules);
      ok = false;
    } else {
      const cJSON* item = nullptr;
      cJSON_ArrayForEach(item, rules) {
        ScheduleBundle::Rule rule;
        if (!decodeRule(item, rule)) {
          ok = false;
          break;
        }
        decoded.addRule(rule);
      }
    }
  }

  if (ok) {
    bool applied = false;
    if (!decodeOverride(root, decoded, applied)) {
      ok = false;
    }
  }

  cJSON_Delete(root);

  if (!ok) {
    return false;
  }

  decoded.markValid();
  bundle = decoded;
  return true;
}

bool ScheduleCodec::readBoundedInt(const cJSON* object, const char* key,
                                   int min, int max, int& out) {
  const cJSON* item = cJSON_GetObjectItemCaseSensitive(object, key);
  if (!cJSON_IsNumber(item) || !std::isfinite(item->valuedouble)) {
    return false;
  }

  const double value = item->valuedouble;
  if (value < static_cast<double>(min) || value > static_cast<double>(max)) {
    return false;
  }

  out = static_cast<int>(value);
  return true;
}

bool ScheduleCodec::decodeProfile(const cJSON*             item,
                                  ScheduleBundle::Profile& out) {
  if (!cJSON_IsObject(item)) {
    ESP_LOGW(TAG, "decode: a profile entry is not an object");
    return false;
  }

  int slot = 0;
  if (!readBoundedInt(item, kSlotKey, kMinSlot, kMaxSlot, slot)) {
    ESP_LOGW(TAG, "decode: a profile has no usable '%s'", kSlotKey);
    return false;
  }
  out.slot = slot;

  // The name is decoration for the log line. Absent or oversized is not worth
  // failing a schedule over, unlike every other field here.
  const cJSON* name = cJSON_GetObjectItemCaseSensitive(item, kNameKey);
  if (cJSON_IsString(name) && name->valuestring != nullptr) {
    out.name.assign(name->valuestring);
    if (out.name.size() > kMaxNameLength) {
      out.name.resize(kMaxNameLength);
    }
  }

  // Seeded with the compile-time defaults, so a profile that omits a key gets
  // the same answer a config document omitting it would - and then clamped by
  // the same bounds, because it is literally the same decoder.
  DeviceSettings values;
  if (!SettingsCodec::decodeObject(item, values)) {
    ESP_LOGW(TAG, "decode: profile in slot %d carries no usable settings", slot);
    return false;
  }
  values.clampToLimits();
  out.values = values;
  return true;
}

bool ScheduleCodec::decodeRule(const cJSON* item, ScheduleBundle::Rule& out) {
  if (!cJSON_IsObject(item)) {
    ESP_LOGW(TAG, "decode: a rule entry is not an object");
    return false;
  }

  int slot        = 0;
  int daysMask    = 0;
  int startMinute = 0;
  int duration    = 0;
  int priority    = 0;
  int ordinal     = 0;

  // A rule is all-or-nothing in a way a profile is not: every one of these
  // decides when a window opens, and a default for any of them would be a window
  // somebody did not ask for.
  if (!readBoundedInt(item, kSlotKey, kMinSlot, kMaxSlot, slot) ||
      !readBoundedInt(item, kDaysKey, 1, 127, daysMask) ||
      !readBoundedInt(item, kStartMinuteKey, 0,
                      ScheduleEvaluator::kMinutesPerDay - 1, startMinute) ||
      !readBoundedInt(item, kDurationKey, 1, ScheduleEvaluator::kMinutesPerDay,
                      duration) ||
      !readBoundedInt(item, kPriorityKey, 0, 1000, priority) ||
      !readBoundedInt(item, kOrdinalKey, 0, INT16_MAX, ordinal)) {
    ESP_LOGW(TAG, "decode: a rule entry is incomplete or out of range");
    return false;
  }

  out.profileSlot     = slot;
  out.daysMask        = static_cast<uint8_t>(daysMask);
  out.startMinute     = static_cast<uint16_t>(startMinute);
  out.durationMinutes = static_cast<uint16_t>(duration);
  out.priority        = static_cast<uint16_t>(priority);
  out.ordinal         = static_cast<uint16_t>(ordinal);
  return true;
}

bool ScheduleCodec::decodeOverride(const cJSON* root, ScheduleBundle& bundle,
                                   bool& applied) {
  applied = false;

  const cJSON* item = cJSON_GetObjectItemCaseSensitive(root, kOverrideKey);
  if (item == nullptr) {
    return true;  // no override is the normal case, not a problem
  }
  if (!cJSON_IsObject(item)) {
    ESP_LOGW(TAG, "decode: '%s' is present but not an object", kOverrideKey);
    return false;
  }

  const cJSON* until = cJSON_GetObjectItemCaseSensitive(item, kUntilKey);
  if (!cJSON_IsString(until) || until->valuestring == nullptr) {
    ESP_LOGW(TAG, "decode: override has no '%s'", kUntilKey);
    return false;
  }

  int64_t untilEpoch = 0;
  if (!CivilTime::parseIso(std::string(until->valuestring), untilEpoch)) {
    // The server formats this explicitly for us; anything else is a contract
    // break, not a variant to be lenient about.
    ESP_LOGW(TAG, "decode: override '%s' is not YYYY-MM-DDTHH:MM:SSZ", kUntilKey);
    return false;
  }

  DeviceSettings values;
  if (!SettingsCodec::decodeObject(item, values)) {
    ESP_LOGW(TAG, "decode: override carries no usable settings");
    return false;
  }
  values.clampToLimits();

  bundle.setOverride(untilEpoch, values);
  applied = true;
  return true;
}
