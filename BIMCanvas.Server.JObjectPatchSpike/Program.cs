// JObject patch spike — 组1 模板 §6 验收三 case 的控制台替代验证 (D1 决策)。
//
// 目的:验证 Newtonsoft.Json JObject 在"读 → 部分 mutate → 写"流程下,
// 未知字段的保留语义(BIMCanvas.Server.Services.ProjectDerivedBootstrapService
// 改造后 EnsureProjectJson 的核心库依赖)。
//
// 范围:本 spike 仅验证 JObject 库语义 + 字段顺序常数的等价复现,
// 不引用 BIMCanvas.Server,不替代 EnsureProjectJson 端到端单测
// (后者归组2 端点改造任务覆盖,主真理源 §5.1 V12a / V12b)。

using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.JObjectPatchSpike;

internal static class Program
{
    private static int Main()
    {
        var results = new List<(string Name, string? Error)>
        {
            ("case 1 顶层未知字段保留", RunSafely(Case1)),
            ("case 2 嵌套对象未知字段保留", RunSafely(Case2)),
            ("case 3 数组中未知元素保留", RunSafely(Case3)),
            ("case 4 BIMCanvas 拥有字段稳定顺序", RunSafely(Case4)),
        };

        Console.WriteLine();
        var pass = 0;
        var fail = 0;
        foreach (var (name, err) in results)
        {
            if (err is null)
            {
                Console.WriteLine($"[{name}] PASS");
                pass++;
            }
            else
            {
                Console.WriteLine($"[{name}] FAIL: {err}");
                fail++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"=== summary: {pass} PASS / {fail} FAIL ===");
        return fail == 0 ? 0 : 1;
    }

    private static string? RunSafely(Func<string?> testCase)
    {
        try
        {
            return testCase();
        }
        catch (Exception ex)
        {
            return $"exception: {ex.GetType().Name}: {ex.Message}";
        }
    }

    // -------------------------------------------------------------------
    // BIMCanvas 拥有字段稳定顺序 (与 ProjectDerivedBootstrapService.ReorderProjectRoot
    // 中 ownedOrder 数组同源;变更生产代码字段顺序时必须同步更新本数组,反之亦然)。
    // -------------------------------------------------------------------
    private static readonly string[] BimCanvasOwnedFieldOrder =
    {
        "id", "name", "version", "createdAt", "updatedAt",
        "coordinateSystem", "activeSchemeId", "schemes", "scenes",
    };

    private static JObject ReorderProjectRootEquivalent(JObject root)
    {
        var ordered = new JObject();
        foreach (var key in BimCanvasOwnedFieldOrder)
        {
            if (root.TryGetValue(key, out var token))
            {
                ordered[key] = token;
            }
        }

        foreach (var prop in root.Properties())
        {
            if (!ordered.ContainsKey(prop.Name))
            {
                ordered[prop.Name] = prop.Value;
            }
        }

        return ordered;
    }

    private static JObject ApplyMinimalPatch(JObject root)
    {
        // 触发"写入更新":只 mutate BIMCanvas 拥有的两个字段,
        // 其余字段(包括未知字段)应原样保留。
        // 用 DateTime token 与生产代码 ProjectDerivedBootstrapService.EnsureProjectJson
        // 中 JToken.FromObject(DateTime.Now, ...) 保持一致的写入形态。
        root["updatedAt"] = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Unspecified);
        root["activeSchemeId"] = "default";
        return ReorderProjectRootEquivalent(root);
    }

    private static JObject ParseAndPatch(string inputJson)
    {
        var root = JObject.Parse(inputJson);
        var patched = ApplyMinimalPatch(root);
        // round-trip 通过 ToString → JObject.Parse 模拟磁盘写回 + 重新读取
        var serialized = patched.ToString(Formatting.Indented);
        return JObject.Parse(serialized);
    }

    // -------------------------------------------------------------------
    // case 1:顶层 plugin 扩展字段必须在 patch round-trip 后原样保留
    // (主真理源 §2.4 卡点 F:第三方 plugin 写入的扩展字段不能被静默抹除)
    // -------------------------------------------------------------------
    private static string? Case1()
    {
        var input = """
        {
          "id": "proj_demo",
          "name": "demo",
          "version": "3.0",
          "createdAt": "2026-01-01T00:00:00",
          "coordinateSystem": "cartesian_mm_yUp",
          "schemes": [],
          "pluginExtensions": {
            "electrical-points-vendor": "acme",
            "license": "Apache-2.0"
          },
          "experimentalFlags": ["x", "y"]
        }
        """;

        var output = ParseAndPatch(input);

        if (output["pluginExtensions"] is not JObject pe)
        {
            return "顶层 pluginExtensions 丢失或类型错误";
        }

        if ((string?)pe["electrical-points-vendor"] != "acme")
        {
            return "pluginExtensions.electrical-points-vendor != 'acme'";
        }

        if ((string?)pe["license"] != "Apache-2.0")
        {
            return "pluginExtensions.license != 'Apache-2.0'";
        }

        if (output["experimentalFlags"] is not JArray flags || flags.Count != 2)
        {
            return "experimentalFlags 数组丢失或长度异常";
        }

        if ((string?)flags[0] != "x" || (string?)flags[1] != "y")
        {
            return "experimentalFlags 元素被改动";
        }

        // updatedAt 只用于确认 patch 已触发;不对 DateTime 序列化具体格式做严格断言
        // (round-trip 经 Newtonsoft DateParseHandling 默认行为后会自动转 Date token)。
        var updatedAt = (DateTime?)output["updatedAt"];
        if (updatedAt is null || updatedAt.Value.Year != 2026 || updatedAt.Value.Month != 5 || updatedAt.Value.Day != 17)
        {
            return $"updatedAt mutate 未生效或值错位:actual={updatedAt?.ToString("o", CultureInfo.InvariantCulture) ?? "<null>"}";
        }

        return null;
    }

    // -------------------------------------------------------------------
    // case 2:BIMCanvas 拥有字段内部嵌套的未知子字段也必须保留
    // 示例:metadata.vendorTrace、coordinateSystemExtensions.precision
    // (主真理源 §4.5:第三方扩展含"嵌套对象的未知字段")
    // -------------------------------------------------------------------
    private static string? Case2()
    {
        var input = """
        {
          "id": "proj_demo",
          "name": "demo",
          "version": "3.0",
          "createdAt": "2026-01-01T00:00:00",
          "coordinateSystem": "cartesian_mm_yUp",
          "schemes": [],
          "metadata": {
            "vendor": "acme",
            "trace": { "id": "abc-123", "stage": "alpha" }
          }
        }
        """;

        var output = ParseAndPatch(input);

        if (output["metadata"] is not JObject meta)
        {
            return "metadata 整体丢失";
        }

        if ((string?)meta["vendor"] != "acme")
        {
            return "metadata.vendor 丢失";
        }

        if (meta["trace"] is not JObject trace)
        {
            return "metadata.trace 子对象丢失";
        }

        if ((string?)trace["id"] != "abc-123" || (string?)trace["stage"] != "alpha")
        {
            return "metadata.trace 子字段被改动";
        }

        return null;
    }

    // -------------------------------------------------------------------
    // case 3:scenes 数组中,单个 scene 项内的未知扩展字段必须保留
    // (主真理源 §3.9 + §4.5:数组中未知元素 + 元素内未知字段)
    // -------------------------------------------------------------------
    private static string? Case3()
    {
        var input = """
        {
          "id": "proj_demo",
          "name": "demo",
          "version": "3.0",
          "createdAt": "2026-01-01T00:00:00",
          "coordinateSystem": "cartesian_mm_yUp",
          "schemes": [],
          "scenes": [
            {
              "sceneId": "interior-layout-1",
              "scene": "residential",
              "plugin": { "id": "indoor-layout", "versionRange": "^1.0.0" },
              "status": "active",
              "createdAt": "2026-02-01T00:00:00",
              "vendorPrivateField": "keep-me",
              "experimentalNested": { "k": 42 }
            },
            {
              "sceneId": "electrical-points-1",
              "scene": "electrical",
              "plugin": { "id": "electrical-points", "versionRange": "^1.0.0" },
              "status": "active",
              "createdAt": "2026-03-01T00:00:00"
            }
          ]
        }
        """;

        var output = ParseAndPatch(input);

        if (output["scenes"] is not JArray scenes || scenes.Count != 2)
        {
            return "scenes 数组丢失或长度异常";
        }

        if (scenes[0] is not JObject scene0)
        {
            return "scenes[0] 类型错误";
        }

        if ((string?)scene0["sceneId"] != "interior-layout-1")
        {
            return "scenes[0].sceneId 被改动";
        }

        if ((string?)scene0["vendorPrivateField"] != "keep-me")
        {
            return "scenes[0].vendorPrivateField 丢失 —— 数组元素内未知字段被抹除";
        }

        if (scene0["experimentalNested"] is not JObject nested || (int?)nested["k"] != 42)
        {
            return "scenes[0].experimentalNested 嵌套对象丢失";
        }

        if (scenes[1] is not JObject scene1 || (string?)scene1["sceneId"] != "electrical-points-1")
        {
            return "scenes[1] 元素本身丢失或被改动";
        }

        return null;
    }

    // -------------------------------------------------------------------
    // case 4:BIMCanvas 拥有字段在写回时按稳定顺序排在最前,未知字段相对顺序保留
    // (Reorder 实现正确性;字段顺序漂移会让 git diff 失真,影响多策略分支可读性)
    // -------------------------------------------------------------------
    private static string? Case4()
    {
        var input = """
        {
          "experimentalFlags": ["x"],
          "version": "3.0",
          "pluginExtensions": { "license": "Apache-2.0" },
          "id": "proj_demo",
          "name": "demo",
          "schemes": [],
          "coordinateSystem": "cartesian_mm_yUp",
          "vendor": "acme",
          "createdAt": "2026-01-01T00:00:00"
        }
        """;

        var output = ParseAndPatch(input);

        var keys = output.Properties().Select(p => p.Name).ToList();

        // 期望前缀:id / name / version / createdAt / updatedAt / coordinateSystem /
        //          activeSchemeId / schemes  (scenes 在 input 中缺失,自然跳过)
        string[] expectedPrefix =
        {
            "id", "name", "version", "createdAt", "updatedAt",
            "coordinateSystem", "activeSchemeId", "schemes",
        };

        for (var i = 0; i < expectedPrefix.Length; i++)
        {
            if (i >= keys.Count || keys[i] != expectedPrefix[i])
            {
                return $"字段顺序错位:第 {i} 位期望 '{expectedPrefix[i]}',实际 '{(i < keys.Count ? keys[i] : "<missing>")}'\n  完整顺序: [{string.Join(", ", keys)}]";
            }
        }

        // 期望未知字段在 BIMCanvas 字段之后,保留 input 相对顺序:
        //   input 顺序:experimentalFlags / pluginExtensions / vendor
        //   reorder 后位置:第 8/9/10 位 (index 从 0 起算,即 [8] [9] [10])
        var tail = keys.Skip(expectedPrefix.Length).ToList();
        string[] expectedTail = { "experimentalFlags", "pluginExtensions", "vendor" };

        if (tail.Count != expectedTail.Length ||
            !tail.SequenceEqual(expectedTail, StringComparer.Ordinal))
        {
            return $"未知字段尾部顺序错位:期望 [{string.Join(", ", expectedTail)}],实际 [{string.Join(", ", tail)}]";
        }

        return null;
    }
}
