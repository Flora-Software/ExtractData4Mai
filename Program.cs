using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;

var inputRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Directory.GetCurrentDirectory();

var outputRoot = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Directory.GetCurrentDirectory();

var dataRoots = DiscoverDataRoots(inputRoot);
if (dataRoots.Count == 0)
{
    Console.Error.WriteLine($"未在 {inputRoot} 下发现可识别的资源数据目录。");
    Console.Error.WriteLine("请在 StreamingAssets 目录下运行，或把 StreamingAssets 路径作为第一个参数传入。");
    return 1;
}

var exporters = new[]
{
    new ExportDefinition("partner", "PartnerSort.xml", "partner", "partner", false),
    new ExportDefinition("plate", "PlateSort.xml", "plate", "plate", false),
    new ExportDefinition("title", "TitleSort.xml", "title", "titles", true),
    new ExportDefinition("chara", "CharaSort.xml", "chara", "chara", false),
    new ExportDefinition("frame", "FrameSort.xml", "frame", "frame", false),
    new ExportDefinition("icon", "IconSort.xml", "icon", "icon", false),
    new ExportDefinition("loginBonus", "LoginBonusSort.xml", "loginbouns", "loginbouns", false),
};

var collectionRoot = ResolveRootForCategories(dataRoots, exporters.Select(x => x.CategoryDirectory));

Directory.CreateDirectory(outputRoot);

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

foreach (var definition in exporters)
{
    var categoryRoot = Path.Combine(collectionRoot, definition.CategoryDirectory);
    if (!Directory.Exists(categoryRoot))
    {
        Console.WriteLine($"跳过 {definition.OutputFileName}: 未找到目录 {categoryRoot}");
        continue;
    }

    var itemsById = LoadItems(categoryRoot, definition.IncludeTitleType);
    var orderedItems = OrderItems(itemsById);

    var outputPath = Path.Combine(outputRoot, $"{definition.OutputFileName}.json");
    object payload = definition.JsonPropertyName == "titles"
        ? new { titles = orderedItems.Select(item => new { item.id, item.name, item.type }) }
        : definition.JsonPropertyName == "loginbouns"
            ? new { loginbouns = orderedItems.Select(item => new { item.id, item.name }) }
            : CreateSimplePayload(definition.JsonPropertyName, orderedItems);

    await File.WriteAllTextAsync(
        outputPath,
        JsonSerializer.Serialize(payload, jsonOptions),
        new System.Text.UTF8Encoding(false));

    Console.WriteLine($"已生成: {outputPath}");
}

var newMusicPath = Path.Combine(outputRoot, "NewMusic.json");
var newMusicItems = LoadLockedMusic(inputRoot);
await File.WriteAllTextAsync(
    newMusicPath,
    JsonSerializer.Serialize(newMusicItems, jsonOptions),
    new System.Text.UTF8Encoding(false));
Console.WriteLine($"已生成: {newMusicPath}");

var mapsPath = Path.Combine(outputRoot, "maps_data.json");
var maps = LoadMaps(dataRoots);
await File.WriteAllTextAsync(
    mapsPath,
    JsonSerializer.Serialize(maps, jsonOptions),
    new System.Text.UTF8Encoding(false));
Console.WriteLine($"已生成: {mapsPath}");

return 0;

static object CreateSimplePayload(string propertyName, IReadOnlyList<ExportItem> items)
{
    var simpleItems = items.Select(item => new { item.id, item.name }).ToArray();

    return propertyName switch
    {
        "partner" => new { partner = simpleItems },
        "plate" => new { plate = simpleItems },
        "chara" => new { chara = simpleItems },
        "frame" => new { frame = simpleItems },
        "icon" => new { icon = simpleItems },
        _ => throw new InvalidOperationException($"不支持的 JSON 属性名: {propertyName}")
    };
}

static Dictionary<int, ExportItem> LoadItems(string categoryRoot, bool includeTitleType)
{
    var result = new Dictionary<int, ExportItem>();

    foreach (var itemDirectory in Directory.GetDirectories(categoryRoot))
    {
        var xmlPath = Directory
            .GetFiles(itemDirectory, "*.xml", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (xmlPath is null)
        {
            continue;
        }

        var document = XDocument.Load(xmlPath);
        var root = document.Root;
        if (root is null)
        {
            continue;
        }

        var id = ExtractId(root, itemDirectory);
        var name = root.Element("name")?.Element("str")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        var type = includeTitleType
            ? root.Element("genre")?.Element("id")?.Value?.Trim() ?? string.Empty
            : null;

        result[id] = new ExportItem(id.ToString("D6"), name, type, id);
    }

    return result;
}

static List<ExportItem> OrderItems(IReadOnlyDictionary<int, ExportItem> itemsById)
{
    return itemsById.Values
        .OrderBy(item => item.SortId)
        .ToList();
}

static int ExtractId(XElement root, string itemDirectory)
{
    var dataName = root.Element("dataName")?.Value?.Trim();
    if (!string.IsNullOrEmpty(dataName))
    {
        var digits = new string(dataName.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var parsedFromDataName))
        {
            return parsedFromDataName;
        }
    }

    var directoryName = Path.GetFileName(itemDirectory);
    var fallbackDigits = new string(directoryName.Where(char.IsDigit).ToArray());
    if (int.TryParse(fallbackDigits, out var parsedFromDirectory))
    {
        return parsedFromDirectory;
    }

    throw new InvalidOperationException($"无法解析条目 ID: {itemDirectory}");
}

static List<string> DiscoverDataRoots(string inputRoot)
{
    var candidates = new List<string>();

    foreach (var directory in Directory.GetDirectories(inputRoot))
    {
        var name = Path.GetFileName(directory);
        if (string.Equals(name, "Table", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (Directory.GetDirectories(directory).Length > 0 || File.Exists(Path.Combine(directory, "DataConfig.xml")))
        {
            candidates.Add(directory);
        }
    }

    return candidates
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static string ResolveRootForCategories(IEnumerable<string> dataRoots, IEnumerable<string> categoryDirectories)
{
    var categories = categoryDirectories.ToArray();

    var ranked = dataRoots
        .Select(root => new
        {
            Root = root,
            Score = categories.Count(category => Directory.Exists(Path.Combine(root, category)))
        })
        .OrderByDescending(item => item.Score)
        .ThenBy(item => item.Root, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();

    if (ranked is null || ranked.Score == 0)
    {
        throw new InvalidOperationException("未找到包含基础采集目录的数据根目录。");
    }

    return ranked.Root;
}

static List<Dictionary<string, string>> LoadLockedMusic(string inputRoot)
{
    var result = new List<Dictionary<string, string>>();

    foreach (var dataRoot in DiscoverDataRoots(inputRoot))
    {
        var musicRoot = Path.Combine(dataRoot, "music");
        if (!Directory.Exists(musicRoot))
        {
            continue;
        }

        foreach (var musicDirectory in Directory.GetDirectories(musicRoot, "music*").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var musicXmlPath = Path.Combine(musicDirectory, "Music.xml");
            if (!File.Exists(musicXmlPath))
            {
                continue;
            }

            var document = XDocument.Load(musicXmlPath);
            var root = document.Root;
            if (root is null)
            {
                continue;
            }

            var lockType = root.Element("lockType")?.Value?.Trim();
            if (lockType is not ("1" or "2" or "3" or "4"))
            {
                continue;
            }

            var musicId = root.Element("name")?.Element("id")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(musicId))
            {
                continue;
            }

            result.Add(new Dictionary<string, string>
            {
                ["id"] = musicId
            });
        }
    }

    return result;
}

static List<MapDataDto> LoadMaps(IEnumerable<string> dataRoots)
{
    var result = new List<MapDataDto>();
    foreach (var dataRoot in dataRoots)
    {
        var mapRoot = Path.Combine(dataRoot, "map");
        var mapTreasureRoot = Path.Combine(dataRoot, "mapTreasure");

        if (!Directory.Exists(mapRoot) || !Directory.Exists(mapTreasureRoot))
        {
            continue;
        }

        foreach (var mapDirectory in Directory.GetDirectories(mapRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var mapXmlPath = Path.Combine(mapDirectory, "Map.xml");
            if (!File.Exists(mapXmlPath))
            {
                continue;
            }

            var document = XDocument.Load(mapXmlPath);
            var root = document.Root;
            if (root is null)
            {
                continue;
            }

            var mapName = ParseIdStr(root.Element("name"));
            var maxDistance = 0;
            var treasures = new List<object>();

            var treasureExDatas = root.Element("TreasureExDatas")?.Elements("MapTreasureExData") ?? Enumerable.Empty<XElement>();
            foreach (var treasureExData in treasureExDatas)
            {
                var distance = ParseInt(treasureExData.Element("Distance")?.Value);
                var flag = treasureExData.Element("Flag")?.Value?.Trim() ?? string.Empty;
                if (flag == "End")
                {
                    maxDistance = distance;
                }

                var treasureId = ParseIdStr(treasureExData.Element("TreasureId"));
                if (treasureId is null)
                {
                    continue;
                }

                var treasureDetail = LoadMapTreasureDetail(treasureId.id, mapTreasureRoot);
                if (treasureDetail is null)
                {
                    continue;
                }

                var treasureInfo = new Dictionary<string, object?>
                {
                    ["treasureId"] = treasureId,
                    ["distance"] = distance
                };

                foreach (var pair in treasureDetail)
                {
                    treasureInfo[pair.Key] = pair.Value;
                }

                treasures.Add(treasureInfo);
            }

            result.Add(new MapDataDto(
                mapName?.id ?? ExtractId(root, mapDirectory),
                mapName,
                maxDistance,
                treasures));
        }
    }

    return result.OrderBy(item => item.mapId).ToList();
}

static Dictionary<string, object?>? LoadMapTreasureDetail(int treasureId, string mapTreasureRoot)
{
    var treasureXmlPath = Path.Combine(
        mapTreasureRoot,
        $"MapTreasure{treasureId:D6}",
        "MapTreasure.xml");

    if (!File.Exists(treasureXmlPath))
    {
        return null;
    }

    var document = XDocument.Load(treasureXmlPath);
    var root = document.Root;
    if (root is null)
    {
        return null;
    }

    var treasureType = root.Element("TreasureType")?.Value?.Trim();
    if (string.IsNullOrWhiteSpace(treasureType))
    {
        return null;
    }

    var result = new Dictionary<string, object?>
    {
        ["treasureType"] = treasureType
    };

    switch (treasureType)
    {
        case "Character":
            result["characterId"] = ParseIdStr(root.Element("CharacterId"));
            break;
        case "MusicNew":
        case "MapTaskMusic":
            result["musicId"] = ParseIdStr(root.Element("MusicId"));
            break;
        case "NamePlate":
            result["namePlateId"] = ParseIdStr(root.Element("NamePlate"));
            break;
        case "Frame":
            result["frameId"] = ParseIdStr(root.Element("Frame"));
            break;
        case "Title":
            result["titleId"] = ParseIdStr(root.Element("Title"));
            break;
        case "Icon":
            result["iconId"] = ParseIdStr(root.Element("Icon"));
            break;
        case "Challenge":
            result["challengeId"] = ParseIdStr(root.Element("Challenge"));
            break;
        case "Present":
            result["presentType"] = "Present";
            break;
        default:
            result["unknownType"] = true;
            AddIfPresent(result, "characteridId", ParseIdStr(root.Element("CharacterId")));
            AddIfPresent(result, "musicidId", ParseIdStr(root.Element("MusicId")));
            AddIfPresent(result, "nameplateId", ParseIdStr(root.Element("NamePlate")));
            AddIfPresent(result, "frameId", ParseIdStr(root.Element("Frame")));
            AddIfPresent(result, "titleId", ParseIdStr(root.Element("Title")));
            AddIfPresent(result, "iconId", ParseIdStr(root.Element("Icon")));
            AddIfPresent(result, "challengeId", ParseIdStr(root.Element("Challenge")));
            AddIfPresent(result, "gateId", ParseIdStr(root.Element("Gate")));
            AddIfPresent(result, "keyId", ParseIdStr(root.Element("Key")));
            break;
    }

    return result;
}

static void AddIfPresent(IDictionary<string, object?> target, string key, IdStrDto? value)
{
    if (value is not null)
    {
        target[key] = value;
    }
}

static IdStrDto? ParseIdStr(XElement? element)
{
    if (element is null)
    {
        return null;
    }

    var idText = element.Element("id")?.Value?.Trim();
    var strText = element.Element("str")?.Value;

    if (!int.TryParse(idText, out var id))
    {
        return null;
    }

    return new IdStrDto(id, strText ?? string.Empty);
}

static int ParseInt(string? value)
{
    return int.TryParse(value?.Trim(), out var parsed) ? parsed : 0;
}

internal sealed record IdStrDto(int id, string str);

internal sealed record MapDataDto(
    int mapId,
    IdStrDto? mapName,
    int distance,
    List<object> treasures);

internal sealed record ExportDefinition(
    string CategoryDirectory,
    string SortFileName,
    string OutputFileName,
    string JsonPropertyName,
    bool IncludeTitleType);

internal sealed record ExportItem(
    string id,
    string name,
    string? type,
    int SortId);
