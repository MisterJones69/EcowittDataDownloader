using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EcowittGui.Models;

/// <summary>User-assigned sensor names per channel, taken from download_rest/live_data.json.</summary>
public sealed class ChannelNames
{
    /// <summary>WH31 temperature/humidity sensors (live_data "ch_aisle"; CSV columns "CHn ...").</summary>
    public Dictionary<int, string> Wh31 { get; } = [];
    /// <summary>WN34 temperature probes (live_data "ch_temp"; CSV columns "WN34 CHn").</summary>
    public Dictionary<int, string> Wn34 { get; } = [];

    public static ChannelNames Load(string restDir)
    {
        var names = new ChannelNames();
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(restDir, "live_data.json")));
            Fill(root?["ch_aisle"], names.Wh31);
            Fill(root?["ch_temp"], names.Wn34);
        }
        catch (Exception)
        {
            // names are optional decoration
        }
        return names;
    }

    private static void Fill(JsonNode? list, Dictionary<int, string> target)
    {
        if (list is not JsonArray array)
        {
            return;
        }
        foreach (var item in array.OfType<JsonObject>())
        {
            var name = item["name"]?.ToString();
            if (int.TryParse(item["channel"]?.ToString(), out var ch) && !string.IsNullOrWhiteSpace(name))
            {
                target[ch] = name.Trim();
            }
        }
    }
}

/// <summary>A titled block of content shown for a REST file.</summary>
public abstract record RestSection(string Title);

public sealed record KeyValueRow(string Label, string Key, string Value);

/// <summary>Name/value list (a JSON object or an id/val list).</summary>
public sealed record KeyValueSection(string Title, IReadOnlyList<KeyValueRow> Rows) : RestSection(Title);

/// <summary>Table (a JSON array of objects).</summary>
public sealed record TableSection(string Title, IReadOnlyList<TableColumn> Columns, IReadOnlyList<string[]> Rows)
    : RestSection(Title);

/// <summary>Column of a <see cref="TableSection"/> or a CSV table: header text and index into each row array.</summary>
public sealed record TableColumn(string Header, int Index);

/// <summary>Known REST files (by file name without .json) with a German title and description.</summary>
public static class RestFileTypes
{
    public static readonly IReadOnlyDictionary<string, (string Title, string Description)> Known =
        new Dictionary<string, (string, string)>
        {
            ["live_data"] = ("Live-Daten", "Aktuelle Messwerte aller Sensoren (get_livedata_info)"),
            ["sensor_info"] = ("Sensoren", "Alle Sensortypen mit ID, Batterie und Signal (get_sensors_info)"),
            ["sensor_filter"] = ("Sensorfilter", "Gefilterte Sensorliste"),
            ["rain_totals"] = ("Regensummen", "Regensummen und Regenmesser-Einstellungen (get_rain_totals)"),
            ["calibration_data"] = ("Kalibrierung", "Offsets und Faktoren (get_calibration_data)"),
            ["device_info"] = ("Geräteinfo", "Zeitzone, Firmware, Access Point (get_device_info)"),
            ["network_info"] = ("Netzwerk", "LAN/WLAN-Konfiguration (get_network_info)"),
            ["ws_settings"] = ("Wetterdienste", "Upload zu Ecowitt, WU, WOW, MQTT ... (get_ws_settings)"),
            ["version"] = ("Version", "Firmware-Version und SD-Status (get_version)"),
        };
}

/// <summary>Turns a downloaded REST JSON file into display sections.</summary>
public static partial class RestFormatter
{
    [GeneratedRegex(@"(pwd|password|passwd|_key$|^key$)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretRegex();

    public static bool IsSecret(string key) => SecretRegex().IsMatch(key);

    /// <summary>Replaces non-empty values of secret keys (passwords, API keys) in place.</summary>
    public static void MaskSecrets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (IsSecret(key) && obj[key] is JsonValue v && v.ToString().Length > 0)
                    {
                        obj[key] = "••••••••";
                    }
                    else
                    {
                        MaskSecrets(obj[key]);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    MaskSecrets(item);
                }
                break;
        }
    }

    /// <summary>Gateway ids used in live_data.json (common_list, rain, piezoRain).</summary>
    private static readonly Dictionary<string, string> LiveIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0x01"] = "Innentemperatur",
        ["0x02"] = "Außentemperatur",
        ["0x03"] = "Taupunkt",
        ["0x04"] = "Windchill",
        ["0x05"] = "Hitzeindex",
        ["0x06"] = "Innenfeuchte",
        ["0x07"] = "Außenfeuchte",
        ["0x08"] = "Luftdruck absolut",
        ["0x09"] = "Luftdruck relativ",
        ["0x0A"] = "Windrichtung",
        ["0x0B"] = "Windgeschwindigkeit",
        ["0x0C"] = "Böe",
        ["0x0D"] = "Regen (Ereignis)",
        ["0x0E"] = "Regenrate",
        ["0x0F"] = "Regen (Stunde)",
        ["0x10"] = "Regen (Tag)",
        ["0x11"] = "Regen (Woche)",
        ["0x12"] = "Regen (Monat)",
        ["0x13"] = "Regen (Jahr)",
        ["0x14"] = "Regen (gesamt)",
        ["0x15"] = "Sonnenstrahlung",
        ["0x16"] = "UV",
        ["0x17"] = "UV-Index",
        ["0x19"] = "Max. Tagesböe",
        ["0x6D"] = "Windrichtung (10-min-Mittel)",
        ["0x7C"] = "Regen (24 h)",
        ["3"] = "Gefühlte Temperatur",
        ["5"] = "VPD (Dampfdruckdefizit)",
    };

    /// <summary>German labels for keys of the object-type REST files.</summary>
    private static readonly Dictionary<string, string> KeyLabels = new(StringComparer.Ordinal)
    {
        // version
        ["version"] = "Firmware",
        ["newVersion"] = "Neue Version verfügbar",
        ["platform"] = "Plattform",
        ["all_sensors"] = "Alle Sensoren",
        ["sensorid_page"] = "Sensorseiten",
        ["sd_status"] = "SD-Status",
        // device_info
        ["sensorType"] = "Sensortyp",
        ["rf_freq"] = "Funkfrequenz",
        ["AFC"] = "AFC",
        ["ntp_server"] = "NTP-Server",
        ["tz_auto"] = "Zeitzone automatisch",
        ["tz_name"] = "Zeitzone",
        ["tz_index"] = "Zeitzonen-Index",
        ["dst_stat"] = "Sommerzeit",
        ["radcompensation"] = "Strahlungskompensation",
        ["date"] = "Datum/Zeit",
        ["upgrade"] = "Auto-Update",
        ["apAuto"] = "Access Point automatisch",
        ["curr_msg"] = "Versionshinweise",
        ["apName"] = "Access-Point-Name",
        ["APpwd"] = "Access-Point-Passwort",
        ["time"] = "Zeit",
        // network_info
        ["mac"] = "MAC-Adresse",
        ["ethIpType"] = "LAN IP-Typ",
        ["ethIP"] = "LAN IP",
        ["ethMask"] = "LAN Netzmaske",
        ["ethGateway"] = "LAN Gateway",
        ["ethDNS"] = "LAN DNS",
        ["ssid"] = "WLAN SSID",
        ["wifi_pwd"] = "WLAN-Passwort",
        ["wifi_DNS"] = "WLAN DNS",
        ["staIpType"] = "WLAN IP-Typ",
        ["wifi_ip"] = "WLAN IP",
        ["wifi_mask"] = "WLAN Netzmaske",
        ["wifi_gateway"] = "WLAN Gateway",
        // rain_totals
        ["rainFallPriority"] = "Bevorzugter Regenmesser",
        ["rainDay"] = "Regen Tag (mm)",
        ["rainWeek"] = "Regen Woche (mm)",
        ["rainMonth"] = "Regen Monat (mm)",
        ["rainYear"] = "Regen Jahr (mm)",
        ["rainTotal"] = "Regen gesamt (mm)",
        ["rainGain"] = "Regen-Faktor",
        ["rstRainDay"] = "Reset Tag (Stunde)",
        ["rstRainWeek"] = "Reset Woche (Wochentag)",
        ["rstRainYear"] = "Reset Jahr (Monat)",
        // calibration_data
        ["SolarRadWave"] = "Solar-Umrechnung (Lux→W/m²)",
        ["solarRadGain"] = "Solar-Faktor",
        ["uvGain"] = "UV-Faktor",
        ["windGain"] = "Wind-Faktor",
        ["inTempOffset"] = "Offset Innentemperatur",
        ["inHumiOffset"] = "Offset Innenfeuchte",
        ["absOffset"] = "Offset Luftdruck absolut",
        ["altitude"] = "Höhe (m)",
        ["outTempOffset"] = "Offset Außentemperatur",
        ["outHumiOffset"] = "Offset Außenfeuchte",
        ["bgtOffset"] = "Offset BGT",
        ["windDirOffset"] = "Offset Windrichtung",
        // live_data wh25 / channels
        ["intemp"] = "Innentemperatur",
        ["inhumi"] = "Innenfeuchte",
        ["abs"] = "Luftdruck absolut",
        ["rel"] = "Luftdruck relativ",
        ["channel"] = "Kanal",
        ["name"] = "Name",
        ["temp"] = "Temperatur",
        ["humidity"] = "Feuchte",
        ["battery"] = "Batterie",
        ["voltage"] = "Spannung (V)",
        ["unit"] = "Einheit",
        // sensor_info
        ["img"] = "Modell",
        ["type"] = "Typ",
        ["id"] = "ID",
        ["batt"] = "Batterie",
        ["rssi"] = "RSSI (dBm)",
        ["signal"] = "Signal (0–4)",
        ["idst"] = "Aktiv",
    };

    /// <summary>German titles for the sections of live_data.json.</summary>
    private static readonly Dictionary<string, string> LiveSections = new(StringComparer.Ordinal)
    {
        ["common_list"] = "Außen",
        ["rain"] = "Regen",
        ["piezoRain"] = "Regen (Piezo)",
        ["wh25"] = "Innen (WH25)",
        ["ch_aisle"] = "Kanal-Sensoren (WH31)",
        ["ch_temp"] = "Temperaturfühler (WN34)",
        ["ch_soil"] = "Bodenfeuchte",
        ["ch_leak"] = "Leckage",
        ["ch_pm25"] = "Feinstaub",
        ["ch_lds"] = "Abstand (LDS)",
        ["lightning"] = "Gewitter",
        ["co2"] = "Luftqualität (CO₂)",
        ["debug"] = "Gateway (Debug)",
    };

    public static string Label(string key) => KeyLabels.GetValueOrDefault(key, key);

    /// <summary>
    /// Builds the sections for a file. Scalars of an object become one key/value section,
    /// arrays of objects become tables, id/val lists become key/value sections.
    /// </summary>
    /// <param name="onlyRegistered">sensor lists: hide sensors with id FFFFFFFF/FFFFFFFE (not paired).</param>
    public static List<RestSection> Format(JsonNode? root, bool onlyRegistered)
    {
        var sections = new List<RestSection>();
        switch (root)
        {
            case JsonArray array:
                sections.Add(Table("Einträge", array, onlyRegistered));
                break;
            case JsonObject obj:
                var scalars = new List<KeyValueRow>();
                foreach (var (key, value) in obj)
                {
                    var title = LiveSections.GetValueOrDefault(key, Label(key));
                    switch (value)
                    {
                        case JsonArray list when list.All(i => i is JsonObject o && o.ContainsKey("id") && o.ContainsKey("val")):
                            sections.Add(new KeyValueSection(title, list.OfType<JsonObject>().Select(IdValueRow).ToList()));
                            break;
                        case JsonArray list when list.Count == 1 && list[0] is JsonObject single:
                            sections.Add(new KeyValueSection(title, ObjectRows(single)));
                            break;
                        case JsonArray list when list.All(i => i is JsonObject):
                            sections.Add(Table(title, list, onlyRegistered));
                            break;
                        case JsonObject child:
                            sections.Add(new KeyValueSection(title, ObjectRows(child)));
                            break;
                        default:
                            scalars.Add(new KeyValueRow(Label(key), key, ScalarText(value)));
                            break;
                    }
                }
                if (scalars.Count > 0)
                {
                    sections.Insert(0, new KeyValueSection("Allgemein", scalars));
                }
                break;
            default:
                sections.Add(new KeyValueSection("Wert", [new KeyValueRow("Wert", "", root?.ToJsonString() ?? "null")]));
                break;
        }
        return sections;
    }

    private static KeyValueRow IdValueRow(JsonObject item)
    {
        var id = item["id"]?.ToString() ?? "";
        var value = ScalarText(item["val"]);
        var unit = item["unit"]?.ToString();
        if (!string.IsNullOrEmpty(unit))
        {
            value += " " + (unit == "C" ? "°C" : unit == "F" ? "°F" : unit);
        }
        // any further fields (e.g. "battery") are appended
        var extra = item.Where(kv => kv.Key is not ("id" or "val" or "unit"))
            .Select(kv => $"{Label(kv.Key)}: {ScalarText(kv.Value)}").ToList();
        if (extra.Count > 0)
        {
            value += "   (" + string.Join(", ", extra) + ")";
        }
        var label = LiveIds.TryGetValue(id, out var name) ? name : $"ID {id}";
        return new KeyValueRow(label, id, value);
    }

    private static List<KeyValueRow> ObjectRows(JsonObject obj)
    {
        var rows = new List<KeyValueRow>();
        string? unit = obj["unit"]?.ToString();
        foreach (var (key, value) in obj)
        {
            if (key == "unit" && unit is "C" or "F")
            {
                continue;
            }
            var text = ScalarText(value);
            if (key.Contains("temp", StringComparison.OrdinalIgnoreCase) && unit is "C" or "F")
            {
                text += unit == "C" ? " °C" : " °F";
            }
            rows.Add(new KeyValueRow(Label(key), key, text));
        }
        return rows;
    }

    private static TableSection Table(string title, JsonArray array, bool onlyRegistered)
    {
        var keys = new List<string>();
        foreach (var item in array.OfType<JsonObject>())
        {
            foreach (var (key, _) in item)
            {
                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }
        var rows = new List<string[]>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var id = item["id"]?.ToString();
            if (onlyRegistered && id is "FFFFFFFF" or "FFFFFFFE")
            {
                continue;
            }
            rows.Add(keys.Select(k => ScalarText(item[k])).ToArray());
        }
        var columns = keys.Select((k, i) => new TableColumn(Label(k), i)).ToList();
        return new TableSection(title, columns, rows);
    }

    private static string ScalarText(JsonNode? node)
    {
        return node switch
        {
            null => "",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            JsonValue v when v.GetValueKind() == JsonValueKind.True => "ja",
            JsonValue v when v.GetValueKind() == JsonValueKind.False => "nein",
            JsonValue v => v.ToJsonString(),
            _ => node.ToJsonString(),
        };
    }
}
