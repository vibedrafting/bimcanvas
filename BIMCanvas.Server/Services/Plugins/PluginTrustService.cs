using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Server.Models.Plugins;

namespace BIMCanvas.Server.Services.Plugins;

/// <summary>
/// plugins-state.json 唯一允许读写的 Service (主真理源 v1.1 §3.13 / R2 / §8.2)。
/// <para>
/// 安全决策:trust 元数据存平台外 (<c>BIMCANVAS_HOME/plugins-state.json</c>),
/// plugin 代码完全不可触达;任何 plugin 内的 <c>.bimcanvas/install.json</c> 都被忽略。
/// </para>
/// <para>
/// 并发安全:全局 <see cref="SemaphoreSlim"/> 单写锁,所有 mutate 操作必须串行;
/// 写入用 .tmp + Move 原子替换。
/// </para>
/// </summary>
public sealed class PluginTrustService
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }, // DTO 上各 enum 已带自身 converter,此为兜底
    };

    /// <summary>
    /// 读取单个 plugin 状态。pluginId 不存在返回 null。
    /// </summary>
    public async Task<PluginInstallState?> GetStateAsync(string pluginId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return null;
        var all = await GetAllStatesAsync(ct);
        return all.TryGetValue(pluginId, out var s) ? s : null;
    }

    /// <summary>
    /// 全量读取 plugins-state.json (主真理源 §3.13)。
    /// 返回值字典的 value 的 <see cref="PluginInstallState.PluginId"/> 字段已用 key 回填。
    /// 文件不存在 / 空 → 返回空字典 (不抛异常)。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, PluginInstallState>> GetAllStatesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await ReadInternalAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// install-time 写入新 plugin 状态。强制 <see cref="TrustState.Untrusted"/> (R1 / R9)。
    /// 若 plugin 已存在 (重装),保留 trustedAt = null 并刷新其他字段。
    /// </summary>
    public async Task MarkInstalledAsync(PluginInstallState state, CancellationToken ct = default)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (state.TrustState != TrustState.Untrusted)
            throw new InvalidOperationException(
                $"MarkInstalledAsync 只允许 trustState=Untrusted 写入,传入 {state.TrustState}。" +
                "首次 trust 必须走 MarkTrustedAsync。");

        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadInternalAsync(ct);
            var dict = all.ToDictionary(kv => kv.Key, kv => kv.Value);
            dict[state.PluginId] = state;
            await WriteInternalAsync(dict, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// trust-time 把 trustState 翻转为 <see cref="TrustState.Trusted"/> + 设 trustedAt (V13 T6c)。
    /// 必须 plugin 已 Installed (Untrusted),否则抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    public async Task MarkTrustedAsync(string pluginId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("pluginId 非空", nameof(pluginId));

        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadInternalAsync(ct);
            var dict = all.ToDictionary(kv => kv.Key, kv => kv.Value);
            if (!dict.TryGetValue(pluginId, out var current))
                throw new InvalidOperationException($"plugin '{pluginId}' 不存在,无法 trust");
            dict[pluginId] = current with
            {
                TrustState = TrustState.Trusted,
                TrustedAt = DateTimeOffset.Now,
            };
            await WriteInternalAsync(dict, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// uninstall 时移除 plugin 状态记录;pluginId 不存在等同 no-op (幂等)。
    /// </summary>
    public async Task RemoveStateAsync(string pluginId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return;
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadInternalAsync(ct);
            if (!all.ContainsKey(pluginId)) return;
            var dict = all.ToDictionary(kv => kv.Key, kv => kv.Value);
            dict.Remove(pluginId);
            await WriteInternalAsync(dict, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ─── 内部:必须在持有 _gate 时调用 ───

    private static async Task<IReadOnlyDictionary<string, PluginInstallState>> ReadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(PluginPaths.PluginsStateFile))
            return new Dictionary<string, PluginInstallState>();

        try
        {
            var json = await File.ReadAllTextAsync(PluginPaths.PluginsStateFile, ct);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, PluginInstallState>();

            var raw = JsonSerializer.Deserialize<Dictionary<string, PluginInstallState>>(json, JsonOptions)
                ?? new Dictionary<string, PluginInstallState>();

            // PluginId 字段标 JsonIgnore,反序列化后为 null,从 dict key 回填
            var result = new Dictionary<string, PluginInstallState>(raw.Count);
            foreach (var kv in raw)
            {
                result[kv.Key] = kv.Value with { PluginId = kv.Key };
            }
            return result;
        }
        catch (Exception)
        {
            // plugins-state.json 损坏 → 视作空 (避免单文件损坏导致整个 plugin 系统不可用)
            // 真实环境损坏应有运维介入,这里采用容错策略让用户能继续重装
            return new Dictionary<string, PluginInstallState>();
        }
    }

    private static async Task WriteInternalAsync(IReadOnlyDictionary<string, PluginInstallState> dict, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PluginPaths.PluginsStateFile)!);
        var tmp = PluginPaths.PluginsStateFile + ".tmp";
        var json = JsonSerializer.Serialize(dict, JsonOptions);
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, PluginPaths.PluginsStateFile, overwrite: true);
    }
}
