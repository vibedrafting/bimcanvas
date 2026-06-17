/**
 * 统一前端日志系统 —— framework-agnostic,任意 .ts/.vue 可直接 import。
 *
 * 设计目标(对齐 Server 日志,功能互补):
 *  - 单一输出口:全前端日志走此模块,不再散用 console.* / debugStore。
 *  - 结构化简洁:`[时间] [Web:域] 动作 key=val`,与 Server `[时间] [Server] msg` 视觉对齐。
 *  - 分级可控:error/warn/info/debug,运行时可切(localStorage / window.__bimlog),
 *    默认 DEV=info、PROD=warn —— 噪音(delta/心跳)降到 debug,默认不可见。
 *  - 互补边界:Web 记「浏览器视角的意图与感知」(用户操作 USER / 收发 STREAM·RECV / 渲染 RENDER / 系统 SYS),
 *    Server 记「服务端执行与状态」(几何/Git/落盘)。两端以 windowId + 时间戳对齐。
 *
 * 面板:reactive `logBuffer` 供 DebugConsole.vue 订阅(newest-first,环形上限 BUFFER_LIMIT)。
 */
import { ref } from 'vue';

export type LogLevel = 'error' | 'warn' | 'info' | 'debug';

/** 五个日志域(短名,渲染时拼成 `Web:STREAM`) */
export type LogDomain = 'USER' | 'STREAM' | 'RECV' | 'RENDER' | 'SYS';

export interface LogRecord {
    id: number;
    ts: number;            // epoch ms,用于排序/导出
    time: string;          // HH:mm:ss.mmm
    level: LogLevel;
    domain: LogDomain;
    msg: string;
    fields?: Record<string, unknown>;
}

const LEVEL_WEIGHT: Record<LogLevel, number> = { error: 0, warn: 1, info: 2, debug: 3 };
const BUFFER_LIMIT = 500;

const IS_DEV = typeof import.meta !== 'undefined' && (import.meta as any).env?.DEV === true;

/** 面板数据源:newest-first */
export const logBuffer = ref<LogRecord[]>([]);

let nextId = 1;
let currentLevel: LogLevel = resolveInitialLevel();

function resolveInitialLevel(): LogLevel {
    try {
        const fromStorage = localStorage.getItem('bimlog');
        if (fromStorage && fromStorage in LEVEL_WEIGHT) return fromStorage as LogLevel;
    } catch { /* SSR / 隐私模式无 localStorage,忽略 */ }
    return IS_DEV ? 'info' : 'warn';
}

export function getLevel(): LogLevel {
    return currentLevel;
}

/** 运行时切级别;持久化到 localStorage,刷新后保留 */
export function setLevel(level: LogLevel): void {
    if (!(level in LEVEL_WEIGHT)) return;
    currentLevel = level;
    try { localStorage.setItem('bimlog', level); } catch { /* 忽略 */ }
}

export function clearLogs(): void {
    logBuffer.value = [];
}

function fmtFields(fields?: Record<string, unknown>): string {
    if (!fields) return '';
    const parts: string[] = [];
    for (const [k, v] of Object.entries(fields)) {
        if (v === undefined) continue;
        let s: string;
        if (v instanceof Error) s = v.message || v.name;
        else if (v === null) s = 'null';
        else if (typeof v === 'object') {
            try { s = JSON.stringify(v); } catch { s = String(v); }
        } else {
            s = String(v);
        }
        if (s.length > 200) s = s.slice(0, 197) + '…';
        if (/\s/.test(s)) s = `"${s}"`;
        parts.push(`${k}=${s}`);
    }
    return parts.length ? ' ' + parts.join(' ') : '';
}

const DOMAIN_COLOR: Record<LogDomain, string> = {
    USER: '#a78bfa',   // 紫:用户操作
    STREAM: '#38bdf8', // 蓝:SSE 流收发
    RECV: '#34d399',   // 绿:SignalR 推送
    RENDER: '#f472b6', // 粉:渲染
    SYS: '#9ca3af',    // 灰:系统
};
const LEVEL_CONSOLE: Record<LogLevel, (...a: unknown[]) => void> = {
    error: (...a) => console.error(...a),
    warn: (...a) => console.warn(...a),
    info: (...a) => console.log(...a),
    debug: (...a) => console.debug(...a),
};

function emit(domain: LogDomain, level: LogLevel, msg: string, fields?: Record<string, unknown>): void {
    // 阈值门控:低于当前级别的直接丢弃(不进 buffer、不打 console),保证简洁
    if (LEVEL_WEIGHT[level] > LEVEL_WEIGHT[currentLevel]) return;

    const now = new Date();
    const time =
        now.toLocaleTimeString('en-US', { hour12: false }) +
        '.' + now.getMilliseconds().toString().padStart(3, '0');

    const fieldStr = fmtFields(fields);

    // console 镜像:带色前缀,消息体保持原色
    LEVEL_CONSOLE[level](
        `%c[${time}] [Web:${domain}]%c ${msg}${fieldStr}`,
        `color:${DOMAIN_COLOR[domain]};font-weight:bold`,
        'color:inherit',
    );

    logBuffer.value.unshift({
        id: nextId++,
        ts: now.getTime(),
        time,
        level,
        domain,
        msg,
        fields,
    });
    if (logBuffer.value.length > BUFFER_LIMIT) logBuffer.value.pop();
}

export interface ScopedLogger {
    error(msg: string, fields?: Record<string, unknown>): void;
    warn(msg: string, fields?: Record<string, unknown>): void;
    info(msg: string, fields?: Record<string, unknown>): void;
    debug(msg: string, fields?: Record<string, unknown>): void;
}

/**
 * 创建某个域的 scoped logger。每个文件按归属域建一个:
 *   const log = createLogger('STREAM');
 *   log.info('turn.completed', { win: 'main', dur: '12s', tokens: 4521 });
 */
export function createLogger(domain: LogDomain): ScopedLogger {
    return {
        error: (msg, fields) => emit(domain, 'error', msg, fields),
        warn: (msg, fields) => emit(domain, 'warn', msg, fields),
        info: (msg, fields) => emit(domain, 'info', msg, fields),
        debug: (msg, fields) => emit(domain, 'debug', msg, fields),
    };
}

// 全局调试钩子:F12 里 `__bimlog.setLevel('debug')` 即可放开全部日志
if (typeof window !== 'undefined') {
    (window as any).__bimlog = {
        get level() { return currentLevel; },
        setLevel,
        clear: clearLogs,
        get buffer() { return logBuffer.value; },
    };
}
