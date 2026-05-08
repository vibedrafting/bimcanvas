<!--
  ModuleSizeEditor — limits-aware single-dimension editor.

  根据 morphology mode 渲染不同形态：
    - readonly  → 灰显数字
    - range     → 数字框 + 连续 slider
    - enum      → chips toggle

  共享给 PlacementSizeBar（布置前）和 PropertyPanel（布置后）。
  外部负责 clamp（用 utils/moduleSize.clampDimension）；本组件只发 raw 输入事件。
-->

<script setup lang="ts">
import { computed } from 'vue';
import type { DimensionMode } from '../../../utils/moduleSize';

interface Props {
  label: string;       // 'Width' / 'Depth' / '宽度' 等
  value: number;
  mode: DimensionMode;
  /** 是否使用紧凑布局（PropertyPanel 用 true，PlacementSizeBar 用 false） */
  compact?: boolean;
}

const props = withDefaults(defineProps<Props>(), { compact: false });

const emit = defineEmits<{
  /** 连续型变更（每次 input 事件）— 给"实时预览"消费者（PlacementSizeBar） */
  (e: 'update:value', next: number): void;
  /** 提交型变更（数字框 change/blur、slider release、chip 点击）— 给"会触发持久化的"消费者（PropertyPanel） */
  (e: 'commit', next: number): void;
}>();

const isReadonly = computed(() => props.mode.mode === 'readonly');

const rangeMode = computed(() =>
  props.mode.mode === 'range' ? props.mode : null
);

const enumMode = computed(() =>
  props.mode.mode === 'enum' ? props.mode : null
);

const hint = computed(() => {
  if (props.mode.mode === 'readonly') return 'fixed';
  if (props.mode.mode === 'range') return `${props.mode.min}–${props.mode.max} mm`;
  return props.mode.values.map(v => `${v}`).join(' / ') + ' mm';
});

const parseInputValue = (event: Event): number | null => {
  const raw = (event.target as HTMLInputElement).value;
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : null;
};

const onNumberInput = (event: Event) => {
  const v = parseInputValue(event);
  if (v !== null) emit('update:value', v);
};

const onNumberCommit = (event: Event) => {
  const v = parseInputValue(event);
  if (v !== null) {
    emit('update:value', v);
    emit('commit', v);
  }
};

const onSliderInput = (event: Event) => {
  const v = parseInputValue(event);
  if (v !== null) emit('update:value', v);
};

const onSliderCommit = (event: Event) => {
  const v = parseInputValue(event);
  if (v !== null) {
    emit('update:value', v);
    emit('commit', v);
  }
};

const onChipClick = (chipValue: number) => {
  emit('update:value', chipValue);
  emit('commit', chipValue);
};
</script>

<template>
  <div class="size-editor" :class="{ compact, readonly: isReadonly }">
    <span class="label">{{ label }}</span>

    <!-- readonly：仅显示数字 + (fixed) 标记 -->
    <template v-if="isReadonly">
      <span class="value-readonly">{{ Math.round(value) }} mm</span>
      <span class="hint">({{ hint }})</span>
    </template>

    <!-- range：数字框 + slider -->
    <template v-else-if="rangeMode">
      <input
        class="value-input"
        type="number"
        :value="Math.round(value)"
        :min="rangeMode.min"
        :max="rangeMode.max"
        step="10"
        @input="onNumberInput"
        @change="onNumberCommit"
        @blur="onNumberCommit"
      />
      <span class="unit">mm</span>
      <input
        class="slider"
        type="range"
        :value="value"
        :min="rangeMode.min"
        :max="rangeMode.max"
        step="10"
        @input="onSliderInput"
        @change="onSliderCommit"
      />
      <span class="hint">{{ hint }}</span>
    </template>

    <!-- enum：chips -->
    <template v-else-if="enumMode">
      <div class="chips">
        <button
          v-for="opt in enumMode.values"
          :key="opt"
          class="chip"
          :class="{ active: Math.abs(value - opt) < 0.5 }"
          @click="onChipClick(opt)"
        >{{ opt }}</button>
      </div>
      <span class="unit">mm</span>
    </template>
  </div>
</template>

<style scoped>
.size-editor {
  display: grid;
  grid-template-columns: 60px 80px 24px 1fr auto;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: #d8d8e0;
  margin-bottom: 6px;
}

.size-editor.compact {
  grid-template-columns: 50px 1fr auto;
  font-size: 12px;
}

.size-editor.readonly {
  grid-template-columns: 60px 1fr auto;
  color: #9a9aa3;
}

.size-editor.compact.readonly {
  grid-template-columns: 50px 1fr auto;
}

.label {
  color: #9a9aa3;
  font-weight: 500;
  letter-spacing: 0.02em;
}

.value-readonly {
  font-family: var(--font-mono);
  color: #d8d8e0;
}

.value-input {
  width: 100%;
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  padding: 4px 8px;
  color: #fff;
  font-family: var(--font-mono);
  font-size: 13px;
  outline: none;
  text-align: right;
}

.value-input:focus {
  border-color: rgba(0, 170, 255, 0.65);
  background: rgba(0, 170, 255, 0.08);
}

.unit {
  color: #6a6a72;
  font-size: 11px;
}

.slider {
  width: 100%;
  -webkit-appearance: none;
  appearance: none;
  height: 4px;
  background: rgba(255, 255, 255, 0.12);
  border-radius: 2px;
  outline: none;
  cursor: pointer;
}

.slider::-webkit-slider-thumb {
  -webkit-appearance: none;
  appearance: none;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  background: rgba(0, 170, 255, 0.9);
  cursor: pointer;
  border: 2px solid #fff;
}

.slider::-moz-range-thumb {
  width: 14px;
  height: 14px;
  border-radius: 50%;
  background: rgba(0, 170, 255, 0.9);
  cursor: pointer;
  border: 2px solid #fff;
}

.hint {
  font-size: 11px;
  color: #6a6a72;
  white-space: nowrap;
}

.chips {
  display: flex;
  gap: 6px;
}

.chip {
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.12);
  color: #d8d8e0;
  padding: 4px 12px;
  border-radius: 8px;
  font-family: var(--font-mono);
  font-size: 12px;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s;
}

.chip:hover {
  background: rgba(255, 255, 255, 0.1);
}

.chip.active {
  background: rgba(0, 170, 255, 0.25);
  border-color: rgba(0, 170, 255, 0.65);
  color: #fff;
}
</style>
