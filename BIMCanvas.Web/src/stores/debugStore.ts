import { defineStore } from 'pinia';
import { ref } from 'vue';

export type LogType = 'info' | 'warn' | 'error' | 'success';

export interface LogEntry {
    id: number;
    timestamp: string;
    type: LogType;
    message: string;
}

export const useDebugStore = defineStore('debug', () => {
    const isVisible = ref(false);
    const logs = ref<LogEntry[]>([]);
    let nextId = 1;

    const toggle = () => {
        isVisible.value = !isVisible.value;
    };

    const addLog = (type: LogType, message: string) => {
        const now = new Date();
        const timeString = now.toLocaleTimeString('en-US', { hour12: false }) + '.' + now.getMilliseconds().toString().padStart(3, '0');

        // Mirror to browser console
        const consoleMsg = `[${type.toUpperCase()}] ${message}`;
        switch (type) {
            case 'error':
                console.error(consoleMsg);
                break;
            case 'warn':
                console.warn(consoleMsg);
                break;
            case 'success':
                console.log(`%c${consoleMsg}`, 'color: #4ade80');
                break;
            default:
                console.log(consoleMsg);
        }

        logs.value.unshift({
            id: nextId++,
            timestamp: timeString,
            type,
            message
        });

        // Limit logs to 100 entries to prevent memory issues
        if (logs.value.length > 100) {
            logs.value.pop();
        }
    };

    const clear = () => {
        logs.value = [];
    };

    // Helper methods for quick logging
    const log = (message: string) => addLog('info', message);
    const warn = (message: string) => addLog('warn', message);
    const error = (message: string) => addLog('error', message);
    const success = (message: string) => addLog('success', message);

    return {
        isVisible,
        logs,
        toggle,
        addLog,
        clear,
        log,
        warn,
        error,
        success
    };
});
