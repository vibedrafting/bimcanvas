<!--
  ModuleSizeEditor — 单维度尺寸编辑器（stepper 形态，参照 SPACE MARK 的 .cell-stepper）。

  设计原则（按用户反馈）：
  - 任意模块都允许编辑（含 fixed），不强制 clamp。
  - +/- 按钮按 50mm 步进；输入框允许自由文本（任意正数）。
  - 推荐范围用灰色文本提示，单独换行右对齐。

  事件：
  - update:value：input/+/- 触发，给"实时预览"消费者（PlacementSizeBar）
  - commit：input change/blur、+ / - 点击 时触发，给"会持久化"消费者（PropertyPanel）
  仅当输入解析为有效正数才发；非法值（NaN/0/负数）静默忽略。
-->

<script setup lang="ts">
import { computed } from 'vue';
import { isValidDimension, type SizeHint } from '../../../utils/moduleSize';

interface Props {
  label: string;       // 'Width' / 'Depth'
  value: number;       // 当前生效尺寸
  hint?: SizeHint;     // formatSizeHint 输出 { text, kind }
  step?: number;       // 模数：+/- 步长，默认 50mm（来自 morphology.step 或兜底）
}

const props = withDefaults(defineProps<Props>(), {
  hint: () => ({ text: '', kind: 'none' as const }),
  step: 50
});

const emit = defineEmits<{
  (e: 'update:value', next: number): void;
  (e: 'commit', next: number): void;
}>();

const parseInputValue = (event: Event): number | null => {
  const raw = (event.target as HTMLInputElement).value;
  const parsed = Number(raw);
  return isValidDimension(parsed) ? parsed : null;
};

const setNext = (next: number, withCommit: boolean) => {
  if (!isValidDimension(next)) return;
  emit('update:value', next);
  if (withCommit) emit('commit', next);
};

const onInput = (event: Event) => {
  const v = parseInputValue(event);
  if (v !== null) emit('update:value', v);
};

const onCommit = (event: Event) => {
  const v = parseInputValue(event);
  if (v !== null) {
    emit('update:value', v);
    emit('commit', v);
  }
};

// 模数对齐：+/- 总是吸附到 step 的整数倍上，避免值偏离模数后越来越偏。
const onDecrement = () => {
  const v = Math.round(props.value);
  // 严格小于当前值的最大 step 倍数
  const next = Math.floor((v - 1) / props.step) * props.step;
  if (next < props.step) return;  // 已到最小一档
  setNext(next, true);
};

const onIncrement = () => {
  const v = Math.round(props.value);
  // 严格大于当前值的最小 step 倍数
  const next = Math.ceil((v + 1) / props.step) * props.step;
  setNext(next, true);
};

// 装饰：default kind 用斜体淡化样式
const hintClass = computed(() =>
  props.hint?.kind === 'default' ? 'hint hint--default' : 'hint'
);

const decrementDisabled = computed(() => Math.round(props.value) <= props.step);
</script>

<template>
  <div class="size-editor">
    <span class="label">{{ label }}</span>
    <span v-if="hint && hint.text" :class="hintClass">{{ hint.text }}</span>
    <div class="stepper">
      <button
        class="stepper-btn"
        type="button"
        :title="`−${step}`"
        :disabled="decrementDisabled"
        @click.stop="onDecrement"
      >−</button>
      <input
        class="stepper-input"
        type="number"
        :value="Math.round(value)"
        :step="step"
        min="0"
        @input="onInput"
        @change="onCommit"
        @blur="onCommit"
      />
      <button
        class="stepper-btn"
        type="button"
        :title="`+${step}`"
        @click.stop="onIncrement"
      >+</button>
    </div>
  </div>
</template>

<style scoped>
/* 单行栅格：[label | hint(灰色，右对齐填充) | stepper] */
.size-editor {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  column-gap: 10px;
  font-size: 12px;
  color: #d8d8e0;
  margin-bottom: 8px;
}

.label {
  color: #9a9aa3;
  font-weight: 500;
  letter-spacing: 0.02em;
}

/* limit kind（range / enum）：硬性可调范围，正常灰度 */
.hint {
  font-size: 11px;
  color: #7a7a82;
  white-space: nowrap;
  text-align: right;
  font-style: normal;
  letter-spacing: 0.02em;
}

/* default kind：仅参考目录默认尺寸，斜体 + 更淡的灰，传达"轻参考、可忽略" */
.hint--default {
  color: #56565e;
  font-style: italic;
  font-weight: 300;
}

/* stepper 容器 — 与 .cell-stepper 同形 */
.stepper {
  width: 132px;
  height: 28px;
  display: inline-flex;
  align-items: center;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 6px;
  background: rgba(0, 0, 0, 0.16);
  font-family: var(--font-mono);
  flex-shrink: 0;
}

.stepper:focus-within {
  border-color: rgba(10, 132, 255, 0.55);
  background: rgba(0, 0, 0, 0.22);
}

.stepper-btn {
  width: 28px;
  height: 100%;
  border: none;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  font: inherit;
  font-size: 0.95rem;
  line-height: 1;
  padding: 0;
}

.stepper-btn:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.08);
  color: var(--text-primary);
}

.stepper-btn:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

.stepper-input {
  flex: 1;
  height: 100%;
  min-width: 0;
  width: 0;
  border: none;
  border-left: 1px solid rgba(255, 255, 255, 0.08);
  border-right: 1px solid rgba(255, 255, 255, 0.08);
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  font-size: 0.82rem;
  font-weight: 600;
  text-align: center;
  outline: none;
  padding: 0 4px;
  appearance: textfield;
  -moz-appearance: textfield;
}

.stepper-input::-webkit-outer-spin-button,
.stepper-input::-webkit-inner-spin-button {
  margin: 0;
  appearance: none;
  -webkit-appearance: none;
}
</style>
