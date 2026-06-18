/**
 * 日志面板的 UI 状态(可见态 + 过滤),数据源来自 utils/logger 的 reactive buffer。
 * 取代旧 debugStore:debugStore 既存日志逻辑又混 UI 状态,且 Pinia 绑定让服务层难调用;
 * 现日志逻辑全部下沉 logger.ts(framework-agnostic),本 store 只剩纯 UI 状态。
 */
import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import {
    logBuffer,
    clearLogs,
    getLevel,
    setLevel,
    type LogLevel,
    type LogDomain,
} from '../utils/logger';

const ALL_DOMAINS: LogDomain[] = ['USER', 'STREAM', 'RECV', 'RENDER', 'SYS'];

export const useLogStore = defineStore('log', () => {
    const isVisible = ref(false);
    /** 域过滤:空集合 = 全部显示 */
    const hiddenDomains = ref<Set<LogDomain>>(new Set());

    const toggle = () => { isVisible.value = !isVisible.value; };

    const toggleDomain = (domain: LogDomain) => {
        const next = new Set(hiddenDomains.value);
        next.has(domain) ? next.delete(domain) : next.add(domain);
        hiddenDomains.value = next;
    };

    const visibleLogs = computed(() =>
        hiddenDomains.value.size === 0
            ? logBuffer.value
            : logBuffer.value.filter(l => !hiddenDomains.value.has(l.domain)),
    );

    /** 当前阈值级别(运行时可切),驱动级别按钮高亮 */
    const level = ref<LogLevel>(getLevel());
    const changeLevel = (next: LogLevel) => {
        setLevel(next);
        level.value = next;
    };

    return {
        isVisible,
        hiddenDomains,
        visibleLogs,
        level,
        domains: ALL_DOMAINS,
        toggle,
        toggleDomain,
        changeLevel,
        clear: clearLogs,
    };
});
