<script setup lang="ts">
/**
 * RateLimitBanner — 全局限流徽章(WP-Web)。
 *
 * 消费 systemStore.rateLimitState(由 useChatStream 在收到后端 runtime.rate_limit
 * event 时更新),按 status 显示:
 *   - allowed_warning → 黄条(amber)警告:正在逼近限流
 *   - rejected         → 红条(danger)阻断:已被限流
 *   - allowed / null    → 不渲染(平时不打扰)
 *
 * 挂载于聊天输入框 antigravity-input-box 内部最顶部(AICommandCenter)。
 * 风格沿用 Aurora Glass:沿用 amber #fbbf24 / danger #f87171 既有 token。
 */
import { computed } from 'vue';
import { storeToRefs } from 'pinia';
import { useSystemStore } from '../../stores/systemStore';

const systemStore = useSystemStore();
const { rateLimitState } = storeToRefs(systemStore);

const variant = computed<'warning' | 'rejected' | null>(() => {
  const state = rateLimitState.value;
  if (!state) return null;
  if (state.status === 'rejected') return 'rejected';
  if (state.status === 'allowed_warning') return 'warning';
  return null;
});

const utilizationPercent = computed<string | null>(() => {
  const u = rateLimitState.value?.utilization;
  if (typeof u !== 'number' || !isFinite(u)) return null;
  return `${Math.round(u * 100)}%`;
});

const resetsAtText = computed<string | null>(() => {
  const resetsAt = rateLimitState.value?.resetsAt;
  if (typeof resetsAt !== 'number' || !isFinite(resetsAt)) return null;
  const nowSec = Math.floor(Date.now() / 1000);
  const delta = resetsAt - nowSec;
  if (delta <= 0) return '即将恢复';
  if (delta < 60) return `${delta} 秒后恢复`;
  if (delta < 3600) return `${Math.ceil(delta / 60)} 分钟后恢复`;
  if (delta < 86400) return `${Math.ceil(delta / 3600)} 小时后恢复`;
  return `${Math.ceil(delta / 86400)} 天后恢复`;
});

const headlineText = computed<string>(() => {
  if (variant.value === 'rejected') return '请求被限流，暂时无法继续';
  return '请求即将触发限流';
});

const rateLimitTypeText = computed<string | null>(() => {
  const t = rateLimitState.value?.rateLimitType;
  if (!t) return null;
  // 后端 SDK 给的是 five_hour / seven_day 之类,展示为可读中文
  switch (t) {
    case 'five_hour': return '5 小时';
    case 'seven_day': return '7 天';
    case 'seven_day_opus': return '7 天 (Opus)';
    case 'seven_day_sonnet': return '7 天 (Sonnet)';
    case 'overage': return '增量';
    default: return t;
  }
});
</script>

<template>
  <div
    v-if="variant"
    class="rate-limit-banner"
    :class="variant"
    role="status"
    :aria-live="variant === 'rejected' ? 'assertive' : 'polite'"
  >
    <svg class="banner-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <circle cx="12" cy="12" r="10"></circle>
      <line x1="12" y1="8" x2="12" y2="12"></line>
      <line x1="12" y1="16" x2="12.01" y2="16"></line>
    </svg>
    <div class="banner-content">
      <span class="banner-headline">{{ headlineText }}</span>
      <span class="banner-detail" v-if="utilizationPercent || resetsAtText || rateLimitTypeText">
        <template v-if="rateLimitTypeText">{{ rateLimitTypeText }}</template>
        <template v-if="rateLimitTypeText && utilizationPercent"> · </template>
        <template v-if="utilizationPercent">用量 {{ utilizationPercent }}</template>
        <template v-if="(rateLimitTypeText || utilizationPercent) && resetsAtText"> · </template>
        <template v-if="resetsAtText">{{ resetsAtText }}</template>
      </span>
    </div>
  </div>
</template>

<style scoped lang="scss">
.rate-limit-banner {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 0 0 8px;
  padding: 8px 12px;
  border-radius: 8px;
  font-size: 0.78rem;
  font-family: 'Inter', system-ui, sans-serif;
  transition: opacity 0.2s ease;

  &.warning {
    background: rgba(251, 191, 36, 0.08);
    border: 1px solid rgba(251, 191, 36, 0.3);
    color: rgba(251, 191, 36, 0.95);

    .banner-icon { color: rgba(251, 191, 36, 0.9); }
  }

  &.rejected {
    background: rgba(248, 113, 113, 0.1);
    border: 1px solid rgba(248, 113, 113, 0.4);
    color: rgba(248, 113, 113, 0.95);

    .banner-icon { color: rgba(248, 113, 113, 0.9); }
  }
}

.banner-icon {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}

.banner-content {
  display: flex;
  flex-direction: column;
  gap: 1px;
  flex: 1;
  min-width: 0;
}

.banner-headline {
  font-weight: 500;
  white-space: nowrap;
}

.banner-detail {
  font-size: 0.7rem;
  opacity: 0.75;
  font-family: var(--font-mono);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
